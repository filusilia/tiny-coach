# 神盾教练研发 Agent 开工铁律 (AGENTS.md)

本文档为仓库最高开发红线，所有代码生成、重构与排障必须 100% 遵循。

---

## 一、 极简编码阶梯与工程流（Ponytail + DeepSeek Notes 融合准则）

1. **动笔前范围界定（Before You Write）**：
  - 先跑 `git status --short` 保护工作区；阅读任务触及的所有代码，列出改动波及的全部调用方（Callers）、测试、配置与导出项。
  - 排查破坏性风险（数据丢失、敏感信息暴露、上下游断链），**这就是全部改动范围，严禁顺手开发任何未被要求的额外功能**。
  - **修 Bug 铁律**：动手修改前必须先 `grep` 目标函数的所有调用方，在共享底层一次性修复根因，严禁在调用处打散装补丁。
2. **最小完整改动阶梯（The Smallest Complete Change）**：
  - 按以下优先级选择**第一个能完整跑通**的方案，对解法保持“克制”，对收尾保持“严谨”：
    1. **真有必要存在吗？** 砍掉没人要求的配置项、扩展性、抽象包装与“留着以后用”的代码，并在回复末尾用一行字列出你跳过的非必要设计；模糊需求只做能完成核心任务的最小闭环。
    2. **仓库已有现成方案？** 优先复用项目现有的 Helper、组件、Pinia Store 与服务模式，保持原有分层与数据结构。
    3. **标准库或平台原生支持？** 优先用原生能力（除非项目已有专属封装，项目组件优先于原生控件）。
    4. **已安装的依赖能解决？** 直接用现有依赖，**严禁为了几行代码新增第三方 Package**。
  - **最短有效 Diff 胜出**：删除优于新增；移动或合并代码时必须完整保留原有的错误处理与边界校验；同等代码量下优先选择边缘情况（Edge Cases）更稳健的写法。
  - **注释与捷径标记**：只用一行注释写代码无法自解释的 **Why**；带有已知局限的临时方案必须按格式标注：`// shortcut: <限制说明>, <何时升级>`。
  - **绝不妥协的底线**：信任边界校验、防数据丢失错误处理、安全性、硬件校准及用户明确要求的功能**一概不准精简**；新增非平凡逻辑（分支、循环、解析器、安全相关）必须附带一个最小单测或断言自检。
3. **决策笔记闭环（Write-Notes-Like-DeepSeek）**：
  - **触发条件**：涉及架构、行为、跨文件契约、落盘/网络/配置或测试策略的非平凡改动，必须按 [.agents/skills/write-notes-like-deepseek/SKILL.md](.agents/skills/write-notes-like-deepseek/SKILL.md) 更新笔记；纯样式/格式化小改直接提交。
  - **状态流转**：先检索 `.agents/notes/` 同主题旧篇就地更新；新方案先入 `proposed/`（或对应分类），落地同批转入生效目录；新方案彻底取代旧决策时，同批将旧篇移入 `archive/` 并标明替代关系。
  - **弃用强论证**：记录被否决方案时，必须**先写支持它的最强理由**，再论证为何放弃。
4. **文档同步与交付门禁（提交前必跑）**：
  - 改动若影响架构拓扑、服务划分、组件职责或契约参数，必须同批更新 `docs/`、`README.md` 与 `tools/assets/`。
  - 提交前必须依次跑通：`pnpm run verify-notes`（红了先修） $\rightarrow$ `pnpm --dir frontend build` $\rightarrow$ 一键打包验证。

---

## 二、 核心业务与 UI 防退化红线

1. **合规与零伪造铁律（CRITICAL）**：
  - **非外挂底线**：严禁读内存、注入 DLL、Hook 渲染或模拟键鼠；实时数据仅限接收本机 GSI HTTP POST（4444 端口）。
  - **零伪造数据**：未建立真实对局连接时，严禁伪造任何虚拟英雄（如预设敌法师）或假快照，必须严格渲染“等待 GSI 4444 端口接入”空态守卫。模拟器必须隔离在独立调试模块，严禁污染生产默认上下文。
2. **性能、缓存与安全**：
  - **高频消费**：10Hz GSI 快照由后端/前端规则引擎顺序消费并通过事件驱动合并状态，严禁阻塞 UI 渲染主线程。
  - **外部请求**：统一走单例与 Single-Flight 并发合并 + L1/L2 缓存。
  - **数据脱敏**：Token 与完整 Steam ID 严禁进入日志或 Prompt，敏感凭据仅限本地安全存储。
3. **UI 零废话与核心防退化**：
  - **DOM 极简铁律**：所有页面、卡片、Tab 头部**只准渲染唯一主标题（Title）**，下方直跟交互控件。**严禁**脑补任何副标题（Subtitle）、模块简介（Description）或营销修饰语（如：只准写`战术偏好与选手`，严禁附加`个人账号指定·9类战术语音...`）；代码中严禁出现解释用途的次级灰色文本节点（如 `text-gray-400/500`）或向组件传入 `description/subtitle` 属性。
  - **业务防退化**：127 英雄中文名 100% 静态常数兜底，严守 1 级基础属性公式，维护官网 56x42 物品格与深色高对比悬浮卡（详见 [docs/HERO_AND_ITEM_DATA_SPEC.md](docs/HERO_AND_ITEM_DATA_SPEC.md)）。

---

## 三、 Wails v3 架构与前后端交互准则

1. **前后端单向分层职责**：
  - **Go 后端**：负责单消费者 GSI 监听、系统服务与原生桥接。
  - **API 层 (`*.ts`)**：仅负责调用后端并返回数据，**严禁**在内部做状态持久化或串联调用其他业务方法。
  - **Store 层 (Pinia)**：负责状态管理与持久化（`settings` / `persistSettings`）。
  - **UI 层 (Vue 3)**：仅调用 Store 与触发 Toast，主窗口与置顶悬浮窗严格隔离。
2. **IPC 绑定唯一通道（Single Source of Truth）**：
  - **前端 $\rightarrow$ Go**：必须且只能通过 `wails3 generate bindings -names -ts` 生成的 `frontend/bindings/` 模块直接 `import` 调用。**严禁**手写 `invokeWails` 猜测方法名、严禁硬编码 FQN 字符串、严禁调用 `window.go` 或 `window.wails.Call`。报“未找到后端方法”时，第一步必须核对生成 `.ts` 文件中的 `$Call.ByName`。
  - **Go $\rightarrow$ 前端**：统一走 `app.Event.Emit` + 前端 `Events.On`（Wails v3 不支持前端向后端 `emit`）。组件卸载（`onUnmounted`）时**必须注销**所有 `Events.On` 与定时器，严防内存/句柄泄漏。
3. **Go 服务注册与 `main.App` 显式门面**：
  - 服务注册默认用 `application.NewService()`，无明确外部 REST 需求**严禁**使用 `NewServiceWithOptions(..., Route: "/xxx")`（会篡改绑定命名空间）。
  - `main.App` 不会自动继承业务 Service 的绑定方法；若需从 `main.App.XXX` 调用，必须在 `main.App` 上**显式编写转发方法**并重新生成绑定。
4. **原生 API、取消语义与降级规范**：
  - **原生对话框**：选目录用 `app.Dialog.OpenFile().CanChooseDirectories(true).CanChooseFiles(false).PromptForSingleSelection()`；选文件追加 `AddFilter`；打开目录用 `app.OpenFileManager(path, false)`。**严禁**手写 `exec.Command("explorer.exe")` 或 PowerShell 兜底脚本（极端受限场景须先写 Agent Note 论证）。
  - **取消与降级语义**：用户取消对话框时 Go 端必须返回 `("", nil)` 而非 `error`，前端按空字符串静默处理，严禁抛错。纯浏览器预览环境（非 Wails 容器）捕获到 IPC 缺失时，必须 `try/catch` 记录日志并平滑降级，严禁直接 `throw` 阻断流程。
5. **构建 Tags 与调试通道**：
  - 打包脚本 `-tags` 必须与源文件 `//go:build` 注释严格对齐（含 `desktop,production,wails,bindings` 等）。
  - 调试构建**严禁**加 `-H windowsgui`（必须保留控制台输出观测 IPC 与 panic）；正式构建可静默，但 CI 必须同步产出带控制台的 Debug 包。
