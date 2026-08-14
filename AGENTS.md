# 神盾教练研发 Agent 指令

本文件约束在本仓库内工作的代码 Agent。产品运行时的教练模型提示词由 `docs/AI_AGENT_PROMPT_TEMPLATES.md` 管理，两者不得混用。

## 0. 沟通与输出语言（中文优先）

本仓库内所有面向用户与面向运行的产出，一律中文优先：

- **提示（Prompt）**：生成的指令、需求描述、教练/分析模板默认中文撰写；代码标识符、API 名、专有名词保留原文。
- **反馈（Feedback）**：对用户的回答、进度汇报、验证结论、风险说明默认使用中文。
- **运行信息（Runtime Info）**：代码注释、日志消息、CLI 输出、错误提示、UI 文案默认中文优先，技术字段与日志标识符保留原文。

## 1. 开始前

1. 先执行 `git status --short`，保留用户和其他 Agent 的已有改动。
2. 按任务读取 `README.md`、`docs/PROJECT_DOCUMENTATION.md`、`docs/ARCHITECTURE_REVIEW.md` 和相关源码；只有需要历史证据时才查 `docs/OPERATION_LOG.md`。
3. 先写清因果链：输入或事件 -> 经过的服务/状态 -> 失效点 -> 用户可见结果 -> 验证方式。
4. 找出任务背后更关键的问题并指出来，但不得用空洞的抽象层级代替证据。

## 2. 当前真实架构

- 唯一产品入口是 `native/AegisCoach.Native`，技术栈为 WinUI 3、C#、.NET 8、Windows App SDK。
- `native/AegisCoach.Native/Core` 是同一项目内的纯逻辑源码目录，不是独立项目，也不生成 `AegisCoach.Core.dll`。
- `tools/GsiSmoke` 和 `tools/GsiReplay` 通过链接源码复用 `Core`。调整 Core 文件路径或依赖时，必须同时检查这两个工具项目。
- `MainWindow` 目前仍承担组合根、导航、页面状态和服务生命周期。新增功能不得继续把可独立测试的规则、解析、缓存或数据转换堆入窗口代码。
- 现阶段保持单产品项目。只有出现明确的独立发布、依赖隔离或编译边界需求时，才讨论恢复多项目结构。

## 3. 代码边界

| 变更类型 | 首选位置 |
|---|---|
| GSI DTO、归一化状态、提醒模型 | `Core/Models` |
| 接收、规则、回放、会话和赛后分析 | `Core/Services` |
| 外部 API、凭据、NPU/云端适配 | `Services` |
| API DTO 与展示模型 | `Models`，新增代码应按领域拆文件 |
| 页面布局与控件样式 | `Views`/`Themes`；现有页面可从 `MainWindow.xaml` 渐进迁移 |
| 页面状态与命令 | `ViewModels`；不得依赖具体控件实例 |
| 窗口级导航和服务装配 | `MainWindow` / `App` |

改动必须遵守：

- 规则引擎先产生确定性结果，AI 只能解释，不得改变触发结果。
- 网络、模型或统计增强失败时，不能阻断 GSI 接收和本地规则提醒。
- 外部字段使用结构化 JSON 解析；不使用字符串替换模拟 schema 兼容。
- 所有长生命周期异步工作接受并传递 `CancellationToken`，窗口关闭时停止。
- token、API key 和完整 Steam ID 不进入日志、报告、模型 prompt 或提交内容。
- 不读取游戏进程内存，不注入或 Hook，不模拟输入，不自动操作，不提供反作弊规避方案。

## 4. UI 设计 Prompt

处理任何 WinUI 页面、控件、样式或交互任务时，把下面内容作为附加 prompt：

```text
你正在设计神盾教练的 WinUI 3 桌面界面。它是中文优先、面向重复使用的 Dota 2 资料与教练工具，不是营销落地页。

目标：信息密集但可扫描，接近 Dota 2 游戏资料面板；强调任务状态、来源、时间和证据，不用装饰替代层级。

硬性约束：
1. 复用 App.xaml 与 Themes/GameReferenceStyles.xaml；颜色优先使用 ThemeResource 和 Windows 系统语义资源，支持 Light、Dark、HighContrast。组件中不新增散落的硬编码色值。
2. 使用 NavigationView 保持一级导航稳定，当前项必须清楚；返回列表时恢复滚动、筛选、选择和输入状态。
3. 用 Grid、ItemsRepeater/ItemsControl 和 VisualStateManager/AdaptiveTrigger 做响应式布局。至少覆盖窄窗 640px、常规 1024px、宽窗 1440px，以及 100%/150%/200% DPI；不得出现横向溢出、文字遮挡或固定列数崩坏。
4. 页面必须分别设计 loading、empty、error、degraded、ready 状态。错误靠近对应区域，并提供明确动作，如“重试”“选择目录”，不能只写“失败”。
5. 所有功能可用键盘完成；Tab 顺序符合视觉顺序，焦点样式可见。图标按钮必须有 AutomationProperties.Name 或 ToolTip；关键文本与背景对比度至少 4.5:1，非文本状态不能只靠颜色表达。
6. 触控/点击目标尽量不小于 44x44，有至少 8px 间距。高密度信息可缩短内边距，但不能缩小可操作区域。
7. 使用 WinUI SymbolIcon/FontIcon 或项目既有图标，不用 emoji，不手画重复图标。命令用图标或图标加短文本；模式选择用单选/分段控件，布尔值用 ToggleSwitch/CheckBox。
8. 动效只用于解释状态变化，通常 150-250ms，并尊重系统减少动画设置；不使用环境光斑、重度模糊、装饰性渐变或持续动画。
9. 不在卡片中嵌套卡片。页面区段保持无框布局；卡片只用于重复条目、明确的详情单元或工具面板，圆角不超过项目现有值。
10. 不展示 GSI 不提供的敌方位置、技能、视野或经济信息；统计必须同时显示样本、范围和来源，样本不足时降级。

交付前输出：用户主路径、状态矩阵、键盘路径、响应式行为、数据来源/限制；随后完成构建，并列出仍需真实桌面验证的项目。
```

设计决策优先级：可访问性 -> 操作反馈 -> 数据可信度 -> 响应式布局 -> 视觉一致性 -> 动效。Dota 风格只能影响材质、密度与强调色，不能牺牲系统主题和可读性。

## 5. 渐进式架构规则

- 修改某个大页面时，优先把该页面完整迁移为 `Views/<Name>Page.xaml(.cs)`；不要为了“统一”一次拆完所有页面。
- 页面首次需要复杂加载、筛选、取消或多状态切换时，引入对应 ViewModel。简单展示控件不强制 MVVM。
- 新服务通过构造函数或一个集中组合根传入；不要继续在页面字段初始化器里创建跨页面服务。
- 当模型文件超过一个领域时按 Hero、Item、Coach、Analysis 拆分；序列化 converter 与 UI 展示模型分开。
- 抽取后必须保持现有用户状态，包括外层滚动位置、筛选条件、选中英雄/物品和返回路径。

## 6. 验证矩阵

按改动范围运行最小充分验证：

```powershell
dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64
dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore
git diff --check
```

- Core、GSI、规则、缓存、设置或赛后分析变更：构建 + GsiSmoke。
- 仅文档变更：检查链接、路径、命令与 `git diff --check`。
- UI 变更：构建 + 键盘/主题/窄窗/DPI/五类状态走查。只有确认捕获的是 AegisCoach 实际窗口后，截图才算证据。
- 外部 API 变更：补 fixture 或最小解析用例；没有网络实测时明确写“未联网验证”。
- 若标准 WinUI 构建因现有 `obj/.../input.json` 被占用或拒绝覆盖而失败，先关闭 Visual Studio/残留构建进程；需要区分代码错误时，可保留现有还原资产并仅隔离实际编译输出：

```powershell
dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --no-restore -p:Platform=x64 -p:IntermediateOutputPath=E:\openSource\demo\.build\native-compile-int\ -p:OutputPath=E:\openSource\demo\.build\native-compile-bin\
```

不要同时改 `BaseIntermediateOutputPath` 后使用 `--no-restore`，因为新目录没有 `project.assets.json`；网络受限时重新还原也可能失败。

最终答复必须区分：已修改、自动验证、未验证风险。不得把编译成功写成 UI、联网、NPU 或真实 Dota 2 对局验收成功。
