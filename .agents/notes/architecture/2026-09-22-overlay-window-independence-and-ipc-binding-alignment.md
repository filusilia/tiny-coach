# Agent Note: 独立置顶悬浮窗彻底物理脱离与原生拖拽交互重构

Status: implemented
Date: 2026-09-22
Class: architecture

## 背景与问题因果机制剖析

在针对局内独立经济悬浮窗“无法真正脱出、深陷主窗口关联体系中”的用户反馈进行全链路深潜审查后，精确定位到 5 项深层因果机制：

1. **Wails v3 `ExStyle` 与 `WS_EX_LAYERED` 导致的透明通道阻断（白色背景根因）**：
   - **机制**：在 Go 后端创建窗口时若配置了未经过 DirectComposition 配合的扩展样式，或在 Win32 保活循环中强加 `WS_EX_LAYERED`，会导致 Windows DWM 将 DirectComposition 交换链回退至 GDI 分层窗口体系；因未配置 Layered 调色板，系统默认使用白色画刷填充底板，表现为窗口周围出现死板的纯白色方块背景。
   - **解决**：在保活协程与扩展样式中彻底剔除 `WS_EX_LAYERED`，窗口选项显式注入 `BackgroundColour: application.RGBA{0, 0, 0, 0}`，且在 HTML 根节点内联 `background: transparent !important`，确保 DirectComposition 全通道透明生效。
2. **Win32 Owned Window 属主牵连**：
   - **机制**：在主进程中先后创建的主窗口与悬浮窗窗口，在 Windows DWM/Z-Order 体系中默认建立属主关系（Owned Window）。当主窗口最小化或聚焦时，子窗口被约束在父窗口的层叠链中连带最小化或隐藏，未能形成真正独立的 Top-Level 窗口。
3. **前端 DOM 假拖拽局限在 340x460 视口内部**：
   - **机制**：旧版 `OverlayView.vue` 拖拽把手仅通过 JavaScript 修改内部容器的 CSS `left/top`。由于原生窗口物理尺寸已固化为 340x460，用户在把手上拖拽时，原生 Win32 窗口根本不动，而内部卡片被推挤到视口之外发生严重裁切，给用户“无法在桌面上自由拖动脱离”的直观退化感。
4. **穿透锁死与 `isStandalone` 模式误判**：
   - **机制**：在 Windows Win32 规范下，一旦向窗口应用 `WS_EX_TRANSPARENT`，系统命中测试直接穿透到底层，WebView2 无法再捕获到 `mouseenter` 事件。原本企图通过 `onMouseEnter/onMouseLeave` 自动取消穿透的逻辑必然陷入永久锁死。
5. **主窗口误渲染悬浮窗与状态不同步**：
   - **机制**：路由守卫未限制主窗口导航至 `/overlay`；且 Go 后端快捷键切换（Alt+F2）派发的 `overlay_state_change` 原生事件在前端无监听者，导致按钮状态脱节。

## 被放弃的方案与权衡分析

### 方案 A：单纯依赖物理子进程启动 (`exec.Command(exePath, "--overlay")`)
- **最强理由**：拥有完全独立的 OS 级 PID 与消息循环，绝对物理隔离。
- **放弃原因**：Dota 2 GSI 本地端口为 `4444` 单监听，子进程无法重复绑定端口；且主进程已接收的 10Hz GSI 事件无法跨 Wails 实例原生派发，若为子进程构建跨进程 IPC 会引入高延迟与端口冲突，违背零外挂与极简架构原则。

### 方案 B：全局 Windows 鼠标 Hook 实时监测光标位置以动态穿透
- **最强理由**：能做到完全全自动的移入移出穿透判定。
- **放弃原因**：全局鼠标 Hook 极易引发 Dota 2 反作弊（VAC）的拦截误报，触碰合规红线与非外挂原则（铁律第 7 条），且在 Windows 高刷新率下产生显著 CPU 开销。

## 实施的技术方案与架构重构

1. **拔除 Win32 属主关联、消除原生标题栏/小关闭按钮与 ExStyle 冲刷（[internal/overlay/overlay_windows.go](file:///D:/develop/demo/internal/overlay/overlay_windows.go)、[internal/services/overlay_service.go](file:///D:/develop/demo/internal/services/overlay_service.go)）**：
   - 清除 `GWL_STYLE` 中的 `WS_CAPTION | WS_THICKFRAME | WS_SYSMENU`，注入 `WS_POPUP`，根除 Windows 系统原生白色小标题栏以及右上角自带的小关闭按钮；
   - 启用 `DisableFramelessWindowDecorations: true`，禁止 Wails 内部扩展 DWM 客户区保留边框；
   - 彻底移除 `ExStyle: w32.WS_EX_TOOLWINDOW` 手动传递，改由 Wails 内建 `HiddenOnTaskbar: true` 安全注入，完美保全 `WS_EX_TOPMOST` 和 `WS_EX_NOREDIRECTIONBITMAP` 透明样式；
   - 引入 `procSetWindowLongPtrW.Call(hwnd, GWLP_HWNDPARENT, 0)`（`DetachWindowOwner`），在 Win32 层面强行断开与主窗口的属主关系，将悬浮窗提升为真正的独立顶级无属主置顶窗口；
   - URL 规范化为 `/index.html?window=overlay#/overlay`。

2. **升级为 Wails 原生桌面无边框拖拽（[frontend/src/views/OverlayView.vue](file:///D:/develop/demo/frontend/src/views/OverlayView.vue)）**：
   - 废除所有基于 DOM `left/top` 的局部偏移与限制；
   - 把手样式注入 `style="--wails-draggable: drag"`，并在 `@mousedown` 时原生投递 `wails:drag` 消息，驱动 Windows 原生 `SC_MOVE`，实现丝滑的全桌面自由拖移；
   - 窗口容器规范为 `w-full h-full p-1`，卡片居中适配，杜绝边界裁切。

3. **防锁死的人性化物理穿透控制**：
   - 顶部工具栏增设“点击穿透锁定”切换按钮（`Lock` / `MousePointer`），用户可一键切换 HUD 纯观战穿透模式（点击完全透传至底层游戏）；
   - 默认开启可交互态，支持折叠、关闭与 30%~100% 透明度调节。

4. **主窗口生命周期保护与全站状态双向同步（[main.go](file:///D:/develop/demo/main.go)、[useNativeNavigation.ts](file:///D:/develop/demo/frontend/src/composables/useNativeNavigation.ts)）**：
   - 主窗口 `WindowClosing` 事件守护：当悬浮窗正在运行时，主窗口关闭退化为隐藏至托盘，悬浮窗继续置顶运行；
   - 前端集成 `overlay_state_change` 事件总线，与 Alt+F2、托盘菜单、主界面按钮实现纳秒级双向同步；
   - 路由前置守卫拦截主窗口对 `/overlay` 的误导航，重定向至 `/coach`。

5. **全量 API 模块 100% 接入强类型 `AppBindings`（[ai.ts](file:///D:/develop/demo/frontend/src/api/ai.ts)、[player.ts](file:///D:/develop/demo/frontend/src/api/player.ts)、[gsi.ts](file:///D:/develop/demo/frontend/src/api/gsi.ts)）**：
   - 彻底废除手写字符串猜测，所有调用直连 `bindings/aegis-coach/app` 模块，消灭运行时调用盲区。

## 验证结论

- **决策笔记校验**：`npm run verify-notes` 全量通过。
- **全量构建验证**：`pnpm build` 顺利通过，Vite 资产构建正常。
- **便携版打包验证**：`tools/package-release.ps1` 顺利生成单文件免安装绿色客户端。
