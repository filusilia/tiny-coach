# 神盾教练性能优化实施规范

文档类型：代码 Agent 执行规范
文档状态：实施中（2026-09-07）
适用版本：当前 dev 分支
目标平台：Windows / WinUI 3 / .NET 10 LTS / Windows App SDK

### 当前阶段判定

本轮检查发现，仓库已有阶段 0、阶段 1 和阶段 2 的部分实现，但原文档仍标记为“待实施”，因此不能把代码存在误写成阶段验收完成。当前按验收条件判定：

| 阶段 | 当前状态 | 已有证据 / 未完成项 |
|---|---|---|
| 阶段 0 性能基线 | 自动化基线已落地，真实桌面基线未完成 | `PerformanceMetrics`、冷/热启动模拟、GSI 连续输入基线已接入 `GsiSmoke`；仍缺真实冷启动、热启动、200 份报告和真实窗口交互测量 |
| 阶段 1 GSI 后台顺序处理与 UI 合并 | 代码实现已落地，真实 GSI 压力验收未完成 | 单消费者管线、规则/会话脱离 Dispatcher、UI 最新状态合并、提醒独立队列已实现；仍需真实 60 秒 10Hz 输入验证 |
| 阶段 2 统一数据服务与启动编排 | 本轮完成主要代码改造，桌面首屏验收未完成 | MainWindow 注入唯一 `DotaDataService`、目录/详情/统计 single-flight、英雄首屏优先和后台任务登记已实现；仍需真实窗口启动时序、取消和联网降级走查 |
| 阶段 3 推荐/克制/AI 去重 | 主要代码已完成，真实联网验收未完成 | 推荐、克制分析和 AI 解释均有状态触发、任务引用、CancellationToken 与过时结果保护；仍需真实联网统计请求、后端取消和窗口行为验收 |
| 阶段 4 历史报告异步加载 | 主要代码已实施，自动化验证已通过，真实窗口验收未完成 | `ReportRepository` 启动只读最近 200 条摘要，选中后异步读取完整报告，批量更新列表，单个损坏文件隔离为错误项；仍需真实窗口、DPI、长期报告压力和关闭竞态走查 |
| 阶段 5 列表虚拟化和滚动 | 未实施 | 仍需真实窗口、多 DPI、多状态和键盘路径验收 |
| 阶段 6 生命周期与安全契约 | 部分实现 | GSI 管线、推荐、克制分析、AI 解释和主要后台任务已有取消/等待链路；Token 启动链路集成测试、NPU 文档一致性和全量后台任务审计仍未完成 |

因此，本轮已完成阶段 4 的主要代码推进，并修正阶段 1 队列指标竞态；阶段 0/1/2/3/4 仍分别缺少真实桌面、真实 GSI、联网、后端和窗口生命周期验收，不能把本轮构建或冒烟测试写成真实 UI、联网、NPU 或 Dota 对局验收。

## 1. 目标与边界

本规范用于解决以下问题：

- 应用冷启动和缓存启动耗时过长；
- 英雄、物品页面首次打开和滚动不流畅；
- GSI 实时快照更新导致 UI Dispatcher 排队；
- 推荐、克制分析、图片预取和网络请求重复执行；
- 后台任务在窗口关闭后仍可能继续运行；
- 文档、实现和安全契约不一致。

本规范不是全量重写计划。所有改动必须保持：

1. GSI、规则、回放和赛后分析的现有业务语义不变。
2. 规则引擎先产生确定性结果，AI 只能解释，不能改变触发结果。
3. 已接受的 GSI 快照必须按顺序进入规则处理；只能合并过时的 UI 展示状态。
4. 网络、统计、图片、AI 或 NPU 失败不能阻断 GSI 接收和本地规则提醒。
5. 只使用官方本机 GSI；不读取游戏进程内存，不注入、不 Hook、不模拟输入、不自动操作游戏。
6. 保持单 Native 项目结构。native/AegisCoach.Native/Core 是 Native 项目内的源码目录，不恢复独立 Core DLL。
7. 用户数据继续保存于 文档\\AegisCoach，不迁移到 AppData，不删除现有用户数据。

## 2. 当前问题的因果链

### 2.1 实时浏览卡顿

当前路径：

    Dota 2 GSI 每约 100ms 发送快照
      -> GsiReceiver 触发 SnapshotReceived
      -> MainWindow 将每个快照排入 DispatcherQueue
      -> UI 线程执行会话记录、规则计算、英雄查询触发、ViewModel 属性更新
      -> 推荐任务、绑定通知和页面布局增加 UI 工作
      -> Dispatcher 延迟、输入延迟和滚动卡顿

相关代码：

- native/AegisCoach.Native/Core/Services/GsiConfigInstaller.cs：GSI throttle 当前为 0.1。
- native/AegisCoach.Native/MainWindow.xaml.cs：OnSnapshotReceived 当前在 Dispatcher 中执行整段快照处理。
- native/AegisCoach.Native/ViewModels/CoachViewModel.cs：每次更新可能触发多项属性通知。
- native/AegisCoach.Native/MainWindow.xaml.cs：每个快照都尝试刷新物品推荐。

优化目标是把规则处理从 UI 线程移出，并让 UI 只消费最新状态。不能简单降低采样频率或直接丢弃快照，因为规则判断依赖相邻快照的状态变化。

### 2.2 启动缓慢

当前路径：

    MainWindow 加载设置
      -> MainWindow、HeroesViewModel、ItemCatalogViewModel 分别创建 DotaDataService
      -> 同时启动英雄目录、物品目录、物品统计、英雄详情和图片预取
      -> 同一资源可能重复读取、解析、请求和写缓存
      -> LoadAnalysisData 同步读取历史报告并逐条更新 ObservableCollection
      -> 首屏可交互时间被后台初始化和磁盘工作拖长

相关代码：

- native/AegisCoach.Native/MainWindow.xaml.cs：StartApplicationAsync 同时启动多个后台任务。
- native/AegisCoach.Native/ViewModels/HeroesViewModel.cs：内部创建自己的 DotaDataService。
- native/AegisCoach.Native/ViewModels/ItemCatalogViewModel.cs：内部创建自己的 DotaDataService。
- native/AegisCoach.Native/Core/Services/MatchSessionTracker.cs：报告读取使用同步文件 API。

优化目标是首屏优先、增强功能延后、同一资源 single-flight，而不是简单增加线程数量。

## 3. Agent 执行协议

代码 Agent 执行每个阶段时必须遵循以下顺序：

1. 先执行 git status --short，保留已有用户改动。
2. 读取本阶段列出的文件和相关文档。
3. 先写或更新测试/测量，再修改实现。
4. 只修改本阶段允许的文件范围；需要扩大范围时先说明原因。
5. 完成后执行构建、测试、git diff --check。
6. 代码阶段还必须执行发布打包脚本。
7. 结果必须区分源码编译、逻辑测试、真实桌面 UI、联网、NPU 和真实 Dota 对局验证。

禁止以下做法：

- 用丢弃 GSI 快照掩盖处理能力不足；
- 用 Task.Run 包裹 UI 控件访问；
- 使用无法追踪的长期 _ = SomeAsync()；
- 为了提速删除 Token 校验、缓存校验或错误处理；
- 整体替换 XAML；
- 恢复多项目结构或进行与性能无关的模型大规模重命名；
- 把构建成功描述为真实 UI 或联网验收成功。

## 4. 阶段 0：性能基线

### 4.1 修改范围

优先修改：

- native/AegisCoach.Native/MainWindow.xaml.cs
- 新增 native/AegisCoach.Native/Core/Services/PerformanceMetrics.cs
- 必要时修改 tools/GsiSmoke/Program.cs

### 4.2 必须记录的指标

启动指标：

- OnLaunched；
- MainWindow 构造完成；
- Loaded；
- 设置读取完成；
- 窗口首次显示；
- 英雄目录可用；
- 首屏可交互；
- 物品目录可用；
- 后台启动任务进入空闲。

实时指标：

- GSI 接收快照数；
- 规则处理快照数；
- 规则处理耗时；
- UI 状态发布数；
- UI 状态合并数；
- Dispatcher 等待时间；
- 推荐/克制分析执行次数和耗时；
- 图片预取数量、失败数和耗时；
- 管线积压数量。

### 4.3 测量要求

- 使用 Stopwatch.GetTimestamp() 或等价单调时钟。
- 日志只记录耗时、计数、阶段和错误类型。
- 不记录 Token、API Key、完整 Steam ID 或原始 GSI JSON。
- 诊断功能默认不改变业务行为。
- 结果写入现有中文运行日志或独立诊断文件，但不得无限增长。

### 4.4 基线场景

至少测量：

1. 无缓存、无网络冷启动。
2. 完整缓存热启动。
3. 200 份历史报告启动。
4. 连续 60 秒、10Hz 模拟 GSI 输入。
5. 启动后立即搜索、点击和滚动英雄页。
6. 启动后立即搜索、点击和滚动物品页。

没有基线数据前，只能说“发现代码热点”，不能说“性能已经改善”。

## 5. 阶段 1：GSI 后台顺序处理与 UI 合并

### 5.1 修改范围

- native/AegisCoach.Native/Core/Services/GsiReceiver.cs
- 新增 native/AegisCoach.Native/Core/Services/GsiProcessingPipeline.cs
- 新增或修改 native/AegisCoach.Native/Core/Models/CoachStateUpdate.cs
- native/AegisCoach.Native/MainWindow.xaml.cs
- native/AegisCoach.Native/ViewModels/CoachViewModel.cs

### 5.2 目标结构

    GsiReceiver
      -> 顺序输入管线
          -> MatchSessionTracker
          -> CoachRuleEngine
          -> 实时状态计算
          -> 规则提醒事件
          -> 最新 UI 状态
      -> DispatcherQueue 仅发布 UI 状态和提醒

### 5.3 实现要求

- GsiReceiver 只负责监听、请求解析、Token 校验、去重、落盘和发出已接受快照。
- 新增单消费者管线，保证快照按接收顺序处理。
- MatchSessionTracker.OnSnapshot 和 CoachRuleEngine.Evaluate 不得在 Dispatcher 中执行。
- 已接受快照不能因为 UI 忙而丢失。管线积压时记录指标并修复消费者性能，不静默丢弃。
- UI 状态使用“只保留最新值”的合并机制。
- Dispatcher 同时最多存在一个待执行的 UI 状态更新任务。
- 规则提醒是事件，必须单独发布，不能因 UI 状态合并而丢失。
- 连接状态仅在连接状态实际变化时更新。
- OnSnapshotReceived 不得执行网络请求、同步磁盘读写、推荐计算或复杂页面操作。
- 所有管线任务接受并传递 CancellationToken。

### 5.4 ViewModel 更新策略

CoachViewModel.UpdateSnapshot 只在值发生变化时更新属性：

- 阶段和比赛状态变化时更新阶段文字；
- 游戏时间按约 500ms 至 1s 更新；
- 快照数值按需要限频，不必每帧通知；
- 装备栏只有装备 ID 集合变化时重建；
- 阵容只有英雄 ID 集合变化时重建；
- 相同字符串和数值不重复触发属性通知。

这里的限频只影响 UI 展示，不影响后台规则处理和本地快照记录。

### 5.5 验收条件

- 连续 60 秒、10Hz 输入下，规则结果与顺序逐帧处理基准一致。
- Dispatcher 待处理 UI 更新不会随输入数量持续增长。
- UI 状态过时内容可以合并，但规则提醒数量和顺序不变。
- 窗口关闭后管线不再更新控件。
- 自动化测试覆盖取消、积压、重复快照和比赛切换。

## 6. 阶段 2：统一数据服务与启动编排

### 6.1 修改范围

- native/AegisCoach.Native/Services/DotaDataService.cs
- native/AegisCoach.Native/ViewModels/HeroesViewModel.cs
- native/AegisCoach.Native/ViewModels/ItemCatalogViewModel.cs
- native/AegisCoach.Native/MainWindow.xaml.cs
- 必要时新增 native/AegisCoach.Native/Services/ApplicationServices.cs

### 6.2 依赖规则

- 整个应用只创建一个 DotaDataService。
- ViewModel 不得继续 new DotaDataService()。
- 共享 HttpClient、请求限速器、缓存目录和资源加载任务。
- 页面由 XAML 创建时，使用明确的服务配置入口注入共享实例；不得保留隐式备用实例。
- 需要替换副作用时使用最小接口，不为单个方法机械创建接口。

### 6.3 single-flight 要求

同一时间同一资源只能有一个实际加载任务：

- 英雄目录一个任务；
- 物品目录一个任务；
- 同一英雄详情一个任务；
- 同一物品详情一个任务；
- 相同统计资源一个任务。

并发调用应等待同一任务，不得重复发起请求、重复解析或重复写缓存。失败任务必须从任务缓存中移除，以便后续重试。

### 6.4 启动顺序

1. 读取设置并完成首次启动门控。
2. 显示窗口和基本导航。
3. 加载默认首屏英雄目录。
4. 让英雄页可搜索、可点击、可滚动。
5. 首屏可交互后再加载物品目录。
6. 空闲时加载物品统计、当前可见图片和过期英雄详情。
7. NPU 能力检测和模型初始化不得阻塞首屏。

启动时不得同时强制执行英雄目录、物品目录、物品统计、全量英雄详情和全部图片预取。

RefreshAllHeroDetailsInBackgroundAsync 必须等待英雄目录任务成功后再获得英雄 ID。不得通过再次调用 LoadAsync 解决依赖，因为这可能因 IsLoading 直接返回。

全量英雄详情只刷新过期缓存，连续失败达到阈值时进入降级状态，不阻塞资料页。

### 6.5 其他修复

- 修正英雄详情缓存清理正则为正确的数字匹配表达式 ^hero-(\d+)-zh-。
- 缓存优先显示，网络刷新作为后台增强。
- 大 JSON 解析和网络请求不得无故回到 UI SynchronizationContext。
- 数据源、版本、获取时间和降级状态必须继续可追踪。

## 7. 阶段 3：推荐、克制和 AI 任务去重

### 7.1 修改范围

- native/AegisCoach.Native/MainWindow.xaml.cs
- native/AegisCoach.Native/ViewModels/CoachViewModel.cs
- 必要时新增实时推荐状态模型或协调器

### 7.2 推荐状态指纹

推荐计算至少使用以下指纹字段：

- 玩家英雄 ID；
- 主装备 ID 集合；
- 游戏阶段；
- 全场英雄 ID 集合；
- 玩家金钱区间。

以下条件之一满足时才刷新物品推荐：

- 玩家英雄变化；
- 装备变化；
- 游戏阶段变化；
- 全场阵容变化；
- 最小刷新间隔到达。

建议物品推荐最短间隔为 3 秒。克制分析只在敌方阵容变化时刷新，不随每个 GSI 快照运行。

### 7.3 后台任务要求

- 每个任务保存任务引用。
- 新请求取消旧请求。
- 取消使用 CancellationToken，不能只依赖请求 ID。
- 任务失败时保留上一次有效结果，并显示降级原因。
- 网络请求、JSON 解析和统计计算不得在 UI 线程执行。
- AI 解释只由新规则事件触发，不按快照触发。
- 每个规则事件最多生成一次解释请求。

## 8. 阶段 4：历史报告异步和按需加载

### 8.1 修改范围

- native/AegisCoach.Native/Core/Services/MatchSessionTracker.cs
- native/AegisCoach.Native/MainWindow.xaml.cs
- native/AegisCoach.Native/MainWindow.xaml
- 必要时新增 ReportRepository.cs
- 必要时新增 MatchReportSummary 模型

### 8.2 实现要求

- 报告列表不得在 UI 线程同步读取和反序列化文件。
- 首次只读取最近报告的摘要信息。
- 按文件时间排序后只选最近一批，不要先读取全部文件再 Take(200)。
- 用户选中报告时再读取完整报告、操作记录和 AI 内容。
- 使用异步 FileStream 或后台仓储任务。
- 批量加入集合，避免 200 次逐条布局更新。
- 单个损坏文件只进入错误状态，不阻断其他报告。
- 保留现有最多 200 条报告的产品限制，必要时增加“加载更多”。

列表项不应默认绑定完整 MatchReport，而应绑定轻量摘要模型。

## 9. 阶段 5：列表虚拟化和滚动

### 9.1 修改范围

- native/AegisCoach.Native/Views/HeroesPage.xaml
- native/AegisCoach.Native/Views/ItemCatalogPage.xaml
- 对应 ViewModel 和主题文件，仅在必要时修改

### 9.2 实现要求

- 不使用 ScrollViewer 包裹 GridView，让 GridView 自己管理滚动和虚拟化。
- 英雄列表继续使用具备虚拟化能力的 GridView。
- 物品页不得把“外层 ScrollViewer + 外层 ItemsControl + 内层 ItemsControl”作为最终大量数据结构。
- 物品分组应改为支持虚拟化的分组控件，或展平成带分组标题的单一列表模型。
- 图片设置固定尺寸、占位图和可复用缓存，避免布局跳动和重复下载。
- 详情数据继续懒加载，不能在列表生成时加载所有详情。
- 键盘可以打开物品详情；详情不能只依赖鼠标悬停。
- 保留页面滚动、筛选、选择和返回状态。

### 9.3 UI 验收

必须用真实 AegisCoach 窗口验证：

- 640、1024、1440px 窗口宽度；
- 100%、150%、200% DPI；
- Light、Dark、High Contrast；
- 英雄列表滚动、物品列表滚动、搜索和筛选；
- loading、empty、error、degraded、ready 状态；
- 键盘 Tab、Enter、方向键和 Escape 路径。

其他窗口的截图不能作为 UI 验收证据。

## 10. 阶段 6：生命周期与安全契约

### 10.1 修改范围

- native/AegisCoach.Native/MainWindow.xaml.cs
- native/AegisCoach.Native/App.xaml.cs
- native/AegisCoach.Native/Core/Services/GsiReceiver.cs
- native/AegisCoach.Native/Core/Services/GsiConfigInstaller.cs
- native/AegisCoach.Native/Models/AppSettings.cs

### 10.2 关闭顺序

窗口关闭时按以下顺序执行：

1. 标记应用正在关闭。
2. 停止接受新的 GSI 快照。
3. 取消 GSI 处理管线。
4. 取消推荐、克制、英雄查询、图片预取、详情加载和 AI 任务。
5. 等待已登记后台任务结束。
6. 停止并释放 GSI 接收器和网络服务。
7. 取消页面级任务并释放定时器。

所有长生命周期任务必须登记或由明确的服务所有者持有，不能只写无法追踪的 _ = SomeAsync()。

### 10.3 GSI Token

必须形成完整链路：

    随机生成 Token
      -> Windows Credential Vault 安全保存
      -> 写入 gamestate_integration_aegiscoach.cfg
      -> 创建 GsiReceiver 时传入同一个 Token
      -> 接收时校验

要求：

- 使用 RandomNumberGenerator 生成不可预测 Token。
- 安装配置和接收器读取同一个值。
- Token 不进入日志、报告、AI Prompt 或提交内容。
- 增加产品启动路径的 Token 集成测试，不能只测试独立的 GsiReceiver 构造。
- 若配置安装失败，导出的 cfg 仍不得泄露到普通日志。

### 10.4 NPU 文档一致性

当前 NpuGenAiCoachService.ExplainAsync 若仍返回 null，文档必须描述为“能力探测和规则降级占位”，不能描述为已经完成真实 NPU 推理。只有完成模型加载、取消、超时、设备失败和资源释放后，才可恢复“已实现”的表述。

## 11. 测试与验收矩阵

新增或修改测试至少覆盖：

- 顺序输入下规则结果不变；
- UI 状态只保留最新值；
- 规则提醒不因 UI 合并而丢失；
- 管线取消后不再更新 UI；
- 同一资源并发调用只产生一个实际请求；
- 相同装备和阵容不重复刷新推荐；
- AI 不按重复快照重复调用；
- 报告摘要先加载、完整报告按需加载；
- 单个损坏报告不阻断其他报告；
- 窗口关闭后没有后台 UI 更新；
- GSI cfg 和接收器使用同一个 Token；
- Token 不出现在日志、回放和 Prompt；
- 英雄详情或图片失败时首屏仍可用。

每次代码编辑后按顺序执行：

    dotnet build native\\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64
    dotnet run --project tools\\GsiSmoke\\AegisCoach.Smoke.csproj --no-restore
    git diff --check
    pwsh -NoProfile -File .\\tools\\package-release.ps1

如果标准 WinUI 构建因已有 obj\\...\\input.json 被占用或拒绝覆盖而失败，先区分环境问题，再使用仓库规定的隔离编译输出目录。不能把隔离编译结果写成标准构建已经通过。

发布产物必须检查：

- release/publish/神盾教练.exe 存在；
- release/AegisCoach-发布版.zip 存在并记录文件大小；
- 发布目录不包含不应提交的本地日志、Token、API Key、缓存或用户报告。

## 12. 阶段依赖与交付顺序

    阶段 0 性能基线
      -> 阶段 1 GSI 后台管线
      -> 阶段 2 数据服务与启动编排
      -> 阶段 3 推荐/克制/AI 去重
      -> 阶段 4 报告异步加载
      -> 阶段 5 列表虚拟化
      -> 阶段 6 生命周期与安全契约

每个阶段单独提交，提交说明必须包含：

- 修改文件范围；
- 改动前后的指标；
- 自动化测试结果；
- 未验证的桌面、联网、NPU 或真实对局风险；
- 是否生成发布包。

## 13. Agent 停止条件

遇到以下情况必须停止当前阶段并报告，不得自行扩大范围：

- 需要删除、迁移或批量改写用户数据；
- 需要读取游戏进程、注入、Hook 或模拟输入；
- 无法证明 GSI 快照没有被错误丢弃；
- 需要改变规则触发条件或提醒文案语义；
- 需要恢复独立 Core 项目；
- 需要整体替换 XAML；
- 需要未经授权下载模型或扩大联网范围；
- 构建或测试失败原因无法区分是源码问题还是环境锁定问题。

## 14. 完成定义

本优化计划只有在以下条件全部满足时才可标记完成：

1. 有冷启动、热启动和 GSI 压力基线。
2. 规则处理不占用 UI Dispatcher，且规则结果保持一致。
3. UI 更新存在背压/合并机制，队列不会无限增长。
4. 数据服务、请求和缓存加载实现共享与 single-flight。
5. 启动只优先加载首屏，增强任务延后执行。
6. 推荐、克制和 AI 任务按变化原因触发并支持取消。
7. 报告异步、摘要优先、详情按需加载。
8. 英雄和物品列表通过真实窗口完成虚拟化和多 DPI 走查。
9. 窗口关闭后没有残留后台任务更新 UI。
10. GSI Token、NPU 文档和实际实现保持一致。
11. 构建、GsiSmoke、差异检查和发布打包结果均已记录。
