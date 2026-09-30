# Agent Note: Wails v3 服务绑定命名空间真相剖析与 main.App 门面健壮性加固

Status: implemented
Date: 2026-09-22
Class: architecture

## 背景与问题因果机制剖析

在针对系统设置中心“选择文件夹”与 Wails v3 IPC 链路的审查中，针对 `../../../cmd/aegis-coach/main.go` 的服务注册与工程质量，梳理出以下关键因果链路：

1. **`Route: "/cache"` 与服务绑定名 FQN 的真实底层机制（破除直觉误区）**：
   - **直觉疑问**：是否因为 `NewServiceWithOptions(systemService, ServiceOptions{ Route: "/cache" })` 导致 FQN 被修改为 `cache.SystemService.SelectDirectoryDialog`，从而造成前端调用失败？
   - **源码真相**：查阅 Wails v3 核心源码 `bindings.go:245`，绑定名的生成逻辑为：
     ```go
     fqn := fmt.Sprintf("%s.%s.%s", packagePath, typeName, methodName)
     ```
     其完全由 Go 语言反射包元数据（`reflect.Type.PkgPath()` 与 `reflect.Type.Name()`）决定，未读取 `Route` 属性。
   - **`Route` 的唯一真实职责**：在 `application/service.go` 中，若服务实现了 `http.Handler`，Wails 会将其挂载至内置 HTTP 服务器的 `Route` 路径。`SystemService` 实现了 `ServeHTTP` 接口，专用于流式返回本地图标（如 `<img src="/cache/heroes/antimage.png" />`）。
   - **红线结论**：**`Route: "/cache"` 绝对不可删除**。若删除，整个应用中 127 英雄、所有物品的本地缓存图标 HTTP 加载将全部遭遇 404 挂起！

2. **跨包匿名结构体内嵌与 AST 代码生成器的穿透盲区**：
   - **因果机制**：`../../../cmd/aegis-coach/main.go` 中声明 `type App struct { *internalApp.App }`。虽然运行时反射能提取内嵌方法，但静态 AST 生成工具（如代码生成器或类型导出器）仅解析当前包当前文件显式声明的接收者方法（Receiver Methods），不会深层穿透跨包匿名字段。
   - **后果**：如果前端仅依赖 `main.App.SelectDirectoryDialog`，在某些生成或绑定模式下该 FQN 可能不存在。

3. **打包与运行时工程隐患**：
   - 缺少构建期 `-ldflags` 注入版本变量（`Version`, `BuildTime`, `GitCommit`）；
   - 使用裸 `println` 打印错误，在 Windows GUI 无终端模式（`-H windowsgui`）下输出静默丢失；
   - 缺少全局 `PanicHandler` 拦截，异常发生时导致窗口无预警闪退；
   - 创建窗口未做空指针防御。

## 被放弃的方案与权衡分析

### 方案 A：移除 `../../../cmd/aegis-coach/main.go` 中的 `Route: "/cache"` 配置
- **最强理由**：直觉上认为去掉选项参数可以让服务恢复最质朴的注册方式，消除任何可能的命名空间副作用。
- **放弃原因**：Wails v3 的 `Route` 与 RPC 绑定没有任何关联，它是流式 HTTP 资产访问的基石。移除后，全站图标缓存直接瘫痪，属于高危负优化。

### 方案 B：仅依赖 Go 反射提升，不显式编写门面方法
- **最强理由**：省去样板转发代码，保持 `../../../cmd/aegis-coach/main.go` 极简。
- **放弃原因**：无法抵御静态代码生成器对跨包内嵌结构体的解析缺失；显式门面声明是 Go 语言最佳实践，具有确定性保证。

## 实施的技术方案与架构重构

1. **显式补全 `main.App` 核心门面方法**：
   在 `../../../cmd/aegis-coach/main.go` 中为 `*App` 显式声明常用 IPC 接收者方法（如 `SelectDirectoryDialog`、`SelectModelFileDialog`、`GetCacheDirectory`、`SetCacheDirectory`、`OpenCacheDirectory`、`ClearCacheDirectory`、`ScanLocalModelFiles`、`IsOverlayMode`、`ToggleOverlayWindow`、`GetLatestGsiSnapshot`、`IsGsiRunning` 等），委托给 `a.App`，确保无论 AST 还是运行时反射均 100% 暴露。
2. **接入结构化日志与顶层崩溃拦截（PanicHandler）**：
   在 `application.Options` 中配置 `LogLevel: slog.LevelInfo` 与 `PanicHandler`，安全捕获非预期 Panic 并落盘输出详细堆栈，杜绝静默闪退。
3. **注入构建元数据变量**：
   在 `../../../cmd/aegis-coach/main.go` 中声明 `Version`、`BuildTime`、`GitCommit`，并在 `tools/package-release.ps1` 中通过 `-ldflags` 动态注入。
4. **窗口与托盘非空防御**：
   对 `app.Window.NewWithOptions` 返回句柄进行防御性检查，失败时记录致命日志。

## 验证结论

- **决策笔记校验**：`npm run verify-notes` 22 篇笔记 100% 格式合法通过。
- **前端全量构建**：`pnpm --filter frontend build` 编译 0 错误。
- **Go 后端全量编译**：`go build -v .` 编译 0 错误。
