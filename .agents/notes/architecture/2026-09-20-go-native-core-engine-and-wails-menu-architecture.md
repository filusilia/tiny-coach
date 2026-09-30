# Agent Note: 核心网络、AI 中枢下沉至 Go 原生内核与 Wails 原生菜单架构

Status: implemented
Date: 2026-09-20
Class: architecture

## 问题背景与因果机制分析

在之前的开发迭代中，由于前端原型验证的便利性，部分核心业务与网络逻辑滞留在 Vue 前端：
1. **浏览器/WebView2 CORS 跨域壁垒**：前端直接通过 `fetch()` 访问第三方 OpenDota API、用户自建或私有云端模型时，经常因服务商未配置 CORS 响应头或存在预检拦截而报错；
2. **凭据安全与合规风险**：用户的云端 API Key 存放在浏览器 LocalStorage 中，直接在前端网络请求头中明文暴露，极易被逆向或控制台泄漏；
3. **本地模型文件无法高效利用**：用户选取的本地 `.gguf` / `.onnx` 模型文件，前端纯 Web 环境受沙箱与内存限制，无法直接调用本地 NPU/GPU 原生加速库进行高效推理；
4. **多窗口状态不同步与频繁限流**：主窗口与独立置顶悬浮窗相互独立，若各自在前端发起网络请求，会导致 API 请求翻倍，极易触发 OpenDota 的 429 访问频次封禁；
5. **缺少系统级原生体验**：此前缺乏 Wails 原生系统级菜单与系统托盘，用户最小化后无法在后台持续静默监听 Valve GSI 4444 端口并自动弹出悬浮窗。

## 决策方案

确立**“Go 原生核心引擎 (Heavy Core) + Vue 电竞 HUD 薄客户端 (Thin Client)”**的黄金架构：

1. **Go 原生 AI 战术推理中枢 (`ai_service.go`)**：
   - 原生 `net/http` 长连接池驱动云端模型（兼容 DeepSeek / OpenAI 协议），管理 API Key 与端点，对前端零暴露；
   - 原生探测本地端点与服务状态（127.0.0.1:11434），提供精确毫秒级时延测量；
   - 实现原生 `GenerateMatchReview`（战绩 AI 深度复盘），吸纳 BrightGir 决策层级，提供结构化分析（大盘胜负手、主视角对线与暴毙分析、装备克制推演、下局落地指南），附带确定性离线兜底；
   - 实现原生 `AskCoach`（即时战术问答）。

2. **Go 原生 Dota 2 战绩网关 (`player_service.go`)**：
   - 原生实现 `NormalizeDotaAccountId`：全角转半角、Emoji/Unicode 清洗、Steam3 格式识别、链接解析、SteamID64 转换、OpenDota 玩家模糊检索；
   - 原生代理拉取 OpenDota 战绩与比赛历史，内置 Single-Flight 机制与本地磁盘缓存池（10 分钟有效期），杜绝重复并发请求与 429 限流。

3. **Wails v3 原生系统菜单与系统托盘 (`../../../cmd/aegis-coach/main.go`)**：
   - 注册原生应用程序菜单（Application Menu）：文件、视图、战术、帮助；
   - 注册系统托盘（System Tray）：支持最小化到托盘并在后台监听 4444 端口，托盘菜单一键唤起/隐藏置顶悬浮窗；
   - 修复 Windows 打包脚本 `tools/package-release.ps1` 智能探测 `go.exe` 路径。

4. **Vue 前端轻量代理与无缝双向兼容**：
   - 前端 `aiCoachService.ts` 与 `playerProfileService.ts` 改造为调用 Go 原生 Binding；
   - 保留纯浏览器/测试环境的优雅降级逻辑，实现开发与打包生产环境 100% 功能兼容。

## 曾考虑的替代方案

### 1. 保持纯前端 fetch 并在 Go 端仅做反向代理转发
- **最强理由**：前端代码几乎不用变动，仅将 URL 替换为本地 localhost 代理端点。
- **为何弃用**：割裂了数据流，无法在 Go 端统一实施缓存落盘、Single-Flight 与离线确定性降级；本地模型文件 (.gguf) 更是无法通过简单代理直接运行。

### 2. 使用 Node.js / Python 作为后台微服务
- **最强理由**：Python AI 生态极其丰富，有很多现成库。
- **为何弃用**：极大地膨胀了安装包体积，且与当前极轻量的 Wails v3 + Go 现代化架构相悖；Go 单二进制交付是最稳固、轻量（仅 23MB）的电竞桌面方案。

## 后果与验证计划

- **收益**：彻底消除 CORS 跨域报错，API Key 安全隔离，本地缓存杜绝 429，多窗口共享 Go 单例状态，支持原生系统托盘与菜单，打包脚本 100% 自动化。
- **验证**：执行 `go vet ./...`、`pnpm build`、运行全量单元测试与打包脚本 `pwsh ./tools/package-release.ps1`，验证通过后执行 `npm run verify-notes`。
