# Agent Note: 前端架构模块化拆分与对局数据单一源收拢

Status: implemented
Date: 2026-09-23
Class: architecture

## 问题背景与因果机制分析

在神盾教练技术栈迭代过程中，前端 6 个核心视图与数据流出现了以下系统性退化：

1. **对局战斗数据双源割裂（Dual-Source Desync）**：
   - `OverlayView.vue`（悬浮窗）消费 `battleStore.snapshot`（原始 GSI 树形结构），并在模板与计算属性中二次解析英雄英文名、血量等；
   - `CoachView.vue`（主界面教练 HUD）直接消费全局响应式单例 `activeBattleContext`（由 `gsiRuleEngine.ts` 解析后的标准实体）；
   - 悬浮窗未初始化 `gsiRuleEngine`，若直接读 `activeBattleContext` 会出现数据停止更新，而主窗口双重监听事件总线，两套响应式系统存在字段映射不一致（如原始英雄类名 vs 中文名）与更新时序偏差。

2. **巨型单文件组件（Monolithic SFC）膨胀严重**：
   - `AnalysisView.vue`（~1500 行）、`HeroesView.vue`（~1100 行）、`SettingsView.vue`（~1400 行）职责高度堆叠，搜索、渲染、SVG 计算、大模型调用全部混杂在单一文件中，多人协作必然冲突，维护成本极高；
   - 雷达图几何运算（5 圈静态多边形背景点）在每次响应式触发时均重复使用三角函数计算，缺乏常量级缓存；
   - 127 英雄图鉴详情切换缺乏路由联动（刷新即丢失当前英雄视角）。

3. **Pinia 状态流越权与类型安全弱化**：
   - `OverlayView.vue` 存在直接给 `appStore.isOverlayRunning = false` 赋值的行为，绕过了 Pinia Action 与单向数据流；
   - OpenDota 接口实体类型大量使用 `any`，缺乏编译器静态防呆；
   - Vue 模板中内联闭包（如 `@click="() => ..."` 与 `@error="(e) => handleImageFallback(...)"`）出现 40+ 次，增加不必要的重新渲染与模板冗余。

## 决策方案

### 1. 战斗数据流统一收拢至 `useBattleStore` 单一权威源
- 将实战上下文 `activeBattleContext` 与 `useBattleStore` 完全打通：`useBattleStore` 作为全应用唯一权威战况状态容器，`activeBattleContext` 保持为响应式代理；
- `useGsiStream` 与 `gsiRuleEngine` 形成单一处理流水线，悬浮窗与主界面组件统一从 `useBattleStore` 获取派生字段（英雄中文名、金钱、买活判定、KDA、GPM/XPM、技能/物品栏）；
- 消除任何基于 `snapshot` 的手工二次解析与命名偏差。

### 2. 状态流 action 规范与 OpenDota 强契约补全
- 在 `stores/app.ts` 明确提供 `setOverlayRunning(running: boolean)` action；
- 在 `api/types/player.ts` 完整定义 `OpenDotaMatchDetail`、`OpenDotaPlayer`、`OpenDotaPlayerProfile` 与 `ItemMeta` 接口契约，全面替换各视图中的 `any`。

### 3. 高复用公共组件提取
- **`RadarChart.vue`**：抽离为纯 SVG 展示组件，模块级常量缓存 5 阶背景网格多边形坐标（`RADAR_GRID_POINTS`），仅按需计算数据层顶点；
- **`SmartImage.vue`**：封装本地资源优先、CDN 平滑兜底与加载失败容灾逻辑，彻底消灭 40+ 处内联 `@error` 闭包。

### 4. 巨型视图模块化解耦
- **`AnalysisView.vue`**：拆分为布局骨架与 7 个原子组件（`MatchSearchBar`、`PlayerProfileBanner`、`TopHeroesPanel`、`RecentMatchList`、`MatchDetailPanel`、`AiReviewPanel`、`RadarChart`）及统计 Composable；
- **`HeroesView.vue`**：拆分为 `HeroStatsCard`、`AbilityShowcase`、`TalentTreePanel`、`HeroBuildPanel`，并支持 `/heroes/:id` 路由持久化与浏览器前进后退；
- **`SettingsView.vue`**：拆分为 7 个独立 Section（`VoiceBroadcastSection`、`AiEngineSection`、`TtsEngineSection`、`PlayerBindingSection`、`StorageSection`、`OverlaySection`、`AboutSection`）。

## 曾考虑的替代方案

1. **方案一：完全废弃 `battleStore`，全仓只使用全局纯 reactive 对象**
   - *最强理由*：代码改动量最小，无需在 Pinia store 和全局对象之间做映射或代理。
   - *为什么不用*：Pinia 是 Vue 3 生态标准状态管理库，具备 DevTools 时间旅行调试、SSR/窗口隔离以及清晰的 actions 边界。直接在全局维护 reactive 对象会丧失状态变更的可追溯性，违反现代前端工程化规范。

2. **方案二：将图标库反向替换回已废弃的 `lucide-vue-next`**
   - *最强理由*：部分旧文档和三方示例曾提及该包名。
   - *为什么不用*：根据官方仓库与本仓 2026-09-20 决策，`lucide-vue-next` 已被官方正式弃用并标注 deprecated，全量图标应且仅应使用官方统一包 `@lucide/vue`。保持 `@lucide/vue` 才是合规与长远之选。

## 后果与验证计划

- **正面收益**：
  - 核心视图代码行数由 ~1500 行急剧缩减至 ~100 行，模块职责单一，易于单测与并行维护；
  - 战斗数据统一，悬浮窗与主界面战况呈现 100% 同步，买活与英雄名永不漂移；
  - 静态网格常量缓存消除无谓计算，`SmartImage` 清理 40+ 处冗余闭包。
- **验证矩阵**：
  - 执行 `npm run verify-notes`：Agent 决策笔记校验通过；
  - 执行 `pnpm --dir frontend build`：Vite 8 全量编译构建成功（0 报错）；
  - 运行单元测试脚本，确保英雄展台、物品规格与业务规则无回归。
