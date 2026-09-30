# Agent Note: Lucide 图标库现代化迁移与全仓文档一致性清理优化

Status: implemented
Date: 2026-09-20
Class: architecture

## 问题背景与因果机制分析

在神盾教练技术栈向 **Wails v3 + Go + Vue 3** 演进的过程中，前端依赖与技术文档存在以下脱节与冗余：

1. **`lucide-vue-next` 废弃与多重依赖错位**：
   - `lucide-vue-next` 是 Lucide 图标库为 Vue 3 提供的早期官方库，上游维护者已正式将其标记为废弃（`deprecated: Package deprecated. Please use @lucide/vue instead.`），统一由 `@lucide/vue` 承载后续版本演化与图标更新；
   - 仓库内根目录 `package.json` 之前曾安装了 `@lucide/vue: ^1.47.0`，而 `frontend/package.json` 却依然保留旧包 `"lucide-vue-next": "^1.0.0"`，代码中共有 11 处 Vue 核心组件（包含 Header、Sidebar、教练 HUD、英雄与物品图鉴、设置页及三段式开关组件）均在引用已废弃的包；
   - 依赖混淆与废弃包的存在不仅会导致 pnpm 安装时抛出 deprecated 警告，还阻碍了后续图标特性扩展与打包体积优化。

2. **核心技术文档（`docs/` 及 `README.md`）存在历史陈旧遗留**：
   - 早期历史脚本（如 `tools/Sync-OfficialHeroes.ps1`、`tools/Normalize-LineEndings.ps1`）与旧配置 `wails.json` 已在架构纯净化中彻底删除，但部分文档（如 `docs/OFFICIAL_HERO_DATA.md`、`README.md`）仍保留对其调用说明；
   - 静态资源路径在迁移至 Vite + Vue 3 后已统一定位至 `frontend/public/assets/`，但部分架构图与说明仍指向根目录 `public/assets/`；
   - Go 后端与 Wails 事件通道的实际注册名已统一为 `"dota_gsi_update"`，但 `docs/SUBSYSTEM_RUNTIMES.md` 时序图中仍写作旧占位符 `"gsi-data"`；
   - 本地两级缓存根目录在 Go 后端已实现为用户文档目录下的 `DotaAICoach`（含 `/heroes/`、`/items/`、`/tts/`、`/rag/` 四大正交子目录并支持设置页原生动态拾取），与早期文档中的 `%APPDATA%/AegisCoach/cache/` 存在偏差。

## 决策方案

### 1. 全面迁移并统一至 `@lucide/vue`
- **依赖纯洁化**：从根目录 `package.json` 中移除前端专用的 `@lucide/vue`，在 `frontend/package.json` 中正式安装并锁定最新 `@lucide/vue`（`^1.47.0`），彻底清除 `lucide-vue-next`；
- **全量组件代码重构**：逐一重命名迁移 11 个前端组件（`TitleBar.vue`、`Sidebar.vue`、`CoachPage.vue`、`HeroesPage.vue`、`ItemsPage.vue`、`AnalysisPage.vue`、`SettingsPage.vue`、`OverlayWindow.vue`、`GsiSimulator.vue`、`ThreeSegmentToggle.vue`、`VoiceBroadcastBar.vue`）中的导入来源至 `'@lucide/vue'`；
- **零破坏性验证**：保留所有图标语义与样式（如 `Shield`, `Clock`, `Coins`, `Volume2`, `Settings` 等），经 Vite 编译构建验证 0 错误。

### 2. 全仓架构与业务文档一致性清理与对齐
- **`docs/OFFICIAL_HERO_DATA.md`**：删除失效的 `Sync-OfficialHeroes.ps1` 命令，明确 127 英雄权威数据以内置 `frontend/public/assets/data/heroes-details-official.json` 为准，审计与回归执行 `node scripts/generate-hero-text-audit.cjs`；更新资源路径与 `@lucide/vue` 图标选型；
- **`docs/SYSTEM_LANDSCAPE.md`**：修正 Mermaid 拓扑中的静态资产路径为 `frontend/public/assets/`；更新技术选型矩阵为 `@lucide/vue`；
- **`docs/SUBSYSTEM_RUNTIMES.md`**：修正时序图中的 Wails 事件名称为实际运行的 `"dota_gsi_update"`；
- **`docs/HERO_AND_ITEM_DATA_SPEC.md`**：更新本地缓存规范，精准对齐 Go 后端当前实现的 `[用户文档目录]/DotaAICoach` 根目录及 `/heroes/`、`/items/`、`/tts/`、`/rag/` 四大子目录，并同步目标补丁版本为 `7.41e`；
- **`docs/BUSINESS_RULES_AND_UI_SPEC.md`**：修正 `frontend/public/assets/` 静态资源同步描述；
- **`docs/DEVELOPMENT_GUIDE.md`**：更新日常调试命令，契合 Wails v3 + Taskfile 与一键打包发布流程；
- **`README.md`**：同步更新项目目录树结构，移除已废弃的 `wails.json` 与根目录 `public/`，清理已删除的换行符脚本，强调基于 Git 属性与自动化验证矩阵的代码准入。

## 曾考虑的替代方案

1. **方案一：继续保留 `lucide-vue-next` 别名包装层**
   - *最强理由*：无需批量触碰 11 个已完成的 Vue SFC 文件，代码改动行数最少。
   - *为什么不用*：`lucide-vue-next` 属于被官方正式废弃的包，在 pnpm 安装和 CI 构建时始终弹出 Deprecated 告警；且长期来看会导致项目脱节于 Lucide 官方后续补丁与新图标发布，治标不治本，违反代码纯洁性原则。

2. **方案二：文档保留历史演进脚本作为“参考脚本”**
   - *最强理由*：让新加入的协作者了解历史版本的数据抓取和处理逻辑。
   - *为什么不用*：已删除的脚本如果留在文档中，会导致开发者误以为脚本仍可执行，执行失败后造成认知混淆与排障浪费；历史演化应交由 Git 历史和 `.agents/notes/archive/` 沉淀，业务与开发指南必须只表达当前“真实有效”的事实。

## 后果与验证计划

- **正面收益**：
  - 前端包依赖干净无废弃警告，构建体积与打包流程进一步健壮；
  - 架构文档、开发指南与真实 Go / Vue 3 代码拓扑达成 100% 一致，杜绝开发者和 AI 被过时文档误导。
- **约束与代价**：
  - 各组件在引入新图标时必须统一遵循 `@lucide/vue` 命名空间。
- **验证矩阵**：
  - 执行 `pnpm build`：Vite 8 编译构建成功（0 报错）；
  - 执行 `node scripts/test-*.cjs`：全套业务规则与 UI 规格测试 100% 通过；
  - 执行 `npm run verify-notes`：Agent 决策笔记校验通过。
