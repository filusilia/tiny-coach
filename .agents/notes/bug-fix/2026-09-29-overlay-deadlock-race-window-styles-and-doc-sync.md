# Agent Note: 悬浮窗重入死锁、数据竞态、锁粒度优化与全仓架构文档版本同步

Status: implemented
Date: 2026-09-29
Class: bug-fix

## 问题背景与因果机制分析

1. **OverlayService.attachWindowHooks 致命重入死锁**：
   - **因果机制**：`SetWindow(w)` 在入口处申请互斥写锁 `s.mu.Lock()`。当处于悬浮窗模式且初次关联窗口时（`s.isOverlay && s.overlayWindow == nil`），其内部调用了 `s.attachWindowHooks(w)`；而 `attachWindowHooks` 自身内部在第 131 行再次执行 `s.mu.Lock()`。
   - **后果**：Go 的 `sync.RWMutex` 为非可重入锁，同一 goroutine 发生锁自旋/死锁，导致程序在初始化阶段彻底卡死，事件循环 `app.Run()` 无法进入，窗口虽被绘制但无法响应任何系统事件，也无法被外部 `CloseOverlay` 销毁。此外，主程序在调用 `ToggleOverlayWindow()` 创建新悬浮窗时同样持有 `s.mu` 并调用 `attachWindowHooks`，引发同样的死锁。
   - **解决措施**：拆分内部方法 `attachWindowHooksLocked(win)`（明确要求调用方已持有 `s.mu`）与公开导出的 `attachWindowHooks(win)`（内部主动加锁并委托）。在 `SetWindow` 和持锁上下文的 `ToggleOverlayWindow` 中改用 `attachWindowHooksLocked`。

2. **CloseOverlay 中读写 s.overlayWindow 的未加锁数据竞态**：
   - **因果机制**：`CloseOverlay` 在第 402 行执行 `s.mu.Unlock()` 后，于无锁保护下直接读取 `if s.overlayWindow != nil`，随后在执行销毁动作后将 `s.overlayWindow = nil`。
   - **后果**：与其他并发协程（如 `SetIgnoreMouseEvents`、`getOverlayWindow`、`ToggleOverlayWindow`）产生读写数据竞态（Data Race），并可能引发重复调用 `Close()` 导致的异常崩溃。
   - **解决措施**：在锁内原子执行指针快照与置空（`win := s.overlayWindow; s.overlayWindow = nil`），释放锁后再安全执行 `win.Position()`、`s.savePosition()`、`win.Hide()` 与 `win.Close()`。

3. **ToggleOverlayWindow 持锁时间过长导致界面卡顿**：
   - **因果机制**：旧实现使用单个函数级 `defer s.mu.Unlock()` 包裹了完整的执行逻辑，包含了磁盘文件 IO（`s.LoadSavedPosition()`）、WebView 窗口创建（`appInst.Window.NewWithOptions`）、多项 Win32 API 跨进/线程调用（`DetachWindowOwner`、`EnsureOverlayExtendedStyle`、`ForceTopMost`）以及启动保活协程。
   - **后果**：持锁时间长达数十至数百毫秒，阻塞其他依赖 `s.mu` 的协程（包括高频触发的 `WindowClosing`、`WindowDidMove` 等事件回调），造成 UI 掉帧卡顿。
   - **解决措施**：锁内仅做状态判定与指针快照，窗口创建、WinAPI 与文件 IO 移至锁外执行，最后仅在更新 `s.overlayWindow` 与钩子注册时短时加锁。

4. **EnsureOverlayExtendedStyle 非穿透时移除了 WS_EX_LAYERED**：
   - **因果机制**：在非穿透分支中，掩码计算错误地将 `WS_EX_LAYERED` 放入了清空掩码 `&^` 中，且未保留 `| WS_EX_LAYERED`。
   - **后果**：Wails 透明窗口依赖 `WS_EX_LAYERED` 实现透明合成，一旦移除，悬浮窗在非鼠标穿透状态下背景会变黑或透明失效。
   - **解决措施**：在非穿透与穿透分支中均严格保留 `WS_EX_LAYERED`，仅对 `WS_EX_TRANSPARENT` 进行开启或剔除。

5. **GSI 初始化端口调用冗余**：
   - **因果机制**：`../../../cmd/aegis-coach/main.go` 中 `appLogic := NewApp(isOverlay)` 内部已根据持久化配置初始化了 GSI 服务端口单例。随后再次调用 `services.GetGsiService(4444)`，由于 `sync.Once` 已固定实例，该调用传参 4444 无效且极易误导维护者。
   - **解决措施**：改为 `services.GetGsiService(0)` 获取现有单例。

6. **项目全仓架构文档严重落后现状**：
   - **落后审计**：
     - 前端状态管理：旧文档记录为单一 `store/index.ts`，实际已演进为 Pinia 分模块多 Store（`stores/app.ts`, `stores/battle.ts`, `stores/player.ts` 等）。
     - 前端页面拓扑：旧文档记录为 `pages/`（`CoachPage.vue` 等），实际已重构为 `views/` 模块化视图（`CoachView.vue`, `AnalysisView.vue`, `HeroesView.vue`, `ItemsView.vue`, `SettingsView.vue`, `OverlayView.vue` 及 `views/analysis/`, `views/heroes/`, `views/settings/` 业务组件群）。
     - 样式引擎：旧文档记录为 `TailwindCSS v4`，实际工程已迁移为电竞暗黑专属 `UnoCSS`。
     - 语音合成：旧文档记录为微软云端 `Edge-TTS`，实际工程已升级为 Go 原生纯离线 `MeloTTS`（ONNX Runtime 本地模型推理）。
     - 后端架构：旧文档停留在单文件 `app.go`，实际已分层为 `../../../internal/app/app.go` 与 `internal/services/` 领域服务。
     - 补丁与版本：文档停留在 7.41e，实际已升级 7.41f 并确立零伪造与网络防劫持原则。
   - **更新行动**：全面同步更新 `README.md`、`docs/SYSTEM_LANDSCAPE.md`、`docs/SUBSYSTEM_RUNTIMES.md`、`docs/DEVELOPMENT_GUIDE.md`、`docs/BUSINESS_RULES_AND_UI_SPEC.md` 与 `docs/HERO_AND_ITEM_DATA_SPEC.md`。

## 被否决的备选方案论证

### 方案 A：在 attachWindowHooks 中通过 runtime.Caller 检测是否持有锁
- **最强理由**：对外接口保持原貌，无需拆分两个函数名。
- **为什么不采纳**：Go 标准库 `sync.RWMutex` 故意不提供任何“当前是否持锁”的查询接口，反射或调用栈检查极其脆弱且在并发下无法保证状态一致性。Go 官方推荐的最佳实践即为拆分带 `Locked` 后缀的内部受限方法。

### 方案 B：在 CloseOverlay 中直接使用同步 Lock() 覆盖整个窗口 Hide 与 Close 过程
- **最强理由**：实现简单直接，一行 `s.mu.Lock(); defer s.mu.Unlock()` 覆盖到底。
- **为什么不采纳**：`win.Close()` 与 `win.Hide()` 内部会向操作系统发送消息并同步分发窗口事件（如 `WindowClosing`）。而 `WindowClosing` 回调内部本身需要获取 `s.mu.Lock()`，在同一个持锁上下文中同步触发回调必定再度引发死锁！必须先置空指针、释放锁，再执行 UI 与操作系统窗口销毁。
