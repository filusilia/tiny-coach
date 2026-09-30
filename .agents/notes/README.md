# 神盾教练工程决策看板与避坑智库 (.agents/notes/)

本目录是神盾教练技术演进中的**工程决策记录（Agent Notes）与避坑智库**，专门沉淀“代码装不下的为什么、技术裁定事实与致命踩坑细节”。

---

## 一、目录结构与领域矩阵

```text
.agents/notes/
├── architecture/     # 现行系统承重墙与核心架构基线决策 (Wails v3 + Go + Vue 3)
├── bug-fix/          # 现行架构下的重大排障与核心机制攻坚（已修复临时缺陷及时清理）
├── research/         # 外部技术选型、可行性调研与合规边界探索
└── archive/          # 历史重大版本演进归档
```

---

## 二、现行活跃决策索引 (Active Baseline)

| 类别 | 决策主题与链接 | 状态 | 涉及模块 | 决策核心要点 |
|---|---|:---:|---|---|
| **架构** | [Wails v3 (Go) 与 Vue 3 极轻电竞战术助手系统架构基线](architecture/2026-09-20-wails-vue3-system-architecture.md) | `implemented` | 核心架构 / 后端 / 前端 | 采用 Wails v3 + Go 后端配合 Vue 3 + TypeScript + Vite 响应式 HUD；零额外 C++ 运行时依赖；内置单消费者 4444 端口 GSI 接收管线与极速反序列化；进程与内存极致轻量，零争抢 Dota 2 游戏帧率 |
| **架构** | [游戏状态集成 (GSI) 零模拟数据守卫与非外挂合规基线](architecture/2026-09-20-gsi-zero-mock-data-compliance-baseline.md) | `implemented` | 实时教练 / GSI / 合规铁律 | 严格遵守 Valve GSI 非外挂红线（严禁内存读取、DLL 注入、Hook 渲染与模拟输入）；未连接真实对局或未选定英雄时 100% 呈现 4444 端口等待接入空态；确定性规则优先（0ms），AI 仅作旁路解释 |
| **架构** | [独立桌面置顶经济悬浮窗与子进程生命周期治理](architecture/2026-09-20-standalone-desktop-overlay-window.md) | `implemented` | 悬浮窗 / 多窗口 / 进程管理 | 基于命令行参数 `-overlay` 启动原生无边框独立置顶窗口；半透明高对比电竞 HUD 呈现经济与资源刷新倒计时；主程序退出自动安全终止子进程，杜绝孤儿进程与句柄泄漏 |
| **架构** | [语音播放栏三段式开关改造与 9 类战术提醒独立判定](architecture/2026-09-20-voice-reminder-categories-three-segment-switch.md) | `implemented` | 语音播报 / 提醒配置 / 三段式开关 / GSI 规则 | 9 类战术提醒独立三段式（关闭 / 仅文字 / 语音+文字）细粒度开关；HUD 快捷控制栏与设置页实时响应联动；战术听觉大幅减负，保障关键团战信息零噪音 |
| **架构** | [127 英雄静态权威常数与官网物品分层网格规范](architecture/2026-09-20-hero-and-item-authoritative-data-baseline.md) | `implemented` | 英雄详情 / 物品图鉴 / 属性公式 / 宏解析 | 127 英雄中文名与 1 级基础属性公式 100% 静态常数兜底；多级宏解析引擎彻底消除裸露占位符与废弃升级；官网 10 大分类分层网格与 56x42 物品方块；悬浮卡片视口边缘自适应翻转防溢出 |
| **架构** | [Lucide 图标库现代化迁移与全仓文档一致性清理优化](architecture/2026-09-20-lucide-vue-migration-and-doc-cleanup.md) | `implemented` | 依赖治理 / 图标组件 / 全仓文档 / 缓存契约 | 移除已废弃的 `lucide-vue-next` 并迁移全量 11 个组件至最新 `@lucide/vue`；清理根目录多余依赖；全仓文档清理已删除历史脚本并精准对齐 Go 缓存拓扑、资产路径与 Wails 事件总线 |
| **架构/规范** | [前端 IPC 绑定单一事实来源、GSI 事件单一消费与死代码清理](architecture/2026-09-30-frontend-ipc-single-source-gsi-single-consumer-and-dead-code-cleanup.md) | `implemented` | IPC 绑定 / GSI / 架构守则 / 死代码清理 | 删除手写胶水 `client.ts` 与猜方法名机制，前端统一直连 `wails3 generate bindings` 强类型绑定；收拢 GSI 快照为 `useGsiStream` 单一消费者与 rAF 帧合并；拔除 1000 行死代码与违规模拟器 |
| **缺陷/架构** | [OpenDota 统一出口收拢与 DTO 契约修复](bug-fix/2026-09-30-opendota-single-gateway-and-dto-contract-fix.md) | `implemented` | OpenDota / 数据出口 / DTO 契约 / 异常穿透 | 彻底收归战绩与比赛拉取为 Go 统一网关，消除前端直连跨域风险；规范 JSON 反序列化蛇形命名契约，杜绝空指针与静默数据丢失 |
| **缺陷/架构** | [远程 AI 协议修复、全栈统一出口收拢与 SingleFlight 并发加固](bug-fix/2026-10-08-remote-ai-protocol-singleflight-and-frontend-deadcode-cleanup.md) | `implemented` | 大模型 / OpenDota / 并发治理 / MeloTTS | 修复云端大模型硬编码 404 协议；收拢前端所有裸 fetch 为 Go 统一出口并平滑离线降级；内置轻量 SingleFlight 合并战绩并发请求防 429 与磁盘句柄竞争；解耦 MeloTTS 推理锁与状态查询锁 |
| **架构/功能** | [Ponytail 极简瘦身审计与 OpenDota 对抗大数据出装推荐架构设计](architecture/2026-10-09-ponytail-lean-audit-and-opendota-matchup-item-recommendation-design.md) | `implemented` | 极简审计 / OpenDota / 对抗出装 / 战术大盘 | 拔除 726 行零引用死代码并修复 Go 标准库导入；设计四层协同大数据推荐流水线（热度+对抗加权+Timing曲线+本地7天L2落盘）；实时教练页重构为三层战术指挥中枢 |
| **缺陷/系统** | [Windows SmartScreen 拦截、僵尸进程竞争与客户端启动安全加固](bug-fix/2026-10-10-windows-smartscreen-and-launch-hardening.md) | `implemented` | Windows 系统 / SmartScreen / MIC 完整性 / 启动容灾 | 注入 Win32 原生模态错误弹窗告别无声闪退；构建流水线自动清除 Web 区域锁定并应用 Authenticode 签名；显式提权 MIC 级别至 Medium 彻底解决 Win32k Lockdown 拒绝 DLL 初始化故障；预置一键解锁批处理 |
| **缺陷/体验** | [局内悬浮窗穿透与尺寸收敛、技能展台流畅度与全英雄出装汉化体验加固](bug-fix/2026-10-10-overlay-passthrough-and-hero-item-showcase-fixes.md) | `implemented` | 悬浮窗穿透 / 技能展台 / 英雄出装 / 物品锚定 / 机制汉化 | 递归 Win32 子窗口实现真正鼠标穿透；悬浮窗高度动态收敛消除透明黑洞；技能大图指定 key+eager 消除切换滞后；移除伤害属性白边；接入 OpenDota 真实各阶段热门出装；物品卡片改用 DOM 边界盒智能锚定；全量装备机制提示 100% 本地化汉化 |
| **架构** | [坚决拒绝离线假数据兜底与真实纯净数据流保障](architecture/2026-10-10-zero-offline-fallback-and-pure-real-data-enforcement.md) | `implemented` | 零伪造原则 / OpenDota / 推荐出装 / 状态机治理 | 彻底移除后端伪造离线出装代码与前端手写出装；OpenDota 拉取失败直接暴露错误或旧真实缓存；界面严格基于加载中/真实数据/真实空态三态流转，保障数据 100% 真实纯净 |
| **架构/体验** | [坚决拔除 AI 复盘规则降级伪造与英雄战绩池统一合并重构](architecture/2026-10-10-zero-fake-ai-review-and-hero-pool-consolidation.md) | `implemented` | AI 复盘 / 零伪造 / 高频英雄 / 近期使用 / Segmented Control | 拔除全部假复盘规则模板，无 AI 直接置灰不可用；合并高频主力与近期使用至统一面板并支持分段切换与联动筛选；清理档案冗余空白占位 |
| **架构/视觉** | [游戏原生截图与 BP 阶段视觉辅助架构规划](architecture/2026-10-14-game-screen-capture-and-bp-vision-architecture-design.md) | `implemented` | 原生截图 / 视觉识别 / BP 对位 / ONNX Web | 基于 Win32 GDI 顶部 ROI 极速截图配合前端 ONNX Runtime Web 端侧推理；解决 BP 阶段 GSI 选人盲区；保持零 CGO 与纯净轻量架构 |

---

## 三、标准 Agent Note 编写规范

新增决策笔记时，统一使用命名格式：`YYYY-MM-DD-slug.md`，并严格遵守四段式：

```markdown
# Agent Note: 简短明了的决策或缺陷名称

Status: implemented | proposed | rejected | archived
Date: YYYY-MM-DD
Class: architecture | performance | bug-fix | feature | research

## 问题背景与因果机制分析
[描述具体背景、引发缺陷的代码路径、因果机制以及为什么现有方案行不通]

## 决策方案
[描述落地的具体裁定、接口设计、状态流转或架构契约]

## 曾考虑的替代方案
[被放弃的方案必须先写其最强的理由，再解释为什么不用，避免未来重复踩坑]

## 后果与验证计划
[客观陈述该决策带来的正面工程收益，以及必须承担的代价、约束或验证方法]
```

---

## 四、代码反向定位注释（Code Anchors）

在核心模块或高风险代码处，推荐通过行内注释反向关联到对应的决策笔记，例如：
```typescript
// Note: 见 .agents/notes/architecture/2026-09-20-standalone-desktop-overlay-window.md
const isOverlay = window.location.search.includes('overlay');
```
