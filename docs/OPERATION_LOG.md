# 神盾教练操作记录

本文件记录每次继续开发后的构建、测试和应用操作。命令均从 `E:\openSource\demo` 执行；结果以实际输出为准。

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
