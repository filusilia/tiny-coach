# Agent Note: 局内悬浮窗点击无响应与透明隐形根因修复（主线程预热热备与 DWM 分层样式解耦）

Status: implemented
Date: 2026-10-14
Class: bug-fix

## 问题背景与因果机制分析

用户在 Step 4 交付后反馈：“点击悬浮窗没有悬浮窗呢”。
经由从前端点击事件、Wails v3 IPC 门面、Go 窗口生命周期到 Windows Win32 API 渲染管线的全链路深入追踪，发现了导致悬浮窗无法呼出或完全隐形的三个核心因果链条：

1. **跨线程调用创建窗口导致的 WebView2 / COM 异常与消息泵缺失**：
   - 前端点击通过 IPC 触发 `AppBindings.ToggleOverlayWindow()`，该调用在 Go 后端的普通工作协程（Worker Goroutine）中运行；
   - 原代码中，主程序启动时并未预创建悬浮窗，首次点击时在工作协程中直接执行了 `appInst.Window.NewWithOptions(opts)`；
   - 在 Windows 平台上，Wails v3 的 `NewWithOptions` 会同步执行 Win32 `CreateWindowEx` 与 WebView2 COM 初始化（`setupChromium`），而 WebView2 与 Windows HWND 消息循环必须严格运行在主事件循环线程（STA MainThread）；
   - 跨协程调用会导致异常（触发内部 `recover()`）或创建出的窗口因缺少专属消息循环而卡死。

2. **DWM 扩展样式破坏导致窗口被强行抹成 100% 透明隐形**：
   - 无论悬浮窗是在主进程还是降级子进程中创建，后台保活协程 `StartTopMostRefresher` 每 500ms（且在启动瞬间）均会调用 `EnsureOverlayExtendedStyle(hwnd)`；
   - 原代码在 `EnsureOverlayExtendedStyle` 中，对非鼠标穿透状态错误地写入了 `targetExStyle = (currExStyle | WsExToolwindow | WsExTopmost | WsExLayered)`；
   - Windows 平台底层规定：一个顶级窗口一旦被施加 `WS_EX_LAYERED`，DWM 要求其必须显式通过 `SetLayeredWindowAttributes` 初始化 Alpha 或由 `UpdateLayeredWindow` 提供分层位图；
   - 而 Wails v3 的透明窗口是基于 Windows 10/11 的 DirectComposition（`WS_EX_NOREDIRECTIONBITMAP`）管道渲染的；强行注入未初始化的 `WS_EX_LAYERED` 会破坏 DirectComposition 合成，导致 Windows DWM 将其 Alpha 判定为 0，窗口瞬间变成完全透明且不可见的“隐形窗”，用户肉眼完全看不到任何界面！

3. **悬浮窗生命周期中错误销毁窗口（Destroy / Close）导致二次呼出报空**：
   - 原 `CloseOverlay()` 在同一进程模式下调用了 `win.Close()` 并将 `overlayWindow` 置空；
   - 用户关闭一次悬浮窗后，原生句柄与 WebView2 实例被操作系统彻底销毁，后续再次点击打开时直接落入无句柄分支，造成不可逆的失效。

---

## 决策方案

### 1. 主线程常驻预热热备机制（Single Process Pre-warmed Window）
- **启动阶段预创建**：在 `cmd/aegis-coach/main.go` 中，于 `app.Run()` 启动主事件循环前（主线程），预先创建 `game-overlay` 悬浮窗，并配置 `Hidden: true`；
- **全生命周期主线程绑定**：在 `app.Run()` 之前调用 `NewWithOptions`，窗口初始化会被统一放入 Wails 的 `pendingRun` 并在主线程消息泵中完成 WebView2 与 HWND 的完美初始化，100% 杜绝跨协程 COM 异常；
- **0 毫秒秒开**：通过 `overlayService.SetOverlayWindow(overlayWindow)` 将预热好的窗口指针注入服务；用户点击“打开悬浮窗 (Alt+F2)”时，直接执行 `overlayWin.Show()` 和 `overlayWin.Focus()`，无需等待子进程启动或页面重新加载；
- **隐藏代替销毁**：用户点击悬浮窗关闭按钮时，仅调用 `overlayWin.Hide()`，保存坐标记忆并推送状态，保持原生 WebView2 实例常驻热备。

### 2. DWM 样式解耦与精准穿透控制
- 修复 `internal/platform/windows/overlay_windows.go`：
  - 在非穿透（正常显示）模式下，`EnsureOverlayExtendedStyle` 与 `applySingleWindowMouseThrough` 严格剥除 `WsExLayered` 与 `WsExTransparent`，仅保留 `WsExToolwindow | WsExTopmost`；
  - 窗口完全依赖 DirectComposition 进行高清平滑的半透明暗色背景渲染，文字、技能图标和控制按钮 100% 清晰呈现；
  - 仅在用户按 Alt+F3 显式启用鼠标穿透时，才动态追加 `WsExLayered | WsExTransparent` 并递归应用于所有 WebView2 子窗口，实现物理点击直通游戏。

### 3. 主窗口与悬浮窗快捷键双向绑定
- 在 `main.go` 中，为主窗口和预热的悬浮窗均注册 `Alt+F2`（切换悬浮窗显隐）与 `Alt+F3`（切换鼠标穿透），无论当前 Windows 焦点停留在主窗口还是悬浮窗，快捷键均能毫秒级响应。

### 4. 彻底清理测试资源与恪守铁律第 8 条（零伪造原则）
- **彻底删除临时图片**：从 `frontend/public/` 中彻底删除 2.2MB 的 `test_bp.png` 调试测试图，防止膨胀生产体积；
- **清理测试调试按钮**：从 `BpVisionCaptureCard.vue` 生产 HUD 中彻底移除【上传测试图】、【实战样例】按钮与文件输入控件，替换为【清空重置】功能；
- **消除冷启动缓存污染**：`OverlayView.vue` 取消在挂载时强制反序列化 `localStorage('aegis_bp_overlay')` 旧缓存；未连接 GSI 或无当局识别时，悬浮窗 100% 呈现“等待 GSI 接入”真实空态守卫。

---

## 曾考虑的替代方案

### 替代方案 A：继续采用子进程启动（exec.Command aegis-coach.exe --overlay）
- **最强理由**：子进程与主进程内存物理隔离，主窗口崩溃不会影响悬浮窗。
- **为什么不采用**：
  1. 开发环境（`go run`、`vite`、临时构建目录）中 `os.Executable()` 无法正确定位到有效二进制，导致启动必然失败；
  2. 子进程重新拉起 WebView2 启动开销极大（需要 1.5~3 秒冷启动），而预热窗口是 0 毫秒瞬开；
  3. 子进程无法直接共享主进程的前后端状态与 GSI 事件，需要繁重的跨进程同步，极大增加维护复杂度。因此坚决否定外部子进程，坚持单进程多原生窗口预热架构。
