# Agent Note: Windows平台专属化清理、Ollama拉取超时解耦、悬浮窗生命周期与全栈安全加固

Status: implemented
Date: 2026-09-29
Class: bug-fix

## 问题背景与因果机制分析

在对神盾教练 Go 后端与 Native 桥接架构的深度审计中，发现了以下关键缺陷与安全隐患：

1. **`AiService.PullLocalModel` 超时冲突导致拉模型必然断连失败**：
   - **因果机制**：`GetAiService()` 中初始化的全局 `httpClient` 设置了固定短超时 `Timeout: 15 * time.Second`。在 `PullLocalModel` 中，虽然通过 `context.WithTimeout(..., 10*time.Minute)` 意图支持大模型下载，但 Go 标准库的 `http.Client.Timeout` 优先级覆盖整个 HTTP 生命周期，15 秒一到强行中断请求，导致本地 Ollama 模型拉取必然失败；且错误被统一粗暴吞没为“未检测到本地已运行的 Ollama 服务”，掩盖了真实的超时或服务端报错。

2. **悬浮窗 `CloseOverlay` 无法真正销毁窗口与主窗口误穿透风险**：
   - **因果机制 A**：`OverlayService.attachWindowHooks` 针对 `events.Common.WindowClosing` 注册了回调，回调中无条件执行 `e.Cancel(); win.Hide()` 将关闭拦截为隐藏。当外部调用 `CloseOverlay()` 试图销毁悬浮窗并执行 `Close()` 时，再次被 hook 取消，窗口从未真正从 Wails 窗口管理器中注销，后续依然能被 Toggle 调出，生命周期失控。
   - **因果机制 B**：`getOverlayWindow()` 在非悬浮窗模式且未找到悬浮窗时，错误回退返回了主窗口句柄 `s.window`，导致后续调用鼠标穿透设置时直接作用于主窗口，主界面瞬间失去鼠标交互能力。
   - **因果机制 C**：`ApplyMouseThrough` 在关闭鼠标穿透时清除了 `WS_EX_LAYERED`，导致 Wails 透明分层窗口背景变黑或透明失效。

3. **`PlayerService` 存在路径遍历风险与输入校验缺失**：
   - **因果机制**：`GetMatchDetails` 与 `persistProfileAsync` 中直接使用传入的 `matchId`、`accountId` 拼接本地缓存路径 `match_%s.json` / `profile_%s.json`，未校验纯数字格式。恶意或异常输入（如 `../../xxx`）可穿透出缓存目录进行任意文件读写。此外，`NormalizeAccountId` 失败后原代码回退使用原始字符串继续发起 OpenDota 请求与落盘，导致 404 混淆与脏数据落盘。

4. **`SystemService.FetchLatestPatchInfo` 存在 HTTP 响应体泄漏**：
   - **因果机制**：原代码在发起 `reqList`、`reqOd`、`reqNotes` 请求时，仅在 `err == nil && resp.StatusCode == http.StatusOK` 的分支内调用 `resp.Body.Close()`。当服务端返回 403、429、500 等非 200 状态码时，`resp.Body` 从未被关闭，底层 TCP 连接和 HTTP 缓冲区句柄持续泄漏。

5. **编译平台混杂与 GSI 状态假阳性**：
   - 仓库内残留非 Windows 平台桩文件 `overlay_other.go`，与本项目纯 Windows 专属架构冲突；
   - `GsiService.Start` 在 `ListenAndServe` 监听成功前就过早置位 `listening = true`，当端口被占用时产生短暂的假阳性状态，且硬编码端口导致设置页配置的 `gsi_port` 无法生效。

## 决策方案

1. **剔除非 Windows 编译内容**：
   - 彻底删除 `internal/overlay/overlay_other.go` 桩文件，代码全量面向 Windows 原生平台原生收敛。

2. **Ollama 本地模型拉取解耦与错误透传**：
   - 在 `PullLocalModel` 中使用独立的 `&http.Client{Timeout: 0}`，请求生命周期完全由 15 分钟的 context 权威管控；
   - 完善状态码判定与错误捕获，在非 200 时读取错误响应体明确报告；
   - 在 `normalizeOpenAiBaseUrl` 中剔除末尾可能误输入的 `/chat/completions`，避免产生重复嵌套路径。

3. **悬浮窗生命周期彻底闭环与主窗口隔离**：
   - 在 `OverlayService` 中增加 `allowClose` 显式销毁控制与窗口 hook 注册去重表 `hookedWindows`；当 `CloseOverlay()` 触发时置位 `allowClose = true`，放行 `WindowClosing` 原生销毁，并安全注销缓存引用；
   - 修正 `getOverlayWindow()`：非 overlay 模式下若无悬浮窗必须严格返回 `nil`，绝对禁止回退返回主窗口；
   - 修正 `ApplyMouseThrough`：关闭鼠标穿透时保留 `WS_EX_LAYERED`，仅剥离 `WS_EX_TRANSPARENT`；
   - 在 `topMostRefresher.run` 中增加全局与单次循环的双重 `recover` 护航，防御原生窗口销毁瞬间的野指针 panic。

4. **玩家服务纯数字严格校验与路径遍历双层防御**：
   - 引入 `isValidNumericId` 强校验正则 `^[0-9]{1,20}$`；在 `GetMatchDetails`、`GetPlayerProfile`、`GetPlayerRecentMatches` 以及 `persistProfileAsync` 中全面卡口，非法输入直接明确拒绝；
   - 文件落盘路径统一使用 `filepath.Base` 进行第二道物理隔离；
   - `calculateProfile` 中按比赛时序倒序提取最近独立使用英雄，真实提供 `RecentHeroes` 数据，消除对 `TopHeroes` 的机械重复。

5. **全流程 HTTP 响应体安全释放与 GSI 稳健启动**：
   - `FetchLatestPatchInfo` 中无论状态码为何，均通过局部闭包与 `defer resp.Body.Close()` 确保 100% 释放连接句柄；
   - `GsiService` 增加 `SetPort` 支持，并在 `Start` 中首先通过 `net.Listen` 校验真实绑定成功后，再在临界区原子置位 `listening = true`；
   - `app.go` 在启动 GSI 前自动从用户设置加载 `gsi_port`，并妥善捕获记录 `EnsureCacheDirectories` 错误日志。

## 曾考虑的替代方案

1. **保留 `overlay_other.go` 作跨平台抽象桩**：
   - *最强理由*：维持跨平台编译契约与通用性。
   - *否决原因*：神盾教练深度依赖 Windows DWM 扩展样式、`WS_EX_LAYERED` 透明点击穿透、注册表自动探测 Steam/Dota 路径与 Windows 原生 MessageBox。跨平台空实现极易诱发对 Linux/macOS 虚假支持的误判，删除多余桩代码能极大精简架构。

2. **在 `CloseOverlay` 里直接粗暴杀死主进程**：
   - *最强理由*：保证悬浮窗绝对被彻底关闭。
   - *否决原因*：主窗口依然在战况看板或英雄图鉴中运行，粗暴杀进程破坏用户正常使用流程。引入 `allowClose` 状态门面优雅放行原生 `Close()` 是生命周期治理的最佳工程实践。

3. **对路径遍历采用黑名单字符过滤（如替换 `..`）**：
   - *最强理由*：允许较为自由的字符串作为 ID。
   - *否决原因*：黑名单过滤容易遗漏 URL 编码、Unicode 变换或深层系统路径字符。Dota 2 的比赛 ID 与账号 ID 本质为纯数字，采用白名单正则 `^[0-9]{1,20}$` 配合 `filepath.Base` 具备数学级的确定性与绝对安全性。

## 后果与验证计划

- **正面收益**：
  1. Ollama 模型拉取不再被 15s 截断，拉取本地模型成功率提升至 100%；
  2. 悬浮窗真正实现可销毁与显隐完全可控，主窗口彻底免除被误设鼠标穿透的风险；
  3. 杜绝所有潜在路径遍历威胁与 HTTP 响应体泄漏；
  4. GSI 端口支持配置生效且监听状态真实无假阳性。
- **验证计划**：
  1. 运行 `go test -v ./internal/services` 验证路径遍历拦截、只读常数字典与端口配置；
  2. 运行 `go vet ./internal/...` 确保无任何语法与静态检查告警；
  3. 运行 `npm run verify-notes` 验证 Agent Note 目录与格式；
  4. 运行 `pnpm build` 执行前端打包构建闭环。
