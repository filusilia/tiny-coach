# Agent Note: 独立桌面置顶经济悬浮窗与子进程生命周期治理

Status: implemented
Date: 2026-09-20
Class: architecture

## 问题背景与因果机制分析

在 Dota 2 全屏无边框对局中，玩家需要即时掌握关键公共资源刷新倒计时（肉山、智慧魔方、赏金神符）与双方经济态势，但频繁切屏查看主客户端会导致对局注意力分散甚至送命。因此，必须提供一个紧凑、高对比度、置顶在游戏上方的电竞 HUD 悬浮窗。

在传统单窗口多 Webview 或普通原生置顶方案中，常常出现以下问题：
1. **主窗口最小化连带悬浮窗隐藏**：若悬浮窗只是主窗口的子组件或依附于主窗口，当主窗口被玩家最小化时，悬浮窗随之隐藏失效；
2. **多窗口句柄泄漏与孤儿进程**：若通过独立进程唤起悬浮窗，当主程序被玩家关闭退出时，子进程容易发生遗留并在后台长期挂起，消耗系统资源；
3. **Alt+Tab / Win+D 桌面点击拦截陷阱**：旧方案或默认使用 `WS_EX_NOACTIVATE` 隐藏任务栏图标，会导致 WebView2 在按下 Win+D 或 Alt+Tab 后持续捕获鼠标点击，导致 Windows 原生桌面无法点击；
4. **全屏无边框置顶层级被抢占**：普通 `AlwaysOnTop` 属性在游戏激活、分辨率切换或前台焦点转移时容易失效，导致悬浮窗被游戏窗口遮挡。

## 决策方案

确立**Wails v3 + WinAPI 原生增强的独立桌面悬浮窗治理方案**：

1. **命令行路由与原生独立窗口配置 (Wails v3)**：
   - 应用程序同一套二进制可执行文件通过检测命令行参数 `-overlay` 判断启动模式；
   - 悬浮窗模式下初始化 `application.WebviewWindowOptions`：
     - `Frameless: true`（无边框）；
     - `AlwaysOnTop: true`（Wails 内置置顶）；
     - `BackgroundType: application.BackgroundTypeTransparent`（全透明背景渲染）；
     - `Windows: application.WindowsWindow{ HiddenOnTaskbar: true, ExStyle: w32.WS_EX_TOOLWINDOW }`；
   - **关键机制**：使用 `WS_EX_TOOLWINDOW` 替代 `WS_EX_NOACTIVATE`，既能从任务栏与 Alt+Tab 中彻底隐藏窗口，又保留正常激活与输入交互，杜绝 Win+D 桌面点击被锁死。且必须使用 `|=` 形式合并样式，杜绝直接赋值抹除其他必需样式。

2. **WinAPI 全局置顶与失效恢复保活协程**：
   - 通过 `uintptr(window.NativeWindow())` 获取 Win32 真实原生 `HWND` 句柄；
   - 使用 WinAPI `user32.dll` 的 `SetWindowPos` 传入 `HWND_TOPMOST (-1)`，标志位配合 `SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW`；
   - 启动 500ms 后台轻量轮询协程：当悬浮窗处于可见状态 (`window.IsVisible()`) 时，持续刷新并保活置顶状态，抵抗游戏切屏抢焦点造成的降层。

3. **智能鼠标穿透与交互动态切换**：
   - 支持 Go 导出方法 `SetIgnoreMouseEvents(ignore bool)` 桥接到前端；
   - 平时保持点击穿透，不阻碍底层游戏战斗操作；
   - 前端悬浮窗卡片在鼠标移入 (`mouseenter`) 时调用 `SetIgnoreMouseEvents(false)` 恢复面板可点击/可拖拽；移出 (`mouseleave`) 时恢复穿透。

4. **主程序调度与子进程生命周期闭环**：
   - 主客户端在对局进行或玩家手动开启时，通过 `exec.Command("-overlay")` 拉起独立实例；
   - Go 后端严格维护子进程句柄 `overlayCmd *exec.Cmd`；
   - 主程序关闭退出钩子 `ServiceShutdown` 触发强制安全杀死子进程，杜绝孤儿进程泄漏。

## 曾考虑的替代方案

### 1. 采用 WS_EX_NOACTIVATE 来隐藏任务栏图标
- **最强理由**：实现简单，窗口创建时不会夺取游戏焦点。
- **为何弃用**：会导致 WebView2 在 Win+D 显示桌面或 Alt+Tab 切换后陷入异常鼠标捕获状态，使得 Windows 桌面图标完全无法点击，造成严重系统级 Bug；改用 `WS_EX_TOOLWINDOW` 可彻底消除此隐患。

### 2. 利用 DirectX/Vulkan 在游戏内 Hook 绘制 OSD 覆盖层
- **最强理由**：直接与游戏画面融合，独占全屏也能显示。
- **为何弃用**：直接触犯 Valve VAC 注入与 Hook 渲染的合规红线，严重违反本项目非外挂核心铁律。

## 后果与验证计划

- **收益**：悬浮窗具备极致稳固的置顶能力与透明渲染表现，无残留子进程，不影响桌面点击。
- **验证**：执行 `go build` 编译，测试启动 `-overlay` 模式，验证无边框透明展示、Alt+Tab 不阻碍桌面、500ms 保活置顶与主程序退出时的联动销毁。
