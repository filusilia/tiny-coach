# 神盾教练（PC 端）

神盾教练是一个面向 Windows PC 的 Dota 2 资料与教练工具，当前使用 WinUI 3 原生桌面技术实现。项目不再维护 Vue、Vite 或浏览器版本，唯一产品入口是 `native/AegisCoach.Native`。

完整的功能、数据口径、完成度和后续计划见：[项目完整文档](docs/PROJECT_DOCUMENTATION.md)。
AI Agent 的统一输入契约、赛后复盘、实时解释、资料问答、数据质量和研发模板见：[AI Agent Prompt 模板](docs/AI_AGENT_PROMPT_TEMPLATES.md)。
实时教练的模型边界、云端/离线部署和回退策略见：[AI 运行设计](docs/AI_RUNTIME_DESIGN.md)。

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
- AI 智能解释可选三种后端：NPU 本地推理（默认，OpenVINO GenAI 跑在 Intel AI Boost，不占 GPU、不占用 CPU 主核）、云端 OpenAI 兼容 API（本地零性能开销）、规则原文（零消耗）。任何后端不可用或超时时自动回退规则原文，不影响 GSI 接收；不再依赖 Ollama。
- 资料数据按目标版本 v7.41e 隔离缓存；官方接口没有可靠 patch 参数，线上版本仍需在可联网环境复核。
- GSI 快照记录：有效快照按日期写入本地 NDJSON，落盘前自动脱敏 token；实时教练页面可直接打开记录目录。
- GSI 配置文件生成与安装：自动探测 Steam 库路径，或手动选择目录/导出/复制 `gamestate_integration_aegiscoach.cfg`。
- Steam Dota 2 GSI 接入步骤见：[Steam Dota 2 GSI 接入手册](docs/STEAM_DOTA2_GSI_GUIDE.md)。

## 构建

```powershell
dotnet build native\AegisCoach.Native\AegisCoach.Native.csproj --configuration Debug --framework net8.0-windows10.0.19041.0 --runtime win-x64 --no-restore
```

生成文件位于：

`native/AegisCoach.Native/bin/Debug/net8.0-windows10.0.19041.0/win-x64/`

## 测试

无外部依赖的端到端冒烟测试（模拟 Dota 2 向本地接收器 POST 快照，验证解析、token 校验、去重、断线检测、规则触发、cfg 安装、快照落盘与 token 脱敏）：

```powershell
dotnet run --project tools\GsiSmoke\AegisCoach.Smoke.csproj
```

离线回放本地 GSI 记录（不启动 Dota 2、不访问网络、不调用 AI）：

```powershell
dotnet run --project tools\GsiReplay\AegisCoach.Replay.csproj -- "%LOCALAPPDATA%\AegisCoach\gsi"
```

## 入口

- AI Agent 模板与安全边界：docs/AI_AGENT_PROMPT_TEMPLATES.md

- 解决方案：`native/AegisCoach.Native.sln`
- WinUI 应用：`native/AegisCoach.Native/AegisCoach.Native.csproj`（主窗口 `MainWindow.xaml`，数据服务 `Services/DotaDataService.cs`）
- GSI 核心库：`native/AegisCoach.Core/`（`Models/GsiModels.cs`、`Services/GsiReceiver.cs`、`Services/CoachRuleEngine.cs`、`Services/GsiConfigInstaller.cs`）
- 冒烟测试：`tools/GsiSmoke/Program.cs`
- NDJSON 回放：`tools/GsiReplay/Program.cs`
- 模型与统计口径：`native/AegisCoach.Native/Models/DotaModels.cs`
