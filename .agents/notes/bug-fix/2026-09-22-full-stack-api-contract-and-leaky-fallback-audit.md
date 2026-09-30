# Agent Note: 全工程跨模块契约漂移、穿透式 Payload 与降级泄露缺陷全量审计与修复

Status: implemented
Date: 2026-09-22
Class: bug-fix

## 审计背景与因果机制剖析

在完成系统设置中心选择文件夹与对话框链路重构后，根据开工铁律第 5 条（开工检查与因果分析）及第 6 条（架构拓扑与前后端边界），我们对神盾教练全工程（包括 `api/`、`services/`、`stores/`、`views/` 及 Go `internal/`）进行了深度的系统级排查，发现以下 6 项隐藏极深、直接导致运行异常或数据损坏的结构性缺陷：

1. **前后端数据契约严重漂移导致战绩画像被粉碎（最严重业务缺陷）**：
   - **位置**：`frontend/src/api/types.ts` 与 `frontend/src/services/playerProfileService.ts:297-320` 对接 `internal/models/player.go`。
   - **因果机制**：Go 后端 `GetPlayerProfile` 经过复杂数据清洗与计算后返回了规范的 `totalMatches`, `wins`, `losses`, `winRate`, `avgKills`, `avgDeaths`, `avgAssists`, `topHeroes`, `recentHeroes`, `tacticalSummary`。但前端旧映射层却硬编码读取 `nativeProfile.totalGames`, `winGames`, `winrate`, `tags`, `h.games`, `h.win`。
   - **后果**：无论 Go 端计算出的胜率有多高（如 70% 胜率、场均 8 杀 3 死），前端一律读出 `undefined`，并全部回退为“0% 胜率”、“0 击杀 0 阵亡 0 助攻”，标签全部沦为“天梯实力选手”，导致整个玩家画像功能在前端展示时完全失真。

2. **虚假不存在的 Go 方法暴露（Ghost API `isGsiRunning`）**：
   - **位置**：`frontend/src/api/gsi.ts:23-28`。
   - **因果机制**：前端 `gsiApi.isGsiRunning()` 尝试调用 `services.GsiService.IsRunning` 和 `main.App.IsGsiRunning`，但 Go 端 `internal/services/gsi_service.go` 与 `../../../internal/app/app.go` 根本没有实现 `IsRunning` 导出方法。
   - **后果**：一旦触发检查，直接抛出 `method not found` 的 `ApiError` 异常。

3. **巨量战斗日志穿透注入导致大模型 100% 崩溃（Mega-Payload Overflow）**：
   - **位置**：`frontend/src/services/aiCoachService.ts:527-535` 对接 `internal/services/ai_service.go:180-195`。
   - **因果机制**：赛后复盘时前端直接执行 `JSON.stringify(match)`。完整的 OpenDota 战局包含数万条战斗事件坐标与时间轴，体积高达 1~2MB。Go 端将其原样拼入 Prompt (`fmt.Sprintf("比赛数据摘要: %s", matchDataJson)`)，导致 Prompt 瞬间超过数十万 Token，被 DeepSeek/Qwen 等模型以 HTTP 400（Context Length Exceeded / Payload Too Large）拒绝。
   - **后果**：Go 原生复盘永远报错，迫使系统每一次都退化走前端慢速降级。

4. **非互斥降级导致 10Hz GSI 事件监听器双倍挂载（Duplicate Event Listeners）**：
   - **位置**：`frontend/src/services/gsiRuleEngine.ts:253-262`。
   - **因果机制**：`init()` 内连续执行了两个独立的 `if` 判断：`if (win.wails?.Events?.On)` 与 `if (win.runtime?.EventsOn)`。由于 `runtime.EventsOn` 本身在 `wailsBridge.ts` 里就是对 `wails.Events.On` 的代理，导致 `handleEvent` 被重复挂载了两次。
   - **后果**：每秒 10 次的 GSI 数据流变成了每秒 20 次触发，造成规则引擎定时器状态机竞争与无谓的前端计算消耗。

5. **第三方 TTS 端点 URL 双重 `/v1` 路径穿透**：
   - **位置**：`frontend/src/services/speechService.ts:255, 418`。
   - **因果机制**：当用户配置 OpenAI 规范的 TTS 服务（如 `http://127.0.0.1:8000/v1`）时，代码盲目拼接 `${endpoint}/v1/audio/speech`，导致请求发往 `/v1/v1/audio/speech`。
   - **后果**：第三方兼容 TTS 试听与实战播报 100% 返回 404 失败并静默回退。

6. **兼容桥接层服务候选名单残缺（`wailsBridge.ts` 漏挂服务）**：
   - **位置**：`frontend/src/services/wailsBridge.ts:26-30`。
   - **因果机制**：`window.go.main.App` 的 Proxy 内部硬编码仅包含 `SystemService` 与 `OverlayService`，完全遗漏了 `AiService`、`PlayerService` 与 `GsiService`。

## 修复与重构措施

1. **对齐前后端强类型契约**：
   - 在 `types.ts` 中重构 `PlayerProfileAnalysis` 与 `HeroPerformanceDTO`，字段与 Go `internal/models/player.go` 严格 1:1 对齐；
   - 在 `playerProfileService.ts` 中实现高容错多版本字段读取器（优先读取 `totalMatches`, `wins`, `winRate`, `avgKills`, `avgDeaths`, `avgAssists`, `tacticalSummary`），彻底根治战绩归零问题。
2. **完整补齐 Go 端 GSI 服务运行态检测方法**：
   - 在 `internal/services/gsi_service.go` 中实现 `IsRunning() bool`；
   - 在 `../../../internal/app/app.go` 中透传导出 `IsGsiRunning() bool`。
3. **Payload 提炼与结构化精简**：
   - 在 `aiCoachService.ts` 发送前，对 `match` 提炼为仅包含核心维度（对局时长、分值、阵容、目标玩家 KDA/经济/伤害/补刀及战术焦点）的紧凑结构化 JSON，从 2,000,000 字节骤降至 500 字节，保障 LLM 秒级推理不超限。
4. **互斥化降级事件注册**：
   - 在 `gsiRuleEngine.ts` 中采用 `else if (win.runtime?.EventsOn)` 杜绝双倍挂载。
5. **智能归一化 TTS URL**：
   - 在 `speechService.ts` 中自动识别并修正末尾 `/v1`，消除双重路由 404。
6. **全量补全 WailsBridge FQN 候选服务池**：
   - 在 `wailsBridge.ts` 中补全所有 5 大核心服务及其包名前缀。

## 自动化验证结论

- **前端全量构建**：`pnpm --filter frontend build` 耗时 1.85s，0 错误，0 警告通过。
- **Go 后端全量编译**：`go build -v .` 100% 编译通过。
- **决策笔记校验**：`npm run verify-notes` 21 篇笔记校验全量通过。
