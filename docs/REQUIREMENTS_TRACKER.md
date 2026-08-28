# 神盾教练（AegisCoach）核心需求与设计规范跟踪文档 (REQUIREMENTS_TRACKER.md)

> **核心原则**：本项目的所有历史需求、UI 设计规范与内置资源映射具有最高优先级，后续任何开发与重构严禁破坏、遗漏或还原已有实现。

---

## 一、英雄模块核心规范

### 1. 中文名称与数据完整性
- **绝对要求**：127 个英雄的中文名称（如“伐木机”、“马格纳斯”、“变体精灵”等）必须 100% 正确展示，**严禁出现“中文英雄名称待同步”或英文名**。
- **实现保障**：内置 AegisCoach.Native.Data.DotaHeroNames 静态常数表与 Assets/Data/herostats.json / herolist.json，即使网络不可用或反序列化层级异常，也能通过 ID/Code 毫秒级秒查中文名。
- **克制与被克制列表**：HeroCountersMe 和 HeroCountersTarget 必须调用 DotaHeroNames.GetChineseName(heroId)，确保对阵英雄列表 100% 中文化。

### 2. 英雄主属性与近战/远程
- **主属性判定**：
  - 力量 (str)：ms-appx:///Assets/Icons/hero_strength.png
  - 敏捷 (gi)：ms-appx:///Assets/Icons/hero_agility.png
  - 智力 (int)：ms-appx:///Assets/Icons/hero_intelligence.png
  - 全才 (ll)：ms-appx:///Assets/Icons/hero_universal.png
- **攻击类型判定**：
  - 近战 (Melee)：ms-appx:///Assets/Icons/melee.svg
  - 远程 (Ranged)：ms-appx:///Assets/Icons/ranged.svg
- **严禁全员落入“全才/远程”**：基准数据严格从内置离线权威表读取，杜绝空值默认分支错误。

### 3. 英雄属性卡片（右侧攻击/防御/机动性）官网原生结构
- **图标素材**：必须使用官网标准图标 PNG，严禁使用系统通用字体图标替代：
  - 攻击：
    - ms-appx:///Assets/Icons/icon_damage.png (攻击力)
    - ms-appx:///Assets/Icons/icon_attack_time.png (攻击间隔)
    - ms-appx:///Assets/Icons/icon_attack_range.png (攻击距离)
    - ms-appx:///Assets/Icons/icon_projectile_speed.png (弹道速度)
  - 防御：
    - ms-appx:///Assets/Icons/icon_armor.png (护甲)
    - ms-appx:///Assets/Icons/icon_magic_resist.png (魔法抗性)
  - 机动性：
    - ms-appx:///Assets/Icons/icon_movement_speed.png (移动速度)
    - ms-appx:///Assets/Icons/icon_turn_rate.png (转身速率)
    - ms-appx:///Assets/Icons/icon_vision.png (视野)

### 4. 技能卡片（AbilityInfoContainer）官网原生结构
- **AbilityMain**：60x60 大技能图标（支持遮罩） + 技能名称（粗体白字） + 变体标签 + 技能正文描述。
- **GenericValues**：两列通用机制卡片：
  - 左列：技能（无目标/单位目标等） + 伤害类型（纯粹/物理/魔法不同色彩高亮）。
  - 右列：无视减益免疫（是/否） + 可以驱散（是/否）。
- **SpecificValues**：网格化特殊参数键值（如 作用范围：325，伤害：60 / 120 / 180 / 240 等）。
- **BottomValues**：
  - 冷却：ms-appx:///Assets/Icons/cooldown.png + 冷却数值。
  - 魔法：奥术蓝方框 + 魔法消耗数值。
- **Lore**：斜体灰字背景故事。
- **阿哈利姆升级卡片**：
  - 神杖升级：ms-appx:///Assets/Icons/aghs_scepter.png + 金色标题 + 升级具体加成效果。
  - 魔晶升级：ms-appx:///Assets/Icons/aghs_shard.png + 奥术蓝标题 + 升级具体加成效果。
- **技能遮罩与角标**：
  - 先天技能遮罩：ms-appx:///Assets/Icons/innate_icon.png
  - 神杖升级遮罩：ms-appx:///Assets/Icons/aghs_scepter.png
  - 魔晶升级遮罩：ms-appx:///Assets/Icons/aghs_shard.png

---

## 二、物品模块核心规范

### 1. 悬浮详情卡片（ItemDetailCardTemplate）
- **透明度与对比度**：背景必须采用深色高对比度不透明背景（#FA141720），配合高亮边框（#405068），严禁使用高透明度磨砂材质导致文字看不清。

### 2. 合成路线（RecipeComponents & BuildsInto）
- **合成配件与卷轴**：必须内置 item_constants.json，离线完整解析每个物品的前置小件、数量倍数、小件单价、卷轴价格以及进阶合成装备。

---

## 三、构建与打包规范

### 1. 离线资源打包
- 项目文件 AegisCoach.Native.csproj 必须保持：
  `xml
  <ItemGroup>
    <Content Include="Assets\**">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
    </Content>
  </ItemGroup>
  `

### 2. 发布目录精简规范
- E:\openSource\demo\release\ 根目录下**仅允许且必须保留两项**：
  1. publish/（解压即可直接独立运行的免安装文件夹，无多余子文件夹）
  2. 神盾教练-v7.41e-x64.zip（便携分发包）
- 自动剔除 DirectML/ONNX/70+多语言/winmd 等多余文件，保持压缩包 ~31MB、运行目录 ~95MB。
