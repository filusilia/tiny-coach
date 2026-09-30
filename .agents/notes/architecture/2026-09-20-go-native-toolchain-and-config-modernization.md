# Agent Note: 纯 Go 原生现代化工程工具箱与全仓基础配置纯净化决策

Status: implemented
Date: 2026-09-20
Class: architecture

## 问题背景与因果机制分析

神盾教练项目已全面演进至 **Wails v3 (Go) + Vue 3 + TypeScript** 极轻电竞战术架构，但在工程配置与辅助工具链层面，仍遗留了多处历史产物与脆弱脚本：

1. **基础配置文件遗留陈旧规则**：
   - `.editorconfig`：仍保留大量 C# (`[*.cs]`)、.NET 解决方案 (`[*.sln]`) 及工程文件缩进规则，缺乏 Go 语言（必须使用 tab 缩进）与现代前端（Vue/TS/CSS 2 格空格）的规范；
   - `.gitattributes`：充斥着 `*.cs`, `*.csproj`, `*.xaml`, `*.pri`, `*.xbf` 等 .NET/WinUI 属性，缺失 Go 源码、现代前端代码文件及 Go 资源文件（`*.syso`）的换行与二进制防损坏标记；
   - `.gitignore`：遗存旧 Rust/Tauri（`src-tauri/target/`）、VS 临时文件及 Python 缓存规则，未针对 Go 编译临时中间物（如动态生成的 `*.syso`）设立隔离屏障；
   - `wails.json`：全量为 Wails v2 遗留的 JSON 配置，而现代 Wails v3 采用纯 Go 声明式代码配置（Code-first Configuration），该文件已不再进入构建体系。
2. **辅助运维脚本依赖 PowerShell 存在固有弊端**：
   - `Normalize-LineEndings.ps1`：基于 PowerShell/pwsh 单线程遍历，非 Windows 平台无法跨环境工作，启动慢且装箱开销大；
   - `update-hero.ps1`：硬编码旧版 `public/` 路径导致运行时直接崩溃，串行 HTTP 请求 127 位英雄耗时近 1 分钟，异常恢复机制脆弱；
   - `package-release.ps1`：超百行 PowerShell 脚本，与操作系统 Shell 语法强绑定，不利于 CI/CD 与跨平台标准化交付；
   - `download-ai-runtime.ps1`：旧 C# ONNX Runtime 扩展包下载器，与当前已确立的“轻量云端/本地标准 HTTP API 旁路架构”完全脱节。

## 决策方案

### 1. 基础配置文件全面纯净化
- **.editorconfig**：移除所有 .NET/C# 遗留，确立 `[*.go]` 严格 `indent_style = tab`（对齐 `gofmt`），确立 `[*.{vue,ts,tsx,js,json,css,html}]` 统一 `indent_style = space; indent_size = 2`；
- **.gitattributes**：全面清理 .NET 规则，为 `*.go`、`*.vue`、`*.ts`、`*.css` 确立统一的 Windows CRLF 换行规范，并将 `*.syso`、`*.woff2`、`*.webp` 明确标为二进制，杜绝 Git 换行符破坏资源；
- **.gitignore**：剔除 Rust/VS/Python 废弃目录，补充 Go 编译临时产物 `*.syso` 拦截；
- **.npmrc & package.json**：规范 pnpm 依赖构建配置，杜绝 npm 执行时的无效警告。

### 2. 打造统一的纯 Go 原生现代化工具箱 (`tools/main.go`)
利用 Go 语言标准库（零额外第三方库依赖）将全套运维与构建脚本重构为统一的 Go CLI (`go run ./tools <subcommand>`)：
- **`normalize`**：利用 `filepath.WalkDir` + `bytes.ReplaceAll` 毫秒级并发扫描/修复全仓文本 CRLF，提供 `--check` CI 检测模式；
- **`sync-heroes`**：利用 Go 协程池与 `net/http` 复用连接池，10 并发极速同步 127 英雄官方 API，3 秒内完成抓取与强类型校验，原子写入数据源；
- **`package`**：一键自动化打包，自动清理缓存、调度 `pnpm build`、检测 `rsrc` 嵌入高分辨率图标与清单、调用 `go build` 生成绿色单文件客户端并整理发布套件。

### 3. 双轨无缝兼容设计
- 在 `package.json` 中配置便捷命令（`npm run package`, `npm run normalize` 等）；
- 保留现存 `.ps1` 脚本作为薄转发包装层，调用 `go run ./tools ...`，确保老用户习惯与既有脚本 100% 兼容。
