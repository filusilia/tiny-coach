# 神盾教练（PC 端）

神盾教练是一个面向 Windows PC 的 Dota 2 资料与教练工具，当前使用 WinUI 3 原生桌面技术实现。

完整的功能、数据口径、完成度和后续计划见：[项目完整文档](docs/PROJECT_DOCUMENTATION.md)。
启动、浏览和实时链路的 AI 可执行优化规范见：[性能优化实施规范](docs/AI_PERFORMANCE_OPTIMIZATION_PLAN.md)。
逐日开发、现场诊断和验证结果见：[操作与验证历史](docs/OPERATION_LOG.md)。
AI Agent 的统一输入契约、赛后复盘、实时解释、资料问答、数据质量和研发模板见：[AI Agent Prompt 模板](docs/AI_AGENT_PROMPT_TEMPLATES.md)。
仓库代码 Agent 的执行边界、UI 设计 prompt 和验证矩阵见：[AGENTS.md](AGENTS.md)。
当前架构评价、主要风险和渐进式演进路线见：[架构评审](docs/ARCHITECTURE_REVIEW.md)。
实时教练的模型边界、云端/离线部署和回退策略见：[AI 运行设计](docs/AI_RUNTIME_DESIGN.md)。
游戏画面捕获的 Microsoft/NVIDIA 选型、视觉证据边界和 PoC 门槛见：[画面捕获与 AI 教练可行性](docs/SCREEN_CAPTURE_AI_FEASIBILITY.md)。

## 当前可用

- 英雄中文资料、英雄图像、属性分类和可滚动响应式网格。
- 英雄详情增加属性/攻击类型徽章、攻击/护甲/移动/射程/生命/魔法数值芯片、技能槽位（QWER）标识，以及仿 Dota 2 的天赋树（每级左右二选一，可点击预览）。
- 技能、天赋、冷却、魔法消耗、技能参数中文展示，覆盖主描述、魔晶和神杖文本。
- 英雄详情内置按英雄整理的 Wiki 风格实战提示，并标注为编辑内容。
- 全部/先锋/卫士/中军/统帅/传奇/万古/超凡/冠绝分段胜率。
- 职业比赛样本中的英雄 Pick、Ban 和 BP 占比。
- 物品中文目录、官网式分类筛选排序、搜索、价格、说明、购买数、胜局数和胜率。
- Windows 系统浅色/深色主题跟随、左侧 WinUI 导航、窗口缩放；界面按 Dota 2 电竞面板风格统一美化（琥珀金点缀、暗色卡片、分段胜率条）。
- 实时教练（GSI 最小闭环）：本地监听 Dota 2 Game State Integration，专注 AI 提醒——页面不再展示血条/蓝条等自身状态，只保留连接状态、对局概览（阶段/时间/比分/昼夜/建筑）与带证据的教练提醒（低血量、低魔量、大招就绪、阵亡、控制锁定、买活窗口、资源完整、比赛开始/结束）。
- AI 智能解释可选三种方式：本地低功耗、云端 API、仅规则提醒。任何智能后端不可用或超时时自动回退规则提醒，不影响 GSI 接收。
- 资料数据按目标版本 v7.41e 隔离缓存；官方接口没有可靠 patch 参数，线上版本仍需在可联网环境复核。
- 资料请求支持重试、指数退避、限速和超时分类；损坏缓存自动隔离，设置与报告使用原子写入。
- 小于 30 的统计样本不展示胜率结论；Cloud/Stratz 密钥保存在 Windows 凭据保险库。
- GSI 快照记录：有效快照按日期写入本地 NDJSON，落盘前自动脱敏 token；可从设置打开统一数据目录。
- GSI 配置文件生成与安装：自动探测 Steam 库路径，探测失败时可在高级设置中手动选择目录并重新安装。
- Steam Dota 2 GSI 接入步骤见：[Steam Dota 2 GSI 接入手册](docs/STEAM_DOTA2_GSI_GUIDE.md)。

## 如何启动

Visual Studio 2022：

1. 打开 `native/AegisCoach.Native.sln`。
2. 顶部配置选择 `Debug` 和 `x64`。
3. 解决方案中只有 `AegisCoach.Native`，将它设为启动项目。
4. 按 `F5` 调试，或按 `Ctrl+F5` 直接运行。

首次启动会先打开基础设置。点击“保存并开始”成功后，应用才会加载资料、安装 GSI 配置并启动已启用服务。

命令行启动：

```powershell
dotnet restore native\AegisCoach.Native.sln -p:Platform=x64
dotnet run --project native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net10.0-windows10.0.19041.0 --runtime win-x64
```

`AegisCoach.Native/Core` 是应用项目内的逻辑源码目录，不是独立项目，也不生成 `AegisCoach.Core.dll`。其中放置 GSI、规则和赛后分析逻辑，目的是让应用 UI 与可重复测试的逻辑保持分开。

如果 Visual Studio 在本次结构更新前已经打开，并仍显示 `.artifacts`、重复 Assembly 特性或缺少 Core DLL：保存其他工作，关闭 Visual Studio，再重新打开 `native/AegisCoach.Native.sln`。旧错误来自 VS 缓存的项目图；新解决方案不会引用 `.artifacts` 或 `AegisCoach.Core.dll`。

## 仅构建

```powershell
dotnet restore native\AegisCoach.Native.sln -p:Platform=x64
dotnet build native\AegisCoach.Native.sln --configuration Debug --no-restore -p:Platform=x64
```

生成文件位于：

`native/AegisCoach.Native/bin/x64/Debug/net10.0-windows10.0.19041.0/`

## 打包发布版（可直接运行的 exe）

可直接运行一键打包脚本（按需求默认仅打包免安装独立 exe 目录，不生成 zip 压缩包以提升打包效率）：

```powershell
pwsh -NoProfile -File .\tools\package-release.ps1
```

也可以手动执行 publish 命令：

```powershell
dotnet publish native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PublishSingleFile=false
```

发布产物（含 Windows App Runtime 与 NPU/OpenVINO 运行时，无需安装额外依赖）位于：

- `release/publish/神盾教练.exe` —— 双击即可直接运行

> 说明：`AegisCoach.Native.csproj` 内置了 publish 后复制 WinUI `.xbf`/`.pri` 资源的 target，发布版 exe 依赖这些 XAML 编译资源。若需要生成 zip 分发包，可执行 `pwsh -File .\tools\package-release.ps1 -Zip`。

## 测试

无外部依赖的端到端冒烟测试（模拟 Dota 2 向本地接收器 POST 快照，验证解析、token 校验、去重、断线检测、规则触发、cfg 安装、快照落盘与 token 脱敏）：

```powershell
dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj
```

离线回放本地 GSI 记录（不启动 Dota 2、不访问网络、不调用 AI）：

```powershell
dotnet run --project tools\GsiReplay\AegisCoach.Replay.csproj -- "$([Environment]::GetFolderPath('MyDocuments'))\AegisCoach\gsi"
```

## 文档索引

- **项目完整技术规范与设计**：[docs/PROJECT_DOCUMENTATION.md](docs/PROJECT_DOCUMENTATION.md)
- **架构评审与渐进演进路线**：[docs/ARCHITECTURE_REVIEW.md](docs/ARCHITECTURE_REVIEW.md)
- **性能优化实施规范**：[docs/AI_PERFORMANCE_OPTIMIZATION_PLAN.md](docs/AI_PERFORMANCE_OPTIMIZATION_PLAN.md)
- **AI 运行时与推理设计**：[docs/AI_RUNTIME_DESIGN.md](docs/AI_RUNTIME_DESIGN.md)
- **AI Agent Prompt 模板与输入契约**：[docs/AI_AGENT_PROMPT_TEMPLATES.md](docs/AI_AGENT_PROMPT_TEMPLATES.md)
- **Steam Dota 2 GSI 接入手册**：[docs/STEAM_DOTA2_GSI_GUIDE.md](docs/STEAM_DOTA2_GSI_GUIDE.md)
- **画面捕获与视觉分析可行性调研**：[docs/SCREEN_CAPTURE_AI_FEASIBILITY.md](docs/SCREEN_CAPTURE_AI_FEASIBILITY.md)
- **研发 Agent 仓库指令规范**：[AGENTS.md](AGENTS.md)
- **开发与验证历史日志**：[docs/OPERATION_LOG.md](docs/OPERATION_LOG.md)

## 代码入口

- 解决方案：`native/AegisCoach.Native.sln`
- WinUI 主项目：`native/AegisCoach.Native/AegisCoach.Native.csproj`
- 视图层（MVVM）：`Views/HeroesPage.xaml`、`Views/ItemCatalogPage.xaml`、`Views/CoachPage.xaml`、`Views/AnalysisPage.xaml`
- 视图模型（ViewModels）：`ViewModels/HeroesViewModel.cs`、`ViewModels/ItemCatalogViewModel.cs`、`ViewModels/CoachViewModel.cs`、`ViewModels/AnalysisViewModel.cs`
- GSI 核心源码：`native/AegisCoach.Native/Core/`（Native 项目内部纯逻辑目录）
- 冒烟测试：`tools/GsiSmoke/Program.cs`
- NDJSON 回放：`tools/GsiReplay/Program.cs`
- 模型与统计口径：`native/AegisCoach.Native/Models/DotaModels.cs`
