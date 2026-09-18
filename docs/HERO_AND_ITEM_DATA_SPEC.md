# Dota 2 英雄与物品核心数据体系规范 (HERO_AND_ITEM_DATA_SPEC.md)

本文档是神盾教练（AegisCoach）在英雄资料、技能天赋、装备物品、合成路线与搜索检索等核心数据体系上的**全量业务规范与技术事实唯一参考来源**。无论后续使用 Rust、Go 还是 TypeScript，所有 AI 与研发人员必须严格以此文档中的数据模型、计算公式、分类算法与降级策略为准。

---

## 一、 数据资产与文件拓扑

所有静态权威数据均位于本地打包资源中，支持纯离线 0ms 秒开：

| 资源相对路径 | 数据内容说明 | 关键模型结构 |
|---|---|---|
| `Assets/Data/heroes-details-official.json` | 官方 127 英雄全量快照（包含技能、天赋、神杖魔晶升级、数值矩阵） | `OfficialHeroSnapshot` |
| `Assets/Data/items-details-official.json` | 官方物品全量详情（包含物品描述、冷却、魔耗、属性加成） | `OfficialItemSnapshot` |
| `Assets/Data/items.json` | 经典物品关系数据库（包含合成配件、升级前置、商店分类、价格） | `Dictionary<string, ItemData>` |
| `Assets/Data/herostats.json` | 127 英雄基础属性（力量/敏捷/智力基数、护甲、移速、攻击范围等） | `List<HeroStat>` |
| `Assets/Data/herolist.json` | 官方英雄中英文映射清单 | `HeroListData` |
| `Assets/Data/item_constants.json` | 物品元数据常量表 | `Dictionary<string, ItemConstant>` |
| `Assets/Icons/` | 游戏高清图标（属性标、神杖、魔晶、天赋树、56x42 物品贴图等） | PNG / SVG 图像资源 |

---

## 二、 127 英雄全景与核心属性公式

### 1. 英雄主属性与攻击类型规范
- **主属性定义（Primary Attribute）**：
  - `str` / `0`：力量英雄（Strength，红标）
  - `agi` / `1`：敏捷英雄（Agility，绿标）
  - `int` / `2`：智力英雄（Intelligence，蓝标）
  - `all` / `3`：全才英雄（Universal，四色核心标）
- **攻击类型判定（Attack Capability）**：
  - `Melee`：近战英雄（对应图标 `melee.svg`）
  - `Ranged`：远程英雄（对应图标 `ranged.svg`）

### 2. 官网原生 1 级基础属性计算公式（严格遵守）
在展示英雄 1 级面板时，必须以基础数值累加属性系数，杜绝静态硬编码：
- **1 级生命值（Health）**：
  $$\text{HP} = 120 + \text{base\_str} \times 22$$
- **1 级生命恢复（Health Regen）**：
  $$\text{HP\_Regen} = \text{base\_health\_regen} + \text{base\_str} \times 0.1$$
- **1 级魔法值（Mana）**：
  $$\text{Mana} = 75 + \text{base\_int} \times 12$$
- **1 级魔法恢复（Mana Regen）**：
  $$\text{Mana\_Regen} = \text{base\_mana\_regen} + \text{base\_int} \times 0.05$$
- **1 级总护甲（Armor）**：
  $$\text{Armor} = \text{base\_armor} + \frac{\text{base\_agi}}{6.0}$$
- **1 级初始攻击力（Attack Damage）**：
  - 力量英雄：$\text{base\_attack} + \text{base\_str}$
  - 敏捷英雄：$\text{base\_attack} + \text{base\_agi}$
  - 智力英雄：$\text{base\_attack} + \text{base\_int}$
  - **全才英雄（Universal）**：
    $$\text{Attack} = \text{base\_attack} + \lfloor (\text{base\_str} + \text{base\_agi} + \text{base\_int}) \times 0.7 \rfloor$$

### 3. 127 英雄中文名 100% 静态常数兜底表（防退化红线）
即使断网或官方接口失效，英雄中文名绝对不可显示为英文代号或“待同步”：

```text
1: 敌法师 (antimage)          2: 斧王 (axe)                 3: 祸乱之源 (bane)
4: 嗜血狂魔 (bloodseeker)     5: 水晶室女 (crystal_maiden)   6: 卓尔游侠 (drow_ranger)
7: 地球灵 (earthshaker)       8: 主宰 (juggernaut)          9: 米拉娜 (mirana)
10: 变体精灵 (morphling)      11: 影魔 (nevermore)          12: 幻影长矛手 (phantom_lancer)
13: 帕克 (puck)              14: 帕吉 (pudge)              15: 影魔 (razor/剃刀)
16: 沙王 (sand_king)         17: 风暴之灵 (storm_spirit)   18: 流浪剑客 (sven)
19: 小小 (tiny)              20: 复仇之魂 (vengefulspirit) 21: 风行者 (windrunner)
22: 宙斯 (zuus)              23: 昆卡 (kunkka)             25: 莉娜 (lina)
26: 莱恩 (lion)              27: 暗影萨满 (shadow_shaman)   28: 斯拉达 (slardar)
29: 潮汐猎人 (tidehunter)     30: 巫医 (witch_doctor)       31: 巫妖 (lich)
32: 裂魂人 (riki/力丸)        33: 谜团 (enigma)             34: 修补匠 (tinker)
35: 狙击手 (sniper)          36: 殁境神蚀者 (necrolyte/死灵法) 37: 术士 (warlock)
38: 兽王 (beastmaster)       39: 痛苦女王 (queenofpain)     40: 剧毒术士 (venomancer)
41: 虚空假面 (faceless_void) 42: 骷髅王 (wraith_king)      43: 死亡先知 (death_prophet)
44: 幻影刺客 (phantom_assassin) 45: 帕格纳 (pugna)         46: 圣堂刺客 (templar_assassin)
47: 冥界亚龙 (viper)         48: 露娜 (luna)               49: 龙骑士 (dragon_knight)
50: 戴泽 (dazzle)            51: 发条技师 (rattletrap)     52: 拉席克 (leshrac)
53: 自然先知 (furion)        54: 噬魂鬼 (life_stealer)     55: 暗影恶魔 (dark_seer)
56: 德鲁伊 (clinkz/克林克兹)  57: 全能骑士 (omniknight)     58: 魅惑魔女 (enchantress)
59: 哈斯卡 (huskar)          60: 暗夜魔王 (night_stalker)   61: 育母蜘蛛 (broodmother)
62: 赏金猎人 (bounty_hunter) 63: 编织者 (weaver)           64: 杰奇洛 (jakiro)
65: 蝙蝠骑士 (batrider)      66: 陈 (chen)                 67: 幽鬼 (spectre)
68: 远古冰魄 (ancient_apparition) 69: 末日使者 (doom_bringer) 70: 熊战士 (ursa)
71: 裂魂人 (spirit_breaker)  72: 矮人直升机 (gyrocopter)   73: 炼金术士 (alchemist)
74: 祈求者 (invoker)         75: 沉默术士 (silencer)       76: 殁境神蚀者 (obsidian_destroyer)
77: 狼人 (lycan)             78: 孤单德鲁伊 (lone_druid)    79: 混沌骑士 (chaos_knight)
80: 米波 (meepo)             81: 树精卫士 (treant)         82: 食人魔魔法师 (ogre_magi)
83: 不朽尸王 (undying)       84: 灰烬之灵 (rubick/拉比克)   85: 干扰者 (disruptor)
86: 撼地者 (nyx_assassin/司夜刺客) 87: 娜迦海妖 (naga_siren) 88: 光之守卫 (keeper_of_the_light)
89: 艾欧 (wisp)              90: 维萨吉 (visage)           91: 斯拉克 (slark)
92: 美杜莎 (medusa)          93: 巨魔战将 (troll_warlord)   94: 半人马战行者 (centaur)
95: 马格纳斯 (magnataur)     96: 伐木机 (shredder)         97: 刚背兽 (bristleback)
98: 巨牙海民 (tusk)          99: 天怒法师 (skywrath_mage)  100: 亚巴顿 (abaddon)
101: 大地之灵 (earth_spirit) 102: 孽主 (abyssal_underlord) 103: 恐怖利刃 (terrorblade)
104: 凤凰 (phoenix)          105: 神谕者 (oracle)          106: 工程师 (techies)
107: 灰烬之灵 (ember_spirit) 108: 大地之灵 (earth_spirit) 109: 齐天大圣 (monkey_king)
110: 破晓辰星 (pangolier/石鳞剑士) 111: 邪影芳灵 (dark_willow) 112: 琼英碧灵 (muerta)
113: 天穹守望者 (arc_warden) 114: 玛尔斯 (mars)           119: 虚无之灵 (void_spirit)
120: 电炎绝手 (snapfire)     121: 邪恶术士 (hoodwink/森海飞霞) 123: 破晓辰星 (dawnbreaker)
124: 玛西 (marci)            126: 兽 (primal_beast)        128: 琼英碧灵 (muerta)
131: 百炼之灵/凯 (ringmaster/擂台霸主) 145: 柯赞 (kez)
```

---

## 三、 技能数据体系与六大升级分类

官方英雄详情包含 `abilities` 数组与 `talents` 数组。每项技能必须根据其内在标志进行六态归类：

### 1. 技能类型判定矩阵

| 分类标识 (Classification) | 判定规则（依序优先匹配） | UI 呈现样式与特权 |
|---|---|---|
| **先天技能 (Innate)** | `ability_is_innate == true` 或 `is_innate == true` | 技能栏最左侧独立放置，常驻先天标识与徽章 |
| **魔晶赋予独立技能 (Granted by Shard)** | `ability_is_granted_by_shard == true` | 奥术蓝框背景，显示魔晶专属小图标，不占主技能栏 |
| **神杖赋予独立技能 (Granted by Scepter)**| `ability_is_granted_by_scepter == true` | 黄金神杖框背景，显示神杖专属小图标，不占主技能栏 |
| **已有技能-魔晶升级 (Shard Upgrade)** | `ability_has_shard == true` 且非独立赋予 | 技能卡片内展示独立的魔晶强化副说明（`shard_loc`） |
| **已有技能-神杖升级 (Scepter Upgrade)**| `ability_has_scepter == true` 且非独立赋予 | 技能卡片内展示独立的神杖强化副说明（`scepter_loc`） |
| **普通/终极技能 (Normal / Ultimate)** | 默认主技能（按 `behavior` 位图区分大招） | 标准 60x60 图标，展示冷却、魔耗与机制参数 |

### 2. 机制属性位图与标签解释规范
- **目标阵营与类型（Target Team / Type）**：
  - `target_team`: 1 友方、2 敌方、3 双向。
  - `target_type`: 1 英雄、2 基础单位、4 建筑、128 树木。
- **伤害类型（Damage Type）**：
  - `1`: 物理伤害（Physical，橙色高亮）
  - `2`: 魔法伤害（Magical，海蓝色高亮）
  - `3` / `4`: 纯粹伤害（Pure，黄金色高亮）
- **驱散机制（Dispellable）**：
  - `1`: 仅强驱散可驱散（Strong Dispels Only）
  - `2`: 可以驱散（Yes）
  - `3`: 不可驱散（No）
- **无视技能免疫（Immunity）**：
  - `1` / `3`: 是（无视减益免疫）
  - `2` / `4`: 否（受减益免疫阻挡）

### 3. 模板占位符与天赋补值算法
技能正文 `desc_loc` 与升级描述 `shard_loc`、`scepter_loc` 包含形如 `%key%`、`{s:bonus_key}` 的宏占位符。解析规则如下：
1. **占位符正则规范**：统一捕获 `%(\w+)%`、`\{s:(\w+)\}`、`\{d:(\w+)\}`、`\{(\w+)\}`。
2. **精确参数取值**：在 `special_values` 数组中查找 `name == key` 的条目。优先取 `values_float` / `values_int` / `values` 中的首个非空数组；若在神杖模板中，优先取 `values_scepter`，魔晶模板优先取 `values_shard`。
3. **天赋动态补值**：
   - 官网天赋项（`special_bonus_unique_*`）的加成数值存放在对应技能的 `special_values[].bonuses` 数组中。
   - 算法：遍历技能的 `bonuses`，若 `bonus.name == talent.name`，提取其 `value` 注入该天赋的数值占位符 `{s:bonus_*}` 中。
   - 示例：伐木机死亡旋风天赋 `+{s:bonus_tree_damage_scale} 死亡旋风树倒额外伤害` 自动精准解析为 `+75 死亡旋风树倒额外伤害`。

---

## 四、 物品体系与合成路线图谱

### 1. 物品分类与规格
- **展示规格**：官网标准 **56x42 像素** 矩形网格（圆角 4px）。
- **顶级分类**：
  - **商店物品（Shop Items）**：
    - 基础类（Basics）：消耗品（Consumables）、属性（Attributes）、军备（Armaments）、奥术（Arcane）。
    - 升级类（Upgrades）：常用辅助（Support）、法器（Caster）、武器（Weapons）、防具（Armor）、神器（Artifacts）。
  - **中立物品（Neutral Items）**：Tier 1 至 Tier 5 五档，带有阶数专属色标。

### 2. 合成图谱与树状解析
利用 `items.json` 与 `item_constants.json` 构建有向无环图（DAG）：
- **配方解析（Components）**：
  - 每个物品包含 `components: string[]`。
  - 若 `recipe == 1` 或存在 `item_recipe_*`，解析出卷轴价格。
  - 递归展开树：可完整解析“大晕锤（Abyssal Blade）= 先锋盾 + 碎颅锤 + 卷轴”，以及各个小件的单价与总金币数。
- **进阶装备（Upgrade Paths）**：
  - 反向索引：以当前物品作为组件的所有更高级合成物品，鼠标悬浮时展示进阶路线。

---

## 五、 搜索分词与别名匹配机制 (Alias & Pinyin)

为实现顶部搜索栏与英雄/物品列表在 10 毫秒内无卡顿筛选，必须建立静态别名与首字母索引表：

### 1. 核心别名映射举例 (`DotaAliasTable.cs`)
- **英雄常用简称**：
  - 敌法 / AM -> 敌法师 (`antimage`)
  - 影魔 / SF -> 影魔 (`nevermore`)
  - 火枪 / Sniper -> 狙击手 (`sniper`)
  - 白虎 / POM -> 米拉娜 (`mirana`)
  - 蓝猫 / ST -> 风暴之灵 (`storm_spirit`)
  - 卡尔 / Invoker -> 祈求者 (`invoker`)
  - 牛头 / ES -> 撼地者 (`earthshaker`)
  - 屠夫 / Pudge -> 帕吉 (`pudge`)
- **物品常用简称**：
  - `bkb` / 黑皇杖 -> 狂战斧 / 黑皇杖
  - `a杖` / 神杖 -> 阿哈利姆神杖
  - `跳刀` / blink -> 闪烁匕首
  - `推推` / force -> 原力法杖
  - `羊刀` / hex -> 邪恶镰刀
  - `大晕` / abyssal -> 深渊之刃
  - `冰甲` / shiva -> 希瓦的守护
  - `强袭` / cuirass -> 强袭胸甲
  - `分身斧` / manta -> 幻影斧

### 2. 检索匹配算法
```text
MatchScore = (精准名称完全匹配 ? 100) 
           + (拼音首字母完全匹配 ? 80) 
           + (别名完全匹配 ? 60) 
           + (包含中英文字符串 ? 40)
```
搜索过滤应在独立 Worker / 异步任务中执行，利用防抖（30ms ~ 50ms）配合取消令牌轮换，UI 仅单次原子重置列表。

---

## 六、 本地两级缓存与版本一致性规范

1. **缓存目录结构**：
   - 用户数据根目录：`%APPDATA%/AegisCoach/cache/`
   - 子目录正交隔离：
     - `/heroes/`：存放 `hero-{id}-zh-v2-v{Patch}.json`
     - `/items/`：存放 `item-details-v{Patch}.json`
     - `/audio/`：存放 Edge-TTS SHA256 语音片段缓存（`.mp3`）
2. **版本号强绑定（TargetPatch = 7.39）**：
   - 凡涉及游戏平衡与技能数值的 JSON 文件，文件名强制携带 `-v{TargetPatch}.json`。
3. **过期缓存自动清理契约**：
   - 应用启动后异步扫描 `/heroes/` 与 `/items/` 目录；
   - 若文件名含有 `-v` 且版本号不匹配当前客户端版本，静默删除旧文件；
   - 坚决杜绝旧版本技能与数值跨版本残留误导玩家。
