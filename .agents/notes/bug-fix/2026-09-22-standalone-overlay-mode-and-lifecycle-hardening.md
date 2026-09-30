# Agent Note: 独立经济悬浮窗模式识别判定链加固、Alt+F2 快捷键解耦与生命周期硬隔离

Status: implemented
Date: 2026-09-22
Class: bug-fix

## 背景与问题因果机制剖析

在桌面独立置顶经济悬浮窗功能中，存在“悬浮窗无法脱出 app（在小窗口内仍渲染主程序外壳）”以及“按 Alt+F2 闪退/无响应/状态错乱”的隐蔽缺陷。经全链路因果分析，根因涉及三层机制：

1. **`isStandaloneOverlay` 判断链断裂与多窗口 IPC 盲区**：
   - **因果机制**：主进程内通过 Wails 多窗口模式（`appInst.Window.NewWithOptions`）拉起悬浮窗时，后端 `OverlayService.IsOverlayMode()` 返回的是进程级别的 `s.isOverlay`。因为主程序并非以 `--overlay` 命令行启动，该值永远为 `false`；
   - **异步时序与错误吞噬**：前端如果在 `onMounted` 中通过异步 IPC 期望推翻初始状态，当 IPC 返回 `false` 或由于跨窗口环境异常被 catch 吞掉后，`isStandaloneOverlay` 依然为 `false`；
   - **路由守卫强行重定向**：此前 `router.beforeEach` 守卫中若仅以 `search.includes('window=overlay')` 裁定，遇到 hash 路由或重写时会触发 `next('/coach')`，将悬浮窗路由硬重定向回教练看板；
   - **最终致命表象**：340x460 的透明无边框窗口渲染了 `App.vue` 的 `v-else` 主分支（包含 TitleBar、Sidebar 与主内容区域），导致在用户视觉上看起来就像“悬浮窗缩在 App 内无法脱出”。

2. **`Alt+F2` 快捷键双重注册导致状态抖动**：
   - **因果机制**：Go 后端在主窗口显式注册了 `window.RegisterKeyBinding("Alt+F2", ...)` 与系统视图菜单加速键；而前端 `App.vue` 中也使用了 `window.addEventListener('keydown', ...)` 监听 Alt+F2 并调用 `overlayApi.toggleOverlayWindow()`；
   - **后果**：主窗口获得焦点时按一次 Alt+F2，Go 宿主与 WebView 前端同时捕获并以数毫秒之差连调两次切换，导致悬浮窗“刚打开瞬间被关闭”或“反复横跳失效”。
   - **事件泄漏**：前端匿名事件监听器在组件重载或 HMR 时无法被回收。

3. **悬浮窗模式下仍在执行主窗口独占逻辑**：
   - `useNativeNavigation()` 在顶层无条件执行，当主窗口触发系统菜单导航（`navigate_to`）时，事件全窗口广播会导致悬浮窗也被带偏跳转至设置或复盘页；
   - 静态导入 `OverlayView.vue` 导致代码包未解耦。

## 决策方案

1. **确定性同步判定与多重特征识别（消除异步 IPC 依赖）**：
   - 在 `App.vue` 与 `router/index.ts` 中建立确定性同步判定函数 `checkIsOverlayWindow()`：
     - 同时校验 `URLSearchParams(search).get('window') === 'overlay'` 与 `mode === 'overlay'`；
     - 校验 `window.location.search`、`window.location.hash` 与全量 href；
     - 校验 Wails 窗口专属命名 `window.name === 'game-overlay'`；
   - Go 后端（`../../../cmd/aegis-coach/main.go` 与 `internal/services/overlay_service.go`）统一在窗口 URL 中完整声明：`/index.html?window=overlay&mode=overlay#/overlay`；
   - 初始即为精确布尔值，杜绝 `onMounted` 异步修改造成的界面闪烁与退化。

2. **彻底解耦 `Alt+F2` 快捷键，消除前端监听**：
   - 完全移除 `App.vue` 中前端 `window.addEventListener('keydown')` 对 Alt+F2 的绑定；
   - 全局响应统一交付 Go 宿主层 `window.RegisterKeyBinding("Alt+F2")` 与应用原生菜单加速键，由操作系统和 Wails 底层完成单一调度。

3. **生命周期与模块硬隔离**：
   - `useNativeNavigation()` 严格仅在 `!isStandaloneOverlay` 时注册，悬浮窗不响应菜单跳转；
   - `gsiRuleEngine.init()`、缓存同步与数据同步服务仅在主窗口执行，防止语音播报重复；
   - `useGsiStream()` 保持按需消费，保障悬浮窗能够持续接收 10Hz GSI 战况快照；
   - 悬浮窗组件改为 `defineAsyncComponent(() => import('./views/OverlayView.vue'))`，并在路由中采用动态导入，使得 `OverlayView`（~8.78 kB）独立拆包。

4. **API 层纯净化（遵循 Wails v3 铁律第 17、25 条）**：
   - 重构 `src/api/overlay.ts`，全面移除手写 `invokeWails` 候选名称猜测数组，直连 `frontend/bindings/aegis-coach/app` 导出的原生强类型绑定。

## 曾考虑的替代方案

### 方案 A：在 Go 端每次 IPC 调用时注入当前调用的 Window 句柄以重写 `IsOverlayMode`
- **最强理由**：能让 Go 端的服务方法准确识别出是哪个窗口发起的 RPC。
- **放弃原因**：Wails v3 单单为了判断窗口形态而在通用 Service 接口注入 WebviewWindow 过于繁琐且容易引发跨窗口死锁；WebView 页面本身的 URL 参数与窗口标识（`window=overlay`）是即时、同步且 100% 具备权威性的天然事实。

## 后果与验证计划

- **收益**：
  - 彻底终结了“悬浮窗缩在 App 主窗口内无法脱出”的布局误判；
  - 彻底解决了按 Alt+F2 出现双重触发抖动、闪现或失灵的问题；
  - 优化了代码体积分包，消除了 Vite 动态导入静态穿透警告。
- **验证矩阵**：
  - `npm run verify-notes`：全量 27 篇 Agent 决策笔记校验 100% 通过；
  - `pnpm build`：Vite 成功生成独立 `OverlayView-*.js` chunk（8.78 kB）；
  - `pwsh -NoProfile -File ./tools/package-release.ps1`：一键打包 24.12 MB 单文件客户端 `release/神盾教练.exe` 验证通过。
