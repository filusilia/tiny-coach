# Agent Note: 悬浮窗位置还原缺陷修复、设置文件夹优先持久化机制与退出生命周期拦截确认

Status: implemented
Date: 2026-09-22
Class: bug-fix

## 问题背景与因果机制分析

1. **悬浮窗本次运行可记录拖拽位置，但重启软件后重新打开位置被强制还原**：
   - **因果机制分析**：
     - 在 `internal/services/overlay_service.go` 与 `../../../cmd/aegis-coach/main.go` 中，虽然代码通过 `LoadSavedPosition()` 成功从 `overlay_position.json` 读取了保存的 `X` 与 `Y` 坐标，但在构建 `application.WebviewWindowOptions` 时， **遗漏了 `opts.InitialPosition = application.WindowXY`**；
     - 在 Wails v3 的 `WebviewWindowOptions` 结构体中，`InitialPosition` 字段默认零值为 `WindowCentered = 0`。即使指定了 `opts.X` 与 `opts.Y`，只要 `InitialPosition` 为 `WindowCentered`，底层 Win32 窗口管理器在初始化排版时就会将窗口强制居中，彻底忽略传入的 X/Y 坐标；
     - 在单次运行期间，悬浮窗隐藏/显示仅调用 `Hide()` 与 `Show()`，属于同一原生窗口句柄内存复用，因此保持了拖动位置；而一旦关闭软件重新启动，窗口重新创建，居中逻辑再次执行，导致用户感知为“重启后位置被还原”；
     - 此外，`overlay_service.go` 中的 `getPositionFilePath()` 错误调用了 `GetDefaultCacheDirectory()` 而非动态生效的 `GetCacheDirectory()`，且在窗口创建后未调用 `overlayWin.SetPosition(x, y)` 强制刷新就位。

2. **软件设置没有遵循“先从设置文件夹内读取，没有再新建”的权威原则**：
   - **因果机制分析**：
     - 前端 Pinia Store (`app.ts`) 此前将所有 `AppSettings` 全量存储在浏览器的 `localStorage` 中，缺乏 Go 后端设置文件夹（`<cacheDir>/settings.json`）的物理落盘与读取机制；
     - 当用户清理浏览器环境、换目录或以独立模式启动时，配置极易丢失；且用户直接在设置文件夹中调整配置无法被软件读取；
     - 软件启动时未遵循“先检查并优先从本地设置文件夹读取已有 `settings.json`；若无文件或文件损坏，才新建一份默认设置并落盘”的确定性闭环。

3. **主窗口关闭时悬浮窗未关闭（孤儿窗口），且悬浮窗开启时缺少退出确认提醒**：
   - **因果机制分析**：
     - `../../../cmd/aegis-coach/main.go` 中的主窗口 `WindowClosing` 监听此前存在设计缺陷：当悬浮窗运行时，直接盲目执行 `e.Cancel(); window.Hide();`，主窗口消失入托盘，悬浮窗依然悬在桌面，用户在没有任何感知的情况下误以为程序已关闭但悬浮窗残留；
     - 缺乏悬浮窗开启时的退出确认机制；当悬浮窗正在对局中工作时，若用户点击主窗口关闭或托盘退出，缺少“悬浮窗当前处于开启状态，是否确定关闭软件”的二次交互确认；
     - 软件退出时未统一执行悬浮窗句柄销毁与子进程回收，导致悬浮窗可能成为孤儿进程。

---

## 决策方案

### 1. 悬浮窗位置记忆与恢复加固
- **设置 `InitialPosition = application.WindowXY`**：
  在 `overlay_service.go` 的 `ToggleOverlayWindow` 与 `../../../cmd/aegis-coach/main.go` 悬浮窗创建入口中，检测到存在历史记忆位置（`hasPos == true`）时，显式指定 `opts.InitialPosition = application.WindowXY`，并在窗口创建后立刻调用 `overlayWin.SetPosition(x, y)` 强制刷新位置。
- **路径与有效性统一**：
  `getPositionFilePath()` 统一改为 `GetSystemService().GetCacheDirectory()` 路径，并对窗口坐标进行异常值防飘屏校验。

### 2. 设置文件夹（`<cacheDir>/settings.json`）第一权威持久化管线
- **Go 端基础设施**：
  - 在 `SystemService` 提供 `LoadSettings() (string, error)` 与 `SaveSettings(settingsJson string) error`；
  - **遵循权威加载逻辑**：优先探测 `<cacheDir>/settings.json` 是否存在且内容合法；若存在直接读取已有设置返回；若不存在或解析失败，则生成官方默认配置、落盘新建 `settings.json` 后返回；
  - 在 `../../../internal/app/app.go` 与 `main.App` 门面上显式暴露该方法，并重新生成 Wails 绑定；
  - 将 `data` 目录加入 `storage.EnsureCacheDirectories` 标准子目录初始化清单。
- **前端 Store 双向驱动**：
  - `appStore` 在启动初始化时，优先调用 `systemApi.loadSettings()` 提取设置文件夹内的持久化配置并覆盖本地状态，再同步至 `localStorage`；
  - 用户在 UI 中调整任何配置时，在更新 `localStorage` 的同时异步写入 `<cacheDir>/settings.json`，确保物理文件与前端状态绝对一致。

### 3. 关闭软件生命周期拦截与悬浮窗联动销毁
- **原生退出确认对话框 (`showExitConfirmDialog`)**：
  - 当悬浮窗处于激活态时，用户触发主窗口关闭（`WindowClosing`）、托盘“退出程序”或系统菜单“退出神盾教练”，立即拦截退出动作并唤起 Windows 原生 `MB_YESNO | MB_ICONQUESTION` 模态弹窗；
  - 提示文案明确告知：“悬浮窗当前处于开启状态，是否确定关闭软件？（关闭软件将同时退出悬浮窗）”；
  - 默认聚焦在“否”（`MB_DEFBUTTON2`），防止用户团战中误触回车误关；
  - 若用户确认退出，先调用 `appLogic.CloseOverlay()` 优雅清理并销毁悬浮窗窗口句柄与子进程，再调用 `app.Quit()` 退出整个程序；若用户取消退出，则取消关闭事件，保持一切运行。
- **悬浮窗未开启时直接退出**：
  - 当悬浮窗未打开时，用户点击主窗口关闭按钮直接关闭软件并退出程序，绝不产生多余拦截或隐形托盘驻留。

---

## 曾考虑的替代方案

### 替代方案 A：直接使用 Wails v3 的 `app.Dialog.Question()` 替代 Win32 原生 `MessageBox`
- **最强理由**：采用 Wails 官方封装的跨平台对话框 API，符合面向对象设计且无需编写平台条件编译。
- **为什么不用**：
  - 经深入核查 Wails v3 源码（`dialogs_windows.go`），其底层通过计算返回值字符串映射（`"Yes"`、`"No"`）并按 `buttonInDialog.Label == result` 分发回调；
  - 若传入中文按钮文本（如“确定退出”或“是”），在 Windows 平台与系统的 `IDYES` / `IDNO` 存在匹配断裂风险，容易导致回调静默不执行；
  - 相比之下，在 Windows 平台上直接调用标准 `windows.MessageBox(hwnd, ...)` 具备绝对确定性，支持将主窗口 HWND 设为模态宿主，且由操作系统自动提供本地化“是(Y) / 否(N)”按钮，万无一失。

### 替代方案 B：悬浮窗位置只由前端在关闭时通过 DOM 事件传递给后端
- **最强理由**：前端 Vue 组件最了解组件卸载时机，由前端统一传递参数最为自然。
- **为什么不用**：
  - 悬浮窗是以无边框透明原生窗口运行，用户可以通过 Alt+F4、任务栏右键或突然关机等途径关闭，此时前端组件可能来不及执行 `beforeUnmount`；
  - Wails v3 原生窗口钩子（`WindowEndMove`、`WindowDidMove`、`WindowClosing`）在操作系统层面直接监听窗口矩形变动，在拖拽结束瞬间即时落盘，可靠性远高于前端轮询或卸载钩子。

---

## 后果与验证计划

### 正面收益
- 悬浮窗重新打开时 100% 精确复原上次所拖拽的屏幕坐标，不再发生被强制居中还原的问题；
- 设置文件夹（`<cacheDir>/settings.json`）成为第一权威配置源，严格遵循“有则读，无则建”的生命周期铁律；
- 主程序关闭时悬浮窗彻底随之退出，杜绝孤儿窗口；开启悬浮窗时退出具备防误触确认弹窗，用户交互安全有保障。

### 验证计划
1. 运行 `npm run verify-notes`：校验全量 Agent Notes 结构格式；
2. 执行全量构建验证（`pnpm build` 与 `go build`）；
3. 验证设置文件夹中的 `settings.json` 读写与默认新建逻辑；
4. 验证悬浮窗坐标在 `overlay_position.json` 与原生窗口初始化之间的闭环；
5. 验证主窗口退出时确认弹窗与悬浮窗同步关闭行为。
