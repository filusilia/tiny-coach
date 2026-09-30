# Agent Note: MeloTTS 并发 Data Race、ONNX 会话释放、悬浮窗关闭健壮性与系统鲁棒性治理

Status: implemented
Date: 2026-09-29
Class: bug-fix

## 问题背景与因果机制分析

在对系统全栈文档与核心 Go 后端代码的专项代码审查中，发现了若干高优先级与中优先级隐患：

1. **`melo_engine.go` 中 `pathsReady` 并发 Data Race 风险**：
   - 在 `Synthesize`、`IsReady` 与 `Status` 中，代码在未持有互斥锁的情况下读取了 `m.pathsReady`。
   - 与此同时，后台下载完成时 `EnsureReadyInBackground` 会调用 `RefreshPaths()`，而在 `RefreshPaths()` 内部会在持锁状态下将 `m.pathsReady` 重置为 `false`。
   - 当多个 goroutine 在并发合成或查询引擎状态的同时发生后台重载时，会触发未同步的内存读写碰撞，在 `-race` 检测下必报 data race，高并发下存在程序崩溃或脏读风险。

2. **`MeloTtsEngine.Close()` 未真正销毁 ONNX Runtime 资源**：
   - `go-speech` 依赖的 `melotts.Engine` 内部持有了 C 侧分配的 ONNX Runtime Session 物理句柄。
   - `melo_engine.go` 的 `Close()` 和 `RefreshPaths()` 仅将 `m.engine` 置为 `nil`，依赖 Go 垃圾回收器回收。
   - 实际上，`melotts.Engine` 显式提供了 `Destroy()` 方法。若不显式调用，底层 C++ Session 池和 ONNX 内存无法被及时释放，在长时间运行或频繁重载后会导致句柄与显存/内存泄漏，甚至导致后续重新初始化失败。

3. **`overlay_service.go` 中 `allowClose` 状态标志脆弱**：
   - 原代码在 `CloseOverlay()` 中通过 `s.allowClose = true`，并在紧随其后的 `defer func() { s.allowClose = false }()` 中重置该标志。
   - 这严重依赖于 Wails v3 的 `wWin.Close()` 必须是 100% 阻塞且同步执行完其注册的 `WindowClosing` 回调。
   - 一旦 Wails 在某些平台或版本中将事件回调调度到下一个消息循环处理，`defer` 会先于回调执行而将 `allowClose` 置回 `false`，回调内的 `e.Cancel()` 会强行拦截关闭并退化为 `Hide()`，导致窗口句柄无法真正销毁。

4. **系统底层鲁棒性细节缺陷**：
   - `melo_engine.go` 的 `downloadFile` 在原子 `os.Rename(tmpPath, destPath)` 失败时未执行 `os.Remove(tmpPath)`，导致半成品 `.part` 文件残留；
   - `storage.Manager` 单例缺少测试用状态重置函数 `ResetForTest()`，导致跨测试用例的缓存目录设置发生单例污染；
   - `app.go` 构造 `NewApp` 时将 GSI 端口硬编码为 4444，随后在异步 goroutine 中才读取 settings 覆盖，存在启动初期的竞态；
   - `system_service.go` 的 `SaveSettings` 使用简单的 `filepath.Clean` 比较路径，因 Windows 驱动器盘符大小写或末尾反斜杠差异，可能误判两目录不等而发生重复镜像写入；
   - `overlay_windows.go` 的 `DetachWindowOwner` 在每次 500ms 置顶刷新器循环中都会无条件调用 Windows API `SetWindowLongPtrW`，未事先判断是否已有属主；
   - `services_test.go` 中 `TestGsiServicePortConfiguration` 缺少初始停止和退出恢复，具有测试副作用；
   - `app.go` `ServiceStartup` 中的 goroutine 缺少 panic recover 保护。

## 决策方案

1. **加锁与路径解析原子化保护**：
   - 将 `melo_engine.go` 中的路径探测升级为锁内安全函数 `resolvePathsLocked()`；
   - 在 `Synthesize`、`IsReady`、`Status` 和 `EnsureReadyInBackground` 中，确保对 `pathsReady` 的读取与路径探测全流程处于 `m.mu` 保护之下，彻底消除 data race；
   - 在 `Synthesize` 开始处增加 `ctx.Done()` 提前取消拦截。

2. **显式调用 `m.engine.Destroy()` 释放 C 侧资源**：
   - 在 `Close()` 与 `RefreshPaths()` 中，在解绑前显式调用 `m.engine.Destroy()`，确保底层 ONNX Runtime 会话与内存安全释放。

3. **悬浮窗销毁意图确定性维护**：
   - 在 `OverlayService` 中引入 `pendingDestroyWindows map[string]bool`，以窗口名称为 Key 记录显式销毁意图；
   - `WindowClosing` 回调在触发时检查该 map，若命中则立即放行销毁并自清理，彻底消除对易挥发的 `defer s.allowClose` 的时序依赖。

4. **系统级鲁棒性加固**：
   - `downloadFile` 捕获 rename 错误，确保失败时彻底移除 `.part` 临时文件；
   - `storage.Manager` 暴露 `ResetForTest()` 接口；
   - `NewApp` 在构造之初同步解析 settings 端口传入 `GetGsiService(port)`；
   - `system_service.go` 对主目录与镜像目录统一使用 `filepath.Abs` 并在 Windows 下使用 `strings.EqualFold` 进行无歧义判等；
   - `DetachWindowOwner` 仅在 `currentOwner != 0` 时调用 `SetWindowLongPtrW`；
   - `services_test.go` 引入测试重置与 defer 还原；
   - `app.go` 启动 goroutine 注入 `defer recover()`。

## 曾考虑的替代方案

1. **为 `pathsReady` 引入单独的 `pathsMu sync.RWMutex`**：
   - 最强理由：读写锁分离可以在只读时减少与引擎推理锁 `m.mu` 的冲突。
   - 为什么不用：MeloTTS 内部的 ONNX 会话本就不支持并发推理（必须串行排队调用），且路径探测通常只在首次加载时触发一次，因此复用主互斥锁 `m.mu` 即可兼顾极高的执行效率与更低的死锁风险，避免过度的锁复杂度。

2. **在 `CloseOverlay` 中直接销毁底层子进程而不通知窗口**：
   - 最强理由：暴力 Kill 进程绝对不会有回调拦截的问题。
   - 为什么不用：悬浮窗可能是同一进程内的 WebviewWindow 窗口实例，暴力杀进程会导致主程序意外退出；通过 `pendingDestroyWindows` 状态机放行是优雅销毁窗口的标准解法。

## 后果与验证计划

- **收益**：
  - `go test -race` 彻底消除并发数据竞争；
  - ONNX Runtime 物理会话在应用退出或重载时 100% 安全释放，无内存泄漏；
  - 独立悬浮窗关闭确定性 100%，杜绝关闭后仍驻留后台或无法隐藏的异常；
  - 启动阶段 GSI 端口无竞态，测试用例互相隔离零污染。
- **验证方法**：
  - 运行 `go test ./...` 验证单测与编译通过；
  - 运行 `node scripts/verify-agent-notes.cjs` 确保决策笔记合规；
  - 执行 `pnpm --filter frontend build` 确保前端构建不受影响。
