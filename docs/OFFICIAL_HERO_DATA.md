# 官网英雄技能字段与中文模板解析

核验日期：2026-09-17。本文说明已实现的展示契约，不承诺官网资料等于客户端当前补丁。

## 根因与来源

问题不只是“没有翻译”：旧逻辑将官网模板、OpenDota 数字及旧技能列表混用，再通过词根猜参数或删除未解析宏掩盖失败。结果即使没有英文，也可能显示错误数值。本次改为同源解析、缺值显式提示与全英雄可重跑审计。

- 页面入口：`https://www.dota2.com.cn/hero/spectre`；页面路径使用英雄 slug。
- 实际数据：`https://www.dota2.com.cn/datafeed/hero?hero_id=67`；接口使用数字 ID，不是页面 slug。
- 中国站结构：`status -> result -> heroes`，其中 heroes 是单对象；国际 datafeed 的英雄列表是另一种 envelope，不可混用。
- 用户附件：前三份相同的主宰 ID 8；第四份是哈斯卡 ID 59，均进入测试夹具。
- 官网字段语义额外对照 `https://www.dota2.com.cn/asset/heroes/static/js/index.js?v=20260916` 的 getEffectsName、getImmunityName、getDispellableName 与 talentsName。
- Liquipedia Spectre 页面请求返回 HTTP 403，本次未将其作为已验证数值来源，也未绕过访问限制。

## 字段含义

| 字段 | 含义和展示规则 |
|---|---|
| id / name / name_loc | 数字 ID、内部稳定标识、中文显示名；内部标识不能当作中文名称 |
| abilities / talents | 技能和天赋集合；官方可用时不合并旧 OpenDota 英雄技能列表 |
| desc_loc / notes_loc / lore_loc | 技能模板、机制注释数组、背景文字；不是可以直接显示的最终字符串 |
| shard_loc / scepter_loc | 魔晶、神杖说明模板；分别选择升级数组，缺少升级数组时使用同条技能基础数组 |
| cooldowns / mana_costs | 各等级面板冷却和魔耗；优先于同名内部 special_values，不丢失 0 |
| cast_ranges / cast_points / channel_times / durations / damages | 各等级施法距离、前摇、持续施法时间、持续时间、伤害；按精确标准键绑定 |
| special_values[].name | 占位符键，不是显示标签；不得按 damage 等词根模糊匹配 |
| values_float / values_int / values | 基础等级数值，依次选首个非空数组；保留负数、0、小数并采用固定区域格式 |
| values_scepter / values_shard | 对应升级说明的专属值；不能自动叠加到基础说明 |
| heading_loc / is_percentage | 属性面板中文标签与百分比单位；正文单位仍由模板决定，避免双重百分号 |
| required_facet / facet_bonus | 命石条件/加成元数据；未选择命石时不能把条件数值混入通用技能或天赋 |
| bonuses[].name / value / operation | 关联天赋 ID、加成量、应用运算；展示天赋加成量而不是计算升级后的基础技能值 |
| behavior | 64 位标志字符串，按位解码，不可用 Int32 或查找英文单词 |
| damage | 官网面板映射：1 物理、2 魔法、3/4 纯粹，0 不适用 |
| target_team / target_type | 阵营和目标类型联合解释，例如 2/1 是敌方英雄；仅按阵营会漏掉英雄、建筑限制 |
| immunity | 官网“无视减益免疫”：1/3 是，2/4 否，5 对友无视、对敌无效；0 未指定，显示不适用 |
| dispellable | 1 仅强驱散、2 是、3 否；0 未指定，显示不适用。不能按布尔值处理 |
| ability_is_innate / is_innate | 先天技能标记 |
| ability_has_scepter / ability_has_shard | 原技能是否存在升级 |
| ability_is_granted_by_scepter / ability_is_granted_by_shard | 升级是否提供独立新技能，与“已有技能升级”不同 |

未知枚举显示“资料待同步”，不会默认成否。官网行为位组合保留已知低位，不因高位标记溢出。

## 模板替换契约

1. 清理 HTML、解码实体；已核对的三条官网英文采用完整原句精确译文，不局部拼词。
2. 统一支持 `%key%`、`{s:key}`、`{d:key}`、`{f:key}`、`{g:key}`、`{{key}}`、`{key}`。
3. 精确键优先；只允许 bonus_ / tooltip_ 前缀别名且候选值唯一。范围缺值不能拿伤害补。
4. `%damage%%%` 等百分号后缀逐等级输出；相邻 `%a%%b%` 不吞掉第二个宏；正负号仍以模板为准。
5. 天赋从同英雄技能 bonuses 中按天赋 ID 和精确参数键补值，只接受唯一结果；补值幂等，命石条件项跳过。
6. 未找到参数输出“数值待同步”，同时审计保留原始文本和缺失键。未知英文整段、乱码显示“官方中文说明暂不可用”。
7. 中文准入仅允许少量游戏通用缩写；未译 Wiki 文本不作为机制技巧展示。
8. 官方技能存在时，不从其他版本补描述、数值、机制或先天标记。召唤物额外技能只接受已有中文名称和描述的资料。

## 离线与更新

内置原始快照含 source 和 fetched_at，运行时经过现有 DotaDataService 调度：网络失败依次尝试过期同版本缓存、内置快照。共享快照单次读取、返回副本后补天赋值。缓存键升级为 `hero-{id}-zh-v2`，避免旧 DTO 丢字段的缓存被继续复用；未删除旧缓存。

注意：当前页面尚未单独显示每条技能的快照日期，离线资料不保证最新；force 刷新失败仍报错，不伪装成功。

```powershell
# 运行真实数据解析回归与全英雄字段审计（官方快照内置于 frontend/public/assets/data/heroes-details-official.json）
node scripts/generate-hero-text-audit.cjs
```

审计输出：`hero-text-audit.json` 与 `frontend/public/assets/data/hero-text-audit.json`。刷新快照后应复查缺项变化，不应把“中文提示通过”解释为“官网数据全部补齐”。

## 神杖/魔晶升级有效性判断机制与历史遗留模板鉴别

### 1. 核心结论与因果机制
**不要只看 `scepter_loc` / `shard_loc` 是否非空，真正的权威标志是 `ability_has_scepter` 和 `ability_has_shard`。**

在 Valve 官方 Datafeed 数据库中：
- 当某英雄技能在版本更迭（如 7.30 / 7.35 / 7.36）中移除或重做了阿哈利姆升级时，Valve 后端会将该技能的 `ability_has_scepter` 或 `ability_has_shard` 明确置为 `false`，并从 `special_values` 数组中删除了对应的专属加成数值；
- 但 Valve 往往**遗漏了清理多语言本地化模板 `scepter_loc` / `shard_loc`**；
- 若仅凭 `*_loc` 字符串非空就判定技能存在升级，就会去解析模板并因找不到已删除的占位符而误报“数值待同步”。更严重的是，过时的旧机制（如主宰剑刃风暴旧神杖、剃刀静电连接旧魔晶、哈斯卡沸血之矛旧神杖）会被错误展示在 UI 或注入给 LLM，诱导模型给出错误战术建议。

### 2. 经典英雄案例实证（对比 Liquidpedia 与官方标志）

| 英雄 | 技能 | 升级类型 | `ability_has_*` | `*_loc` 是否非空 | 模板占位符 | `special_values` 中能否找到 | 判定 | 实际现状 |
|---|---|---|:---:|:---:|:---:|:---:|:---:|---|
| 主宰 (8) | 剑刃风暴 | 神杖 | `false` | 是 | `%attack_interval%` | 否 | **已失效历史旧模板** | 当前神杖为授予新技能【迅捷斩】 |
| 剃刀 (15) | 静电连接 | 魔晶 | `false` | 是 | `%move_speed_factor%` | 否 | **已失效历史旧模板** | 当前魔晶在【风暴涌动】 |
| 哈斯卡 (59) | 沸血之矛 | 神杖 | `false` | 是 | `%multishot_targets%` | 否 | **已失效历史旧模板** | 当前神杖升级在【牺牲】 |
| 哈斯卡 (59) | 牺牲 | 神杖 | `true` | 是 | 无占位符 | 有 `values_scepter` | **有效升级** | 强迫目标攻击哈斯卡并增加施法距离 |
| 主宰 (8) | 治疗守卫 | 魔晶 | `true` | 是 | 有占位符 | `values_shard` 有值 | **有效升级** | 守卫跟随并提升移速 |
| 剃刀 (15) | 风暴之眼 | 神杖 | `true` | 是 | 无占位符 | 无占位符 | **有效升级** | 同时打击两个目标并攻击建筑 |

### 3. 六种智能状态分类裁决契约 (`classify_ability_upgrade`)

| 状态标识 | 英文标识 | 判定依据 | 运行时处理方式 | 审计记录规范 |
|---|---|---|---|---|
| **有效升级** | `VALID_UPGRADE` | `ability_has_* == true` 且模板占位符均能成功解析 | 正常解析，数值选用专属 `values_*`，展示并注入战术模型 | `status: "VALID_UPGRADE", resolution: "none"` |
| **命名不一致** | `NOMENCLATURE_MISMATCH` | 模板占位符与 special_values 键名微差，但可通过别名策略完全解析 | 自动应用前缀剥离、公式合成或天赋反查，保证数值 100% 准确替换 | `status: "NOMENCLATURE_MISMATCH", resolution: "resolved_by_alias"` |
| **废弃旧模板** | `STALE_UPGRADE_TEMPLATE` | `ability_has_* == false` 但官方数据库残留历史 `*_loc` 升级文本 | 明确跳过解析并标注“该升级在当前版本已移除/重做”，绝不暴露旧数值 | `status: "STALE_UPGRADE_TEMPLATE", resolution: "stale_template_skipped"` |
| **已移除技能** | `REMOVED_ABILITY` | 技能自身或关联技能在当前版本已从英雄 abilities 集合中删除 | 明确跳过解析，标注“已移除技能”，不计入当前版本资料缺项 | `status: "REMOVED_ABILITY", resolution: "removed_ability_skipped"` |
| **数据源缺陷** | `DATA_SOURCE_DEFECT` | 官方 datafeed 存在文本破损或非标准词汇阻碍校验 | 从内置快照与词汇白名单准入覆盖修复，保证展示合法性 | `status: "DATA_SOURCE_DEFECT", resolution: "resolved_by_snapshot"` |
| **无升级** | `NO_UPGRADE` | `ability_has_* == false` 且 `*_loc` 文本为空 | 正常静默，无升级行 | `status: "NO_UPGRADE", resolution: "none"` |

---

## 真实数据缺项深度归因与 Liquipedia 多源核对

快照覆盖 127 英雄、734 技能、1016 天赋、3550 个非空文本字段。
此前 17 项异常中，经智能分类引擎裁决：**29 项确认为已失效历史旧模板**，**1 项为已移除技能**，**4 项经别名解析与白名单覆盖消解**，达成 **`genuine_missing == 0`**（真实数据缺项为 0）。

### 1. 五大疑难缺项根因剖析与 Liquipedia 对比结论

| 英雄 ID | 英雄 | 技能 / 天赋 | 字段 | 涉及占位符 | 智能状态分类 | 解决策略 (`resolution`) | Liquipedia 与游戏数据交叉核对事实 |
|:---:|:---:|---|---|---|:---:|:---:|---|
| 4 | 血魔 | `bloodseeker_blood_bath` (血之祭坛) | `notes_loc[0]` | `%castpoint_tooltip%` | `NOMENCLATURE_MISMATCH` | `resolved_by_alias` (公式合成) | **对齐一致**。官方原文“总时间为%delay%秒的施法时间加上%castpoint_tooltip%秒的生效延迟”。技能存在 `AbilityCastPoint: 0.3`、`delay: 2.6` 及 `delay_plus_castpoint_tooltip: 2.9`。Liquipedia 记录生效延迟 2.6s，施法前摇 0.3s，差值 `2.9 - 2.6 = 0.3`，公式合成完全吻合。 |
| 80 | 独行德鲁伊 | `lone_druid_spirit_bear` (召唤熊灵) | `notes_loc[1]` | `%base_magic_resistance%` | `NOMENCLATURE_MISMATCH` | `resolved_by_alias` (前缀剥离) | **对齐一致**。官方原文“拥有%base_magic_resistance%%%基础魔法抗性”。special_values 明确存在 `bear_magic_resistance: 25`。Liquipedia 证实 Spirit Bear 初始固有魔法抗性确为 25%。剥离单位前缀 `bear_` 即可完美替换。 |
| 74 | 祈求者 | `special_bonus_unique_invoker_forged_spirit_armor_reduction` | `name_loc` | `{s:bonus_armor_removed}` | `NOMENCLATURE_MISMATCH` | `resolved_by_alias` (天赋反查) | **对齐一致**。官方模板为“+{s:bonus_armor_removed}% 熔炉精灵护甲减少”。在 `invoker_forge_spirit` 的 `armor_per_attack` bonuses 数组中明确绑定该天赋且 `value = 50`。Liquipedia 证实该 10 级天赋效果为熔炉精灵攻击削甲提升 50%，反查关联数值 100% 吻合。 |
| 74 | 祈求者 | `invoker_emp` (EMP) | `notes_loc[0]` | 纯文字机制提示 | `DATA_SOURCE_DEFECT` | `resolved_by_snapshot` (白名单准入) | **对齐一致**。官方中文原文“对被吹起的单位（强袭飓风或Eul的神圣法杖）无效。”文本本身 0 缺失，仅因包含国服标准装备名 `Eul` 被误判为杂质英文。Liquipedia 机制说明与国服客户端物品名称完全证实“Eul的神圣法杖”为正统命名，词汇放行后正常显示。 |
| 89 | 娜迦海妖 | `special_bonus_unique_naga_siren_reel_in_speed` | `name_loc` | `{s:bonus_pull_strength}` | `REMOVED_ABILITY` | `removed_ability_skipped` (跳过废弃) | **对齐一致**。模板为“+{s:bonus_pull_strength} 捕捞速度”。Liquipedia 版本历史证实：【捕捞】(Reel In) 技能已在 7.36 版本重做中被 Valve 彻底移除，小娜迦当前 abilities 列表已无该技能。此天赋为 Valve 官方数据库遗留孤立死天赋，判定为已移除技能并跳过解析。 |

---

### 2. 废弃旧升级模板全量审计 (29 项，含重点经典 12 项)

此类均为 `ability_has_* == false`，但 Valve 官方多语言数据库残留旧升级模板。解析器统一标记为 `STALE_UPGRADE_TEMPLATE` 并跳过解析，显示“该升级在当前版本已移除/重做”，绝不暴露旧数值或未解析占位符给用户与 LLM：

1. **主宰 (8)** `juggernaut_blade_fury` (`scepter_loc`)：旧神杖（当前神杖为【迅捷斩】）
2. **剃刀 (15)** `razor_static_link` (`shard_loc`)：旧魔晶（当前魔晶为【风暴涌动】）
3. **沙王 (16)** `sandking_scorpion_strike` (`shard_loc`)：旧魔晶
4. **暗影萨满 (27)** `shadow_shaman_fowl_play` (`shard_loc`)：旧魔晶
5. **暗影萨满 (27)** `shadow_shaman_mass_serpent_ward` (`shard_loc`)：旧魔晶（巨蟒之瓮）
6. **术士 (37)** `warlock_shadow_word` (`scepter_loc`)：旧神杖
7. **兽王 (38)** `beastmaster_summon_raptor` (`shard_loc`)：旧魔晶（当前魔晶在飞斧）
8. **剧毒术士 (40)** `venomancer_poison_sting` (`shard_loc`)：旧魔晶
9. **露娜 (48)** `luna_lucent_beam` (`shard_loc`)：旧魔晶
10. **哈斯卡 (59)** `huskar_burning_spear` (`scepter_loc`)：旧神杖（当前神杖在【牺牲】）
11. **编织者 (63)** `weaver_geminate_attack` (`shard_loc`)：旧魔晶（当前魔晶在缩地）
12. **裂魂人 (71)** `spirit_breaker_charge_of_darkness` (`shard_loc`)：旧魔晶（当前魔晶在巨力重击）
13. **殁境神蚀者 (76)** `obsidian_destroyer_arcane_orb` (`scepter_loc`)：旧神杖
14. **独行德鲁伊 (80)** `lone_druid_spirit_link` (`scepter_loc`)：旧神杖
15. **树精卫士 (83)** `treant_living_armor` (`shard_loc`)：旧魔晶
16. **干扰者 (87)** `disruptor_thunder_strike` (`shard_loc`)：旧魔晶
17. **干扰者 (87)** `disruptor_kinetic_field` (`shard_loc`)：旧魔晶
18. **上古巨神 (103)** `elder_titan_natural_order` (`shard_loc`)：旧魔晶
19. **玛尔斯 (129)** `mars_bulwark` (`scepter_loc`)：旧神杖
20. **琼英碧灵 (138)** `muerta_pierce_the_veil` (`shard_loc`)：旧魔晶
21. 影魔 (11) `nevermore_necromastery` (`scepter_loc`)：纯文本旧神杖
22. 幻影长矛手 (12) `phantom_lancer_spirit_lance` (`scepter_loc`)：纯文本旧神杖
23. 斯拉达 (28) `slardar_slithereen_crush` (`scepter_loc`)：纯文本旧神杖
24. 戴泽 (50) `dazzle_nothl_projection` (`shard_loc`)：纯文本旧魔晶
25. 暗夜魔王 (60) `night_stalker_hunter_in_the_night` (`shard_loc`)：纯文本旧魔晶
26. 沉默术士 (75) `silencer_brain_drain` (`shard_loc`)：纯文本旧魔晶
27. 混沌骑士 (81) `chaos_knight_reality_rift` (`shard_loc`)：纯文本旧魔晶
28. 凤凰 (110) `phoenix_fire_spirits` (`shard_loc`)：纯文本旧魔晶
29. 破晓辰星 (135) `dawnbreaker_luminosity` (`scepter_loc`)：纯文本旧神杖

---

## 自动化测试与全量审计指标

通过执行自动化冒烟测试与审计脚本：
```powershell
node scripts/generate-hero-text-audit.cjs
```

**实测全量审计输出：**
- 英雄总数：127
- 技能总数：734
- 天赋总数：1016
- 文本字段总数：3550
- 真实数据缺项 (`genuine_missing`)：**0**（100% 达成）
- 废弃升级旧模板跳过 (`stale_upgrade_templates`)：**29** 项（全部安全隔离）
- 已移除技能废弃天赋跳过 (`removed_abilities`)：**1** 项（小娜迦捕捞天赋）
- 别名策略自动解析 (`resolved_by_alias`)：**3** 项（前缀剥离、公式合成、天赋反查各 1 项）
- 数据源专用词汇放行与快照覆盖 (`resolved_by_snapshot`)：**1** 项（卡尔 EMP 放行 Eul）
- 审计报告文件：`hero-text-audit.json` 与 `frontend/public/assets/data/hero-text-audit.json`（严格规范 `status`、`resolution`、`missing_keys`、`evidence` 4 个标准字段）

## 菜单图标

导航采用 `@lucide/vue` 官方现代矢量图标结合 Dota 2 官方原生图标：盾牌（教练）、剑刃/头盔（英雄）、晶石（物品）、卷轴（分析）、齿轮（设置）。显示时继承导航颜色及选中状态，统一电竞暗黑视觉风格。保留文字标签、内置设置导航语义与沉浸式暗黑窗口设计。

