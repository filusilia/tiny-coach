# Agent Note: 官网 AbilityRight 英雄技能详情与 Liquipedia Spellcard 物品浮窗视觉重构

Status: implemented
Date: 2026-09-28
Class: architecture

## 问题背景与因果机制分析

在神盾教练对局辅助与英雄/物品资料库中，用户引入了三个新官方资源：
- `cooldown_icon.png` (冷却时间图标)
- `manacost_icon.png` (魔法消耗图标)
- `talents_icon.svg` (天赋树图标)

并针对英雄技能详情与物品浮窗提出了具体且高标准的对齐要求：
1. **英雄技能详情**：模仿 Dota 2 官网 `<div class="AbilityRight">` 结构进行视觉重构：
   - 包含 `AbilityMain`（大图、技能名称、技能描述）、`GenericValues`（双列结构：技能类型、影响目标、伤害类型、无视减益免疫、可以驱散）、`SpecificValues`（各等级数值属性矩阵）、`BottomValues`（带新冷却与蓝耗图标的底部条）、`Lore`（背景传说引言）；
   - 天赋树入口替换为用户新增的 `talents_icon.svg`，保持纯中文“英雄天赋树”与原有切换状态；
   - 严格保留现有的神杖/魔晶升级卡片、技能实战要领 Tips、官方机制注解 Notes 与版本补丁更新 Patch Notes，只做增量优化与结构升级，严禁数据简化。
2. **物品浮窗卡片**：参考 Liquipedia `<div class="spellcard wiki-bordercolor-light">` 样式进行重构：
   - 将技能描述中的各个技能效果升级为 Liquipedia 电竞 Wiki 风格的技能卡片头部（带暗色高质感渐变背景、左侧微标签与阴影标题）；
   - 浮窗底部的冷却与蓝耗使用新传入的 `cooldown_icon.png` 与 `manacost_icon.png`，采用类似 Liquipedia 的底栏结构；
   - 严格保留全部基础属性加成（2 列药丸）、前置配件清单（带图纸与价格）、进阶合成去向、官网机制提示 notes 与版本更新日志。

## 决策方案

### 1. `AbilityShowcase.vue`：官网 `AbilityRight` 深度对齐
- **天赋树按钮**：使用 `/assets/icons/talents_icon.svg` 替换原 Lucide `<Shield>` 图标，并保留 `title="英雄天赋树"`。
- **AbilityMain 区块**：
  - 左侧展示 80x80 大图，叠加先天、神杖、魔晶遮罩；
  - 右侧展示技能名、先天/被动微标签以及宏变量替换后的完整描述文本。
- **GenericValues 矩阵（双列）**：
  - 左列：技能（点目标/单位目标/无目标/被动等）、影响（友方英雄/敌方单位等，通过 `target_team`/`target_type` 解析）、伤害类型（物理红、魔法蓝、纯粹金）；
  - 右列：无视减益免疫（是/否）、可以驱散（是/否/仅强驱散）。
- **SpecificValues 等级数值列表**：
  - 采用官网两列流式卡片，左侧展示属性标题，右侧突出展示各等级数值。
- **BottomValues 底部容器**：
  - 分别采用 `CooldownContainer` 与 `ManaContainer`，渲染新冷却与蓝耗图标，并列展示各等级冷却秒数与蓝耗。
- **Lore 背景引言与高级扩展**：
  - 保留并美化背景故事 Lore，完整保留神杖与魔晶效果卡片、实战要点 Tips、机制注解 Notes 及版本补丁 Patch Notes。

### 2. `ItemsView.vue`：Liquipedia Spellcard 质感升级与图标替换
- **技能卡片 Liquipedia 化**：
  - 将 `parseItemSkills` 产出的每一个技能块设计为类似 Liquipedia `spellcard` 样式：
  - 具有暗色渐变头部（主动为暗红/暗金渐变，被动为暗翠绿渐变，开关为暗青渐变），带文字阴影的技能标题与微标签；
  - 描述正文排版清晰紧凑。
- **冷却与蓝耗图标全面替换**：
  - 替换原 Lucide 图标为 `/assets/icons/cooldown_icon.png` 与 `/assets/icons/manacost_icon.png`，结合黑色磨砂底块与金/蓝文字。
- **全部现有业务事实 100% 保持**：
  - 基础属性 2 列药丸、前置配件清单与进阶反查、官网机制提示 notes、版本补丁日志完整保留。

## 曾考虑的替代方案

1. **方案一：完全丢弃现有神杖、魔晶卡片和实战 Tips，仅机械保留官网示例的几行 HTML**
   - *最强理由*：代码量最少，结构最简单。
   - *为什么不用*：直接违背用户“注意不能丢失现在的技能技巧 tip 等信息，优化添加，而不是简化”以及开工铁律第 11 条（业务事实与核心 UI 防退化），神杖、魔晶与教练实战技巧是核心竞争力。

2. **方案二：继续使用 Lucide 矢量图标替代用户新增的 png/svg 图标**
   - *最强理由*：无需依赖本地静态资源。
   - *为什么不用*：用户已明确指出“在项目中增加了 cooldown 冷却时间图标，以及 manacost 蓝量图标，talents_icon 天赋树图标”，必须精准消费指定资源路径，确保还原官方原生与 Liquipedia 真实视觉。

## 后果与验证计划

- **正面收益**：
  - 英雄技能呈现完全对齐 Dota 2 官网 `AbilityRight` 交互与排版；
  - 物品浮窗兼具官网严谨数据与 Liquipedia 优质电竞审美；
  - 冷却、蓝耗、天赋树官方图标就位，质感大幅提升。
- **验证矩阵**：
  - 运行 `npm run verify-notes` 确保笔记合规；
  - 运行 `node scripts/test-hero-ui-redesign.cjs` 确保展台、遮罩、段位契约；
  - 运行 `node scripts/test-hero-and-item-spec.cjs` 确保多层分组与零模拟数据契约；
  - 运行 `npx vue-tsc --noEmit` 与 `npx vite build` 确保前端 0 错误编译通过；
  - 运行 `pwsh -NoProfile -File ./tools/package-release.ps1` 确保打包通过。
