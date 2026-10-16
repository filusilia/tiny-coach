# Agent Note: 坚决拒绝离线假数据兜底与真实纯净数据流保障

Status: implemented
Date: 2026-10-10
Class: architecture

## 问题背景与因果机制分析

1. **“所有英雄出装千篇一律”的致命根因（因果链）**：
   - 此前开发中为了应对“断网”或“无网络请求”场景，在后端 `internal/services/player/recommendation.go` 中引入了 `fillOfflineRuleRecommendations` 与 `fillDefaultItemStages`。
   - 当 OpenDota API 请求超时、未连接互联网或 API 响应异常时，代码为了所谓的“界面不为空白”，直接填充了一套硬编码的假出装（假腿、魔杖、系带、狂战斧、分身斧、BKB、大晕锤、蝴蝶、撒旦）。
   - 同时，前端 `frontend/src/data/heroGuides.ts` 也手写了一套静态兜底出装（`builds`）。
   - 结果导致用户在未连入外网或接口排队时，切换任何英雄看到的出装完全一模一样，引发用户严重误解和合理抗议（“拒绝任何离线兜底 检查并汇报”）。
2. **违反神盾教练开工铁律第 8 条（零伪造原则）**：
   - AGENTS.md 明确规定：严禁伪造模拟数据（零伪造原则）。任何数据必须实事求是。
   - 数据存在就是存在，不存在就是不存在。网络失败就是网络失败，不能用人工手写虚构的假出装去掩盖真实的异常，剥夺用户的真实知情权。

---

## 决策方案

### 1. 后端彻底拔除所有离线伪造假出装
- 在 [recommendation.go](file:///D:/develop/demo/internal/services/player/recommendation.go) 中：
  - 彻底删除 `fillOfflineRuleRecommendations`、`fillDefaultItemStages`；
  - 彻底删除针对性出装中 `if len(counterMap) == 0` 硬塞 BKB 与跳刀的保底逻辑，若无针对性威胁英雄则保持空数组；
  - 严格真实契约：优先读取本地已落盘的真实历史缓存（若存在）；若无真实历史缓存且远程 OpenDota 接口请求失败，直接返回明确的系统错误 `fmt.Errorf("无法连接 OpenDota 大数据接口且无历史真实缓存: %v", popErr)`，绝不返回包含假出装的伪造数据包；
  - `Source` 字段仅允许 `"opendota"`（实时在线）或 `"cache"`（真实历史缓存），彻底废除 `"offline_rule"`。

### 2. 前端知识库剥离手写出装字段
- 在 [heroGuides.ts](file:///D:/develop/demo/frontend/src/data/heroGuides.ts) 中：
  - 彻底删除 `COMMON_ITEMS` 假出装常量表与所有英雄的硬编码 `builds` 字段；
  - 本模块纯粹保留真实的英雄核心机制与战略打法微操技巧（`tips`），不再参与任何出装生成。

### 3. 前端界面确立三态真实状态机（加载中 / 真实数据 / 真实空态）
- 在 [HeroBuildPanel.vue](file:///D:/develop/demo/frontend/src/views/heroes/components/HeroBuildPanel.vue) 与 [HeroesView.vue](file:///D:/develop/demo/frontend/src/views/HeroesView.vue) 中：
  - 加载态（`loading`）：展示微光加载动画与“正在拉取 OpenDota 实时大数据出装...”；
  - 真实数据态（`builds.length > 0`）：展示 OpenDota 真实各阶段玩家出装，并带数据源认证徽章（`OpenDota 大数据` 或 `真实历史缓存`）；
  - 真实空态或异常态（`!loading && (!builds || builds.length === 0)`）：展示明确的虚线空态守卫，诚实告知用户“网络未连接或 OpenDota 大数据源暂不可用，神盾教练严格恪守零伪造原则，不提供任何离线虚假出装”，并提供【重新拉取】按钮。
- 在 [MatchupRecommendationPanel.vue](file:///D:/develop/demo/frontend/src/views/coach/MatchupRecommendationPanel.vue) 中同步清除“离线规则兜底”伪造徽章，并建立相同的空态保护。

---

## 曾考虑的替代方案

### 方案 A：按敏捷/力量/智力/全才属性分门别类手写 4 套离线差异化兜底出装
- **最强理由**：实现极其简单且没有网络时界面总能展示出看起来很合理的装备，能掩盖网络抖动问题。
- **为何坚决不用**：这是典型的“伪造数据掩耳盗铃”。Dota 2 每个英雄的核心战术打法千差万别（例如同为力量英雄的屠夫与哈斯卡出装南辕北辙，同为敏捷英雄的敌法师与毒龙路线截然不同）。任何手写模板不仅无法代表高分段天梯现行版本真实胜率，更是对用户智商的侮辱。一旦发生断网，用户误以为是最新大数据，将严重误导实战决策。零伪造原则必须是铁律，实事求是呈现空态才是对用户最大的尊重。

---

## 后果与验证计划

### 正面收益
1. **数据 100% 真实纯净**：用户看到的每一件推荐装备、每一个热度数值，均 100% 源自 OpenDota 高分段真实比赛统计；
2. **状态透明**：断网或 API 异常时，界面诚实展示空态守卫并允许用户一键重试，杜绝任何假象；
3. **架构极简**：剔除了 150+ 行后端伪造逻辑与 200+ 行前端假出装字典，代码维护性大幅提升。

### 代价与约束
- 在完全离线、没有网络连接且本地未缓存过该英雄出装时，出装面板将呈现真实的空状态守卫，不会展示假出装。

### 验证方法
1. `go test -v ./internal/services/player`：验证 `TestParsePopularityStage` 与 `TestGenerateCounterItemsRealLogics` 均 PASS；
2. `pnpm run typecheck` 与 `pnpm lint`：验证全仓 TypeScript 0 error，ESLint 0 warning；
3. `npm run verify-notes`：验证全量 Agent 决策笔记索引与校验通过。
