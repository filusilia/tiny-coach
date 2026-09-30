# Agent Note: Wails v3 根包 FQN 绑定寻址修复、系统目录与 TTS 连通、Dota 2 7.41f 全量数据对齐及图标本地缓存落盘

Status: implemented
Date: 2026-09-21
Class: bug-fix

## 问题背景与因果机制分析

在神盾教练客户端运行与测试过程中，暴露了以下核心因果链缺陷：

1. **TTS 试听与悬浮窗唤起报“未能在运行时找到对应的后端方法 (已尝试: main.App.TestTtsEndpoint / ToggleOverlayWindow...)”**：
   - **因果机制分析**：
     - 在 Go 运行时中，位于根目录下的 `package main`，其反射类型 `reflect.TypeOf(app).Elem().PkgPath()` 对 main 包始终返回 `""`（空字符串）或 `"main"`。
     - Wails v3 的方法注册机制对于 main 包结构体生成的真实完全限定方法名（FQN）正是标准的 **`"main.App.<MethodName>"`**。
     - 历史排查误以为 Go 根包 FQN 会加上模块名前缀 `"aegis-coach.App"`，并在 `client.ts` 中将其硬编码为首选候选；当 Wails v3 收到未绑定方法调用时返回 HTTP 422 `unknown bound method name 'aegis-coach.App.*'`，而前端的异常过滤未能平滑捕获 422 状态码，导致直接抛出 ApiError 中断后续重试。
     - 同时，`TestTtsEndpoint` 签名在 Go 端需要 4 个字符串入参 `(endpoint, apiKey, engineType, voice string)`。前端历史调用未做入参填充清洗，传递 undefined 或参数数量缺失，触发 Wails v3 参数反序列化抛出 `TypeError: expects 4 arguments, got 0`，最终表现为调用失败。

2. **点击“选择文件夹”提示“未选择目录或已取消”，点击“在资源管理器打开”无效**：
   - **因果机制分析**：
     - “选择文件夹”：在 Windows 平台上，Wails v3 的 `dialog.PromptForSingleSelection()` 运行在 Go HTTP 协程调用栈中，由于缺少主线程窗体句柄或线程公寓（STA）冲突，内部触发了 `panic: runtime error: invalid memory address or nil pointer dereference (github.com/wailsapp/wails/v3/pkg/application.(*App).dispatchOnMainThread)`。
     - 因后端未对该 panic 执行 `recover` 兜底，Wails 返回 422 RuntimeError；前端 `appStore.selectCacheFolder()` 捕获后静默降级返回 `null`，导致页面误判定为“未选择目录或操作已取消”。
     - “在资源管理器打开”：同样因上述 FQN 调用异常导致未执行资源管理器唤起。此外，Windows 下路径格式需要规范化反斜杠，且在缺少备用唤起手段时可能因进程权限受阻。

3. **Alt+F2 无反应与独立悬浮窗唤起失败**：
   - **因果机制分析**：
     - 在 Windows 平台上，Wails v3 应用系统菜单（Application Menu）的快捷键未主动注册到当前活跃窗口的 Accelerator 表，导致主窗口内按下 `Alt+F2` 无法被 Win32 消息循环捕获；
     - `OverlayService.ToggleOverlayWindow()` 在创建窗口时使用了查询参数 `URL: "/?window=overlay"`，而前端 Vue Router 采用 `createWebHashHistory()`（哈希路由），导致加载后被默认重定向至 `/coach`；
     - 悬浮窗创建在非 UI 主线程调用时存在静默失败风险，缺少降级启动 `--overlay` 子进程的弹性保护。

4. **Dota 2 已更新至 7.41f，客户端图标缺乏本地持久化缓存**：
   - **因果机制分析**：
     - Valve 官方平衡性补丁 7.41f 调整了魔龙枪、大推推、大炮、玲珑心等核心装备价格与撒旦、希瓦等技能数值，客户端常量需全面同步；
     - 客户端直接引用外网 CDN 容易在离线或弱网下破图，缺少自动本地落盘与兜底流转体系。

## 决策方案

1. **Wails v3 IPC 根包 FQN 权威修正与桥接对齐**：
   - 彻底移除无效的 `aegis-coach.App.${methodName}` 前缀；
   - 在 `frontend/src/api/client.ts` 的 `expandMethodCandidates` 中，将 **`main.App.${methodName}`** 置为第一顺位最高优先级候选，并依次排列各二级领域服务 `aegis-coach/internal/services.<DomainService>.${methodName}`；
   - 强化 `invokeWails` 入参安全清洗：自动将 `undefined` 转换为 `""` 或对应缺省值，规避 Go 反射参数缺失类型错误；
   - 强化 `invokeWails` 入参安全清洗：自动将 `undefined` 转换为 `""` 或对应缺省值，规避 Go 反射参数缺失类型错误；
   - 彻底修复 `isMethodMissing` 误将 422 视作方法不存在的死循环：Wails v3 中后端返回业务 error 时的 HTTP 状态码统一为 422，仅当明确为 `unknown bound method name` 时才判定为方法缺失并回退，业务错误立刻如实向外抛出。

2. **系统原生对话框与资源管理器唤起链路加固 (对齐 Wails v3 官方规范)**：
   - 深入遵循 Wails v3 官方规范（`https://v3.wails.io/zh-cn/features/dialogs/file/`）：
     - 明确 Wails v3 官方设计原则：Wails v3 不再鼓励直接在前端直接访问无状态的 Dialog 全局对象，而是强调在 Go 后端通过 `app.Dialog.OpenFile()` 组织服务并绑定给前端调用；
     - 彻底消除“取消操作误报为异常”的死循环：在 Go 端包装方法 `SelectDirectoryDialog` 与 `SelectModelFileDialog` 中，显式捕获 Windows COM 层的取消错误 `ErrorCancelled`（以及包含 `cancelled` 或 `0x800704c7`），直接返回安全空字符串 `("", nil)`；在前端捕获层同样精准识别取消，绝不将取消视作异常去触发二次重试弹窗；
     - `internal/services/system_service.go`：
       - `SelectDirectoryDialog` 与 `SelectModelFileDialog`：去除后台协程对窗口句柄的强行 `AttachToWindow`（避免与 Windows STA 消息循环冲突死锁）；增加 `defer recover()` 保护；一旦 Wails 原生对话框发生崩溃或未就绪，立即无缝降级调用带有 `HideWindow: true` 的 PowerShell `-STA` 模式原生 `FolderBrowserDialog` / `OpenFileDialog`，彻底消除黑框闪烁，100% 确保原生窗口稳定弹出；
       - `OpenCacheDirectory`：使用 `os.MkdirAll` 预先确保目录存在，规范化 Windows 绝对路径，依次通过 `explorer.exe`、`cmd /c start` 与 `rundll32 url.dll,FileProtocolHandler` 三重降级唤起，增加 panic recover 保护；
       - 前端 `appStore.selectCacheFolder()`：发生真实异常时明确向上传递，展示精准 Toast。
     - 打包发布工具 `tools/package-release.ps1` 实施双可执行文件同步镜像：每次编译完成自动同步产出权威 `release/神盾教练.exe` 与兼容入口 `build/bin/dota-ai-coach.exe`，确保无论从哪个路径启动，用户测试的都是最新二进制。

3. **独立悬浮窗与 Alt+F2 快捷键多层闭环**：
   - 悬浮窗 URL 规范统一为 **`/#/overlay`**（匹配 Vue Router HashHistory），并在 `router/index.ts` 增加 `?window=overlay` 守卫重定向；
   - 在 `../../../cmd/aegis-coach/main.go` 中为主窗口增加 `window.RegisterKeyBinding("Alt+F2", ...)`；
   - 在前端 `App.vue` 全局挂载 `window.addEventListener('keydown', e => { if (e.altKey && (e.key === 'F2' || e.code === 'F2')) ... })`，实现窗口与 Webview 双重按键拦截；
   - 在 `OverlayService.ToggleOverlayWindow()` 中增加进程内创建异常捕获，失败时自动降级启动 `--overlay` 独立子进程。

4. **TTS 试听发音与本地降级闭环**：
   - 在 `SettingsView.vue` 中补齐第三方 TTS 测试入参；若第三方服务未运行，给出精准失败信息并自动无缝调用本地系统语音发音兜底。

5. **Dota 2 7.41f 全量更新数据与本地图标缓存**：
   - 全量同步 7.41f 装备价格与英雄技能数值；
   - 后端挂载 `/cache` HTTP 路由实现本地磁盘命中优先、官方 CDN 异步落盘、前端 `@error` 兜底的三级防线。

## 曾考虑的替代方案及被否决理由

1. **方案：在 IPC 客户端将 HTTP 422 状态码统一视作“方法未找到”（Method Missing）**
   - **最强理由**：当客户端向未注册的 BoundMethod 发送请求时，Wails v3 后端会响应 HTTP 422，判定 422 可以捕捉这种未定义方法。
   - **为什么不用**：致命漏洞在于，Wails v3 的 `transport_http.go` 中，**任何 Go 后端方法执行内部返回 `error` 时，HTTP 响应同样是 422！** 如果将 422 当作方法缺失，会导致所有正常注册但返回业务错误的有效方法被盲目跳过，最终掩盖真实异常并错误抛出“未能在运行时找到对应的后端方法”。

2. **方案：纯粹依赖 Wails v3 Dialog.OpenFile().PromptForSingleSelection() 不做 recover 降级**
   - **最强理由**：纯原生 Wails 封装，不依赖外部 PowerShell 进程，形式上最统一。
   - **为什么不用**：在 Windows 平台上，Wails v3 的 Common Item Dialog 严重依赖主线程消息分发与 STA 线程模型。一旦在后台协程中发起且缺少有效窗口句柄，极易触发底层空指针 panic 导致整个 IPC 崩溃。增加 `recover` 并降级至 PowerShell STA 窗体能够实现 100% 绝对弹窗可靠性。

## 验证计划

- **单元与运行时验证**：运行 `scratch/test_runtime_call.go`，验证 `main.App.ToggleOverlayWindow`、`main.App.TestTtsEndpoint`、`main.App.SelectDirectoryDialog` 均能正常返回或成功恢复；
- **前端静态检查与构建**：执行 `pnpm --filter frontend exec vue-tsc --noEmit` 和 `pnpm --filter frontend build`；
- **决策笔记校验**：执行 `npm run verify-notes` 确保笔记合规；
- **一键打包闭环**：执行 `pwsh -NoProfile -File ./tools/package-release.ps1`，验证输出最终版可执行文件。
