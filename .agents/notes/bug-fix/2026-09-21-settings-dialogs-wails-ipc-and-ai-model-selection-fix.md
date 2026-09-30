# Agent Note: 设置页原生对话框呼出失效、Wails v3 IPC 绑定调度与云端 AI 选型交互修复

Status: implemented
Date: 2026-09-21
Class: bug-fix

## 问题背景与因果机制分析

在神盾教练客户端设置中心（`SettingsView.vue`）使用过程中，发现了以下几组因果链缺陷与 TypeScript/Linter 异常：

1. **设置页选择文件夹/选择模型点击后提示“未选择目录或操作已取消”，点击在资源管理器打开无反应**：
   - **底层因果链**：
     - 在 Wails v3 的前端运行时中，绑定的 Go 方法调用入口是通过 `window.wails.Call.Call({ methodName, args })` 提供的。而前端旧桥接层（`client.ts` 与 `wailsBridge.ts`）错误地仅探测了 `window.wails?.Call?.ByName` 与 `window.wails?.Call?.ByID`。
     - 由于 Wails v3 运行时对象并不存在 `ByName` 属性，因此 `wailsBridge.ts` 中的 Proxy 总是回退到 `return null`。
     - 前端接收到 `null` 结果后，逻辑直接判定为“未选择目录或操作已取消”，且打开资源管理器请求（`OpenCacheDirectory`）无法送达后端。
     - 此外，在 Go 后端 `system_service.go` 中，当用户在原生对话框点击取消时，旧逻辑返回了当前正在生效的路径 `current` 而非空字符串，导致逻辑混淆。

2. **TypeScript 强类型契约漂移与字段不匹配**：
   - `VoiceBroadcastBar.vue`：错误解构了并不存在的 `state` 属性，且未通过 `storeToRefs` 解构 Pinia 计算属性 `currentBroadcastStrategy`，导致 TS 报错 `Property 'state' does not exist` 与 `Property 'value' does not exist on type ...`。
   - `SettingsView.vue`：第 607 行模板引用了不存在的 `cat.title`，而 `ReminderCategoryConfig` 规范定义的显示名称属性为 `cat.label`。
   - `aiCoachService.ts`：Go 端后端 DTO（`AiEndpointTestResultDTO`）与前端测试实体属性命名差异（`modelUsed`/`rawResponse` 与旧的 `model`/`responseSample`）；`askCoach` 传递的 `clockTime` 类型未转换为字符串；赛后复盘接口缺少 `userContext` 可选参数与未使用的 `_match` 变量。
   - `playerProfileService.ts`：Go 导出的 `PlayerProfileAnalysis` DTO 与前端组件专用的分析实体字段结构不一致，直接赋值报缺少属性错误。
   - `OverlayView.vue`：GSI 状态中 `gpm`/`xpm` 字段应为 `goldPerMin`/`xpPerMin`，买活相关字段位于 `hero` 下而非 `player` 下。
   - `abilityTextResolver.ts`：`extractPlaceholderKeys` 函数漏声明了局部变量 `const keys: string[] = []`。

3. **WebStorm/Linter 检查告警与代码风格冗余**：
   - 多处输入框样式中 `border-slate-700` 与 `focus:border-amber-400` 产生重复设置 `border-color` 冲突告警；
   - `handleBindPlayerId` 中在 `try` 块内手动抛出并立刻在本函数 `catch` 捕获的异常处理风格（“本地捕获异常的 throw”）；
   - 包含多处未被 `await` 的异步函数调用（如 `onMounted` 内的 `fetchLocalModels` 与 `checkOverlayStatus`）；
   - `stores/app.ts` 与 `ai_service.go` 中残留的 `qwen2.5` / `deepseek-r1:7b` 历史兼容代码。

4. **云端模型测试连接后未提供模型选择列表**：
   - 云端 API 模式下仅存在一个输入框与“拉取列表”按钮，拉取成功后模型仅存储在内存中，未在界面提供可选模型项或候选下拉组件，导致用户无法便捷点击选用。

## 决策方案

1. **Wails v3 IPC 桥接全协议兼容升级**：
   - 在 `frontend/src/api/client.ts`、`frontend/src/services/wailsBridge.ts` 和 `frontend/wailsjs/go/main/App.js` 中完整适配 `window.wails.Call.Call({ methodName, args })`、函数式调用及历史方法兜底。
   - 在 `system.ts` 中针对浏览器开发预览环境补充基于 File System Access API / HTML File Picker 的降级机制；在非桌面环境点击打开资源管理器时给出友好提示。
   - 在 Go 后端 `system_service.go` 中，当用户主动点击取消对话框时严格返回空字符串 `"", nil`，杜绝误更新或弹窗重复冒泡。

2. **全面修复 TypeScript 强类型契约**：
   - `VoiceBroadcastBar.vue`：规范采用 `storeToRefs(appStore)` 解构响应式属性，直接通过 `appStore.settings` 读取配置，消除 `state` 报错与 `value` 访问警告。
   - `SettingsView.vue`：将 `cat.title` 统一修正为规范的 `cat.label`。
   - `aiCoachService.ts`：修正 DTO 字段映射；对 `BattleContextDTO.clockTime` 做强类型转串；为 `generateMatchReview` 补充 `userContext?: string` 参数；未使用入参加上划线 `_match`。
   - `playerProfileService.ts`：增加 DTO 到业务实体的精准适配器逻辑，无缝转换英雄胜率榜单与战术标签。
   - `OverlayView.vue`：精确绑定 `player.goldPerMin`、`player.xpPerMin` 以及 `hero.buybackCost`、`hero.buybackCooldown`。
   - `abilityTextResolver.ts`：补齐 `const keys: string[] = []`。

3. **清理旧模型兼容逻辑与重构样式/异常**：
   - 彻底删除 `stores/app.ts` 中针对 `qwen2.5` 系列与 `deepseek-r1:7b` 的旧版硬编码判定，默认且统一以最新的轻量端侧模型 `qwen3.5:1.5b`（严格 ≤ 2GB 内存开销）为基线。
   - 同步更新 Go 端 `internal/services/ai_service.go` 缺省模型至 `qwen3.5:1.5b`。
   - 将冲突的 `border-slate-700` + `focus:border-amber-400` 重构为高对比度暗色电竞风的 `border border-slate-700 rounded-lg bg-slate-900 focus:outline-none focus:ring-1 focus:ring-amber-400`，不仅彻底消除了 CSS 属性覆盖冲突，而且大幅提升了输入框聚焦时的视觉感知度。
   - `handleBindPlayerId` 改为基于 `if (res.ok)` 显式分支处理，消除无意义的内部 throw。
   - 规范异步函数调用的 `await` 与 `void` 处理。
   - 优化缩短模块导入路径为 `../api` 与 `../stores`。

4. **云端模型测试与快速选用列表交互闭环**：
   - 在 `runEndpointTest` 实测端点成功时，云端模式自动触发 `fetchRemoteModels()` 刷新模型列表。
   - 在云端模型名称输入框旁绑定 `<datalist id="cloud-models-datalist">` 提供自动补全。
   - 在输入框下方新增“可用模型选项 (点击选用)”高亮徽章列表，支持一键点击选中、立即生效并持久化到本地配置。

## 曾考虑的替代方案

1. **方案：通过单独引入一个全量 Select 下拉框替代原生输入框**
   - **放弃理由**：许多自建或小众云端代理（如本地 OneAPI、NewAPI、转发网关）并不开放标准 `/models` 端点，或者返回的模型名称与用户实际开通权限的专属通道不一致。若强行使用固定 `Select`，将导致用户无法自由录入自定义模型 ID（如 `deepseek-chat-01`）。
   - **最终选定**：保持“可自由输入的 Input + Datalist 智能补全 + 探测到模型后的快捷点击选用 Pills”多重方案，兼顾易用性与完全的输入自由度。

## 后果与验证计划

- **构建与类型验证**：`npx vue-tsc --noEmit` 全量静态类型检查通过，0 错误；
- **代码规范验证**：`npx eslint .` 全量扫描通过，0 错误 0 警告；
- **生产打包验证**：`pnpm build` 顺利产出构建包；
- **工程决策校验**：`npm run verify-notes` 校验通过。
