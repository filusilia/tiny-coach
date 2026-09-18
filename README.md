# 神盾教练（AegisCoach）

神盾教练是一个面向 Windows PC 的现代化、极轻量 Dota 2 实时资料与战局教练工具。
架构基于 **Tauri v2 + TypeScript + Vite + React + Rust** 构建，便携安装包缩减至 **~10MB 级别**，常驻内存仅 **~30MB**。

- 127 英雄与全物品数据契约规范见：[英雄与物品数据规范 (docs/HERO_AND_ITEM_DATA_SPEC.md)](docs/HERO_AND_ITEM_DATA_SPEC.md)。
- 官方英雄技能与状态机文本规范见：[官方英雄数据规范 (docs/OFFICIAL_HERO_DATA.md)](docs/OFFICIAL_HERO_DATA.md)。
- 架构决策、性能攻坚与隐蔽避坑细节见：[工程决策看板 (.agents/notes/)](.agents/notes/README.md)。
- 代码 Agent 必读红线原则与关键链接见：[AGENTS.md](AGENTS.md)。

---

## 核心特性

- **100% 官方离线数据与 127 英雄常数兜底**：包含官方技能、天赋、魔晶、神杖升级、1 级基础属性公式以及 56x42 物品图鉴 DAG。
- **现代化电竞暗色 HUD 界面**：琥珀金点缀，高对比度防眩光，原生支持 Windows 明暗模式与系统主题色跟随。
- **物理隔离标题栏与 IME 输入保护**：窗口拖拽区域物理隔离，支持中文拼音防抖搜索，全键盘与鼠标流畅交互。
- **10Hz GSI 高频单消费者流水线**：基于 Rust Axum 本地轻量监听（端口 4444），非外挂合规原则，无需注入游戏、不修改内存。
- **确定性战术规则 + 智能语音播报**：内置 21 条战局裁定规则，提供 **三档语音播报滑块**（0: 关闭 / 1: 仅文字播报 / 2: 语音+播报），内置电竞语音词典。
- **独立置顶经济与战局悬浮窗**：多窗口架构，可独立浮于游戏上方实时呈现团队净胜与关键经济差。

---

## 项目结构

```text
├── public/                 # 静态数据与游戏图标 (127 英雄、全物品 JSON、官方高清图标)
├── src/                    # 前端电竞 HUD (React + TypeScript + Vite + TailwindCSS)
│   ├── components/         # 物理隔离 TitleBar、Sidebar、HeroCard 等
│   ├── data/               # 127 英雄中文常数映射表、拼音别名表
│   ├── pages/              # 实时教练、英雄资料、物品图谱、赛后复盘、系统设置
│   ├── store/              # Zustand 响应式全局状态
│   ├── utils/              # 1 级基础属性计算器、技能分类与占位符填充
│   └── windows/            # 独立置顶透明经济悬浮窗 (OverlayWindow)
├── src-tauri/              # Rust 后端与 Tauri v2 宿主
│   ├── src/gsi.rs          # 10Hz GSI 单消费者 Axum 异步服务 (端口 4444)
│   ├── src/rules.rs        # 21 条确定性战术告警规则
│   └── src/models.rs       # 战局状态机与数据模型
├── tests/fixtures/         # 真实比赛 GSI 快照数据集 (Huskar, Juggernaut 等)
├── tools/                  # 自动化辅助工具 (换行符检测、数据同步、打包发布)
└── docs/                   # 完整架构规范与技术文档
```

---

## 开发与调试

### 1. 安装依赖

本项目采用 Node.js + pnpm 与 Rust 2021 工具链：

```powershell
pnpm install
```

### 2. 启动前端热重载开发调试

```powershell
pnpm dev
```
开发服务器将运行在 `http://localhost:1420`。

### 3. 运行桌面端开发

```powershell
cargo tauri dev
```

---

## 构建与打包

运行全自动化打包脚本，支持自动归档为便携 ZIP 发布包：

```powershell
# 仅生成便携发布目录 (release/publish)
pwsh -NoProfile -File .\tools\package-release.ps1

# 生成便携发布目录并压缩为 release/AegisCoach-发布版.zip
pwsh -NoProfile -File .\tools\package-release.ps1 -Zip
```

---

## 换行符与代码规范

本项目全仓严格执行 Windows CRLF (`\r\n`) 规范与中文优先原则。在每次提交或推送前，可运行：

```powershell
pwsh -NoProfile -File .\tools\Normalize-LineEndings.ps1
```
