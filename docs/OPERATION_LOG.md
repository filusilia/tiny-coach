# 神盾教练操作记录

本文件记录每次继续开发后的构建、测试和应用操作。命令均从 `E:\openSource\demo` 执行；结果以实际输出为准。

> 这是按日期保存的历史证据。旧条目中的 AppData 路径、端口设置方式、模型后端、构建输出和测试数量只代表当时状态，不是当前配置。当前行为、目录和完成度以 `docs/PROJECT_DOCUMENTATION.md` 为准；历史事实不因后续实现变化而删除。

## 2026-08-11：编译结构、首次设置、统一文档目录与可靠性修复

### 编译与 Visual Studio

- 将 `AegisCoach.Core` 正式加入 `native/AegisCoach.Native.sln`，修复设计时找不到 `CoachRuleOptions`、`ApiMatchAnalyzer`、`MatchSessionTracker`、`AegisCoachPaths`、`MatchReport` 等 Core 类型的问题。
- 新增 `native/Directory.Build.props`，默认构建中间产物隔离到 `native/.artifacts`，避免旧 `bin/obj` 资产干扰设计时编译。
- 新增 `.editorconfig` 与 `.gitattributes`，仓库文本统一为 UTF-8、CRLF；检查结果为混合行尾 0、仅 LF 文件 0。

### 后续修正：单项目整合

- 复现重复 Assembly 特性：`.artifacts` 重定向改变了 SDK 默认排除目录，项目旧 `obj/**` 中生成的 AssemblyInfo 被当成普通源码再次编译；Core 构建失败后，Native 才继续报找不到 `AegisCoach.Core.dll`。
- 删除 `Directory.Build.props`，恢复 Visual Studio 标准 `bin/obj` 输出。
- 删除独立 `AegisCoach.Core.csproj`，解决方案只保留 `AegisCoach.Native`；Core 的 Models/Services 作为共享源码直接编入 Native，不再产生 Core DLL。
- `GsiSmoke` 与 `GsiReplay` 同样编译这份共享源码，保留无 WinUI 依赖的规则回归能力。
- 后续将共享源码物理移动到 `native/AegisCoach.Native/Core`，Native 使用 SDK 默认源码发现，不再依赖项目外部链接；仓库目录和解决方案均只呈现一个应用项目。

### 首次启动、设置与存储

- 所有应用数据统一到用户 `文档\AegisCoach`，不再读取或迁移旧 AppData 配置。
- 首次启动必须先完成设置；保存成功前不访问资料 API、不启动 NPU、不启动 GSI、不加载报告。
- 普通设置隐藏端口和 Token 等实现细节；高级设置保留规则阈值、逐规则冷却、模型/API/Stratz 与手动 Dota 路径。
- Cloud 和 Stratz 密钥改存 Windows 凭据保险库，不写入 `settings.json`。

### 文件与网络可靠性

- 新增 `AegisCoachPaths`、`AtomicFile`、`AppCredentialStore`。
- 设置、GSI cfg、习惯和报告使用原子替换；损坏 JSON 缓存隔离为 `.corrupt`。
- 资料请求和赛后 API 对网络错误、408、429、5xx 执行最多三次重试、指数退避、`Retry-After`、请求间隔和错误分类。
- 统计样本少于 30 时保留样本量，但不展示胜率结论。
- 窗口关闭会取消资料、统计、赛后 API、全场英雄和智能解释请求，并停止 GSI。

### 验证

| 操作 | 结果 |
|---|---|
| Native Debug x64 构建（临时 artifacts 路径） | 成功，0 警告，0 错误 |
| 单项目结构干净还原与构建 | 成功，输出仅 `AegisCoach.Native`，0 警告，0 错误；程序集无 `AegisCoach.Core` 引用 |
| 标准解决方案 `Debug|x64` 构建 | 成功，输出到 Native 标准 `bin/x64/Debug`，0 警告，0 错误 |
| Core 源码移入 `AegisCoach.Native/Core` 后干净构建 | Native 与 GsiReplay 均 0 警告、0 错误；GsiSmoke 128 项通过、0 项失败 |
| `tools/GsiSmoke` | 128 项通过，0 项失败 |
| XAML XML、名称唯一性和事件绑定检查 | 通过 |
| `git diff --check` | 通过 |
| 启动干净构建 | 进程响应正常，窗口标题“神盾教练” |
| 待实机验证 | Visual Studio 重载后 F5、首次设置完整交互、真实 Dota 2 GSI、在线 API 与 NPU |

## 2026-08-08：数据源修复、Wiki 风格页面、克制分析与赛后分析

### 英雄资料加载失败根因与修复

- 根因：`AbilityDefinition` 与 `AbilityAttribute` 中的 `JsonElement` 属性（`behavior`、`damage_type`、`attrib[].value` 等）在反序列化后未克隆，其底层 `JsonDocument` 已被回收；写缓存时 `JsonSerializer` 抛出 `Operation is not valid due to the current state of the object`，导致 `abilities` 缓存写为 0 字节，`LoadCatalogAsync` 整体失败，页面显示"无法加载英雄数据"。
- 修复：`DotaModels.cs` 对上述 `JsonElement` 属性改为 setter 内 `Clone()`（与 `HeroStat.Stats` 一致）；`DotaDataService.WriteFileAsync` 改为先写 `.tmp` 再原子替换，序列化失败不再残留截断缓存。
- 说明：本机 `abilities-v7.41e.json`（0 字节）与 `items-v7.41e.json`（旧 OpenDota 降级数据）由正在运行的旧进程占用；新版本启动后会自动重取/重过滤，无需手动删除。

### 物品只保留当前版本

- 官方 `itemlist` 数据源本身包含大量历史/下线物品，OpenDota constants 同样如此，二者都没有"下线"标志。
- 修正 `IsCurrentItem` 黑名单：移除误杀的 `item_stout_shield`（圆盾仍在售）；新增已确认下线物品：`necronomicon/2/3`、`iron_talon`、`ring_of_aquila`、`diffusal_blade_2`、`elixer`、`pennant`、`furion_gold_bag`、`grand_stock` 及 7.33 中立物品重做时淘汰的旧中立（`mango_tree`、`pupils_gift`、`repair_kit`、`tome_of_aghanim`、`quickening_charm`、`philosophers_stone`、`paladin_sword`、`titan_sliver`、`wraith_pact`、`greater_mango`、`greater_faerie_fire`、`ironwood_tree`）。
- 已核对：穷鬼盾（T1 中立）、韧鼓（`ancient_janggo`，7.33 后即韧鼓）、散夜对剑（仍在售）不在黑名单内，避免误杀当前版本物品。

### 英雄/物品页 Wiki 风格重构

- 英雄详情：顶部横幅加高并新增角色标签（核心/爆发/先手/控制等，来自 OpenDota `roles`）；新增 Liquipedia 风格"基础属性"表（力量/敏捷/智力及成长、生命、魔法、护甲、攻击、移速、射程、攻速、转身、魔抗）；新增"克制与被克制"区（对阵胜率 ≥55% / ≤45%，样本 ≥100）。
- 物品详情：改为 Wiki 风格头部（大图标 + 中文名 + 英文名 + 分类徽章 + 价格），说明/备注/背景故事分节，保留合成路线与统计。
- 新增 `CollectionCountToVisibilityConverter`、`ResultToBrushConverter`。

### 实时教练页：克制与被克制选人推荐

- 新增"克制与被克制 · 选人推荐"面板：敌方克制我（我对阵 ≤45%）、我克制敌方（≥55%）、选人推荐（遍历敌方英雄对阵数据，统计克制面并排除已在场英雄）。
- 数据来自 OpenDota `heroes/{id}/matchups`，内存缓存，随全场英雄追踪每 40 秒刷新一次。

### AI 更智能

- `CoachAiPrompts.BuildPrompt` 改为完整战场状态：时间/阶段/阵营/比分、血量魔法、KDA/连杀/补刀/反补、金钱/GPM/XPM、昼夜、肉山、建筑、雾中/魔免、装备与技能状态。
- `NpuGenAiCoachService.ExplainAsync` 增加 `maxTokens` 参数；`CloudCoachService` 新增通用 `GenerateAsync`。

### 游戏习惯记录与赛后分析

- 新增 `MatchSessionTracker`（Core）：消费 GSI 快照，累积阵亡/击杀/买装备/拆塔/肉山/升级等关键节点与 5 分钟经济采样，赛后产出 `MatchSessionRecord`。
- 习惯库：`%LOCALAPPDATA%\AegisCoach\habits\player-habits.json`（最近 300 场，含胜负/KDA/装备/常用英雄/平均时长汇总）。
- 赛后报告：`%LOCALAPPDATA%\AegisCoach\analysis\report-*.json`，AI 复盘由 NPU/云端后端生成（失败回退规则摘要）。
- 新增"赛后分析"导航页：报告列表 + 复盘详情（AI 文本、最终装备、关键节点、经济曲线）与习惯汇总。

### 追加修复（同日第二轮）

- 英雄资料仍失败：`JsonElement.Clone()` 实测在底层 JsonDocument 释放后失效（`tools/JsonProbe` 独立复现），克隆方案不可靠。改为彻底物化：`HeroStat.Stats` 用自定义转换器转为 long/double/string/bool/List 原始值；`AbilityDefinition` 的 behavior/damage_type/cd/mc 与 `AbilityAttribute.value` 用 `FlexibleStringConverter` 读成字符串；`HeroAbilityMap.Abilities` 物化为对象列表。真实数据验证：127 英雄、3084 技能解析后重写与回读全部通过。
- 物品旧数据：官方 itemlist/OpenDota 均无"下线"标志且游戏文件保留全部历史定义；依据版本记录与近 30 天真实购买数据把黑名单改为集合，并补充事件/特殊模式/装饰类物品（河川药水、口袋塔/肉山、变异墓碑、超级闪烁、纪念遗物、旧中立等）的过滤。
- 合成路线：OpenDota components 不含卷轴且组件名缺 `item_` 前缀导致解析失败；修复为前缀归一化 + 按内部名追加卷轴组件（卷轴图标用通用 recipe.png，实测 200）。BKB 路线验证：秘银锤 + 食人魔之斧 + 卷轴。
- AI 提示精简：`CoachAdvice` 新增 `FriendlyEvidenceText`，把 `clock_time=300` 等原始参数翻译/过滤成 Dota 术语（开雾=开雾游走、肉山状态、建筑被摧毁、买活费用等），卡片不再展示原始字段。
- 设置页存储位置：新增"存储位置"区，可自定义物品/英雄资料缓存目录、赛后分析报告目录；AI 模型目录补"选择"按钮。设置持久化到 settings.json，启动时生效。
- 实时教练页重构：新增顶部状态卡、"我的英雄"状态卡（头像/等级/生命/魔法条/KDA/金钱 GPM XPM/当前装备 6 格）；教练提醒移入右侧并限高；顶部新增"设置"入口。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 76 项通过，0 项失败（新增对局跟踪断言） |
| `dotnet build tools\GsiReplay\AegisCoach.Replay.csproj --configuration Debug --no-restore` | 成功，0 警告，0 错误 |
| `tools\JsonProbe`（真实 OpenDota 数据） | 技能物化重写、物品合成路线含卷轴、heroStats 物化回读全部通过 |

## 2026-08-13：架构评审与研发 Agent 契约

### 结论

- 当前单一 WinUI 3 产品项目适合 MVP；`Core` 保持 Native 项目内的纯逻辑目录，不因文件较大而立即恢复独立 DLL。
- 主要结构风险位于表示层：`MainWindow.xaml.cs` 约 2586 行、`MainWindow.xaml` 约 1260 行，同时承担组合根、导航、页面状态、异步加载与服务生命周期。
- 更关键的产品问题是缺少真实比赛基准、外部 API fixture 和 UI 五态矩阵。没有这些证据时，大规模重构只能改善文件形态，不能证明提醒质量和产品可靠性。

### 文档

- 新增根目录 `AGENTS.md`：固化真实项目边界、代码归属、渐进式页面拆分策略、安全边界和验证矩阵。
- 新增 `docs/ARCHITECTURE_REVIEW.md`：记录架构评分、P0/P1/P2 改进路线、拆分条件和可衡量的完成定义。
- 更新 `docs/AI_AGENT_PROMPT_TEMPLATES.md`：明确产品运行时 Agent 与研发 Agent 不得混用，并增加 WinUI UI 设计 prompt。
- UI prompt 要求中文优先、密集可扫描、系统主题、640/1024/1440px 自适应、100%/150%/200% DPI、键盘与可访问性，以及 loading/empty/error/degraded/ready 五态。

### 验证

- 文档路径、项目入口和工具引用已与当前工作区逐项核对。
- `git diff --check` 通过；Git 仅提示现有文档下次写入时将按仓库规则转为 CRLF。
- 发现现有 `obj/.../input.json` 无法被 WinUI XAML 编译器覆盖；标准构建因此失败。后续使用隔离中间目录区分代码错误与本地构建产物锁定。
- 修正运行时 prompt 的证据边界：肉山死亡时不再随机生成 8 至 11 分钟中的某个值，而是明确说明 GSI 没有准确复活倒计时。
- `GsiSmoke` 显式链接无 WinUI 依赖的 `CoachAiPrompts.cs`，新增确定性与证据边界断言；最终 131 项通过、0 项失败。
- 使用现有还原资产、仅隔离 `IntermediateOutputPath` 与 `OutputPath` 后，Native 构建成功，0 警告、0 错误。这证明源码可编译，但不等同于标准 `obj` 锁定已解除。

## 2026-08-06：GSI 快照落盘

### 实现

- 新增 `native/AegisCoach.Core/Services/GsiSnapshotRecorder.cs`。
- 通过 token 校验的快照按本地日期写入 `%LOCALAPPDATA%\AegisCoach\gsi\snapshots-YYYY-MM-DD.ndjson`。
- 写盘前将 `auth.token` 替换为 `[REDACTED]`；落盘失败只写日志，不阻断 GSI 接收和规则计算。
- 实时教练页面增加“打开目录”入口，并显示记录位置和脱敏说明。
- GSI 设置页增加“应用启动时自动监听 GSI”和“打开 cfg 目录”；安装成功提示明确要求重启 Dota 2。

### 验证

| 时间 | 操作 | 结果 |
|---|---|---|
| 16:30 | `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 通过 28 项，失败 0 项；覆盖接收、解析、token、去重、断线、规则和快照落盘 |
| 16:35 | `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |
| 16:37 | 启动 `native\AegisCoach.Native\bin\Debug\net8.0-windows10.0.19041.0\win-x64\AegisCoach.Native.exe` | 首次 6 秒检查只看到进程，未观测到端口；延长检查后确认窗口标题为“神盾教练”、进程响应正常，`127.0.0.1:4444` 为 `Listen` |
| 17:09 | `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 新增指定目录 cfg 安装后通过 31 项，失败 0 项 |
| 17:12 | 启动最终 Debug exe 并等待 12 秒 | 进程响应；当前自动化桌面会话未稳定观测到窗口标题或 `127.0.0.1:4444`，未宣称真实 GSI 接入完成 |

### 未决项

自动化测试已验证指定目录 cfg 安装；真实 Steam Dota 2 重启、cfg 加载和 GSI POST 尚未完成，且应用窗口/监听状态在当前自动化桌面会话中不稳定，这两项属于下一次可见桌面/真机验收。

## 2026-08-07：资料库与回放稳定化准备

### 已确认

- GSI 冒烟测试保持通过：31 项通过，0 项失败。
- 英雄缓存包含 127 条目录数据。
- 物品页面、官方物品接口和最近 30 天统计查询代码已存在，但当前物品缓存为空，尚未通过运行时验收。
- 物品加载失败的直接原因已定位：OpenDota constants/abilities 的 attrib[].value 在部分记录中是数组，而 DTO 当前只接受字符串，导致目录加载链路中断。
- 计划修复物品列表与统计查询的故障隔离，并新增 NDJSON 离线回放；这些改动在编辑审批服务恢复前尚未写入。

### 当前阻塞

- 工作区补丁工具的内部审批代理持续返回 404：codex-auto-review 模型未配置。补丁未进入工作区，因此未使用 PowerShell 重写文件绕过编辑保护。

### VAC 边界

- 实时对接只允许 Dota 2 GSI 通过 127.0.0.1 发送的 HTTP 快照。
- 不读取游戏进程内存，不注入 DLL，不做 DirectX Hook，不模拟键鼠，不自动执行施法、购买、移动或其他游戏操作。
- 真机验收前仍需在另一台 Dota 2 电脑手动指定 cfg 目录，安装配置并重启 Dota 2；未完成这条链路前不宣称真实对局已接入。

## 2026-08-07：AI Agent Prompt 模板

### 实现

- 新增 docs/AI_AGENT_PROMPT_TEMPLATES.md，统一维护 system prompt、结构化证据输入契约、输出结构和评估清单。
- 覆盖赛后复盘、实时提醒解释、英雄/技能/物品资料问答、数据质量检查和研发 Agent 五类场景。
- 明确 evidence_id、时间范围、数据完整性、样本不足和“无法判断”规则，禁止把统计相关性写成因果结论。
- 明确只允许解释 Dota 2 GSI 本机 HTTP 快照；禁止进程内存读取、注入、Hook、输入模拟、自动游戏操作和 VAC 规避。
- 在 README.md 和 docs/PROJECT_DOCUMENTATION.md 增加模板入口及使用契约说明。

### 验证

- 模板文件存在，共 225 行。
- 已检查 INPUT_DATA、OUTPUT_SCHEMA、evidence_id、脱敏、GSI 127.0.0.1、JSON 校验和 VAC 边界等关键内容。
- 已检查 README 与完整项目文档中的模板链接均指向现有文件。

## 2026-08-07：资料库网络失败隔离

### 已确认

- 应用日志显示官方 herolist 和 itemlist 请求在 TLS 握手阶段失败，底层错误为证书链 PartialChain，不是 HTTP 404。
- 当前机器的 LOCALAPPDATA\AegisCoach 当时没有 catalog.json 或 items.json，因此请求失败会直接表现为 0 条目录。
- OpenDota 网络探测在当前沙箱中被网络策略拦截，尚未完成真实降级接口的联网验收。

### 已实现

- 官方英雄中文目录改为可选增强源；OpenDota 英雄统计和技能常量仍是基础目录链路。
- 物品官方中文目录失败时回退到 OpenDota /constants/items，并记录当前目录来源。
- 物品统计请求单独失败时继续显示物品目录，统计栏显示暂不可用。
- OpenDota 技能参数 attrib[].value 兼容字符串、数字和数组，数组会归一化为可读文本。
- 官方英雄/物品详情失败时保留基础资料，不使用关闭 TLS 证书校验的方式规避网络问题。

### 验证

| 操作 | 结果 |
|---|---|
| GsiSmoke 冒烟测试 | 通过 31 项，失败 0 项 |
| 原生 Debug 构建（普通沙箱） | 被现有 WinUI obj 目录 input.json Access denied 阻塞 |
| 原生 Debug 构建（受控构建权限） | 成功，0 警告，0 错误；生成 WinUI x64 Debug 程序 |
| OpenDota 在线探测 | 当前沙箱禁止访问 api.opendota.com:443，未宣称在线数据源已恢复 |

### 仍需用户环境验证

- 在可访问 dota2.com 和 api.opendota.com 的 Windows 机器启动应用，确认英雄目录、物品目录和物品详情的实际数量。
- 若官方接口仍报 PartialChain，检查系统日期时间、Windows 根证书更新、代理/企业 HTTPS 检查和杀毒软件证书代理；不要关闭证书验证。

### 应用启动验收

- 2026-08-07 启动最新 Debug 程序成功，窗口标题为“神盾教练”，进程响应正常。
- 当前未收到另一台 Dota 2 电脑发来的真实 GSI POST，因此未宣称真实对局接入完成。

## 2026-08-07：资料数值、当前物品、分段出装与页面重构

### 实现

- 物品描述和技能描述统一解析 `%value%`、`{s:value}`、`{{value}}` 等占位符；官方数值数组、字符串和数组格式由兼容转换器归一化。
- 物品目录过滤图纸、信使/共享消耗品和已知下线物品；官方目录优先，OpenDota 只做降级和合成组件补充。
- 物品详情增加合成路线；组件只使用数据源返回的组件 ID，不在缺失时猜测。
- 英雄详情增加天赋等级/左右分支展示，以及当前分段最近 30 天出场率排序的出装列表。
- 页面减少英文装饰标题和大圆角卡片，改用中性资料库配色、紧凑网格和明确的详情分区。
- 新增 `docs/AI_RUNTIME_DESIGN.md`，明确实时教练当前是确定性规则，不依赖模型；模型只作为可选解释层，失败时回退规则原文。
- `App.xaml` 改用 WinUI `System*Color` 资源，背景、文字、边框和强调色跟随 Windows 浅色/深色主题与系统强调色。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build tools\GsiSmoke\AegisCoach.Smoke.csproj --configuration Debug --no-restore` | 成功，0 警告，0 错误 |
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |
| 本机官方物品缓存过滤审计 | 408 条中排除 9 条明显内部/下线物品；真实线上当前版本仍需用户环境联网复核 |
| 官网视觉参考页 | 浏览器安全策略拒绝访问 `dota2.com.cn`，未绕过；布局按官方资料库/商店信息层级完成重构 |

### 未决项

- OpenDota Explorer 的 `avg_rank_tier` 出装查询需要在可访问 OpenDota 的环境中运行一次，确认接口字段和样本量；查询失败时不显示推荐出装，不影响资料页。
- 真实 Dota 2 当前版本的新增/下线物品仍以官方 `itemlist` 为准，离线黑名单只能作为保护性过滤。
## 2026-08-07：v7.41e 数据隔离、天赋与本地教练

### 实现

- OpenDota 物品 components 字段按 null 归一为空集合，修复物品页 Value cannot be null (Parameter source)。
- 合成组件显示中文名，组件缺失时不猜测路线。
- 天赋数值合并官方 special_values 与 OpenDota attrib；特殊天赋支持单值回退和通用数值 ID 回退，移除未解析占位符。
- 目录、详情、统计和分段出装缓存加入 v7.41e 后缀；官方接口没有可靠 patch 参数，因此界面标注目标版本而不是已验证版本。
- 实时教练增加控制锁定、买活窗口、资源完整三条规则；增加可选 LocalCoachService，连接 Ollama 进行异步短解释，超时回退规则原文。
- 统一 WinUI 系统 ThemeResource 颜色，保持 Windows 浅色/深色主题与系统强调色。

### 验证

- WinUI x64 Debug 构建：0 警告，0 错误。
- GSI 冒烟测试通过 34 项，失败 0 项；真实官方/OpenDota 联网与 Dota 2 GSI 仍需用户环境复核。

## 2026-08-07：低占用解释与 NPU 能力探测

### 实现

- 实时解释请求增加 3 秒默认间隔、单请求串行闸门和重叠请求丢弃，避免模型调用堆积影响 GSI。
- Ollama 请求限制为 512 tokens 上下文、48 tokens 输出，模型驻留时间设置为 20 秒。
- 新增 Windows NPU 设备名探测，并在实时教练页面展示能力提示；未把硬件探测结果误报为已启用 NPU 推理。
- 加入 Windows ML execution provider 发现与注册；仅尝试 NPU/QNN/OpenVINO/VitisAI provider，明确跳过 DirectML、CUDA、TensorRT 和 ROCm。
- Ollama 解释请求增加 `num_gpu=0`，即使启用本地解释也不使用 GPU；NPU provider 注册为后续量化 ONNX 模型执行做准备。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |

## 2026-08-08：UI 美化、Dota2 天赋树与 AI 后端替换

### UI 美化（使用 ui-ux-pro-max 技能）

- 安装 `ui-ux-pro-max` 技能到项目 `.agents/skills/ui-ux-pro-max`（仓库 nextlevelbuilder/ui-ux-pro-max-skill；本机无 Node/npm，按官方 Codex 模板手动放入 `.agents/skills`）。
- 按技能生成的设计系统（Real-Time Monitoring + Dota 2 电竞面板方向：琥珀金点缀、暗色卡片、属性色徽章）统一重构 `App.xaml` 主题资源与 `MainWindow.xaml`：
  - 英雄页：页头、属性页签配色、英雄卡片（属性角标 + 胜率进度条）、详情头部（属性/攻击类型徽章 + 攻击/护甲/移动/射程/生命/魔法数值芯片）、技能 QWER 槽位标识、天赋树。
  - 天赋树：仿 Dota 2 布局（等级徽章居中、左右分支、连接线、选中高亮），每级左右二选一，点击可预览选择，仅资料展示不修改游戏。
  - 物品页：页头与卡片圆角/描边统一。
  - 实时教练页：移除血条、蓝条、击杀/金钱/装备/技能栏等自身状态展示，专注 AI 提醒；保留连接状态、对局概览（阶段/时间/比分/昼夜/建筑）、全场英雄与出装推荐。

### AI 后端替换（移除 Ollama）

- 删除 `LocalCoachService`（Ollama 专用：/api/generate、/api/tags、/api/pull），提示词构建移至 `CoachAiPrompts`。
- 新增 `CloudCoachService`：OpenAI 兼容 `/chat/completions`，密钥不写日志，超时/失败回退规则原文。
- `AppSettings` 新增 `AiBackend`（npu/cloud/rules，默认 npu）与云端配置；旧 `npuEnabled` 自动迁移。
- 设置页“AI 与数据”：后端下拉（NPU 本地推理/云端 API/规则原文）、NPU 模型目录、云端接口/模型/密钥、解释间隔、检查 NPU provider、测试云端连接。
- NPU 默认启用：本机已存在 OpenVINO/Qwen2.5-1.5B-Instruct-int4-ov（约 930 MB），不占 GPU、不占用 CPU 主核；失败回退规则原文。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 61 项通过，0 项失败 |
| 启动 Debug exe | 窗口标题“神盾教练”正常；非沙箱环境下 `127.0.0.1:4444` 为 Listen（沙箱内启动会被网络沙箱拦截监听，属环境限制非代码问题） |
| 文档 | README、AI_RUNTIME_DESIGN、PROJECT_DOCUMENTATION、OPERATION_LOG 已同步移除 Ollama 并记录三后端与 UI 美化 |

### 待用户环境验证

- NPU 首次解释需约 30 秒加载（仅一次），稳定态约 3.3 秒/次；请在真实对局中确认提醒 + 智能解释链路。
- 云端后端需要用户自备 OpenAI 兼容接口地址与密钥，设置页可“测试云端连接”。
- 真实 Dota 2 GSI 与 OpenDota 联网数据仍需用户环境复核。

## 2026-08-08：第二轮 UI 重构与天赋数值修复

### 启动问题排查

- 本机多次构建、启动验证均正常（窗口标题“神盾教练”、`127.0.0.1:4444` 监听）；WER 存档中 00:02 的三条 AegisCoach 崩溃报告属于旧版本，10:13 有两次 MoAppHang（挂起）记录。
- 已确认 `last-error.txt` 中的 OpenDota Explorer 400 只是出装查询降级日志，界面有兜底，不会导致崩溃。
- 若用户环境仍报错，需要用户提供报错对话框文字（当前会话无法读取截图内容）。

### 天赋数值修复（离线可用）

- 根因：`DotaCatalog.Abilities` 与 `HeroAbilities` 未持久化，缓存命中时每次都要联网取 OpenDota `/constants/abilities`；网络失败后天赋 `{s:bonus_xxx}` 占位符无数值来源，数字消失。
- 修复：新增 `abilities-v7.41e.json`、`hero-abilities-v7.41e.json` 持久化缓存；缓存命中时先读本地、再后台联网刷新，失败保留旧缓存。
- 天赋解析：只有通用 `{s:value}` 占位符才用技能 ID 末尾数值回退（如 `special_bonus_hp_300` → +300）；命名占位符（如 `bonus_vampiric_aura`）数值只来自技能定义，缺失时去掉残留符号/单位，不再伪造错误数值。
- 天赋行按原始顺序的真实等级（10/15/20/25）分组，不再因单侧缺失导致等级错位。

### UI 重构（去“AI 味”，贴近游戏辅助软件）

- 收敛圆角（3px 为主）、收紧间距与留白，页头改为普通标题 + 细分隔线，去掉金色装饰条。
- 标准图标统一为 14px SymbolIcon，实时教练页重排：顶部状态栏（连接 + 对局概览 + 提醒计数）→ 教练提醒（主区域）→ 阵容与出装（下方三栏、顶部对齐）。
- 天赋树按 Dota 2 游戏内样式精修：中央竖脊 + 等级徽章（10/15/20/25）、左右节点、连接线、悬停金色描边、选中金色填充。
- 英雄详情属性芯片、技能 QWER 槽位、物品卡片统一收敛为 2-3px 圆角与紧凑排版。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 61 项通过，0 项失败 |
| 非沙箱启动 Debug exe | 窗口“神盾教练”正常，`127.0.0.1:4444` 为 Listen |
| 天赋解析模拟 | 通用天赋显示正确数值（+300 生命、+50 攻击速度）；独有天赋缺失数值时不显示伪造数字 |

## 2026-08-07：Ollama 模型安装与 GSI/NPU 现场诊断

### 模型安装

- 本机 Ollama（127.0.0.1:11434，版本 0.32.6）原无任何模型。
- 安装 `qwen2.5:1.5b`（Q4_K_M，约 985 MB），与应用默认 `LocalModelName` 一致。
- 用应用同款提示词实测：输出符合 35 字以内中文可执行建议；纯 CPU 下短回答约 1.2 秒，48 token 上限约 2.5~3.5 秒，超过应用当前 800ms 超时会回退规则原文。

### GSI 现场诊断（Dota 2 已启动但页面未连接）

- 应用进程监听 127.0.0.1:4444 正常；`%LOCALAPPDATA%\AegisCoach\gsi\snapshots-2026-08-07.ndjson` 在 21:16 之前只有冒烟测试数据。
- cfg 位于 `C:\Program Files (x86)\Steam\steamapps\common\dota 2 beta\game\dota\cfg\gamestate_integration_aegiscoach.cfg`，URI 4444、空 Token，写入时间 20:48 早于 Dota 2 启动 21:11，配置链路正确。
- 应用侧端到端验证通过：向 4444 发送完整结构快照，HTTP 200 且 NDJSON 成功追加一行。
- 40 秒 TCP 监测确认 dota2.exe 未向 127.0.0.1:4444 发起任何连接；结论是 Dota 2 未发送 GSI 数据（GSI 只在进入对局/选人阶段发送，主菜单不发送），非应用侧故障。
- 待用户操作：进入一场对局后确认"已连接"；若对局中仍无数据，为 Dota 2 添加 Steam 启动项 `-gamestateintegration` 后重启验证。

### NPU 现场诊断

- 本机存在 Intel(R) AI Boost NPU（PCI VEN_8086 DEV_AD1D，驱动 Intel(R) NPU Software & Drivers 1.0.16.0），NpuCapabilityDetector 关键字可识别。
- 事实确认：Ollama 官方后端不含 Intel NPU（仅 CPU/CUDA/ROCm/Metal 等），当前 `num_gpu=0` 的 CPU-only 路径不会使用 NPU。
- 使用 NPU 的真实路径是量化 ONNX 模型 + Windows ML OpenVINO provider（应用已有 provider 注册脚手架，缺少模型与生成回路），或 onnxruntime-genai + OpenVINO EP NPU bundle。
- 未做代码改动；是否实现 NPU 推理路径待用户确认（涉及约 1~2 GB 量化模型下载与新增推理依赖）。

## 2026-08-07：解释超时修复与 GSI 复检

### 实现

- `LocalCoachService` 解释请求超时由 800ms 放宽到 4 秒：实测 qwen2.5:1.5b 纯 CPU 短回答约 1.2 秒、48 token 上限约 2.5~3.5 秒，800ms 会让智能解释几乎必然超时回退。
- HttpClient 全局超时调整为 30 分钟，避免影响模型下载；探测仍为 800ms。
- `docs/AI_RUNTIME_DESIGN.md` 同步更新超时口径并明确解释路径不占用 GPU。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |

### GSI 复检（用户新开人机后）

- 用户于 22:31:49 重启 Dota 2（新 PID）；Steam 启动选项含 `-perfectworld -gamestateintegration`，但 localconfig.vdf 写入时间（22:31:53）晚于游戏启动 4 秒，本实例可能未吃到启动参数。
- 30 秒 TCP 监测：dota2.exe 仍无任何到 127.0.0.1:4444 的连接；快照文件仍只有测试数据。
- 待用户按顺序执行：完全退出 Dota 2 → 确认启动选项含 `-gamestateintegration` → 重新启动 → 进入人机且进入"对局中"阶段 → 页面应变"已连接"。

### NPU 实现路线（待确认）

- 已确认机器为 Intel AI Boost NPU（Lunar Lake 系），驱动 1.0.16.0 已装；未在系统发现 OpenVINO 运行时。
- 推荐路线：onnxruntime-genai（OpenVINO EP，device_type=NPU）+ Qwen2.5-1.5B INT4 量化模型；需安装 OpenVINO 运行时（约数百 MB）并下载量化模型（约 1~2 GB），再新增 C# 生成回路与 Ollama/CPU 回退。

## 2026-08-07：GSI 根因修复与 NPU 后端落地

### GSI 根因

- 根因：Dota 2 只读取 `game\dota\cfg\gamestate_integration\` 子目录中的 GSI 配置；此前程序把 cfg 安装到了 `cfg\` 根目录，游戏从未加载，因此 Dota 2 不发送任何数据。
- 证据：子目录中已有 Logitech 的 `gamestate_integration_logitech.cfg`，而根目录的 cfg 始终未被读取；40 秒级 TCP 监测多次确认 dota2.exe 无任何到 127.0.0.1:4444 的连接。
- 修复：`GsiConfigInstaller.LocateDotaCfgDirectory` 改为定位/创建 `cfg\gamestate_integration` 子目录；cfg 已实际写入该目录；`docs/STEAM_DOTA2_GSI_GUIDE.md` 同步更正路径。
- 验证：`dotnet build` 0 警告 0 错误；GsiSmoke 冒烟测试 42 项通过，0 失败（探测目录已输出正确子目录）。
- 待用户操作：完全退出并重启 Dota 2，进入对局后确认页面"已连接"。

### NPU 后端

- 新增 `NpuGenAiCoachService`：OpenVINO GenAI C# API（JYPPX.OpenVINO.CSharp.API 3.3.1 + GenAI runtime 2026.3.0），设备 `NPU`，管线常驻；失败自动回退 CPU/Ollama。
- 模型：`OpenVINO/Qwen2.5-1.5B-Instruct-int4-ov`（约 930 MB），下载到 `%LOCALAPPDATA%\AegisCoach\models\qwen2.5-1.5b-int4-ov`。
- 设置新增 `NpuEnabled`、`NpuModelPath`；实时教练页新增"启用 NPU 推理"开关与模型目录输入。
- 实测：OpenVINO 识别 CPU/GPU/NPU；NPU 首次加载约 30 秒，稳定态每次解释约 3.3 秒（首字 2.6 秒，10~13 tok/s）；同模型 CPU 约 5.2 秒；Ollama CPU 短回答约 1.2 秒。NPU 默认关闭，收益为 CPU 空闲与低功耗。
- llmware 版 int4-ov（1.1 GB）在本机 NPU 编译器报重复节点错误，已弃用；已清理该模型目录。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 42 项通过，0 项失败 |
| `tools\NpuProbe` NPU 推理 | 设备枚举 CPU/GPU/NPU；NPU 生成正确中文建议；首次加载 30.6 秒，稳定态约 3.3 秒/次 |

## 2026-08-07：教练页布局修复、全场英雄追踪与出装推荐

### 布局修复

- 根因：实时教练页没有外层滚动条，AI 设置区（智能解释/NPU/模型目录）被挤到窗口可视区外，用户看不到"AI 区"。
- 修复：`CoachPage` 外层改为 ScrollViewer，页面可整体滚动；教练提醒列表限制最大高度 420，避免无限增高。

### 展示更多

- "连接与对局"新增昼夜、建筑（天辉/夜魇存活建筑数）两行；GSI 配置新增 `buildings` 数据并已写入 Dota 2 目录。
- "我的英雄"新增"当前装备"行（本地化物品名）。

### 全场英雄与实时出装推荐

- GSI 事实：自己比赛的 GSI 只返回本玩家与本英雄；敌方/队友英雄需通过 OpenDota 公开实时对局列表获取，人机/私人局不提供。
- 新增 `LiveMatchHeroService`：轮询 OpenDota `/api/live`，按自身 steamid 定位当前公开比赛，返回全场 10 名英雄（只读公开接口，不上传数据）。
- 新增 `ItemRecommendationEngine`（Core 确定性规则）：按敌方法术爆发/治疗救人/强控/敏捷物理核心构成给出黑皇杖、否决挂穗、清莲宝珠、护甲闪避等建议，每条带英雄名单、时间、金币证据。
- 实时教练页新增"全场英雄与出装推荐"面板（我方/敌方英雄 + 推荐出装）；设置新增"启用全场英雄追踪与出装推荐"开关（默认开）。
- GSI 快照每 40 秒轮询刷新一次全场英雄，比赛结束或阶段退出自动停止。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 45 项通过，0 项失败（新增出装推荐 3 项断言） |
| OpenDota `/api/live` 现场探测 | 返回 100 场公开对局，players 含 account_id/hero_id/team，结构可用 |
| 应用重启 | 新版启动成功，`127.0.0.1:4444` 监听正常；cfg 已含 buildings 数据 |

## 2026-08-07：教练页现代化重构与大数据出装

### 页面重构

- 实时教练页整体重做：连接状态改为右上角小绿灯（图标式，不再置顶大块监控信息）；GSI 配置、教练规则、AI 设置、日志全部移入"设置"对话框（教练页右上角"设置"按钮）。
- 我的英雄卡片：英雄头像（官方 CDN 图标）、等级徽章、Dota2 风格绿血条/蓝法条（渐变 + 数值叠加）、击杀/死亡/助攻/补刀/金钱/每分钟金钱与经验；装备区为 3×2 格子，物品图标 + 中文名悬浮提示，空格为暗色槽，不显示 empty。
- 全场英雄改为头像 + 中文名芯片（我方/敌方分组）；对局信息（阶段、时间、比分、昼夜、建筑）合并为一行。
- 教练提醒全部中文（严重/警告/提示 + 中文标题与正文），卡片化展示。

### 大数据出装推荐（替代简单规则为主）

- 新增 OpenDota `heroes/{id}/itemPopularity`（按早/中/后期物品购买热度）与 `heroes/{id}/matchups`（对阵胜率）接入，内存缓存。
- 推荐逻辑：按当前游戏时间选取对应阶段物品热度榜，取前 3 件并标注热度占比与近期胜率；对阵数据识别"敌方对你有胜率优势"的英雄并给出局势提示；规则引擎只作补充提示（黑皇杖/否决等）。
- 推荐面板文案："排名. 中文物品名 —— 热度 X% · 胜率 Y%" + "局势：敌方 X 对你有 54% 胜率优势" + "提示：BKB（理由）"。

### AI 与设置

- 智能解释默认开启（`SmartCoachEnabled` 默认 true，用户本地 settings.json 已同步）；规则事件由本地 AI（CPU 或 NPU）生成中文建议，失败回退规则原文。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 45 项通过，0 项失败 |
| 应用重启 | 新版启动成功，4444 监听正常，smartCoachEnabled=true |
| 待真机验证 | 公开比赛中的全场英雄芯片、物品热度与对阵提示、装备图标渲染 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 34 项通过，0 项失败；GSI、规则、token、cfg 和快照验证通过 |
| 应用输出检查 | 已包含 Windows ML、ONNX Runtime 运行时；代码未注册 GPU provider |

## 2026-08-07：NDJSON 规则离线回放

### 实现

- 新增 `GsiReplayService`，读取 `GsiSnapshotRecorder` 生成的脱敏 NDJSON，按 `received_at` 排序后重新进入 `CoachRuleEngine`。
- 新增 `tools/GsiReplay` 命令行工具，支持单个记录文件或 `%LOCALAPPDATA%\AegisCoach\gsi` 目录，输出快照摘要、规则提醒和坏行位置。
- 回放不启动 GSI 监听、不访问网络、不调用 Ollama 或 NPU，确保规则回归测试可重复。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build tools\GsiReplay\AegisCoach.Replay.csproj --configuration Debug --no-restore` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 39 项通过，0 项失败；新增快照回放和低血量规则回归断言 |
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |

## 2026-08-08：第二轮验收修复（物品白名单、克制数据、教练页 UI、小技巧）

### 物品目录改为"当前版本白名单"（彻底修复过时物品）

- 根因：官方 itemlist 数据源会返回全部历史物品定义，之前的黑名单式过滤无法覆盖所有已移除/旧中立物品；OpenDota 当前数据集过旧（仅 446 场近 30 天比赛），不能作为当前版本权威。
- 修复：新增 `native/AegisCoach.Native/Data/CurrentItemCatalog.cs`（由 `tools/GenCurrentItemCatalog.py` 从 d2vpkr 当前版本游戏文件 `shops.txt` + `neutral_items.txt` 生成）：
  - 商店物品 190 个（当前商店页签 + 守卫供应商/信使/芒果/知识之书/刷新碎片等）；
  - 中立物品 68 个（按中立分层表，附魔 tier=0），并写入 `NeutralTier` 供页面分类；
  - 特殊物品 7 个（肉山掉落/升级件）。
  - `IsCurrentItem` 改为白名单判定，旧缓存读取时也会重新过滤；合成路线组件同步过滤已移除物品。
- 验证：372 项旧缓存中移除 110 项（圆盾、淬毒珠以外的大量旧中立如 keen_optic/grove_bow、永世法衣、丰饶之环、死灵书、达贡 2-5 等），保留 262 项；穷鬼盾/勋章/风暴宝器等当前中立正确标记 tier。

### 克制/被克制为空修复

- 根因：OpenDota 当前样本量小（对阵数据单英雄最多几百场），原 `GamesPlayed>=100 + 胜率 45/55` 阈值过滤后一边常为空。
- 修复：样本门槛降到 30，阈值改为 52/48，且任一侧不足 3 条时用胜率两端补足；英雄详情页与实时教练页同步修正，页面文案同步改为 ≥52%/≤48%。

### 实战小技巧不再为空

- 新增按英雄实际技能数据生成的提示（对线/大招/连招/定位/身板），并补充热门英雄手写条目（卡尔、火枪、影魔、PA、主宰、小牛、莉娜、风行、蓝猫、莱恩、萨尔、炸弹人、蓝胖、猴子、复仇之魂、巫妖等）；技能加载完成后自动重刷。

### 实时教练页重构（游戏辅助盒子风格）

- 整体换成深蓝游戏面板配色（CoachPanelTint 系列 + 金色点缀）：顶部状态条、我的英雄卡（金边头像、等级角标、红蓝条、KDA/经济）、当前装备六格、教练提醒、阵容芯片、克制分析统一风格。
- 图标修复：GSI 每秒推送导致装备/头像每帧重建 BitmapImage 而闪图/空白；改为按物品名缓存图标并仅在装备变化时重建集合，头像按英雄 ID 缓存。

### 天赋树 / 基础属性对齐

- 天赋节点改为左右等宽（236px）居中排版，脊线与等级徽章居中对齐。
- 基础属性卡固定等高，HeroStats 数值卡统一 118px 宽、3 列对齐。

### 出装推荐查询 400 修复

- OpenDota 当前 schema 已无 `avg_rank_tier` 列，`LoadHeroBuildsAsync` 的按分段过滤 SQL 返回 400；移除该列引用，改为最近 30 天全分段统计。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj -c Debug -p:Platform=x64` | 成功，0 警告，0 错误 |
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj -c Debug` | 76 项通过，0 项失败 |
| 物品目录过滤交叉验证（脚本） | 372 → 262 项保留，110 项过期/已移除物品剔除 |
| OpenDota 出装/物品统计 SQL 实测 | 修复后返回正常（修复前 400） |
| 应用启动 | 新版启动成功，窗口正常显示 |

## 2026-08-08（追加）：赛后复盘改为基于回放操作记录的真实点评

### 背景

之前的赛后复盘提示词只含聚合统计（KDA/补刀/GPM）与字符串关键节点，AI 只能复述信息，无法点评操作。本机已按日落盘的 GSI NDJSON 回放数据没有进入复盘链路。

### 实现

- 新增 `MatchOperationExtractor`（Core）：从 `%LOCALAPPDATA%\AegisCoach\gsi\snapshots-*.ndjson` 按 matchid 提取整场操作记录：
  - 技能施放与主动道具使用（冷却从就绪变为施放）、回城卷轴消耗；
  - 购买时间线（主栏出现新物品）；
  - 阵亡上下文：阵亡前 35 秒窗口血量轨迹、大招/TP/关键主动道具是否就绪未用、阵亡前 20 秒操作、窗口内补刀、距上次阵亡间隔、可买活及是否买活；
  - 击杀、买活、诡计之雾、肉山、建筑事件；
  - 每 10 分钟阶段背景（击杀/死亡/补刀增量、平均 GPM/XPM、该阶段购买）。
- `CoachAiPrompts.BuildMatchReviewPrompt` 改为以操作记录为输入，明确要求模型：不复述数据、按时间点点评操作问题（关键道具/大招该用未用、阵亡前决策、装备节奏、买活选择）、区分事实与推断、每条建议带游戏时间。
- 赛后报告新增“回放操作记录（GSI 提取）”展示与“重新生成复盘”按钮；旧报告可在回放记录存在时重新分析并重新生成。
- 无 AI 后端时的规则摘要也改为引用操作事实（对线期补刀/死亡、阵亡上下文、错过的买活窗口），不再只是罗列统计。

### 边界

- GSI 没有英雄坐标、敌方英雄状态与视野，模型只能点评自身操作时机与决策，不能点评走位、视野与团战细节。
- 本地 NPU 1.5B 输出偏短，高质量长复盘建议使用云端 OpenAI 兼容后端。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 103 项通过，0 失败（新增回放操作提取 26 项断言） |
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |

## 2026-08-08（追加）：按比赛 ID 从 OpenDota 拉取整场数据复盘

### 实现

- 新增 `ApiMatchAnalyzer`（Core）：按 matchid 调 OpenDota 公开接口 `GET /api/matches/{id}`（无需密钥），自动定位本人在场玩家（Steam ID 64 换算 account_id，留空时可用英雄 ID 唯一匹配），提取：
  - 双方阵容与各自 KDA/GPM/XPM/补刀/最终装备；
  - 本人装备购买时间（purchase_time，过滤消耗品/卷轴/初始物品）、击杀时间线、全场 kills_log 反推阵亡时间、买活时间；
  - 团战（参与、伤害、团战内阵亡/买活）、本人推塔事件、视野（真假眼）、每分钟经济/经验/补刀曲线、经济分位（benchmarks）、APM 与英雄/塔伤害。
- 装备字段兼容两种形态：字符串内部名（item_blink）与数字 ID（老数据）。
- `MatchSessionRecord` 新增 `SteamId`，对局结束自动写入习惯库，供赛后 API 定位本人。
- 赛后复盘流程升级：本地 GSI 回放与 OpenDota 整场数据互补——本地提供阵亡前逐秒状态（技能/道具/大招就绪），API 提供全局战局（阵容、装备时间、团战、视野、经济分位）；两者可同时进入同一份提示词。
- 赛后分析页右上角新增“输入比赛 ID + 从 API 拉取复盘”，可对任意公开比赛（不依赖本机录制）生成整场复盘。

### 边界

- OpenDota 免费接口只收录公开/已解析比赛；人机、未公开或刚结束尚未解析的对局会返回未收录，此时仍可用本地 GSI 回放复盘。
- API 没有阵亡前逐秒状态（本地回放才有），两者点评侧重点不同。

### 验证

| 操作 | 结果 |
|---|---|
| `dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj --no-restore` | 121 项通过，0 失败（新增 API 整场分析 18 项断言） |
| 真实 OpenDota 比赛数据（约 197KB，18 分钟对局） | 定位玩家、KDA 3/2/14、阵亡时间 2、装备时间 20、团战 8、经济点 18，全部正确 |
| `dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore` | 成功，0 警告，0 错误 |
