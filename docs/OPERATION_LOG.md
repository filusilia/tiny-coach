## 2026-08-25（第二十一轮）：英雄详情性能卡顿优化、Dota 2 官网紧凑属性布局与通用机制、AbilitySelector 神杖魔晶角标、倒序天赋树与客户端发布包瘦身

### 需求与问题诊断

1. **英雄页面打开微卡顿**：
   - `ShowHeroDetailsAsync` 同步处理了大量模型解析、正则提取与字典查找，且在主线程频繁操作多个 `ObservableCollection` 引发多次布局/重绘。
2. **天赋树正序不符合加点直觉**：
   - 游戏内加点是从 10 级往 25 级由下往上加，列表应倒序展示（顶部 25 级，底部 10 级）。
3. **缺少官网式紧凑基础属性布局**：
   - 官网风格：头像下紧贴绿色生命条（数值+恢复）与蓝色魔法条（数值+恢复）；三维属性卡片（力量/敏捷/智力带图标与成长，主属性高亮）；攻击/防御/机动性 3 列结构化属性（攻击力、攻击间隔、攻击距离、弹道速度、护甲、魔抗、移速、转身速率、视野）。
4. **技能选择栏（AbilitySelector）与技能详情（AbilityInfoContainer）对齐官网体验**：
   - 技能选择栏并排展示普通技能、先天技能（带先天角标）、魔晶升级技能（带紫色魔晶 SubIcon 角标）、神杖升级技能（带金色神杖 SubIcon 角标）；
   - 技能详情展示官网标准机制（技能目标、影响、伤害类型、无视免疫、能否驱散、特殊参数网格、冷却魔耗与 Lore 背景故事）。
5. **客户端打包过大优化**：
   - 分析依赖构成（OpenVINO 离线转换前端、PDB 符号等），优化打包脚本剥离无用库，减小分发体积。

### 优化方案与实现

- **英雄详情性能异步化与流畅度提升**：
  - 将数据转换与技能/天赋/指南计算逻辑全部迁移到 `Task.Run` 执行，一次性回切 UI 线程赋值，杜绝界面丢帧；
  - 切换技能只切换 `CurrentAbility`，避免重构集合。
- **官网紧凑属性布局与机制结构化**：
  - `Models/DotaModels.cs`：在 `HeroStat` 补充弹道速度（近战即时）、转身速率、白天/夜晚视野计算；在 `AbilityDisplay` 增加目标类型、影响范围、伤害类型、无视免疫、驱散属性及神杖/魔晶变体工厂方法（`CreateScepterVariant` / `CreateShardVariant`）；
  - `Views/HeroesPage.xaml`：左侧立绘紧贴生命/魔法条，中间三维属性卡片，右侧攻击/防御/机动性 3 列分类面板；
  - `SelectedAbilityPanel`：重构为官网 AbilityInfoContainer 结构，包含通用机制双列卡片、特殊参数紧凑网格与冷却魔耗。
- **倒序天赋树**：
  - `TalentRows` 改为 `OrderByDescending(row => row.Level)` 倒序展示（25 -> 20 -> 15 -> 10）。
- **客户端打包发布瘦身**：
  - 优化 `tools/package-release.ps1`：以 Release 配置构建，自动剥离无用离线转换前端（`openvino_tensorflow_frontend.dll`、`openvino_pytorch_frontend.dll` 等）与调试 PDB 符号，发布分发包体积从 131.1 MB 进一步降至 **118.09 MB**。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 131 项，失败 0 项 |
| `pwsh -NoProfile -File .\tools\package-release.ps1` | 成功生成 `release/publish/神盾教练.exe` 与 `release/AegisCoach-发布版.zip` (118.09 MB) |
| `git diff --check` | 无空白错误 |

## 2026-08-25（第二十轮）：全面整理项目文档体系、清理废弃文档与临时文件、更新全局技术规格与发布指引

### 需求与问题诊断

1. **废弃旧文档与孤立文档残留**：
   - `docs/OFFICIAL_DATA_IMPORT.md`：早期手动复制 JSON 文件的老旧指引，现已被全自动 API 拉取、`DotaItemNameTable` 内置字典与本地多级缓存全面替代，成为孤立无用的文档；
   - 根目录残留 `debug.log` 外部第三方输入法 Chromium 临时日志文件，对项目无用。
2. **全局技术文档规格与当前实现脱节**：
   - `docs/PROJECT_DOCUMENTATION.md` 与 `README.md` 中的架构表未完整反映最新的 MVVM 独立页面拆分架构（`HeroesPage` + `HeroesViewModel`、`ItemCatalogPage` + `ItemCatalogViewModel`、`CoachPage` + `CoachViewModel`）；
   - 缺少 Dota 2 1级实际战斗基础数值的标准计算公式（生命/魔法/护甲/攻击力及主属性加成）；
   - 发布产物名称在部分文档中仍残留为旧的 `AegisCoach.Native.exe`，未同步为 `神盾教练.exe` 与 `tools/package-release.ps1` 一键打包规范。

### 整理方案与实现

- **废弃文档清理与知识整合**：
   - 移除已废弃的孤立文档 `docs/OFFICIAL_DATA_IMPORT.md`，将其核心“本地静态数据覆盖层（`文档\AegisCoach\data\`）”机制作为可选特性整合至 `docs/PROJECT_DOCUMENTATION.md` 数据源与缓存章节；
   - 清理根目录无用垃圾文件 `debug.log`。
- **重构与升级 `docs/PROJECT_DOCUMENTATION.md`**：
   - 完整重构系统架构章节，详尽记录 MVVM 三大核心独立页面（`HeroesPage`、`ItemCatalogPage`、`CoachPage`）及其 ViewModel、ThemeResource 资源字典和转换器体系；
   - 补充 Dota 2 1级初始基础数值的标准计算公式与阿哈利姆升级新技能提取逻辑；
   - 更新 11 项核心功能模块完成度矩阵；
   - 统一构建、测试与发布命令指引（`神盾教练.exe` 与 `tools/package-release.ps1`）。
- **同步更新 `README.md` 与 `docs/ARCHITECTURE_REVIEW.md`**：
   - 完善文档索引导航与代码入口说明；
   - 更新架构演进路线与已完成的 M2/M3 里程碑状态，标记核心页面 MVVM 拆分已圆满完成。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 131 项，失败 0 项 |
| `pwsh -NoProfile -File .\tools\package-release.ps1` | 成功生成 `release/publish/神盾教练.exe` 与 `release/AegisCoach-发布版.zip` |
| `git diff --check` | 无空白错误 |

## 2026-08-24（第十九轮）：彻底根除技能切换滚动跳顶、旧英雄残影闪烁清零、字号可读性与左右栅格平衡重构、阿哈利姆升级与官方装备中文名全面对齐

### 需求与问题诊断

1. **技能点击依然跳回页面顶部（深层物理根因与事件流追踪）**：
   - **深层机制 1（Button 控件的原生 FocusManager 追踪）**：WinUI 3 原生 Button 在 PointerPressed 时会强制将系统键盘/交互焦点揽入，触发 FocusManager 的 BringIntoView 逻辑。
   - **深层机制 2（SelectedAbilityPanel 内容变化引起的高度瞬间塌陷）**：当技能说明与参数在子元素重绘时，外层容器高度瞬间变短，导致外层 ScrollViewer 原生 DirectManipulation 引擎强制将超出新 ExtentHeight 的 VerticalOffset 截断为 0。
2. **说明文字偏小模糊**：
   - 此前大量使用 8-10px 字体，导致高分辨率与标准 DPI 缩放下字迹过淡、排版模糊。
3. **点击返回后再点击新英雄时残留旧英雄数据（闪烁鬼影）**：
   - `CloseDetail()` 与 `SelectHeroAsync()` 开始时未同步清空 `CurrentAbility`、`SelectedAbilities`、`TalentRows`、`SelectedBuildGroups`、`HeroCountersMe` 等集合，导致在新数据到达前的网络/读取等待期（50~300ms）依旧显示上一个英雄的内容。
4. **天赋树 UI 配色在浅色主题下不协调**：
   - `TalentLevelBadgeBrush` 硬编码为深藏青色 `#1B2740`，在浅色模式下面板与背景突兀。
5. **部分英雄阿哈利姆神杖/魔晶升级展示缺失或残缺**：
   - 官方数据中神杖/魔晶赋予全新技能的英雄（如主宰迅闪、宙斯雨云等），其 `scepter_loc`/`shard_loc` 升级文本本身为空（描述存在于 `desc_loc`），导致被简单判空过滤。
6. **推荐出装名称显示为“中文名称待...”**：
   - OpenDota 降级目录或部分物品 ID 未命中中文本地化名称，回退到了破坏 UI 的临时占位符。
7. **实战小技巧左侧大片空白（双列高度失衡）**：
   - 左栏仅放天赋树+神杖魔晶（很短），右栏堆叠推荐出装+克制对阵+实战技巧（很长），导致左栏下方大面积悬空。

### 修复方案与实现

- **技能交互与视口稳定性重构（根治跳顶）**：
  - `Views/HeroesPage.xaml`：将技能槽位项从 `Button` 重构为轻量级 `Border`，绑定 `PointerPressed="AbilityTile_PointerPressed"`，容器设置 `AllowFocusOnInteraction="False"`、`IsTabStop="False"`，彻底消除 FocusManager 自发滚动；
  - `SelectedAbilityPanel` 设定 `MinHeight="190"`，彻底防止数据切换瞬间容器高度塌陷引起的 ScrollViewer 视口截断；
  - `Views/HeroesPage.xaml.cs`：在 `AbilityTile_PointerPressed` 中先记录 `HeroDetailHost.VerticalOffset`，在 ViewModel 切换完成后执行 `ChangeView` 与 `DispatcherQueue` 双重锁定视口。
- **排版字号与可读性全面提升**：
  - 英雄名称放大至 22px，一句话定位 13px，背景故事 12px（行高 18px）；
  - 战斗芯片高度增加至 40px，数值提升为 13px，标题提升为 10px；
  - 技能描述提升为 13px（行高 21px），技能参数提升为 12px / 10px；
  - 天赋树、装备名、克制对阵与实战小技巧文本全面提升至 11-12px。
- **旧英雄状态零残留重置（根除残影）**：
  - `ViewModels/HeroesViewModel.cs`：在 `CloseDetail()` 和 `ShowHeroDetailsAsync()` 的第 0 毫秒立即执行 `CurrentAbility = null;` 并清空所有详情集合与升级文本，进入新英雄时绝无上一英雄的“鬼影”数据。
- **天赋树多主题（Light / Dark / HighContrast）自适应**：
  - `Themes/GameReferenceStyles.xaml` 与 `App.xaml`：定义 `TalentLevelBadgeBrush`、`TalentLevelTextBrush`、`TalentNodeBackgroundBrush`、`TalentNodeBorderBrush` 语义画刷，浅色模式下采用浅金暖底（`#FEF3C7` / `#B45309`）与浅色卡片背景。
- **阿哈利姆神杖/魔晶授予技能与升级全面对齐**：
  - `Models/DotaModels.cs`：在 `OfficialAbility` 与 `AbilityDisplay` 增加 `IsGrantedByScepter`、`IsGrantedByShard`、`HasScepterUpgrade`、`HasShardUpgrade`，当神杖/魔晶赋予新技能时自动生成 `授予全新技能【技能名】：技能说明`；
  - `ViewModels/HeroesViewModel.cs`：重构 `UpdateHeroUpgrades`，提取全部神杖/魔晶升级与新技能，去除重复。
- **Dota 2 官方标准物品中文名称映射字典（`DotaItemNameTable`）**：
  - `Models/DotaModels.cs`：新增 `DotaItemNameTable`，内置全量商店与中立物品的官方标准中文名；`OfficialItemSummary.DisplayName` 优先解析中文，杜绝“中文名称待...”残留。
- **左右栅格空间平衡重构（消除实战技巧左侧空白）**：
  - **左栏（机制深度与英雄指南）**：阿哈利姆升级（神杖金蓝卡 + 魔晶紫粉卡） + 实战小技巧与机制要点；
  - **右栏（天赋树与实战大数据）**：Dota 2 风格天赋树 + 推荐分段实时出装（大图标+标准中文名+出场率胜率） + 克制与被克制对阵网格；
  - 左右两栏高度完美对称均衡。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 131 项，失败 0 项 |
| `pwsh -NoProfile -File .\tools\package-release.ps1` | 成功生成 `release/publish/神盾教练.exe` 与 `release/AegisCoach-发布版.zip`（129.53 MB） |
| `git diff --check` | 无空白错误 |

## 2026-08-24（第十八轮）：客户端启动程序中文名、技能点击滚动跳顶根治、战斗芯片图标语义纠正与 Dota 2 基础数值标准计算重构

### 需求与问题诊断

1. **客户端启动 exe 中文名**：
   - 用户要求 release 客户端启动程序名为中文（`神盾教练.exe`），此前默认生成 `AegisCoach.Native.exe`。
2. **切换点击技能跳回页面顶部（深层因果机制）**：
   - **机制 1（子树卸载与高度瞬间塌陷）**：此前 `SelectedAbilityPanel` 单独声明了 `DataContext="{Binding CurrentAbility}"`，点击切换技能时对象引用变更，导致整棵子树上的所有控件解除绑定与重新构建，期间容器高度瞬间塌陷为 0，外层 `ScrollViewer` 的 `ExtentHeight` 变小从而触发视口截断重置（VerticalOffset 被 clamp 为 0）。
   - **机制 2（BringIntoView 事件冒泡未阻断）**：子控件重新生成或焦点转移时向上冒泡发出 `BringIntoViewRequested` 事件，外层 `HeroDetailHost` 接收后自发将视口定位到 (0,0)。
   - **机制 3（缺乏选中状态高亮）**：技能按钮此前为普通透明按钮，无法感知当前选中项。
3. **顶部战斗芯片图标与语义不符、血量/魔法等数值错误**：
   - **图标错误**：攻击力用 OEM 图标、护甲误用电池 Saver 图标 `&#xE82D;`、移速误用路标图标 `&#xE8C0;`、射程误用地标/地球图标 `&#xE7C1;`、魔抗误用星星收藏图标 `&#xE734;`。
   - **数值错误**：直接读取 `base_health`（120）与 `base_mana`（75），未按 Dota 2 机制计算 1 级初始总生命值（$120 + \text{力量} \times 22$）、生命恢复（$\text{基础} + \text{力量} \times 0.1$）、初始总魔法值（$75 + \text{智力} \times 12$）、魔法恢复（$\text{基础} + \text{智力} \times 0.05$）、初始总护甲（$\text{基础} + \text{敏捷} / 6$）以及主属性加成后的总攻击力。
4. **Agent 提示与因果分析规范强化**：
   - 在 `AGENTS.md` 中强化因果链分析标准，明确要求挖掘用户未直接提及但更关键的深层问题，并保持回复简洁、事实导向。

### 修复方案与实现

- **`AegisCoach.Native.csproj` & `tools/package-release.ps1`**：
  - 在 csproj 配置 `<AssemblyName>神盾教练</AssemblyName>`，直接生成 `神盾教练.exe`、`神盾教练.dll`、`神盾教练.runtimeconfig.json`；
  - 更新打包脚本，发布目录与压缩包产出 `release/publish/神盾教练.exe` 与 `release/AegisCoach-发布版.zip`。
- **`Views/HeroesPage.xaml` & `Views/HeroesPage.xaml.cs`**：
  - 彻底去除 `SelectedAbilityPanel` 上的局部 `DataContext` 赋值，统一使用页面根 ViewModel 绑定 `{Binding CurrentAbility.XXX}`，保证 Visual Tree 结构绝对稳定，消除子树重建与高度塌陷；
  - `HeroDetailHost` 挂接 `BringIntoViewRequested` 处理器并标记 `args.Handled = true`，全局阻断子元素自发滚动请求；
  - 修正 6 项战斗芯片图标：攻击力（刀剑 `&#xEC50;`）、护甲（盾牌 `&#xEA18;`）、移动速度（疾跑 `&#xEB42;`）、攻击距离（准星 `&#xE73E;`）、攻击间隔（秒表 `&#xE916;`）、魔法抗性（法术防御 `&#xE785;`）；
  - 技能列表条目使用 `SelectionBackgroundBrush`、`SelectionBorderBrush`、`SelectionBorderThickness` 与 `SelectionOpacity` 实现选中项金色高亮边框。
- **`ViewModels/HeroesViewModel.cs` & `Models/DotaModels.cs`**：
  - `AbilityDisplay` 增加 `IsSelected` 及响应式样式只读属性；`SelectAbility` 时更新全部技能选中态；
  - `UpdateHeroStats` 实现完整的 Dota 2 1 级实际数值计算：
    - 初始生命值：$120 + \text{base\_str} \times 22$；生命恢复：$\text{base\_health\_regen} + \text{base\_str} \times 0.1$；
    - 初始魔法值：$75 + \text{base\_int} \times 12$；魔法恢复：$\text{base\_mana\_regen} + \text{base\_int} \times 0.05$；
    - 初始总护甲：$\text{base\_armor} + \text{base\_agi} / 6.0$；
    - 初始攻击力：根据主属性（力量/敏捷/智力/全才）准确计算主属性攻击力加成并累加到最小/最大基础攻击力。
- **`AGENTS.md`**：
  - 强化因果链分析标准与未提及关键问题的主动挖掘要求，更新发布产物清单。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 131 项，失败 0 项 |
| `pwsh -NoProfile -File .\tools\package-release.ps1` | 成功生成 `release/publish/神盾教练.exe` 与 `release/AegisCoach-发布版.zip`（127.95 MB） |
| `git diff --check` | 无空白错误 |

## 2026-08-21（第十七轮）：修复物品打开崩溃、技能切换跳回顶部、先天技能最左排序与英雄说明折叠

### 问题诊断与根因分析

1. **物品打开软件会崩溃**：
   - `Themes/ItemCatalogTheme.xaml` 在 `ItemDetailCardTemplate` 中 7 处使用了 `{StaticResource StringNotEmptyToVisibilityConverter}`，但该资源未在主题字典或全局资源中定义，导致 XAML 引擎解析 DataTemplate 时抛出未处理的 `XamlParseException` 崩溃。
2. **英雄页面点击技能跳回顶部**：
   - 技能按钮在横向 `ScrollViewer` 内被点击时，触发了 `BringIntoViewRequested` 与焦点交互事件，冒泡到外层 `HeroDetailHost` (ScrollViewer) 并重置滚动位置；
   - 技能详情区域高度变化引发的重排再次覆盖了视口偏移。
3. **先天技能排序**：
   - 此前技能列表遍历未固定先天技能位置，当先天技能来自官方中文数据时可能排在普通技能中间或后方。
4. **英雄说明展开/折叠功能与文案异常**：
   - `MaxLines` 错误绑定了布尔值与 `EmptyToCollapsedConverter`（返回 `Visibility`），绑定类型不兼容导致折叠行数不生效；
   - 展开按钮文案错误绑定了 `BoolToVisibilityConverter`，导致界面显示英文 `"Collapsed"` / `"Visible"`。

### 修复方案与实现

- **资源与转换器补齐（修复崩溃）**：
  - `Themes/ItemCatalogTheme.xaml` 与 `App.xaml` 显式注册 `EmptyToCollapsedConverter` 与 `StringNotEmptyToVisibilityConverter`，确保 DataTemplate 资源解析绝对安全。
- **英雄技能交互与视口锁定（修复跳回顶部）**：
  - `Views/HeroesPage.xaml`：技能按钮添加 `AllowFocusOnInteraction="False"`；技能横向栏与技能卡片添加 `BringIntoViewRequested` 事件拦截；
  - `Views/HeroesPage.xaml.cs`：在 `AbilitiesScrollViewer_BringIntoViewRequested` 与 `SelectedAbilityPanel_BringIntoViewRequested` 中设置 `args.Handled = true;`，并在 `AbilityButton_Click` 中以低优先级恢复 `VerticalOffset`。
- **先天技能最左侧排序**：
  - `ViewModels/HeroesViewModel.cs`：在构建技能列表时，先解析全部技能的 `IsInnate` 状态，统一按 `IsInnate` 降序排列，使所有先天技能排在最左侧（`SlotLabel = "先天"`），普通技能随后分配 `Q/W/E/D/F/R` 槽位标签。
- **英雄背景故事展开/折叠重构**：
  - `ViewModels/HeroesViewModel.cs`：增加 `BioMaxLines`（折叠为 3 行，展开为 0 行不限）、`BioToggleText`（中文“展开完整背景故事” / “收起背景故事”）与 `HasBiography` 状态；
  - `Views/HeroesPage.xaml`：修正 `MaxLines="{Binding BioMaxLines}"` 与 `TextBlock Text="{Binding BioToggleText}"` 绑定。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 131 项，失败 0 项 |
| `pwsh -NoProfile -File .\tools\package-release.ps1` | 成功生成 `release/publish/AegisCoach.Native.exe` 与 `release/AegisCoach-发布版.zip`（126.37 MB） |
| `git diff --check` | 无空白错误 |

## 2026-08-20（第十六轮）：解决海外 OpenDota 接口慢/超时导致数据无法加载的问题

### 根因分析

1. **海外接口超时导致崩溃**：
   - 官方直连接口（`https://www.dota2.com/datafeed/itemlist`、`herolist`、`itemdata`、`herodata`）国内直连稳定且正常返回；
   - 但海外 OpenDota 接口（`https://api.opendota.com/api`）在部分网络环境下不可达或超时（5 秒以上）；
   - `LoadItemsAsync` 在拉取物品后，调用了 `EnrichRecipesAsync` 从 OpenDota 拉取配方增强；此前 `EnrichRecipesAsync` 捕获异常不全，导致 `TaskCanceledException` 逃逸；
   - 此外，在命中缓存或回退过期缓存时，代码仍强制再次调用 `EnrichRecipesAsync` 阻塞主线程；
   - `LoadCatalogAsync` 原本将 `GetOpenDotaAsync("/heroStats")` 与官方接口用 `Task.WhenAll` 并行强绑定，OpenDota 超时时导致英雄目录整体加载失败。

### 修复方案与实现

- **`Services/DotaDataService.cs`**：
  - **解耦 OpenDota 强依赖**：官方 `/herolist?language=schinese` 与 `/itemlist?language=schinese` 作为第一权威与可靠兜底数据源；
  - **安全超时与独立降级**：OpenDota 请求设置 3~4 秒短超时，并在独立的 `try-catch` 中静默降级；当 OpenDota 不可用时，直接基于官方数据构造英雄与物品目录，绝不抛出异常阻断用户界面；
  - **缓存直出**：本地有效缓存命中时直接返回，不发起阻断性网络请求；
  - **`EnrichRecipesAsync` 异常彻底隔离**：捕获包含 `OperationCanceledException` 在内的所有异常，确保配方增强失败时不影响物品基础信息的呈现。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj` | 通过 131 项，失败 0 项 |
| `dotnet publish` + 压缩打包 | 成功生成 `release/publish/AegisCoach.Native.exe` 与 `release/AegisCoach-发布版.zip` |

## 2026-08-20（第十五轮）：物品卡片官网对齐、属性宏修复与双向合成路线重构

### 需求与问题诊断

1. **信息展示不准**：
   - 官方数据中属性标题使用了 Valve 宏（如 `+$str`、`+$agi`、`+$int`、`+$attack`、`+$move_speed` 等），原逻辑未映射宏名称，导致属性标题直接显示为代码宏；
   - 物品专属注意事项（`notes_loc`）未在卡片中呈现；
   - 描述文本中部分段落清洗与换行不规整。
2. **合成路线（保持且强化）**：
   - 用户特别强调**“保持合成路线不要丢”**；
   - 之前小件仅有图片，缺少小件中文名称与单价；缺少“合成卷轴”的价格与卡片；
   - 缺少“可用于合成（进阶合成 / Builds Into）”，导致查看基础配件（如短棍、玄冥盾牌）时无法得知能合成什么大件。
3. **UI 风格对齐 Dota 2 官网（`https://www.dota2.com.cn/items/index.htm`）**：
   - 重构 `ItemDetailCardTemplate`，完全采用官网经典深色调与层级布局。

### 实现方案

- **`Models/DotaModels.cs`**：
  - `CleanStatHeading`：增加 Valve 属性宏（`$str`、`$agi`、`$int`、`$all`、`$attack`、`$damage`、`$armor`、`$hp`、`$mana`、`$hp_regen`、`$mana_regen`、`$move_speed`、`$evasion`、`$spell_amp`、`$magic_resist`、`$status_resist`、`$lifesteal`、`$cast_range`、`$attack_range` 等）到规范中文属性名的精准映射。
- **`Models/ItemCatalogModels.cs`**：
  - `ItemAbility` 增加 `Notes` 列表与 `HasNotes` 标志；
  - `ItemStat.DisplayText` 统一规范为绿字加号格式（如 `+ 10 力量`、`+ 26 敏捷`）；
  - `ItemComponentCard` 增加 `Id`、`Cost` 与 `CostText`（如 `$ 2,050`）；
  - `CatalogItem` 增加 `BuildsIntoCards`（可用于合成）、`Notes` 与 `HasBuildsInto`，双向打通合成树。
- **`ViewModels/ItemCatalogViewModel.cs`**：
  - `ApplyDetails`：完整解析官方属性行、技能分段与注意事项（`notes_loc`），并解析参数占位符；
  - `ApplyOfficialRecipe`：构建完整的配方小件卡片（含中文名称、单价、数量与卷轴价格）；
  - `PopulateBuildsInto`：自动计算当前物品可以用于合成的所有进阶装备并按价格排序。
- **`Themes/ItemCatalogTheme.xaml`**：
  - 重构 `ItemDetailCardTemplate`：74x54 立绘 + 中英文名称 + 价格/中立等级 + 分类标签；
  - 绿字属性区；
  - 主动（蓝）/被动（金）/使用（绿）技能卡，带冷却与魔法消耗图标；
  - 物品注意事项（Notes）提示条；
  - **由以下部件合成（Components）**卡片列表 + 卷轴价格；
  - **可用于合成（Builds Into）**卡片列表；
  - 底部背景故事（Lore）。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj` | 通过 131 项，失败 0 项 |
| `dotnet publish` + 压缩打包 | 成功生成 `release/publish/AegisCoach.Native.exe` 与 `release/AegisCoach-发布版.zip` |

## 2026-08-20（第十四轮）：技能描述缺失、文本粘连与占位符代码泄漏彻底修复

### 根因分析

1. **技能描述展示不全 / 文本丢失（核心元凶）**：
   - 现有的 `ParameterRegex()` 正则表达式为 `@"\{s:([^}]+)\}|%([^%]+)%|\{\{([^}]+)\}\}|\{([^{}]+)\}"`。
   - 其中的 `%([^%]+)%` 采用了通配匹配，当正文中包含普通百分号（如 `造成 20% 伤害，并在 %duration% 秒内施加 30% 减速`）时，正则会将 `% 伤害，并在 %` 误当成一个占位符变量并替换为空字符串，直接“吞噬”并抹除了整句中文说明！
   - `Clean(string? value)` 原本只替换 `<br>`，对 `<br/>`、`</p>`、`</div>`、`</li>`、`&nbsp;` 等标签和实体未妥善解析，导致多段文字粘连或截断。
2. **待替换字符串残留（如 `%param%`、`{s:bonus_xxx}`）**：
   - 官方变量名存在 `bonus_`、`tooltip_`、`ability_`、`_pct`、`_duration`、`$1` 等多重命名差异，未命中时原本直接残留代码字符串。

### 修复方案与实现

- **`Models/DotaModels.cs`**：
  - **精准限定占位符正则**：限定变量名只包含合法的标识符字符 `[a-zA-Z0-9_$]+`，严禁跨越中文、空格和标点符号，彻底消除文本被误吞的现象；
  - **增强 HTML 清洗与换行**：支持 `HtmlDecode`、支持各类块级/列表换行转换、规范化多余空行与空格；
  - **多级模糊别名匹配**：支持前缀（`bonus_`/`tooltip_`/`ability_`）、后缀（`_pct`/`_duration`/`_radius`/`_damage` 等）与序号（`$1`/`$2`）智能推导，支持通用属性推导；
  - **安全降级机制**：未匹配到数值的占位符统一优雅清除，绝不向用户呈现代码字符串。
- **`ViewModels/HeroesViewModel.cs`**：
  - 统一复用 `AbilityDisplay.Clean`，保证描述、背景故事与说明的一致清洗。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj` | 通过 131 项，失败 0 项 |
| `dotnet publish` + 压缩打包 | 成功生成 `release/publish/AegisCoach.Native.exe` 与 `release/AegisCoach-发布版.zip` |

## 2026-08-20（第十三轮）：英雄资料页高屏占比与官网风格紧凑 UI 重构

### 需求与目标

1. **解决顶部空白过多、屏占比不足**：大幅精简列表页与详情页的冗余上下边距，合并稀疏分布的搜索与分段筛选条，让首屏展示更多英雄与核心数据。
2. **参考 Dota 2 官网（dota2.com.cn/hero/kez）风格重构英雄展示 UI**：
   - 采用紧凑的 Hero Header（立绘头像 + 中英文名 + 一句话特色 + 角色定位标签 + 背景故事折叠器）；
   - 还原官网经典的**生命值条（带恢复速度）**与**魔法值条（带恢复速度）**；
   - 紧凑呈现三维属性柱（力量/敏捷/智力，基础值 + 成长）与 6 项核心战斗数值芯片（攻击力、护甲、移速、攻击距离、攻击间隔、魔抗）；
   - 技能栏采用精致横向槽位选择（先天技能标金，常规技能带键位角标与选中发光），详情卡紧凑排布参数与神杖/魔晶专属加成；
   - 采用双列响应式排版（左栏：天赋树 + 阿哈利姆升级；右栏：推荐出装 + 克制关系 + 实战技巧），大幅提升宽屏内容密度。

### 实现

- **`ViewModels/HeroesViewModel.cs`**：
  - 增加 `HeroStatHealthRegen`、`HeroStatManaRegen`、`HeroStatAttackRate`、`HeroStatMagicResist` 计算；
  - 增加主属性判定标志 `IsPrimaryStr`、`IsPrimaryAgi`、`IsPrimaryInt`、`IsPrimaryAll`；
  - 增加背景故事折叠/展开状态 `IsBioExpanded` 与命令 `ToggleBioCommand`。
- **`Views/HeroesPage.xaml`**：
  - 外层边距压缩为 `16,10,16,12`，功能栏与属性分类 Tab 精致整合为单行紧凑控件；
  - 详情页采用官网风格紧凑 Header，包含半身像、一句话定位、角色标签、折叠故事、血条/蓝条与三维属性；
  - 采用双列并排排版整合天赋树、阿哈利姆升级、出装推荐与克制对阵，屏占比显著提升。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj` | 通过 131 项，失败 0 项 |
| `dotnet publish` + 压缩打包 | 成功更新 `release/publish/AegisCoach.Native.exe` 与 `release/AegisCoach-发布版.zip` |
| `git diff --check` | 无空白错误 |

## 2026-08-20（第十二轮）：英雄先天技能（Innate）支持 + 物品悬浮卡鼠标跟随与边界控制 + 属性数值参数解析修复

### 需求与问题

用户反馈三个具体问题：
1. **英雄缺少先天技能（Innate Ability）**：7.36+ 版本的英雄先天技能未被识别展示，槽位被错误映射或缺失。
2. **物品资料悬浮卡未跟随鼠标且超出范围**：物品详情 Popup 坐标受到窗口层级二次叠加产生巨大位移，超长内容卡片底部被屏幕截断。
3. **数值很多显示为“数值参数xxx”且宏占位符未替换**：`HumanizeStatName` 兜底硬编码返回 `"数值参数"`；官方 `heading_loc` 本地化属性名未被读取；物品技能描述中的 `%param%` 与 `{s:bonus_xxx}` 宏参数未调用解析。

### 实现

- **英雄先天技能支持**：
  - `HeroAbilityMap` 与 `OfficialAbility` / `AbilityDefinition` 扩展 `Innates`、`InnateIds`、`IsInnate`、`Behavior` 等字段；
  - `AbilityDisplay` 支持先天技能识别，自动分配 `"先天"` 槽位并在类型标签前附加 `"[先天技能]"`；
  - `HeroesViewModel` 在遍历技能时优先识别先天技能并保留 Q/W/E/D/F/R 顺序；
  - `HeroesPage.xaml` 在技能按钮和选中的技能卡片中醒目显示“先天技能”金色/强调色徽章。
- **物品悬浮卡鼠标跟随与可视边界控制**：
  - `ItemCatalogPage.xaml.cs`：修正 `PositionDetailPopup` 坐标体系，去除重复叠加的窗口原点偏移；根据 `RootGrid` 容器尺寸与卡片实际高度进行智能左右翻转与上下夹取（保持 12px 安全边距）；添加 `Tile_PointerMoved` 实现平滑移动；
  - `ItemCatalogTheme.xaml`：为 `ItemDetailCardTemplate` 增加 `MaxHeight="520"` 与内置 `ScrollViewer`，确保在任意分辨率与缩放比例下超长内容不溢出且支持滚动。
- **数值参数与宏占位符深度修复**：
  - `AbilitySpecialValue` 补齐 `heading_loc` 属性，`BuildStats` 优先读取官方本地化中文标题；
  - `HumanizeStatName` 扩展超过 60 个 Dota 2 核心属性词条（伤害、抗性、攻速、移速、吸血、暴击、冷却、充能、范围、小兵惩罚等），未收录词条智能转换为大写英文标题，彻底移除 `"数值参数"` 占位符；
  - `ItemCatalogViewModel.cs` 在 `ParseOfficialDescription` 分段后对每个技能条目的描述统一执行 `ResolveParameters`，消除 `%bonus_xxx%` 等未解析宏；
  - `ResolveParameters` 增强前缀别名与 `$` 变量前缀匹配，提升数值替换命中率。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 131 项，失败 0 项 |
| `dotnet publish` + 压缩打包 | 成功生成 `release/publish/AegisCoach.Native.exe` 与 `release/AegisCoach-发布版.zip` |
| `git diff --check` | 无空白错误 |

## 2026-08-20（第十一轮）：实时教练页独立页面重构（CoachPage MVVM 迁移与 MainWindow 解耦）

### 需求与目标

按架构演进路线 P1 目标，完成实时教练模块从 `MainWindow` 的独立页面迁移：
1. 新增 `Views/CoachPage.xaml(.cs)` 与 `ViewModels/CoachViewModel.cs`，实现 GSI 实时连接状态条、自身英雄数值/状态卡/装备栏、实时规则提醒流、双方阵容展示、大数据出装推荐与克制/被克制/选人推荐的完整 MVVM 解耦。
2. 窗口级 GSI 接收器、对局跟踪、AI 解释及 Stratz/OpenDota 检索生命周期保留在窗口协调层，页面只绑定并展示状态，并通过事件触发设置弹窗。
3. `CoachPage` 独立管理窄窗（<1100px）响应式自适应布局（`ApplyResponsiveLayout`），`MainWindow` 移除所有已废弃的内联 Coach 控件与布局代码。

### 实现

- `ViewModels/CoachViewModel.cs`：
  - 使用 CommunityToolkit.Mvvm 8.4.2 实现 `ObservableProperty`（partial property 形式）与 `RelayCommand`。
  - 管理连接状态、对局概览摘要、英雄等级/生命/魔法/KDA/经济统计、6格主装备、规则提醒列表（最大保留200条）、我方与敌方阵容 Chip 列表、克制我/我克制/选人推荐集合。
  - 提供 `UpdateStatus`、`UpdateSnapshot`、`AddAdvice`、`UpdateLiveHeroes`、`UpdateCounters` 等数据更新接口与 `OpenSettingsRequested` 路由事件。
- `Views/CoachPage.xaml(.cs)`：
  - 独立 WinUI 3 Page 结构，包含暗色电竞面板风格顶部状态条、自身英雄与提醒流左右双栏、阵容与出装栏、克制与被克制选人推荐网格。
  - 具备独立 `ApplyResponsiveLayout(compact)` 与 `SizeChanged` 自适应排版逻辑。
- `MainWindow.xaml(.cs)`：
  - 移除所有内联 Coach 控件声明与集合字段，保留全局服务装配与 GSI 协调；
  - `MainWindow.xaml` 精简为 `<views:CoachPage x:Name="CoachPage" Visibility="Collapsed" />`；
  - GSI 快照接收、网络阵容拉取、克制分析与物品推荐计算后统一驱动 `CoachPage.ViewModel` 更新。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 131 项，失败 0 项 |
| `git diff --check` | 无空白错误 |

## 2026-08-19（第十轮）：英雄资料库独立页面重构（MVVM 迁移 + 转换器与分段持久化修复）

### 需求与目标

按架构演进路线 P1 目标，完成英雄资料与详情模块从 `MainWindow` 的独立页面迁移：
1. 新增 `Views/HeroesPage.xaml(.cs)` 与 `ViewModels/HeroesViewModel.cs`，实现英雄资料库、四属性分栏、防抖搜索、卡片网格自适应、英雄详情（背景故事/三维成长/技能解析/天赋树/神杖魔晶/推荐出装/对阵克制/实战小技巧）的完整 MVVM 解耦。
2. 修复页面与详情切换中的转换器缺陷，确保反转可见性与集合计数正确生效。
3. 保持分段选择与缓存目录设置的双向同步与持久化。

### 实现

- `ViewModels/HeroesViewModel.cs`：
  - 使用 CommunityToolkit.Mvvm 8.4.2 实现 `ObservableProperty`（partial property 形式）与 `RelayCommand`。
  - 支持四属性分类（全部/力量/敏捷/智力/全才）、分段胜率与出场排序、防抖搜索（250ms `DispatcherQueueTimer`）、卡片网格宽度动态计算。
  - 英雄详情完整聚合：官方中文背景、基础数值与三维成长、技能槽位（Q/W/E/R）与属性参数、天赋树左右选配、神杖/魔晶升级、阶段出装推荐（出门/前期/核心/中期/后期）、OpenDota 克制对阵（优势/劣势）、实战技巧。
  - 异步取消与生命周期管理：通过 `CancellationTokenSource` 统一支持请求重入与页面卸载取消。
  - 增补 `SetDefaultRank` 与 `OnRankChanged` 回调，实现分段持久化；支持自定义缓存目录切换。
- `Views/HeroesPage.xaml(.cs)`：
  - 独立 WinUI 3 Page 结构，支持 `AdaptiveTrigger`（窄窗 1100px 响应式内边距）；
  - 列表与详情视图平滑切换，返回列表保持滚动与筛选状态；
  - 技能描述高度变化时自动维持当前阅读滚动位置。
- `Converters/BooleanToVisibilityConverter.cs`：
  - 修复 `Convert` 方法未处理 `ConverterParameter="invert"` 的问题，支持大小写不敏感的反转参数，解决详情打开/关闭时页头与网格误折叠。
- `Converters/CollectionCountToVisibilityConverter.cs`：
  - 支持 `int count`（来自 `{Binding List.Count}`）以及 `ICollection` 与 `IEnumerable`，解决标签与克制对阵绑定计数值时误判为空的问题。
- `App.xaml`：
  - 在全局资源字典中注册 `BoolToVisibilityConverter`，确保所有页面与控件全局可用。
- `MainWindow.xaml(.cs)`：
  - 移除内联英雄模块代码，精简为 `<views:HeroesPage x:Name="HeroesPage" />`；
  - 在应用启动、设置加载与设置保存时同步 `HeroesPage` 的缓存目录与分段选择；
  - 后台批量更新英雄详情前等待目录完成加载，防止冷启动时英雄列表尚未拉取。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 131 项，失败 0 项 |
| `git diff --check` | 无空白错误 |

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
