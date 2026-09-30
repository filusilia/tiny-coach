# Agent Note: Wails v3 (Go) 与 Vue 3 极轻电竞战术助手系统架构基线

Status: implemented
Date: 2026-09-20
Class: architecture

## 问题背景与因果机制分析

Dota 2 电竞实时战术辅助工具需要在玩家对局过程中以极高的帧率与极低的系统占用运行。传统桌面客户端方案通常面临以下架构瓶颈：
1. **重型运行时导致游戏掉帧**：采用 Chromium 全量打包或多层重型框架会导致内存占用达数百兆甚至吉字节，CPU 在 GC 和重度渲染时争抢 Dota 2 游戏主进程资源，直接造成微卡顿；
2. **C++ 动态链接库与运行库依赖脆弱**：依赖复杂的 MSVC 运行库或本地编译环境，导致在普通玩家的精简版 Windows 系统上频繁发生缺少 DLL、闪退或环境不兼容；
3. **主线程阻塞与 GSI 吞吐瓶颈**：Dota 2 Game State Integration (GSI) 以最高 10Hz 频率高频推送完整战局快照，若数据接收、反序列化与 UI 渲染共用单个执行线程，会导致界面卡死或 GSI 堆积延迟；
4. **多窗口与置顶能力受限**：早期桌面框架对多窗口隔离、透明无边框点击穿透与全屏覆盖层支持粗糙，容易发生置顶失效或 Alt+Tab 桌面点击被锁死。

## 决策方案

全面升级并确立 **Wails v3 (`v3.0.0-beta.23`) + Go + Vue 3 + TypeScript + Vite + UnoCSS/Tailwind** 现代化极轻电竞客户端架构基线：

1. **宿主与后端服务化模型（Go + Wails v3）**：
   - 采用操作系统内置的 WebView2 作为渲染引擎，二进制单文件绿色发布，运行内存轻盈，零额外 MSVC/C++ 动态依赖；
   - 升级为 Wails v3 现代 `application.New` 与 `application.Service` 架构模型，生命周期接入 `ServiceStartup` 与 `ServiceShutdown` 严谨钩子；
   - 采用 Wails v3 现代事件总线 `application.Get().Event.Emit(...)` 与原生对话框管理 `application.Get().Dialog`；
   - Go 后端单通道监听 `127.0.0.1:4444`，无阻塞接收 Dota 2 GSI 10Hz 快照，原子缓存供主前端与独立悬浮窗按需拉取或事件驱动更新。

2. **Windows 原生与游戏置顶浮窗强化**：
   - 浮窗采用 `application.WebviewWindowOptions` 创建，配合 `BackgroundTypeTransparent` 与 `Frameless`；
   - 采用 `WS_EX_TOOLWINDOW` 替代易致桌面点击失效的 `WS_EX_NOACTIVATE`，并通过 WinAPI `SetWindowPos(HWND_TOPMOST)` 与 500ms 保活协程抵抗游戏前台切屏造成的置顶失效；
   - 提供 Go 端 `SetIgnoreMouseEvents` 与前端移入/移出联动，平衡点击穿透与面板交互。

3. **前端电竞 HUD 与双向兼容桥接（Vue 3 + TypeScript + Vite）**：
   - 采用 Vue 3 组合式 API (Composition API) 与极轻响应式 Store；
   - 前端封装兼容桥接层：平滑适配 Wails v3 的 `window.wails.Call`、`window.wails.Events` 与旧版 `window.go.main.App`，确保既有组件零破坏；
   - 采用 UnoCSS / Tailwind 实现纯深色、高对比度电竞视觉风格。

4. **前后端职责边界契约**：
   - Go 后端：负责 GSI 本地网络监听、多窗口进程调度、WinAPI 置顶守护、系统原生对话框与文件 I/O；
   - 前端 Webview：负责电竞战术图谱、经济天平计算、语音播报合成交互与用户界面响应。

## 曾考虑的替代方案

### 1. 停留在 Wails v2 架构
- **最强理由**：原有代码无需改动 API，稳定性有历史验证。
- **为何弃用**：Wails v2 缺少针对 WebView2 鼠标穿透、任务栏隐藏（旧逻辑易误触发 `WS_EX_NOACTIVATE` 锁死桌面点击）以及多窗口细粒度控制的原生支持；Wails v3 原生支持现代服务模型、更轻的二进制与直接暴露原生窗口句柄的能力，更有利于实现电竞级置顶覆盖。

### 2. Electron 框架方案
- **最强理由**：生态成熟，库丰富，多窗口与前端兼容性完备。
- **为何弃用**：Electron 自带完整的 Node.js 与 Chromium 实例，安装包通常 >120MB，运行内存往往超过 300MB，对高负荷运行的 Dota 2 客户端存在明显的资源争抢风险，不符合“极轻电竞助手”的核心定位。

## 后果与验证计划

- **收益**：架构更加模块化与健壮，多窗口置顶稳固可靠，杜绝桌面点击拦截 Bug，内存与 CPU 消耗维持在极限水准。
- **验证**：每次构建通过 `go build` 与 `pnpm build` 全量类型检查与打包，运行 `npm run verify-notes` 保证工程文档完整性。
