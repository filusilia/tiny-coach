## 2026-08-14（第九轮）：官网 itemdata 详情 + 悬浮卡 Popup + 合成路线修复

### 需求

用户提供 www.dota2.com.cn 的 itemdata 物品详情 JSON（desc/attrib/mc/cd/lore/requirements），要求：
1. 修复悬浮详情卡数值描述不对的问题（此前走 datafeed 占位符解析，官网中文最终文本没被采用）；
2. hover 悬浮卡不超出应用窗口、跟随鼠标；
3. 物品格去掉大 padding 空白，直接展示物品图片；
4. 把合成路线（小件 + 卷轴价格）加回来。

### 实现

- `Models/OfficialCatalogModels.cs`：新增 `OfficialItemDetailsRoot` / `OfficialItemDetailsEntry`，
  解析 `{"itemdata":{...}}` 结构（id/dname/cost/desc/attrib/mc/cd/lore/requirements/img_url）。
- `Services/DotaDataService.cs`：`LoadItemDetailsAsync` 优先读取 `data\items-details-official.json`（可选覆盖层）；
  attrib HTML 解析成属性行（`+攻击力：9`）；`ApplyOfficialItemDetailOverrides` 用官网价格补全目录 Cost，
  使合成路线能算出卷轴价格。
- `ViewModels/ItemCatalogViewModel.cs`：`ApplyDetails` 改为实例方法并解析官网 desc 的 `<h1>主动：技能名</h1>` 分段
  （主动/被动/使用/切换/升级 → 技能类型），冷却/魔耗用官网 cd/mc，价格与背景故事同步补齐；
  `ApplyOfficialRecipe` 用官网 requirements 重建合成路线（组件按出现次数计数、卷轴价 = 总价 - 组件价）。
- `Views/ItemCatalogPage.xaml(.cs)`：悬浮详情从 ToolTip 改为**窗口内 Popup**（TransformToVisual 换算窗口坐标，
  跟随指针 +14 偏移并夹取到窗口矩形内，移出延迟 120ms 关闭，移入卡片不关闭）；
  物品格改为 56x42（匹配 88x62 图标素材比例）、图片零边距铺满，去掉上下留白。
- `Themes/ItemCatalogTheme.xaml`：ItemTileStyle 56x42、详情卡 Header 图标 72x52 匹配素材比例。
- `docs/OFFICIAL_DATA_IMPORT.md`：补充物品详情 JSON 导入说明。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 成功，0 警告，0 错误 |
| 实机 UIA 取证（悬浮护腕） | 详情卡显示 `$ 505`、`+力量：5`、`+敏捷：2`、`+智力：2`、`+生命恢复：0.75`、`+生命：50`、`合成路线`、`卷轴 $ 210`、背景故事，全部为官网原文 |
| 合成路线日志 | `counts=2 price=505 recipeCost=295 scroll=210`（圆环 155 + 力量手套 140，卷轴 210） |
| 物品格 | 56x42 横长（UIA 实测），图标铺满无上下白边 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 131，失败 0 |
| `git diff --check` | 无空白错误 |

### 备注

- 悬浮 Popup 在本环境（远程桌面 + DSH 浏览器遮挡）中鼠标事件受限，实机验证时用临时自动打开 + UIA 取证，
  验证后已删除全部临时诊断代码。
- 官网详情文件是可选项：未放置时悬浮卡回退到网络 datafeed 缓存（原逻辑）。
- 完整官网 itemdata JSON 由用户提供；当前测试数据目录保留了代表性物品子集用于验证。
## 2026-08-14（第八轮）：修复物品资料页分组渲染“全是代码”

### 现象

实机打开“物品资料”页：分组头（基础物品/消耗品/8）正常，但物品格全部渲染成一行英文代码
`AegisCoach.Native.Models.CatalogItem`（CatalogItem 的默认 ToString），图标不显示，即用户反馈的“物品展示全是代码”。

### 根因

`Views/ItemCatalogPage.xaml` 用 `ItemsControl + GroupStyle`（HeaderTemplate + Panel）配合
`ItemsControl.ItemTemplate` 做分组网格。实测（窗口截图 OCR 每个物品格均为类型名）确认：
**WinUI 3 的 `ItemsControl.GroupStyle` 只渲染分组容器与 HeaderTemplate，不会把
`ItemsControl.ItemTemplate` 应用到分组内的物品**，物品按默认容器调用 `ToString()` 渲染。
第七轮日志“待实机验证”清单中的“分组网格（ItemsControl + GroupStyle）的运行时表现”正是此问题。

### 修复

- `Views/ItemCatalogPage.xaml`：去掉 `GroupStyle`，改为手动分组——外层 `ItemsControl` 绑定
  `Groups`，每个 `ItemGroup`（`List<CatalogItem>`）在 DataTemplate 内渲染分组头（SectionLabel/Key/Count）
  与嵌套 `ItemsControl`（ItemsWrapGrid + 物品格 ItemTemplate）。不再依赖 WinUI 3 半成品的 GroupStyle。
- `ViewModels/ItemCatalogViewModel.cs`：删除 `GroupedView`（`CollectionViewSource`）与相关 using，
  XAML 直接绑定 `Groups`。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 成功，0 警告，0 错误 |
| 实机窗口（重启修复版后 UIA 取证） | 商店模式 115 个 56x42 物品按钮，AutomationProperties.Name 为中文名（岗哨守卫/净化药水/魔瓶/树之祭祀…）；截图 OCR 物品区不再出现类型名代码，图标彩色像素正常 |
| 悬停详情卡（全屏截图 OCR） | ToolTip 卡正常显示物品名 + 分类/阶级（如“狗头人酒杯 · 中立物品 T1 · [主动]/[被动]”） |
| 中立物品模式（UIA 切换） | 73 个物品按钮，T1-T5/附魔 分组齐全 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 131，失败 0 |
| `git diff --check` | 无空白错误 |

### 备注

- 数据侧无需修复：`items-v7.41e.json` 缓存一直包含完整中文名（name_loc）与英文名，图片已缓存 519 张；
  此前用 PowerShell `ConvertFrom-Json` 检查显示 `LocalizedName` 为空是属性名大小写误判（JSON 字段为 `name_loc`）。
- 修复后建议在真机复核：深色/浅色主题、DPI、窄窗下的分组网格布局（本机已确认 1440x753 默认窗口无溢出）。
## 2026-08-14（第七轮）：物品资料页重构为独立页面（官网式分组 + 悬浮详情卡）

### 实现

- 新增 `Views/ItemCatalogPage.xaml(.cs)` + `ViewModels/ItemCatalogViewModel.cs`（CommunityToolkit.Mvvm 8.4.2，`ObservableProperty`/`RelayCommand`），
  将原内联在 MainWindow 的物品页迁移为独立页面；加载时机由 MainWindow 控制（首次启动门控后触发，遵守“保存设置前不访问在线数据源”）。
- 数据模型按官网结构建模：`Models/ItemCatalogModels.cs`（`CatalogItem`/`ItemAbility`/`ItemStat`/`ItemGroup`/`AbilityType`），
  展示模型挂在 `CollectionViewSource(IsSourceGrouped)` 分组上：大分类（基础物品/升级物品/中立物品）→ 子分类 → 56x42 物品格。
- 页面功能：商店/中立 RadioButtons 切换、子类 Chip 多选（T1-T5/附魔/消耗品等）、AutoSuggestBox 防抖搜索（250ms，匹配中文名/英文名/内部 ID/子类英文标签/拼音）、实时匹配计数、loading/error/empty 四态。
- 悬浮详情卡：`ToolTipService.ToolTip` + `ItemDetailCardTemplate`（Header 图标/名称/子类/金币价格/中立阶级；属性区绿字加号；技能区 [主动]蓝/[被动]强调色 + 冷却/魔法消耗；合成路线小件缩略图 + 卷轴价格；背景故事斜体三级文本）；
  悬浮进入时懒加载官方 `itemdata` 详情（属性/技能/传说），失败保留目录级信息；图片经 `ImageCacheService` 本地缓存。
- 主题：`Themes/ItemCatalogTheme.xaml` 的 ThemeDictionaries 定义 `GoldPriceBrush`（深 #FFD700 / 浅 #D97706）、`ManaBlueBrush`（#38BDF8/#0284C7）、
  `StatGreenBrush`（#4ADE80/#16A34A）与高对比映射；其余全部使用 WinUI 语义 ThemeResource（CardBackground/CardStroke/TextFill/Accent 等）；
  物品格 Hover 用 VisualStateManager 实现微缩放 1.05 + 强调色描边（150ms）。
- `MainWindow.xaml(.cs)`：内联物品页替换为 `views:ItemCatalogPage`；删除旧物品页 UI 与处理代码，保留教练页依赖的 `_allItems`（物品推荐热度/胜率/名称反查）；
  设置保存时同步页面缓存目录。
- 按“WinUI 3 最新 API”要求：`[ObservableProperty]` 全部改为 partial property 形式（CsWinRT/WinRT AOT 兼容），`LangVersion=latest`。
- 配合并行 agent 的第六轮修复（ToolTip 时序附加属性在 WinUI 3 不可用已移除、ResourceDictionary 内 x:Bind 改 Binding、`_` 丢弃符冲突改 await、publish 全量复制 .xbf/.pri），当前树构建通过。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 成功，0 警告，0 错误（`[ObservableProperty]` 已按 CommunityToolkit.Mvvm 8.4+ 的 WinUI 3 推荐改用 partial property + `LangVersion=latest`，消除 MVVMTK0045 警告） |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 131 项，失败 0 项 |
| `git diff --check` | 无空白错误（仅 LF→CRLF 归一化提示） |

### 待实机验证

- 悬浮卡 ToolTip 内容绑定（DataContext 继承）与分组网格（ItemsControl + GroupStyle）的运行时表现需真实窗口确认。
- 深色/浅色/高对比主题切换、100%/150%/200% DPI、窄窗 640px 下的分组网格与筛选条无溢出走查。
- 悬浮详情懒加载的官方 `itemdata` 字段（special_values 中文标签、冷却/魔耗系列值）需联网复核。

# 神盾教练操作记录

本文件记录每次继续开发后的构建、测试和应用操作。命令均从 `E:\openSource\demo` 执行；结果以实际输出为准。

> 这是按日期保存的历史证据。旧条目中的 AppData 路径、端口设置方式、模型后端、构建输出和测试数量只代表当时状态，不是当前配置。当前行为、目录和完成度以 `docs/PROJECT_DOCUMENTATION.md` 为准；历史事实不因后续实现变化而删除。

## 2026-08-14（第七轮）：联网全量同步、缓存精简、天赋树与技能数值修复
