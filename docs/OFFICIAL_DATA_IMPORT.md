# 官网数据导入（可选）

应用支持把 **dota2.com.cn 官网** 返回的英雄/物品列表 JSON 作为数据增强来源，
用于补齐**物品价格**、**官网图片 URL** 和**中文名**；图片会自动下载到本地缓存，断网时也能显示。
另有**物品详情 JSON**（itemdata 结构）可提供权威的属性、技能、冷却/魔耗、背景故事与合成配方，
悬浮详情卡会直接展示官网原文。

## 使用方法

把从官网获取的 JSON 保存到以下位置（文件名固定）：

| 数据 | 保存路径 |
|---|---|
| 物品列表 JSON | `文档\AegisCoach\data\items-official.json` |
| 英雄列表 JSON | `文档\AegisCoach\data\heroes-official.json` |
| 物品详情 JSON（itemdata） | `文档\AegisCoach\data\items-details-official.json` |

保存后**重新启动应用**（或点击页面的刷新按钮）即可生效。

## JSON 格式

物品列表 / 英雄列表必须是官网返回的原始结构（`{"status":"success","result":{...}}`）：

- 物品：`result.basic` / `result.upgrade` / `result.neutral` 三个分类数组，
  每个分类含 `name`（中文分类名）与 `items` 数组；
  物品项字段：`item_id`、`name`、`name_loc`（中文名）、`cost`（价格）、`img_url`（官网图片）。
- 英雄：`result.heroes` 数组；字段：`id`、`name`、`name_loc`（中文名）、`crops_img`（头像图）。

物品详情 JSON 结构为 `{"itemdata":{"<内部名>":{...}}}`（www.dota2.com.cn 的物品详情接口）：

- 每个物品项字段：`id`（字符串）、`dname`（中文名）、`cost`（价格）、`desc`（描述，HTML，
  含 `<h1>主动：技能名</h1>` 分段）、`attrib`（属性行 HTML，如 `+攻击力：9`）、
  `mc` / `cd`（魔法消耗 / 冷却）、`lore`（背景故事）、`requirements`（合成配方数组，
  元素为组件内部名，`recipe_` 前缀为卷轴）。

## 效果

- 物品卡片显示价格（如 `$1400`），图标使用官网图片。
- 英雄头像使用官网图片（`crops_img`）。
- 悬浮详情卡：属性区展示官网 attrib 原文（绿字），技能区按 `<h1>` 分段显示主动/被动、
  冷却/魔耗与描述，底部展示背景故事；`requirements` 提供**合成路线**（小件缩略图 + 卷轴价格），
  详情文件中的价格同时用于补全组件价格。
- 所有图片按 URL 缓存到 `文档\AegisCoach\cache\images\`，之后断网也能显示。

## 刷新

点击页面的刷新按钮（⟳）会重新从网络 API（OpenDota / Dota 2 datafeed）拉取最新数据，
并重新生成图片缓存；官网数据文件始终作为当前版本的覆盖层。
