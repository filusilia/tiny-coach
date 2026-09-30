# Agent Note: 英雄技能 HTML 标签清理、Tip 占位符解析、神杖标识纠错与出装大图标排布优化

Status: implemented
Date: 2026-09-28
Class: bug-fix

## 问题背景与因果机制分析

在全量检查英雄信息（以食人魔魔法师「蓝胖」为例）以及出装推荐模块时，发现了以下四处关键缺陷：

1. **蓝胖及其他英雄技能信息出现 HTML 标签（如 `<font color='#2dd5e4'>眩晕</font>时间：`）**：
   - **因果链路**：Valve 官方技能数据源中，`special_values[].heading_loc`（各等级数值属性名）含有大量未脱壳的富文本标签（全英雄共 254 处含有 `<font>` 标签）。在 `getAbilityLevelValues`（`src/utils/abilityMeta.ts`）中仅去除了末尾冒号而未调用 `cleanInlineText`，并在 `AbilityShowcase.vue` 中作为文本插值 `{{ v.label }}：` 直接输出，导致 Vue 把它当作字面文本渲染，直接在界面裸露出 `<font ...>`。
2. **技能 Tip / 机制注解出现替换符（如 `%multicast_delay%` 未被数值替换）**：
   - **因果链路**：在 `AbilityShowcase.vue` 的官方机制注解（`currentAbilityNotes`）展示时，直接执行了 `{{ cleanHtml(note) }}`，完全未调用 `resolveAbilityText`。蓝胖的 `%multicast_delay%`、`%ignite_multicast_aoe%` 等宏变量占位符（全英雄共 100 处）没有与 `special_values` 发生数值联动，直接在界面裸露出英文宏占位符。
3. **神杖赋予的技能在徽章上被错误显示为“魔晶提供新技能”**：
   - **因果链路**：在 `AbilityShowcase.vue` 的技能徽章条件渲染中，`activeSelectedAbility.ability_is_granted_by_scepter` 对应的标签文本被笔误写成了“魔晶提供新技能”，导致蓝胖的「未精通的火焰爆轰」（神杖赋予）显示错误文案且颜色也是魔晶的青蓝色。
4. **推荐出装图标过小、间距松散**：
   - **因果链路**：在 `HeroBuildPanel.vue` 中，物品图标采用 `h-4.5 w-6.5`（仅 18px × 26px），且每个物品包覆着大量水平内边距和文字，既浪费纵向空间，又无法看清精美的物品原画，背离了经典电竞装备栏的紧凑大图标直观体验。

## 决策方案

1. **技能属性标签全面清洗**：
   - 在 `frontend/src/utils/abilityMeta.ts` 的 `getAbilityLevelValues` 中，引入 `cleanInlineText` 对 `rawLabel` 进行严格 HTML 标签剥离和空白规范化，产出纯净的“眩晕时间”、“治疗/伤害”等中文属性；
   - 在 `AbilityShowcase.vue` 模板渲染处增加双重防护。
2. **机制注解与 Tip 接入宏占位符解析引擎**：
   - 在 `AbilityShowcase.vue` 中，将机制注解的展示统一接入 `cleanHtml(resolveAbilityText(note, activeSelectedAbility, "none", activeDetails || undefined))`；
   - 强化 `abilityTextResolver.ts` 的解析兜底，确保 `multicast_delay`、`ignite_multicast_aoe` 等变量精准命中并格式化输出。
3. **神杖与魔晶徽章严格区分**：
   - 将 `ability_is_granted_by_scepter` 判定分支文案修正为 **“神杖提供新技能”**，并赋予金色/琥珀色专属样式（`text-amber-300 border-amber-500/50 bg-amber-950/60`）；
   - 保持 `ability_is_granted_by_shard` 为“魔晶提供新技能”（青蓝色）。
4. **推荐出装大图标高辨识度与紧凑网格排布**：
   - 物品图标放大至 **`h-8 w-11`**（约 32px × 44px），呈现高质量视觉质感；
   - 减小外层与内部间距，采用紧凑排列，图标下方标注清晰的金色价格；
   - 鼠标悬停呈现高亮发光与包含品名和金币价格的详细悬浮 Tooltip。

## 曾考虑的替代方案

1. **在前端直接使用 `v-html` 渲染 `<font color="...">`**：
   - *最强理由*：可以保留官方原始的青蓝眩晕色。
   - *否决原因*：官方 `<font color="...">` 颜色未经全盘暗色系电竞主题校准，部分颜色在黑底上对比度低，且直接暴露 `v-html` 带来不必要的安全隐患与样式碎片化。统一清洗为干净中文并在电竞 HUD 统一排版更专业。
2. **在原始 JSON 数据文件中直接写死替换所有字符**：
   - *最强理由*：前端不需要额外的运行期解析代码。
   - *否决原因*：官方数据源随着未来版本更新（如 7.42+）会被重新抓取覆盖，硬编码改写离线数据治标不治本，必须通过基础库 `abilityMeta.ts` 与 `abilityTextResolver.ts` 在渲染管道上进行规范化解耦。

## 后果与验证计划

- **正面收益**：蓝胖及全英雄技能属性 100% 消除 `<font>` 等裸露 HTML 标签；机制注解占位符 100% 替换为真实数值；神杖赋予技能文案严谨；出装物品图标清晰大方且排布紧凑。
- **验证计划**：
  1. 编写独立测试脚本 `scripts/test-hero-ability-and-build-fix.cjs` 断言蓝胖及全量英雄的技能属性 label、notes 占位符、神杖徽章以及出装 UI 属性；
  2. 运行 `npm run verify-notes` 确保笔记合规；
  3. 运行 `pnpm --filter frontend lint` 与 `pnpm --filter frontend build` 闭环验证。
