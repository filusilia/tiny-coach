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
