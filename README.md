# 神盾教练（AegisCoach）

神盾教练是一个面向 Windows PC 的现代化、极轻量 Dota 2 实时资料与战局教练工具。
架构基于 **Wails v3 + Go + Vite + TypeScript + Vue 3** 构建，便携安装包缩减至 **~17MB 级别**，常驻内存仅 **~30MB**。

- 127 英雄与全物品数据契约规范见：[英雄与物品数据规范 (docs/HERO_AND_ITEM_DATA_SPEC.md)](docs/HERO_AND_ITEM_DATA_SPEC.md)。
- 官方英雄技能与状态机文本规范见：[官方英雄数据规范 (docs/OFFICIAL_HERO_DATA.md)](docs/OFFICIAL_HERO_DATA.md)。
- 架构决策、性能攻坚与隐蔽避坑细节见：[工程决策看板 (.agents/notes/)](.agents/notes/README.md)。
- 代码 Agent 必读红线原则与关键链接见：[AGENTS.md](AGENTS.md)。

---

## 核心特性

- **100% 官方离线数据与 127 英雄常数兜底**：包含官方技能、天赋、魔晶、神杖升级、1 级基础属性公式以及 56x42 物品图鉴 DAG。开机自动异步检测版本增量并在新英雄上展现动态 `NEW` 标签。
- **现代化电竞暗色 HUD 界面与实战纯化视口**：实时教练页纯实战设计（血蓝条动画、KDA、GPM/XPM、买活判定、技能CD倒计时、56x42 物品格与肉山/魔方时钟），零设置干扰；所有系统与 AI 配置统一收纳至设置中枢。
- **物理隔离标题栏与 IME 输入保护**：窗口拖拽区域物理隔离，支持中文拼音防抖搜索，全键盘与鼠标流畅交互。
- **10Hz GSI 高频单消费者流水线**：基于 Go 原生 HTTP 本地轻量监听（端口 4444），非外挂合规原则，无需注入游戏、不修改内存。
- **2026 Qwen 3.5/3.7/3.8 最新端侧 NPU 推理 + 全模态 AI 接入**：
  - 默认首选 `qwen3.5:4b` 端侧模型（~2.8GB 内存，延迟 <180ms，支持 Intel NPU / Ryzen AI / 骁龙 / RTX）；
  - 支持纯确定性规则引擎降级模式（0 延迟、0 网络消耗、纯离线运行）；
  - 支持端点连通性测试与 API Response 原始响应体实时回显。
- **OpenDota 玩家近 20 场战绩画像接入**：通过 `api.opendota.com/api/players/{account_id}/matches?limit=20` 获取玩家常用英雄与胜率特征，严格脱敏后动态注入 AI Prompt，提供高度契合选手技术水平与风格的定制化对战指引。
- **确定性战术规则 + 智能语音播报**：内置战略节拍与战术裁定规则，提供三段式提醒开关控制与拟真人声语音。
- **独立置顶经济与战局悬浮窗**：多窗口架构，可独立浮于游戏上方实时呈现团队净胜与关键经济差，支持号角吹响自动拉起。

---

## 项目结构

```text
├── build/                  # Wails v3 宿主配置、Windows 高清图标与清单
├── frontend/               # 前端电竞 HUD (Vue 3 + TypeScript + Vite + TailwindCSS)
│   ├── public/             # 静态数据与游戏图标 (127 英雄、全物品 JSON、官方高清图标)
│   ├── src/
│   │   ├── components/     # TitleBar、Sidebar、VoiceBroadcastBar、GsiSimulator 等
│   │   ├── data/           # 127 英雄中文常数映射表、7.41e 物品图谱、出装指南
│   │   ├── pages/          # 实时教练、英雄资料、物品图谱、赛后复盘、系统设置
│   │   ├── services/       # GSI 规则引擎、语音播报服务、配置持久化
│   │   ├── store/          # Vue 3 响应式全局战局状态
│   │   ├── utils/          # 1 级基础属性计算器、技能分类与宏解析、HTML 清洗
│   │   └── windows/        # 独立置顶透明经济悬浮窗 (OverlayWindow.vue)
│   └── wailsjs/            # Wails 自动生成的 Go 后端 IPC 绑定
├── tests/fixtures/         # 真实比赛 GSI 快照数据集 (Huskar, Juggernaut 等)
├── tools/                  # 自动化发布脚本 (package-release.ps1)
├── docs/                   # 完整架构规范与技术文档
├── app.go                  # Wails 后端核心业务与 GSI HTTP 接收器
├── main.go                 # Wails 桌面入口与多窗口/无边框总控
├── Taskfile.yml            # Wails v3 构建任务定义
└── package.json            # 根工程脚本调度与依赖管理
```

---

## 开发与调试

### 1. 安装依赖

本项目采用 Node.js + pnpm 与 Go 1.27+ 工具链：

```powershell
pnpm install
```

### 2. 启动前端热重载开发调试

```powershell
pnpm dev
```

### 3. 构建与打包

运行全自动化打包脚本，产物直接生成至 `release/` 根目录下：

```powershell
# 直接在 release/ 根目录下生成神盾教练.exe、使用说明与 GSI 配置文件
pwsh -NoProfile -File .\tools\package-release.ps1
```

---

## 代码规范与验证矩阵

本项目全仓严格执行 Windows CRLF (`\r\n`) 规范（由 `.gitattributes` 与 `.editorconfig` 统一约束）与中文优先原则。在提交代码前，必须执行全量验证：

```powershell
# 前端构建验证 (0 错误)
pnpm build

# Agent 决策笔记校验
npm run verify-notes
```
