# 2026-09-11（第二十六轮）：ViewModel 状态恢复与取消闭环、OpenVINO GenAI 真机推理驱动、GSI 报文安全与 Advice 快照时序绑定

### 需求与问题诊断

1. **ViewModel CancellationTokenSource 不可逆取消**：
   - HeroesViewModel、ItemCatalogViewModel、AnalysisViewModel 中均将 _cts 声明为 private readonly CancellationTokenSource _cts = new();。
   - 当用户在导航中切走页面触发 Cancel() 后，_cts.IsCancellationRequested 永久变为 true。当用户切回页面再次点击英雄或物品时，后续发起的异步任务会立刻抛出 TaskCanceledException，导致详情面板永久瘫痪、转圈或空白。
2. **NPU 真实模型推理未落地**：
   - 原 NpuGenAiCoachService.cs 虽检测了 OpenVINO 硬件设备，但推理核心 ExplainAdviceAsync 内部直接返回模拟字符串（$"[NPU 本地规则解释] 触发原因: {advice.Message}"），未引入并调用真实 LLMPipeline。
3. **AI 规则解释快照时序脱钩**：
   - 规则触发产生 CoachAdvice 时未绑定当时的 MatchSnapshot；MainWindow 收到 Advice 后调用 AI 解释时，直接取当时全局最新的 _liveMatch 快照。在高频 GSI 事件中，实时数据可能已发生变化，导致“解释依据的快照”与“触发建议时的现场数据”出现严重时序漂移。
4. **GSI 报文缺乏边界长度限制与安全防御**：
   - GsiReceiver.cs 直接按客户端上报的 contentLength 分配缓冲，缺乏上限保护，容易遭受畸形大包 OOM 攻击；且未根据 contentLength 进行精确切片。
5. **MainWindow 缓存字典缺乏并发安全保障**：
   - _popularityCache 与 _matchupCache 为标准 Dictionary<int, ...>，在多异步任务（克制推荐、出装分析）并发调用时存在哈希表竞态破坏风险。
6. **SDK 版本不匹配**：
   - global.json 硬编码为 10.0.400，而本机开发环境为 .NET SDK 10.0.401，造成构建还原告警。

### 优化方案与实现

- **ViewModel 优雅取消与活跃 Token 自动重置**：
  - 在 HeroesViewModel、ItemCatalogViewModel、AnalysisViewModel 中将 _cts 改为可重置模式，实现 GetActiveToken()：当旧 Token 被取消或为空时，安全释放旧实例并原子化创建新的 CancellationTokenSource，彻底解决切出页面后再切入加载失败的致命缺陷。
- **OpenVINO GenAI 真实推理驱动接入**：
  - 在 AegisCoach.Native.csproj 补充 JYPPX.OpenVINO.CSharp.API 与 JYPPX.OpenVINO.GenAI.runtime.win 依赖。
  - 在 NpuGenAiCoachService.cs 中实现完整的 LLMPipeline 实例化、模型路径合法性自检、后台异步延迟加载、NPU/GPU/CPU 自动降级兜底、并发信号量限制（SemaphoreSlim(1, 1)）与超时熔断控制（5秒自动截断防止阻塞 UI）。
- **GSI 快照与 Advice 现场精准绑定**：
  - 在 CoachAdvice 模型增加 [JsonIgnore] public MatchSnapshot? Snapshot { get; set; } 字段。
  - 在 GsiProcessingPipeline.cs 规则评估生成建议的瞬间，直接将触发时的现场 snapshot 赋值给建议对象。
  - 在 MainWindow.HandleAdvicePublished 中优先使用 advice.Snapshot 进行 AI 解释，确保分析所基于的英雄状态与战场时钟 100% 对应。
- **GSI 报文安全边界与并发哈希表**：
  - GsiReceiver.cs 增加 1MB 报文上限限制（超出直接响应 413 Payload Too Large 并丢弃），并按 contentLength 严格边界读取 UTF-8 字节。
  - MainWindow.xaml.cs 将 _popularityCache 和 _matchupCache 升级为线程安全的 ConcurrentDictionary。
- **构建环境修正**：
  - 更新 global.json 为本机已安装的 SDK "version": "10.0.401"。

### 本轮验证

| 操作 | 结果 |
|---|---|
| dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64 | 通过，0 个警告，0 个错误 |
| dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore | 通过 1656 项，失败 0 项；1500 帧连续输入基线通过，UI 状态合并 1499 正常 |
| git diff --check | 通过，无格式与空白错误 |
| pwsh -NoProfile -File .\tools\package-release.ps1 | 成功生成独立免安装可执行程序与发布包 |

### 已生成发布包

- `release/publish/神盾教练.exe`
- `release/AegisCoach-发布版.zip`

### 未验证风险

- 真实英特尔 NPU（Core Ultra 平台）下加载 INT4 权重的显存与系统内存占用峰值。
- 本地缺少权重文件或权重格式损坏时，UI 设置界面的明确告警与状态指示走查。
- 真实 Dota 2 对局连续运行 60 分钟以上的大模型推理稳定性与温度发热情况。


# 2026-09-07（第二十五轮）：完成赛后分析独立组件化（AnalysisPage/AnalysisViewModel）与 GSI Token 安全闭环

### 需求与问题诊断

1. **赛后分析视图与宿主强耦合**：此前 `HeroesPage`、`ItemCatalogPage` 和 `CoachPage` 均已完成独立 MVVM 抽取，但赛后分析（Analysis）依然以 130+ 行 XAML 嵌套在 `MainWindow.xaml` 中，并在 `MainWindow.xaml.cs` 中混杂了 500+ 行的报告列表维护、详情面板赋值、OpenDota API 拉取、回放提取和复盘调度逻辑，违反了单一职责与渐进式架构规则。
2. **GSI Token 安全契约断裂**：此前 `InstallCoachConfig` 传入 `string.Empty`，`GsiReceiver` 也以 null 运行，未落地不可预测随机 Token 与 Windows 凭据保险库绑定，无法在局域网/本机抵御非授权的虚假 GSI POST 注入。

### 优化方案与实现

- **独立组件化抽取**：
  - 新建 `native/AegisCoach.Native/Models/BatchObservableCollection.cs`，提供单次 Reset 批量通知机制，避免 200 份报告逐条触发 UI 重新测量与布局。
  - 新建 `native/AegisCoach.Native/ViewModels/AnalysisViewModel.cs`，使用 CommunityToolkit.Mvvm partial properties，彻底封装报告摘要异步加载（对接 `ReportRepository`）、按需读取完整报告、单损坏文件隔离、OpenDota 比赛拉取、操作回放提取与 AI 复盘调度，并提供 `Cancel()` 优雅取消。
  - 新建 `native/AegisCoach.Native/Views/AnalysisPage.xaml` 与 `Views/AnalysisPage.xaml.cs`，对齐 Dota 2 电竞暗色风格，支持自适应双栏（<1100px 自动切单栏折叠）与加载/空/错误状态。
  - `MainWindow.xaml` 与 `MainWindow.xaml.cs` 瘦身超过 550 行，回归纯粹的导航、服务依赖装配与顶层调度。
- **GSI Token 安全契约闭环**：
  - 在 `MainWindow` 实现 `EnsureGsiAuthToken`，通过 `RandomNumberGenerator` 生成不可预测的 16 字节 Hex Token，并保存在 Windows 凭据管理器（`AppCredentialStore`）。
  - 安装 `gamestate_integration_aegiscoach.cfg` 时自动写入该 Token；
  - 启动 `GsiReceiver` 时同步校验该 Token，阻断未授权快照，且 Token 严禁进入普通日志与模型 Prompt。

### 本轮验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 通过，0 个警告，0 个错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 1656，失败 0；1500 帧顺序处理、队列峰值与最新 UI 状态合并基线全部保持 |
| `git diff --check` | 通过 |
| `pwsh -NoProfile -File .\tools\package-release.ps1` | 通过，成功生成独立免安装可执行文件与发布压缩包 |

### 已生成发布包

- `release/publish/神盾教练.exe`：295,424 字节
- `release/AegisCoach-发布版.zip`：34,369,824 字节（约 32.78 MB）

### 未验证风险

- 真实 Dota 2 完整局下历史报告落盘、复盘提取与按需展示的端到端走查。
- 窄窗 640px、多 DPI（150%/200%）在真实 Windows 桌面物理屏幕上的视觉留白细节。
- 真实网络环境下 OpenDota 比赛拉取与云端大模型复盘生成的网络延迟与配额走查。

# 2026-09-07（第二十四轮）：完成阶段 3 主要代码，并修正阶段 1 队列指标竞态

### 需求与问题诊断

继续检查 `docs/AI_PERFORMANCE_OPTIMIZATION_PLAN.md` 后，当前项目不是“进入阶段 4”，而是阶段 0/1/2 已有自动化实现、阶段 3 代码推进中。更关键的问题是：如果只取消推荐和克制请求而不保存任务引用，窗口关闭时仍无法证明旧任务已经停止；如果只修正队列计数顺序而不保留容量槽，生产者并发写入仍可能让性能报告出现超过 1024 的虚假峰值。

### 优化方案与实现

- 阶段 1：`GsiProcessingPipeline` 增加容量槽位预占与归还，`QueueDepth` 统计“已占用或等待写入”的快照数，避免消费者先递减造成峰值竞态；取消或关闭路径只归还本次确实取得的槽位。
- 阶段 3：克制分析增加敌方阵容/玩家英雄指纹，只在阵容变化时刷新；新请求取消旧请求，过时任务不能覆盖当前结果，失败时保留上一次有效结果并显示降级原因。
- 阶段 3：推荐任务纳入后台任务登记，关闭时会等待已登记的推荐任务结束。
- 阶段 3/6：AI 解释保存任务引用并接入 `CancellationToken`；新解释请求取消旧请求，窗口关闭或取消后不得回写 UI。
- 阶段 6：对局切换、停止英雄跟踪和窗口关闭都会取消克制分析，避免旧对局的网络结果写入新对局页面。

### 当前阶段判定

- 阶段 0：自动化基线已落地；真实桌面启动、200 份报告和真实窗口交互基线未完成。
- 阶段 1：代码与 GSI 冒烟基线已落地；真实 Dota 2 60 秒、10Hz 压力验收未完成。
- 阶段 2：主要代码已完成；真实窗口启动时序、取消、联网降级未验收。
- 阶段 3：主要代码已完成，自动化构建/冒烟通过；真实联网阵容变化、推荐/克制请求数量和 AI 后端取消未实测。
- 阶段 4、5：未实施。
- 阶段 6：部分完成；Token 启动链路集成测试、NPU 真实推理和全量后台任务审计仍未完成。

### 本轮验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 标准输出目录的 WinUI `obj\\...\\input.json` 被现有进程/句柄占用，失败；按规范使用隔离编译输出后通过 |
| `dotnet build native\\AegisCoach.Native\\AegisCoach.Native.csproj --configuration Debug --no-restore -p:Platform=x64 -p:IntermediateOutputPath=E:\\openSource\\demo\\.build\\native-compile-int\\ -p:OutputPath=E:\\openSource\\demo\\.build\\native-compile-bin\\` | 通过，0 警告，0 错误 |
| `dotnet run --project tools\\GsiSmoke\\AegisCoach.Smoke.csproj --no-restore` | 通过 1652，失败 0；1500/1500 入队，1500/1500 处理，丢弃 0，UI 合并 1499，队列峰值 1024 |
| `git diff --check` | 通过 |
| `pwsh -NoProfile -File .\\tools\\package-release.ps1` | 通过；生成免安装程序和发布压缩包 |

### 已生成发布包

- `E:\\openSource\\demo\\release\\publish\\神盾教练.exe`：294,400 字节
- `E:\\openSource\\demo\\release\\AegisCoach-发布版.zip`：34,358,916 字节（约 32.77 MB）

### 未验证风险

- 未用真实 AegisCoach 窗口完成 640/1024/1440px、100%/150%/200% DPI、Light/Dark/High Contrast、键盘路径及五类状态走查。
- 未用真实 Dota 2 GSI 连续运行 60 秒验证背压、窗口关闭时序和网络增强失败降级。
- 未联网验证克制阵容变化只产生一次请求、推荐去重和 AI 取消行为。
- 未验证 NPU 模型加载、取消、超时、设备失败和资源释放；当前自动化结果不等于 NPU 推理验收。
# 2026-09-07（第二十三轮）：按 AI 性能优化规范推进阶段 1/2，并开始阶段 3 去重

### 需求与问题诊断

本轮检查 `docs/AI_PERFORMANCE_OPTIMIZATION_PLAN.md` 后确认：代码已经出现阶段 0/1/2 的实现，但文档状态仍为“待实施”，且阶段 1 管线原先在有界通道满时使用 `TryWrite`，会把“已接收但尚未处理”的 GSI 快照计为丢弃；阶段 2 也只是用信号量串行化，`force=true` 并发调用仍会重复执行实际加载，不能称为 single-flight。

更关键的问题不是单个方法慢，而是生命周期所有权不完整：启动增强任务、推荐任务和对局跟踪任务没有统一登记，窗口关闭时无法证明它们全部停止。因此本轮同时补齐任务登记、取消和等待链路。

### 优化方案与实现

- 阶段 1：`GsiProcessingPipeline.Enqueue` 改为在有界通道满时对接收线程施加背压，不再用 `TryWrite` 静默丢弃已接受快照；关闭或取消才计为拒绝。保持单消费者顺序处理和 UI 最新状态合并。
- 阶段 1：修正 `GsiSmoke` 性能基线测试，使用规范化后的 `MatchSnapshot`，并继续验证 1500 帧顺序处理、规则提醒不丢失、UI 只发布最后状态。
- 阶段 2：`HeroesViewModel` 和 `ItemCatalogViewModel` 移除内部 `new DotaDataService()`，统一由 `MainWindow` 显式注入共享实例。
- 阶段 2：`DotaDataService` 的目录、物品、英雄详情、物品详情、物品统计、英雄出装和克制数据改为 `Lazy<Task>` single-flight；并发调用等待同一实际任务，失败后任务从字典移除以便重试。
- 阶段 2：启动顺序改为先等待英雄目录首屏可用，再登记物品、报告、物品页预热和过期英雄详情等后台增强任务；窗口关闭时取消页面任务并等待已登记任务结束。
- 阶段 3：物品推荐增加英雄、装备、阶段、阵容和金币区间指纹；相同原因且未达到最短间隔时不重复执行，指纹变化时取消旧推荐任务。
- 同步更新优化规范中的阶段判定，明确“自动化证据”和“真实桌面验收”不是同一件事。

### 本轮验证

以下结果以本轮实际执行为准，发布打包在命令结束后补录：

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64` | 通过，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 1652，失败 0；GSI 性能基线 1500/1500 入队、处理 1500/1500、丢弃 0、UI 合并 1499 |
| `git diff --check` | 待本轮全部编辑完成后执行 |
| `pwsh -NoProfile -File .\tools\package-release.ps1` | 待执行 |

### 未验证风险

- 未用真实 AegisCoach 窗口完成 640/1024/1440px、100%/150%/200% DPI、Light/Dark/High Contrast 和键盘路径走查。
- 未用真实 Dota 2 GSI 连续运行 60 秒验证接收线程背压、窗口关闭时序和网络增强失败降级。
- 未联网验证官方目录/统计 single-flight 的请求数量变化。
- 未验证 NPU 模型加载、取消、超时和设备失败路径。

## 2026-09-07（第二十二轮）：依赖版本升级、集中管理与 .NET 10 SDK 固定

### 需求与问题诊断

1. 项目已切换到 .NET 10 LTS，但 Windows App SDK、Windows SDK BuildTools 和 OpenVINO GenAI 运行时仍使用旧版本，依赖版本分散在各个 `.csproj`，容易造成开发机与 CI 还原结果不一致。
2. 只升级包版本不能解决可复现构建问题：没有 `global.json` 时，不同机器可能使用不同 .NET SDK；没有集中版本入口时，后续 AI 修改项目文件容易遗漏某个工具项目。

### 优化方案与实现

- `Microsoft.WindowsAppSDK`：`2.3.1` 升级为 `2.4.0`。
- `Microsoft.Windows.SDK.BuildTools`：`10.0.28000.2526` 升级为 `10.0.28000.2705`。
- `JYPPX.OpenVINO.GenAI.runtime.win`：`2026.3.0` 升级为 `2026.3.1`。
- 保持 `CommunityToolkit.Mvvm 8.4.2` 与 `JYPPX.OpenVINO.CSharp.API 3.3.1` 不变；NuGet 过期检查确认当前没有更高稳定版。
- 新增根目录 `Directory.Packages.props`，集中声明所有顶级 NuGet 版本，并启用传递依赖集中固定。
- 新增 `global.json`，固定 .NET SDK `10.0.400`，避免本机与 CI 因 SDK 漂移产生不同构建行为。
- 修正 `AGENTS.md` 中残留的 `.NET 8` 架构描述为 `.NET 10 LTS`。

### 验证

本轮实际验证结果：

```powershell
dotnet list native\AegisCoach.Native\AegisCoach.Native.csproj package --outdated
dotnet list tools\NpuProbe\NpuProbe.csproj package --outdated
dotnet restore native\AegisCoach.Native.sln -p:Platform=x64
dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64
dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore
git diff --check
pwsh -NoProfile -File .\tools\package-release.ps1
```

- Native 依赖过期检查：当前源无可用稳定更新。
- OpenVINO 依赖过期检查：当前源无可用稳定更新。
- Native Debug 构建：成功，0 警告，0 错误。
- NPU Probe Debug 构建：成功，0 警告，0 错误。
- GSI 冒烟测试：通过 146，失败 0。
- `git diff --check`：通过。
- 发布打包：成功生成 `release/publish/神盾教练.exe` 与 `release/AegisCoach-发布版.zip`，压缩包大小 32.76 MB。



---

# 历史演进摘要（第 1 ~ 21 轮归档）

本段落归档整理早期已废弃或已完全稳定的技术演进记录。详细当前实现以 docs/PROJECT_DOCUMENTATION.md 为准。

- **第 1 ~ 6 轮（项目基建与 WinUI 3 迁移）**：
  - 完成从旧技术栈到 WinUI 3 + .NET 10 LTS 的全面现代化迁移，确立单项目单执行文件架构；
  - 实现 Dota 2 GSI 监听接收器与本地规则引擎初版，涵盖肉山刷新、神符刷新、买活金钱监控等核心规则。
- **第 7 ~ 12 轮（离线数据与多级缓存体系）**：
  - 接入 OpenDota 与 Dota 2 官方静态数据 API，实现本地三级文件缓存（内存 -> 用户文档目录 -> 官方内置预设）；
  - 实现英雄列表、物品大厅首屏毫秒级加载与网络离线断网自动降级。
- **第 13 ~ 17 轮（页面组件化与 MVVM 拆分）**：
  - 将庞大的 MainWindow.xaml 逐步拆分独立：先后抽取 HeroesPage（英雄图鉴）、ItemCatalogPage（物品全鉴）与 CoachPage（实时教练对局）；
  - 引入 CommunityToolkit.Mvvm，消除跨视图直接操作控件实例的坏味道。
- **第 18 ~ 21 轮（Dota 2 细节体验对齐与发布瘦身）**：
  - 根除技能与物品点击时 WinUI 3 FocusManager 导致视口跳顶的深层机制 Bug；
  - 依据 Dota 2 官方机制重构英雄 1 级基础生命/魔法/护甲/攻击力计算公式；
  - 补充阿哈利姆神杖/魔晶升级变体、倒序天赋树（25->10级）、全量官方中文映射表；
  - 优化 	ools/package-release.ps1 打包脚本，剔除无用离线模型转换动态库，生成独立自解压/免安装程序 神盾教练.exe。
