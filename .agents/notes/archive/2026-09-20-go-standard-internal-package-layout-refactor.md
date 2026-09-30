# Agent Note: Go 原生业务模块重构为标准 internal 分层架构

Status: archived
Date: 2026-09-20
Class: architecture
Superseded by: .agents/notes/architecture/2026-09-30-refactor-consolidation-build-integrity-and-typecheck-gate.md

> **归档说明（2026-09-30）**：本篇的包拆分方向（`internal/` 领域子包）仍然成立并被后续继承；
> 但其「入口必须留在仓库根目录」的核心论证已被现方案取代——入口现位于 `cmd/aegis-coach/main.go`，
> 并通过把 embed 声明下沉到资源所在目录（`frontend/assets.go`、`build/assets.go`）绕开了本篇
> §曾考虑的替代方案 1 所述的 `//go:embed` 禁止 `..` 约束。原论证的前提已解除，故归档并标明取代者。

## 问题背景与因果机制分析

在完成核心 AI 调度与 OpenDota 战绩网关下沉至 Go 语言原生实现后，所有 Go 业务逻辑与数据结构均直接堆叠在项目根目录（`main.go`、`app.go`、`ai_service.go`、`player_service.go`、`overlay_windows.go`）。随着业务能力的持续扩展，引发了如下架构组织缺陷：
1. **代码包权责混杂**：所有结构体、全局变量与业务方法均隶属于 `package main`，缺乏包级命名空间隔离，内部依赖与生命周期边界模糊；
2. **违背 Go 标准项目布局（Standard Go Project Layout）**：在成熟的电竞助手与客户端软件（如 BrightGir/dota-ai-coach）中，非入口的业务逻辑、数据流解析、硬件与外部 API 适配器应当收敛于 `internal/` 保护目录下，禁止外部包非法越权引用；
3. **Wails v3 桥接与编译器 embed 约束**：Wails v3 的前端自动绑定依赖于 `App` 导出的服务方法，且 Go 1.16+ `//go:embed` 严禁跨越上级目录（禁止 `..` 相对路径）。若生硬将应用入口整体移入深度嵌套子目录，将直接破坏前端资源嵌入与现有打包流程。

## 决策方案

确立**“标准 internal 领域子包 + 根目录 Wails 门面中枢 (Facade Coordinator)”**的现代化架构：

1. **`internal/dota` (GSI 游戏状态与核心常数)**：
   - 收敛 127 英雄权威标准中文常数映射表（落实开工铁律第 12 条）；
   - 封装 Dota 2 GSI 本地 HTTP 4444 端口单消费者接收器与内存最新快照存储。

2. **`internal/storage` (持久化与路径管理)**：
   - 封装 Windows 注册表用户真实文档物理路径解析（规避个人文档盘符重定向问题）；
   - 统一管理 `heroes`、`items`、`matches`、`tts`、`rag` 缓存子目录创建与清理。

3. **`internal/overlay` (Windows 原生窗口样式与置顶保活)**：
   - 隔离 Win32 API 跨平台实现，封装 `ForceTopMost`、`EnsureOverlayExtendedStyle`、`StartTopMostRefresher` 等窗口保活逻辑。

4. **`internal/ai` (端侧与云端 AI 战术推理中枢)**：
   - 封装统一 HTTP 长连接池、超时熔断与 Solution Hierarchy 战术决策优先级；
   - 提供即时战术问答、赛后 AI 深度复盘（对齐四大核心维度）、毫秒级端点网络探测与云端/本地可用模型原生拉取。

5. **`internal/player` (玩家战绩与 OpenDota 代理网关)**：
   - 封装高鲁棒性 Dota 2 玩家 ID 提取器（支持全角、Emoji、Steam3、Steam64、URL 与昵称模糊搜索）；
   - 提供 10 分钟 TTL 内存缓存与单场不可变历史比赛本地磁盘持久化，杜绝 429 频次封禁。

6. **根目录协调门面 (`app.go` + `../../../cmd/aegis-coach/main.go`)**：
   - `App` 结构体持有 `internal/` 各子系统实例，作为 Wails v3 IPC 的统一门面中枢，保持前端调用契约（`window.go.main.App.*`）零变动、零回归风险；
   - 保留根目录 `assets embed.FS` 与 `trayIconData []byte`，100% 契合 Go embed 规范并保证一键打包脚本无缝运行。

## 曾考虑的替代方案

### 1. 将所有入口与代码直接移至 `cmd/game-helper/`
- **最强理由**：完全复刻开源项目的目录外观。
- **为何弃用**：Go `//go:embed` 严禁使用 `..` 引用上级目录，移至深度子目录后无法直接嵌入根目录的 `frontend/dist`；同时会改变现有 `package-release.ps1` 的构建上下文，带来严重的破坏性风险。

### 2. 继续维持根目录下平铺单包模式
- **最强理由**：无需做包重组，改动最小。
- **为何弃用**：随着 RAG 向量知识库与更多战术规则落地，根目录将迅速膨胀为数千行大杂烩，严重降低可维护性与测试隔离度。

## 后果与验证计划

- **收益**：包职责单一清晰，无死锁与循环引用风险，前端 IPC 契约保持完全兼容，一键发布打包无缝执行。
- **验证**：运行 `npm run verify-notes`、`go vet ./...`、`pnpm build`、`node scripts/*.cjs` 与 `pnpm run package` 全流程自动化校验。
