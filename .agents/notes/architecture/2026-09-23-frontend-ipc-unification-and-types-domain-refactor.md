# Agent Note: 前端 IPC 统一调用收敛、契约按域拆分与历史兼容清理

Status: implemented
Date: 2026-09-23
Class: architecture

## 背景与问题因果机制剖析

在针对前端架构与通信链路的深度审查中，识别出以下关键因果问题：

1. **IPC 调用层三套体系并存**：
   - `api/client.ts` 内部使用 `expandMethodCandidates` 进行 FQN 遍历寻址；
   - `services/wailsBridge.ts` 用 Proxy 伪装 Wails v2 的 `window.go.main.App`，导致全局存在早已被 Wails v3 废弃的虚拟命名空间；
   - 各业务 API 文件（`ai.ts`, `gsi.ts`, `overlay.ts`, `player.ts`, `system.ts`）充斥 30+ 处重复的 `try { AppBindings.XXX } catch { invokeWails(...) }` 样板代码，不仅代码冗余，且对取消异常（`isCancelError`）和方法缺失（`isMethodMissing`）的处理边界模糊。

2. **数据契约层膨胀与历史兼容包袱**：
   - `api/types.ts` 单文件承载全部业务 DTO，包含 8 个以上已废弃的历史兼容器字段（如 `HeroPerformanceDTO` 中的 `games`, `win`, `winrate`；`PlayerProfileAnalysis` 中的 `personaname`, `avatarUrl`, `winGames` 等），与 Go 后端结构体（`internal/models/`）脱节，诱导前端多处书写 `?? h.games`、`?? h.win` 冗余降级分支；
   - `aiCoachService.ts` 中的 `TacticalContext` 与 `BattleContextDTO` 重复定义 12 个相同字段。

3. **数据同步职责交叉**：
   - `dataSyncService.ts` 与 `gameDataService.ts` 职责边界模糊，`dataSyncService` 内联直接 `fetch` OpenDota 英雄接口，未走 `gameDataService` 的全局缓存，带来双重请求与潜在数据不一致。

## 被放弃的方案与权衡分析

### 方案 A：直接全部依赖裸 `AppBindings`，彻底删除降级寻址逻辑
- **最强理由**：实现极其简洁，严格遵循“Wails 代码生成器为唯一调用通道”，0 冗余代码。
- **放弃原因**：在 Windows 多窗口（如独立悬浮窗子进程）或调试预览热更新场景下，部分方法若因构建环境轻微差异导致 AST 生成与运行时反射略有偏差，缺少保底会导致进程抛错甚至白屏。采用 `callGo` 包装器，优先直调强类型方法，**仅在精确判定为方法未导出（`isMethodMissing`）时**才进行 FQN 候选逐级降级，同时若检测为业务异常或用户取消则坚决外抛，兼顾了类型安全与运行韧性。

### 方案 B：一次性对全局大文件进行大手术级零碎拆分
- **最强理由**：单次任务追求极高指标，将所有 500+ 行文件全部分拆。
- **放弃原因**：大重构涉及全局状态机（`activeBattleContext`）与 8 条战术判定规则的跨文件生命周期闭环，一次性修改风险面过大，极易引发响应式丢失或事件监听泄露。根据工程质量优先级，坚决先落地影响面大、收益明确的 P0 级 IPC 调用层统一、数据契约域拆分与数据源收敛。

## 实施的技术方案与架构重构

1. **收敛与标准化 IPC 核心通信层**：
   - 新建 `frontend/src/api/errors.ts`，导出 `ApiError`、`isCancelError`（判定取消语义）与 `isMethodMissing`（判定方法名未绑定）；
   - 重构 `frontend/src/api/client.ts`，提供统一入口 `callGo<T>(bindingFn, fallbackCandidates?, ...args)`，彻底移除 `window.go` 机制；
   - 重构 `api/ai.ts`、`api/gsi.ts`、`api/overlay.ts`、`api/player.ts`、`api/system.ts`，全部改用 `callGo` 优雅单行或紧凑结构，业务 API 样板代码精简 60% 以上；
   - 彻底删除 `frontend/src/services/wailsBridge.ts`，并在 `frontend/src/main.ts` 中移除初始化注入，清除 Wails v2 历史残余。

2. **契约模型按业务域拆分与去伪存真**：
   - 建立 `frontend/src/api/types/` 目录，按业务域拆分为 `system.ts`、`player.ts`、`ai.ts`、`gsi.ts`、`battle.ts`，由 `types/index.ts` 统一重导出；
   - 保留 `api/types.ts` 作为统一导出入口，确保平滑向后兼容；
   - 剔除 `HeroPerformanceDTO` 和 `PlayerProfileAnalysis` 中的废弃历史兼容字段，与后端 Go 结构体对齐；同步清理 `playerProfileService.ts` 中的 `?? h.games` 等兼容分支；
   - 将 `BattleContextDTO.clockTime` 设为 `number | string`，在 `aiCoachService.ts` 中消除重复的 `TacticalContext` 接口，统一使用 `BattleContextDTO`。

3. **数据服务单一数据源收敛**：
   - 改造 `dataSyncService.checkForUpdates`，将其内部内联的 `fetch('https://api.opendota.com/api/heroes')` 切换为调用 `gameDataService.getHeroStats()`，复用本地 JSON 缓存与集中调度机制。

## 验证结论

- **决策笔记格式校验**：执行 `npm run verify-notes`，全量 34 篇决策笔记结构与格式校验通过。
- **前端全量构建**：执行 `pnpm --filter frontend build`，Vite 编译无任何类型错误或警告，产物体积健康。
- **打包流水线验证**：执行一键打包验证，保证前后端桥接与构建标签完整无损。
