# Agent Note: 原生文件夹选择与文件管理器交互纯净化及 Wails v3 绑定自动同步

Status: implemented
Date: 2026-09-22
Class: architecture

## 背景与问题因果机制剖析

根据 Wails v3 原生规范与架构分层原则，用户指出了原实现中几处隐蔽的结构性缺陷：

1. **Go 后端 `SelectDirectoryDialog` 职责越权篡改状态**：
   - **因果机制**：原 `SelectDirectoryDialog()` 内部在拿到 `selectedDir` 后，擅自调用了 `s.SetCacheDirectory(selectedDir)`。
   - **后果**：本应只负责唤起系统原生弹窗并返回用户选取的物理路径的方法，擅自修改了服务运行态与磁盘目录结构。若前台仅为临时挑选或后续校验未通过，后端已强行写入，破坏了“API 只负责交互、Store 负责状态维护”的分层底线。

2. **资源管理器调用未充分利用 Wails v3 原生能力**：
   - **因果机制**：此前通过手动拼装 `exec.Command("explorer.exe", target)` 唤起资源管理器，虽然在 Windows 下可用，但未能利用 Wails v3 官方提供的环境管理器 `app.Env.OpenFileManager(target, false)`（具备多平台统一适配与窗口线程同步优势）。

3. **前端绑定（Bindings）脱节隐患**：
   - **因果机制**：开发过程中若修改 Go 导出方法，若未及时运行 `wails3 generate bindings -names -ts -b`，可能导致前端生成的绑定与后端真实 FQN 错位。

## 被放弃的方案与权衡分析

### 方案 A：在 Go 端弹窗成功后立即写入系统配置
- **最强理由**：一步到位，前端少调一次后端接口。
- **放弃原因**：严重违反单一职责原则。状态管理属于前端应用层逻辑（Pinia Store），弹窗服务仅应是纯净的 I/O 桥接，越权写入会导致状态不一致与无法取消。

### 方案 B：在生产打包时由开发者手动运行 bindings 命令
- **最强理由**：打包脚本只关心构建，不介入代码生成。
- **放弃原因**：极易产生“改了 Go 后端代码直接跑打包脚本，结果前端绑定未更新”的人为失误。自动化打包流程中应做到 0 人工介入的自闭环。

## 实施的技术方案与架构重构

1. **Go 后端纯净化 `SelectDirectoryDialog`**：
   - 在 `internal/services/system_service.go` 中，移除 `s.SetCacheDirectory(selectedDir)`，用户选择成功后直接返回 `strings.TrimSpace(selectedDir), nil`；用户点击取消或 Esc 时返回 `"", nil`。
2. **接入 `app.Env.OpenFileManager` 并健全降级链**：
   - 在 `OpenCacheDirectory` 中优先调用 `app.Env.OpenFileManager(target, false)`；若实例不存在或调用失败，自动降级至 `explorer.exe` 与 `cmd start`。
3. **Store 层与 API 层职责严密对齐**：
   - API 层（`system.ts`）只返回路径或发起操作，不产生副作用；
   - Store 层（`app.ts`）统一负责将路径写入 `settings.cache_directory`，执行持久化，并调用 `systemApi.setCacheDirectory` 同步底层存储引擎；
   - 对 `openCacheDirectory` 补充空值前置检查与自动回退当前生效路径。
4. **打包流水线（`package-release.ps1`）自动化集成 Bindings 同步**：
   - 在 Vite 静态构建前，自动检测并执行 `wails3 generate bindings -names -ts -b`，确保 `frontend/bindings/` 永远与 Go 保持 100% 同步。

## 验证结论

- **决策笔记校验**：`npm run verify-notes` 全量 23 篇 Agent 决策笔记 100% 格式合法。
- **便携版一键打包验证**：运行 `.\tools\package-release.ps1`：
  - [1/4] 自动生成 Wails v3 bindings 成功；
  - [2/4] 前端 Vite 构建 840ms 成功；
  - [3/4] Go 编译成功（24.1 MB）；
  - [4/4] 生成便携客户端 `release/神盾教练.exe` 成功。
