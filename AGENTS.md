# 神盾教练研发 Agent 开工铁律 (AGENTS.md)

本文档是所有进入本仓库工作的代码 Agent 的**开工必读最高红线**。每一条均为不可逾越的开发原则，详细规范请点击右侧链接查看。

---

## 核心开工铁律与底线原则

1. 非平凡改动（改了行为、架构、跨文件契约、流程与工具链、测试策略、落盘/网络/配置格式）前，遵循 .agents/skills/write-notes-like-deepseek/SKILL.md 写或更新笔记；机械性小改（样式、格式化、打标、不改行为的补丁）直接提交。
2. 写之前先检索 .agents/notes/ 里的同主题旧笔记：有归属就地更新；新想法先放 proposed/，落地随同代码改动转 implemented/；新方案彻底取代旧决策时，同批归档旧篇并标明被谁取代。
3. 被放弃的方案先写它最强的理由，再解释为什么不用。
4. 提交前跑 npm run verify-notes，红了先修再交。
5. **开工检查与因果分析**：开工前必须先执行 `git status --short` 保护既有修改；拆解分析问题必须讲清完整的因果链路机制。
6. **架构拓扑与前后端边界**：采用 **Wails v3 + Go + Vite + TypeScript (Vue 3)** 现代化极轻架构。前端负责电竞 HUD 视图渲染与状态展现，Go 后端负责单消费者 GSI 监听与系统服务/原生桥接，严格保持模块分层。
7. **合规红线与非外挂原则**：严禁读取游戏内存、注入 DLL、Hook 渲染或模拟键鼠输入；实时数据仅允许接收本机 GSI HTTP POST。
8. **严禁伪造模拟数据（零伪造原则）**：实时教练、战况看板与悬浮窗严禁在未建立真实对局连接时伪造任何虚拟英雄（如预设敌法师、假战局快照等）；当游戏未连接或英雄未就绪时，必须严格呈现真实的“等待 GSI 4444 端口接入”空态守卫。开发调试用的模拟器必须处于独立隐蔽的调试模块，绝不允许污染生产环境的默认上下文。
9. **高频 GSI 后台处理与 UI 合并**：10Hz 快照由 Go 后端/前端规则引擎顺序消费，前端通过事件驱动合并最新状态，严禁阻塞 UI 渲染主线程。
10. **数据服务单例与 Single-Flight**：外部请求统一调度，实施并发任务合并与 L1/L2 缓存，杜绝频繁请求封禁。
11. **业务事实与核心 UI 防退化**：127 英雄中文名必须 100% 静态常数兜底，严守 1 级基础属性公式，严格维护官网 56x42 物品格与深色高对比悬浮卡。详见 [docs/HERO_AND_ITEM_DATA_SPEC.md](docs/HERO_AND_ITEM_DATA_SPEC.md)。
12. **多窗口与组件生命周期闭环**：主窗口与独立置顶经济悬浮窗独立隔离，组件卸载时彻底注销定时器与事件监听器，杜绝内存与句柄泄漏。
13. **数据脱敏与凭据安全**：Token、完整 Steam ID 严禁进入日志或模型 Prompt；敏感配置与凭据本地安全存储。
14. **重大决策沉淀至 Agent Notes**：复杂架构改动、被否决的备选方案与隐蔽踩坑细节，必须沉淀为决策笔记。详见 [.agents/notes/README.md](.agents/notes/README.md)。
15. **自动化验证与打包闭环（编辑后必跑）**：每次代码修改后必须执行前端全量构建验证 (`pnpm build`)、与一键打包验证。

---

## Wails v3 前后端交互专项准则

1. **方法绑定是前端 → Go 的唯一通道**：前端调用 Go 必须且只能通过 Wails 方法绑定（`wails3 generate bindings` 生成），严禁手写 `invokeWails` 胶水代码猜测方法名（如 `main.App.XXX`、`services.SystemService.XXX` 盲试数组候选）；严禁遍历 `window.go` 命名空间树（Wails v3 已废弃该机制）；严禁直接调用 `window.wails.Call` 等未文档化的内部 API。所有绑定必须由 `wails3 generate bindings -names -ts` 生成，前端直接 `import` 生成函数调用。
2. **服务注册命名规范与 `Route` 禁令**：Go 服务注册默认使用 `application.NewService()`，除非有明确的 HTTP 端点需求（如暴露 REST 路由给外部调用），否则**禁止**使用 `application.NewServiceWithOptions(..., Route: "/xxx")`。`Route` 字段会改变 Wails 生成的绑定命名空间，导致前端按 `services.SystemService.XXX` 调用时全部落空。服务必须保持默认命名空间，前端绑定名以 `wails3 generate bindings` 输出的 `.ts` 文件为准。
3. **`main.App` 作为统一 IPC 门面的显式转发原则**：`main.App` 只嵌入 `internalApp.App`，不会自动继承 `SystemService`、`OverlayService` 等业务服务的方法。若前端需要以 `main.App.XXX` 稳定路径调用，必须在 `main.App` 上**显式添加转发方法**（如 `func (a *App) OpenCacheDirectory(dir string) error { return services.GetSystemService().OpenCacheDirectory(dir) }`），并在添加后**重新生成绑定**。禁止依赖“嵌入会自动传递方法绑定”的错误假设。
4. **前后端职责分层铁律**：API 层（`system.ts`、`aiCoachService.ts` 等）**只负责调用后端并返回结果**，严禁在 API 层内部做状态持久化、二次跳转调用（如 `selectDirectoryDialog` 内部又调 `setCacheDirectory`）；Store 层（Pinia）负责状态管理与持久化（写入 `settings`、调用 `persistSettings`）；UI 层只调用 Store 并处理 Toast 展示。跨层职责混用是“选完没反应”“成功后报错”类问题的头号根因。
5. **事件系统方向约束**：Go → 前端推送统一使用 `app.Event.Emit(name, data...)` + 前端 `Events.On(name, callback)`；前端 → Go **必须通过方法绑定调用 Go 方法**，Wails v3 不支持前端直接 `emit` 事件给后端。组件卸载时必须注销所有 `Events.On` 监听器，防止内存与句柄泄漏（呼应铁律第 13 条）。
6. **原生 API 使用规范（对话框与资源管理器）**：选择文件夹使用 `app.Dialog.OpenFile().CanChooseDirectories(true).CanChooseFiles(false).PromptForSingleSelection()`；选择文件在 `OpenFile()` 上追加 `AddFilter(DisplayName, Pattern)`；在资源管理器中打开目录使用 `app.OpenFileManager(path, false)`。**禁止**手写 `exec.Command("explorer.exe", path)`、禁止 PowerShell `FolderBrowserDialog` 兜底脚本绕过官方 API。官方 API 不满足的极端场景必须沉淀为 Agent Note 并给出被否决方案的完整论证。
7. **错误处理与取消语义统一**：Go 端返回 `(value, error)`，`error` 非空时前端 Promise 会 reject；用户取消对话框属于正常流程，Go 端应返回 `("", nil)` 而非抛错，前端统一按“空字符串 = 用户取消”处理，不得将取消误判为系统异常并 `throw`。前端 `invokeWails` 类的兜底逻辑**禁止在 IPC 失败后 `throw` 阻断后续降级分支**（如浏览器预览降级），必须逐级 `try/catch` 并记录日志后继续。
8. **构建标签与调试通道完整性**：Wails v3 构建必须包含 `desktop,production` 及项目实际使用的所有 build tags（如 `wails`、`bindings`，以 `//go:build` 注释为准），打包脚本中的 `-tags` 必须与源文件一致，禁止在未核对源文件 build tag 的情况下臆测。调试构建禁止使用 `-H windowsgui`，必须保留控制台输出以观察 IPC 绑定、服务注册、panic 与原生调用错误；正式构建可使用 `-H windowsgui` 静默日志，但 CI 必须同时产出带控制台的 debug 版本用于问题定位。
9. **绑定名唯一事实来源（Single Source of Truth）**：前端任何调用 Go 方法的位置，其方法名必须直接引用 `frontend/bindings/` 下由 `wails3 generate bindings -names -ts` 生成模块导出的函数，严禁在业务代码中硬编码字符串方法名。当出现“未找到后端方法”类错误时，**第一步动作**是打开生成的 `.ts` 文件核对 `$Call.ByName("...")` 中的真实 FQN，而非在 `invokeWails` 候选数组中增加更多猜测项。

---

## 总结：Wails v3 交互口诀

> **服务用 `NewService`，绑定靠 `generate`，前端 `import` 直调，不猜名字不手写胶水；API 只拿数据，Store 管状态，UI 管展示；Go → 前端走 `Event`，前端 → Go 走绑定；`Route` 不乱加，`main.App` 要显式转发；取消返空不抛错，调试去 `windowsgui`。**
