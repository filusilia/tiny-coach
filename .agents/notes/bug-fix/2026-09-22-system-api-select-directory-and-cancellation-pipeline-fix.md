# Agent Note: 设置中心原生选择文件夹职责越权、降级通道阻断与取消异常判定深度修复

Status: implemented
Date: 2026-09-22
Class: bug-fix

## 问题背景与因果机制分析

在神盾教练客户端（`SettingsView.vue`）使用过程中，玩家点击“选择文件夹”按钮曾出现“点击没反应”或“选择后弹窗异常/二次报错”的现象。深度溯源代码因果链后发现，问题根源并非后端或 AI 链路，而是位于 `frontend/src/api/system.ts`、`frontend/src/api/client.ts` 与 `frontend/src/stores/app.ts` 之间存在的多处严重职责错位与设计缺陷：

1. **API 层职责越权与循环副作用触发（最致命缺陷）**：
   - 在 `systemApi.selectDirectoryDialog()` 中，当用户成功通过原生对话框（`win.wails.Dialogs.OpenFile`）选择目录后，代码错误地执行了 `return await this.setCacheDirectory(selected.trim())`。
   - `selectDirectoryDialog` 的职责应当严格收敛为**只获取用户选择的路径字符串**。由于内部擅自调用 `setCacheDirectory`，若后端在建构目录或方法调用中抛出任何异常，该异常会被弹窗外层的 `catch` 错误捕获。
   - `catch` 块误以为是“弹窗打开失败”，进而触发第二级 Go IPC 降级重试（导致玩家面临二次重复弹窗或直接抛出错误）。

2. **第二级降级中错误使用 `throw ipcErr` 阻断后续降级**：
   - 当第二级 Go IPC 桥接方法失败时，`catch (ipcErr)` 块内直接调用了 `throw ipcErr`。
   - 该抛错导致函数调用栈立刻终止并向外抛出未捕获错误，使第三级（浏览器沙箱安全兜底）彻底沦为不可达死代码（Unreachable Code）。

3. **取消错误判定脆弱（Cancel Error Detection Incomplete）**：
   - 旧逻辑仅粗糙检查 `errStr.includes('cancel') || errStr.includes('0x800704c7')`。
   - 在 Windows 环境下，用户主动取消原生对话框时的错误码和消息形态多样（如 Win32 `1223` 即 `ERROR_CANCELLED`、有符号整型 `-2147023673`、Web API `AbortError` 以及操作系统的本地化中文“取消”）。这些取消动作被误当成系统致命异常抛出，导致 UI 弹出错误 Toast。

4. **违反零伪造原则（Zero Mock/Fabrication Rule 8 违规）**：
   - 在原第三级浏览器开发降级中，代码在调用 `showDirectoryPicker()` 后硬编码合成了虚假路径 `D:/Dota2/Cache/${handle.name}`。
   - 浏览器沙箱根本无法穿透获取宿主机真实的 Windows 物理盘符路径，虚假路径送达后端必然导致落盘失败。

5. **Wails IPC 候选列表遍历中断（`client.ts` 匹配不完全）**：
   - 在 `invokeWails` 遍历候选方法名时，原有的 `isMethodMissing` 仅判定了大小写敏感的少量字串。若 Wails v3 运行时返回 `method not found`、`404` 或类似异常，候选轮询会提前中断抛出 `ApiError`。

## 决策与重构方案

1. **彻底解耦 API 层与 Store 层的职责边界**：
   - `systemApi.selectDirectoryDialog()` 严格收敛为纯粹的原生弹窗交互器，仅返回用户选中的清洗路径（`string`），绝不触发任何持久化或目录写入副作用。
   - 路径持久化、Pinia 响应式状态更新（`settings.cache_directory`）以及与后端的安全同步（`systemApi.setCacheDirectory`）全部由 `appStore.selectCacheFolder()` 统一编排；且后端同步置于非阻塞的独立 try/catch 中，防止因单个子目录写入权限问题破坏用户前台选择体验。

2. **全局统一规范的 `isCancelError` 判定工具**：
   - 统一导出 `isCancelError(e)` 函数，全面覆盖 `AbortError`、Win32 错误码 `1223`、`-2147023673`、`0x800704c7` 以及大小写不敏感的 `cancel`、`abort`、`取消`。在捕获到用户取消时安全静默返回空字符串 `""`。

3. **修复多级降级通道连贯性**：
   - 移除第二级 Go IPC 中的 `throw ipcErr`，记录降级调试日志后自然流转至后续检查，杜绝异常阻断链路。
   - 同样在 `selectModelFileDialog()` 中修复该问题。

4. **消除虚假路径伪造，恪守零伪造原则**：
   - 在浏览器沙箱预览模式下，明确抛出提示说明浏览器限制无法获取本地物理路径，坚决不再伪造 `D:/Dota2/Cache/...` 等虚拟磁盘路径。

5. **扩充 `client.ts` 的方法缺失容错模式**：
   - 将 `isMethodMissing` 统一转为小写判定，包含 `unknown bound method`、`method not found`、`not found`、`undefined`、`no method` 等变体，确保多候选 FQN 充分重试。

## 验证结论

- 执行 `pnpm --filter frontend build`，前端编译构建 100% 成功（耗时 625ms，0 错误，0 警告）。
- 执行 `npm run verify-notes`，决策笔记校验 100% 通过。
