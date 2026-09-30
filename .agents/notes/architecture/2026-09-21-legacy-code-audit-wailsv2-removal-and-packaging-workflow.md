# Agent Note: 遗留代码审查审计 (Wails v2 清理与 Rust/C 排除) 与闭环打包工作流

Status: implemented
Date: 2026-09-21
Class: architecture

## 问题背景与因果机制分析

在神盾教练从早期技术验证架构全面演进至现代化 **Wails v3 + Go + Vue 3 (TypeScript)** 架构的过程中，代码库中可能残留历史阶段的生成文件、向下兼容垫片以及非目标技术栈的假设，需要全面审查与净化：
1. **Wails v2 遗留生成代码冗余**：
   - 仓库曾存在 `frontend/wailsjs/` 目录，包含 `wailsjs/runtime/runtime.js` (330 行旧版 Wails v2 客户端代码) 及旧式 Go 绑定代理。这些代码直接调用 `window.runtime.*` 与 `window.go.main.App.*`；
   - 在已建立现代化 `frontend/src/api/` 强类型 IPC 层之后，`frontend/src` 内业务组件均已不再引用 `wailsjs`，但该目录依然保留在代码树中，造成技术栈演进认知混乱；
   - 规则引擎 `gsiRuleEngine.ts` 中存在仅检查 `(window as any).runtime.EventsOn('dota_gsi_update')` 的残留逻辑，未能直接接入 Wails v3 原生 `window.wails.Events.On`，且与 Go 后端规范派发的 `"gsi_data"` 存在通道脱节风险。
2. **Rust / C++ / CGO 潜在侵入风险排查**：
   - 为确保电竞战术助手免去庞大环境依赖与外部 C/C++ 动态链接库（DLL 缺失/VC++ Redistributable 冲突），必须彻底确认全仓是否存在 Rust (如 Tauri/Cargo) 或 C/C++ (CGO / Makefile) 遗留代码；
3. **闭环打包交付铁律固化**：
   - 开发改动后若仅运行前端构建或单包单元测试，容易遗漏 Go 宿主静态嵌入、Windows 高清清单与图标资源编译的潜在破坏，必须建立“每次开发完成即一键自动化打包单文件绿色 exe”的最高标准闭环。

## 决策方案

1. **全面净化 Wails v2 残留，确立 Wails v3 纯粹通信流**：
   - **移除废弃目录**：彻底删除 `frontend/wailsjs/` 目录，并在 `README.md` 与 `frontend/eslint.config.js` 中同步清除对其的历史忽略与索引；
   - **规则引擎事件现代化改造**：重写 `frontend/src/services/gsiRuleEngine.ts` 初始化逻辑，优先使用 `window.wails?.Events?.On` 监听权威事件通道 `"gsi_data"` 与兼容通道 `"dota_gsi_update"`，并自动解包 `eventPayload.data`，同时保留垫片兼容能力；
   - **Go 端双通道广播**：在 `internal/services/gsi_service.go` 中，接收到 Dota 2 客户端 GSI 快照后同时派发 `"gsi_data"` 与 `"dota_gsi_update"` 事件，确保前台主窗口与悬浮窗 100% 接收到快照数据；
   - **同步技术文档**：将 `docs/SUBSYSTEM_RUNTIMES.md` 时序图中的 `runtime.EventsEmit` 修正为 Wails v3 的 `app.Event.Emit("gsi_data", rawPayload)`。
2. **排查事实裁定：全仓零 Rust、零 C/C++、零 CGO**：
   - **Rust 审计结果**：全仓 0 个 `.rs` 文件，0 个 `Cargo.toml` / `Cargo.lock`，无任何 Tauri 相关配置。所有包含 `rust` 子串的检索项均为英文单词（如 `ui-ux-pro-max` 数据集中的 "trust", "thrust"）或 Dota 2 官方物品名（`trusty_shovel` 信任手铲）；
   - **C / C++ 审计结果**：全仓 0 个 `.c` / `.cpp` / `.h` / `.hpp` 文件，0 个 `Makefile` / `CMakeLists.txt`。Go 源码中零 `import "C"`（CGO 完全关闭）；
   - **Windows 原生能力**：Windows 窗口扩展样式、置顶保活与无边框拖拽均通过 Go 标准库 `syscall.NewLazyDLL("user32.dll")` 动态加载，无需任何外部 C 编译器即可完美跨平台编译。
3. **开发即打包 (Always Deliver Executable) 闭环固化**：
   - 每次功能迭代或缺陷修复完成后，必须依次执行：
     1. 前端类型严格校验：`pnpm --filter frontend exec vue-tsc --noEmit`；
     2. 前端生产静态资产打包：`pnpm --filter frontend build`；
     3. Windows 原生免安装便携版一键编译打包：`pwsh -NoProfile -File ./tools/package-release.ps1`（输出至 `release/神盾教练.exe`）；
     4. 规范与决策笔记验证：`npm run verify-notes`。

## 曾考虑的替代方案

1. **继续保留 `frontend/wailsjs` 目录作为历史备份**：
   - *被放弃的最强理由*：若未来开发者在缺少 Wails v3 认知的情况下误引入 `wailsjs`，代码将退化回对 `window.runtime` 的依赖，破坏现有极简纯净的 `src/api` 强类型调用链；
   - *为什么不用*：`src/api` 已经完整封装了所有 Go 后端服务门面（`aiApi`, `systemApi`, `playerApi`, `overlayApi`, `gsiApi`），`wailsjs` 已沦为 100% 孤立死代码，必须果断清除。
2. **在 `gsi_service.go` 中仅保留新事件名称 `"gsi_data"` 并强行破坏旧事件**：
   - *被放弃的最强理由*：严格单一命名能保证命名完全统一；
   - *为什么不用*：双发代价极低（仅数微秒内存操作），能提供对未来多窗口或不同子版本脚本的绝对兼容防护。

## 后果与验证计划

- **收益**：
  - 减少了 5 个无效生成文件与 400+ 行死代码，前端项目更加轻盈整洁；
  - GSI 事件总线实现了 Wails v3 原生直连，消除了中转层潜在的丢包与时钟差；
  - 明确了工程在纯 Go（无 CGO / 无 Rust / 无 MSVC 依赖）上的极致纯净度。
- **验证矩阵**：
  - `pnpm --filter frontend exec vue-tsc --noEmit`：0 错误通过；
  - `pnpm --filter frontend build`：Vite 产物打包 100% 成功；
  - `pwsh -NoProfile -File ./tools/package-release.ps1`：成功产出 24.09 MB 单文件绿色可执行程序 `release/神盾教练.exe`；
  - `npm run verify-notes`：全量 19 篇 Agent 决策笔记校验 100% 通过。
