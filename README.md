# 神盾教练（AegisCoach）

神盾教练是一个面向 Windows PC 的现代化、极轻量 Dota 2 实时资料与战局教练工具。
架构基于 **Wails v3 + Go + Vite + TypeScript + Vue 3 + UnoCSS** 构建。

- 127 英雄与全物品数据契约规范见：[英雄与物品数据规范 (docs/HERO_AND_ITEM_DATA_SPEC.md)](docs/HERO_AND_ITEM_DATA_SPEC.md)。
- 官方英雄技能与状态机文本规范见：[官方英雄数据规范 (docs/OFFICIAL_HERO_DATA.md)](docs/OFFICIAL_HERO_DATA.md)。
- 核心业务计算与 UI 呈现规范见：[核心业务规则与 UI 交互规范 (docs/BUSINESS_RULES_AND_UI_SPEC.md)](docs/BUSINESS_RULES_AND_UI_SPEC.md)。
- 子系统运行时与降级机制见：[子系统运行语义与降级机制 (docs/SUBSYSTEM_RUNTIMES.md)](docs/SUBSYSTEM_RUNTIMES.md)。
- 架构拓扑与全景图谱见：[系统全景图谱与工程分层契约 (docs/SYSTEM_LANDSCAPE.md)](docs/SYSTEM_LANDSCAPE.md)。
- 架构决策、性能攻坚与隐蔽避坑细节见：[工程决策看板 (.agents/notes/)](.agents/notes/README.md)。
- 代码 Agent 必读红线原则与关键链接见：[AGENTS.md](AGENTS.md)。

---

## 核心特性

- **100% 官方离线数据与 127 英雄常数兜底**：官方 7.41f 现行版本技能、天赋、魔晶、神杖升级、1 级基础属性精确公式以及 56x42 物品图鉴 DAG。开机自动异步检测版本增量；严格践行零伪造原则，未连入网络时实事求是置空，具备防企业防火墙 302 劫持防御。
- **现代化电竞暗色 HUD 界面与实战纯化视口**：实时教练页纯实战设计（血蓝条动画、KDA、GPM/XPM、买活判定、技能 CD 倒计时、56x42 物品格与肉山/魔方时钟），零设置干扰；所有系统、AI 与语音配置统一收纳至设置中枢。
- **物理隔离原生沉浸式窗口与输入保护**：主窗口采用 Windows 原生暗色标题栏，拖拽区域物理隔离，支持中文拼音防抖搜索，全键盘与鼠标流畅交互。
- **10Hz GSI 高频单消费者流水线**：基于 Go 原生 HTTP 本地轻量监听（端口 4444，支持持久化自定义配置），非外挂合规原则，无需注入游戏、不读取内存。
- **本地 Ollama 与云端 OpenAI 兼容双引擎 AI 架构**：
  - 支持本地 Ollama 端侧模型（如 `qwen3.5:1.5b`，≤ 2GB 内存开销，延迟 <120ms，支持 Intel NPU / Ryzen AI / RTX）；
  - 支持云端商用模型（DeepSeek-V3/R1 等）端点连通性探测、健康延迟检测与一键自动拉取模型列表；
  - 提供赛后 AI 深度复盘系统（对线期得失、团队关键节奏、出装质变节点、败因归因反思、五维雷达图与经验/净值优势走势曲线）；
  - 支持纯确定性规则引擎降级模式（0 延迟、0 网络消耗、纯离线秒级裁定）。
- **双通道混合离线语音引擎（预置 Fast-Path + 本地 MeloTTS）**：
  - **高频预置离线语音免生成**：项目自有高保真音色，涵盖资源刷新（赏金/力量神符、痛苦魔方、肉山）、关键倒计时、系统战场事件与等级质变，极简客观事实播报；采用 96kbps 高度压缩（外置于 `audio/preset/`，35 条音频总计仅 **0.70MB**），0ms 零延迟、0% CPU 占用秒开播报；
  - **动态语义神经网络合成**：未命中预置切片的个性化战术出装、英雄克制与深度战局建议，无缝回退调用本地纯离线 MeloTTS 神经网络引擎实时生成；
  - 内置电竞术语发音校正词表，支持三段式提醒开关控制（关闭 / 仅文字 / 语音+文字）。
- **OpenDota 玩家战绩画像与全景大盘接入**：通过统一网关获取玩家常用英雄与胜率特征，常用英雄池深度融入赛后资料大盘；下半区全宽呈现天梯历史卡片与 AI 深度复盘。
- **局内独立置顶经济与战局悬浮窗**：多窗口与常驻热备支持，Windows 原生独立顶级窗口（无父属主不受主窗最小化影响），DirectComposition 高清透明渲染，支持快捷切换（Alt+F2）与 `WS_EX_TRANSPARENT` 鼠标物理点击穿透（Alt+F3）。
- **原生 Win32 GDI 极速截屏与端侧 ONNX 对战识别**：突破 Dota 2 选人阶段 GSI 盲区，基于 Win32 GDI 18% ROI 截图与 OS 线程级 F10 热键锁定（带 600ms 防抖）；前端借助 ONNX Runtime WebAssembly 运行时（跨域隔离 COOP/COEP 加固）执行轻量端侧 YOLOv8n 推理，自适应识别双方 10 英雄槽位，闭环驱动 OpenDota 大数据针对性出装建议并跨窗口实时同步至置顶悬浮窗；实时教练页纯化为 AI 主动战术决策流，杜绝实战打字干扰。

---

## 项目结构

```text
├── audio/                  # 外置高频预置离线语音库 (audio/preset/，5大类轻量压缩 MP3)
├── build/                  # Wails v3 宿主配置、Windows 高清图标与清单
├── cmd/                    # 应用程序主二进制入口
│   └── aegis-coach/        # main.go 入口、Wails 应用构建、统一 IPC 门面与托盘
├── internal/               # Go 后端标准领域分层与系统服务
│   ├── app/                # App 门面实体、Wails 生命周期与服务总控
│   ├── services/           # 核心领域服务 (System, Ai, Gsi, Overlay, Player, Tts, Vision)
│   ├── platform/windows/   # Win32 底层系统集成 (置顶保活、分层穿透、GDI 截图、F10 热键)
│   ├── models/             # 前后端 1:1 契约强类型 DTO 实体
│   ├── storage/            # 缓存目录路径解析与持久化存储管理
│   └── dota/               # GSI 监听接收器与游戏状态反序列化
├── frontend/               # 前端电竞 HUD (Vue 3 + TypeScript + Vite + UnoCSS)
│   ├── public/             # 静态数据与游戏图标 (127 英雄、全物品 JSON、官方高清图标、ONNX 模型)
│   ├── bindings/           # Wails v3 自动生成强类型 IPC 绑定
│   ├── src/
│   │   ├── api/            # 强类型 Wails v3 IPC 客户端门面 (system, ai, overlay, player, tts, vision)
│   │   ├── components/     # TitleBar、Sidebar、ThreeSegmentToggle、ItemTooltipCard 等
│   │   ├── composables/    # useGsiStream、useNativeNavigation 响应式钩子
│   │   ├── data/           # 127 英雄常数映射表、7.41f 物品图谱、出装指南
│   │   ├── stores/         # Pinia 全局响应式状态中枢 (app, battle, player, vision)
│   │   ├── views/          # CoachView、HeroesView、ItemsView、AnalysisView、SettingsView、OverlayView
│   │   │   ├── coach/      # 战备 HUD、赛前 BP 视觉识别卡片、大数据推荐面板
│   │   │   ├── analysis/   # 赛后复盘雷达图、优势曲线、AI复盘面板等
│   │   │   ├── heroes/     # 技能展示台、英雄出装面板、基础属性卡片等
│   │   │   └── settings/   # AI引擎、存储目录、语音播报等分区设置
│   │   ├── services/       # GSI 规则引擎、AI 教练服务、语音播报服务、赛前 BP 端侧视觉识别 (services/vision/)
│   │   └── utils/          # 1 级基础属性计算器、技能宏解析、HTML 清洗、资产防破损
├── lib/                    # 原生运行时动态库 (onnxruntime.dll)
├── melo_weights/           # 本地离线 MeloTTS 模型权重与词表
├── tests/fixtures/         # 真实比赛 GSI 快照数据集 (Huskar, Juggernaut 等)
├── tools/                  # 自动化发布脚本 (package-release.ps1) 与便携附件
├── docs/                   # 完整架构规范与技术文档
├── Taskfile.yml            # Wails v3 构建任务定义
└── package.json            # 根工程脚本调度与依赖管理
```

---

## 开发与调试

### 1. 安装依赖

本项目采用 Node.js 24+ 与 pnpm，以及 Go 1.27+ 工具链：

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

本项目全仓严格执行 UTF-8 (无 BOM)、Unix LF (`\n`) 规范与中文优先原则。**每次开发更新代码若影响技术规范、架构拓扑或数据契约，必须同步更新对应技术文档**。在提交代码前，必须执行全量验证：
【全局 UI 生成铁律】
1. 零废话原则：所有页面、卡片、Tab 和模块头部**只允许渲染主标题（Title）**，下方直接跟核心交互组件（表单/表格/按钮/图表）。
2. 严禁副标题与说明：绝对禁止脑补任何副标题（Subtitle）、模块简介（Description）、修饰性长句或营销文案（例如：只允许写“战术偏好与选手”，严禁在下面附加“个人账号指定·9类战术语音播报矩阵...”这类解释性小字）。
3. 严禁生成对应的 DOM 节点：代码中不得包含用于解释模块用途的次级灰色小字（如 `text-gray-400/500` 的说明段落）或组件的 `description/subtitle` 属性。

```powershell
# 1. 前端构建与 TypeScript 类型检查 (0 错误)
pnpm build

# 2. Go 后端语法与静态检查
go vet ./...

# 3. Agent 决策笔记校验
npm run verify-notes
```
