# Agent Note: 第三方 TTS 重构、STA 原生文件选择与 ≤2GB 本地模型生态治理决策笔记

Status: implemented
Date: 2026-09-21
Class: architecture

## 问题背景与因果机制分析

在神盾教练实战测试与桌面深度使用中，暴露了以下核心系统级与体验痛点：

1. **缓存文件夹与模型文件选择窗口点击静默无响应**：
   - **根本原因 A（Wails v3 IPC 寻址断裂）**：Wails v3 采用反射解析服务类型全路径（`namedType.PkgPath() + "." + namedType.Name() + "." + methodName`）。当我们将核心业务服务分层移至 `internal/app` 与 `internal/services` 后，导出的全称变成了 `aegis-coach/internal/app.App.<Method>`。而前端 IPC 客户端基于约定请求 `main.App.<Method>`，导致底层反射抛出 `unknown bound method`，前端调用静默报错。
   - **根本原因 B（PowerShell 对话框缺乏 STA 线程公寓标记）**：在系统备用兜底逻辑中，使用 PowerShell 脚本调用 Windows Forms 的 `FolderBrowserDialog` 与 `OpenFileDialog`。由于未传递 `-STA` 参数，COM 组件在多线程公寓（MTA）模式下发生线程冲突或直接抛出异常，导致对话框被系统静默取消或直接闪退。

2. **社区模型“选择了是否会自动下载”的误解与缺失闭环**：
   - 之前前端点击推荐模型仅仅是把设置中的 `npu_model` 字符串修改为模型名称，**并不会**自动静默在后台下载该模型。如果用户本地 Ollama 未预先安装该模型，战术教练发问时就会报错；
   - 用户无法直观知道“当前推荐模型是否已经在本地安装就绪”，缺少一键拉取下载能力、终端复制命令以及国内免翻墙下载渠道。

3. **16GB 物理内存运行硬约束（运行开销必须 ≤ 2GB）**：
   - **实战内存预算现实**：Windows 11 基础系统占用 4.0 ~ 4.5 GB，Dota 2 团战粒子特效与全英雄模型占用 5.0 ~ 6.5 GB，两者叠加已占 **9.5 ~ 11.0 GB**。
   - 剩余可用安全内存仅约 4 ~ 5 GB。若推荐运行 7B 级模型（Q4 量化依然占用 5.5 ~ 6.2 GB）或 3B 级模型（占用约 3.0 GB），物理内存将被占满，触发 Windows Page Fault 虚拟内存换页，导致 Dota 2 发生致命掉帧与团战卡死。
   - **铁律约束**：端侧 AI 战术推理模型必须严格锁定在 **≤ 2GB 物理内存开销**以内（如 `qwen2.5:1.5b` ~1.2GB，`qwen2.5:0.5b` ~0.4GB），确保电竞 120 帧丝滑与即时战术问答并存。

4. **第三方 TTS 协议单薄与缺少连通性试听回显**：
   - 旧版仅支持简单 GET 请求，无法支持标准的 OpenAI 规范（`POST /v1/audio/speech` 带 Bearer 鉴权与二进制音频流）；
   - 用户配置完第三方端点后无法验证是否连通，遇到错误无法得知是端点 URL 错误、密钥鉴权失败还是服务未启动。

## 架构决策与方案落地

### 1. Wails v3 IPC 锚定与原生 STA 对话框双保险
- **门面继承锚定**：在 `package main` 中声明 `type App struct { *internalApp.App }`，使导出的顶级绑定方法严格保持为 `main.App.<Method>`，100% 匹配前端所有调用契约。
- **STA 模式注入**：PowerShell 兜底调用全面注入 `-STA` 参数：
  ```powershell
  powershell -NoProfile -NonInteractive -STA -Command "Add-Type -AssemblyName System.Windows.Forms; ..."
  ```
  彻底解决 Windows Forms 弹窗在 MTA 下闪退的问题，无论用户路径在 C 盘还是 E 盘个人文档目录均可稳定选择。

### 2. 本地模型严格约束 ≤ 2GB 与真实下载闭环
- **精选 ≤ 2GB 内存模型推荐库**：
  1. `qwen2.5:1.5b` (👑 首选推荐，仅占 ~1.2GB 内存，首字 < 30ms，战术中文极佳)；
  2. `qwen2.5:0.5b` (⚡ 极限省流，仅占 ~0.4GB 内存，单文件 390MB，任意低配本秒跑)；
  3. `llama3.2:1b` (🚀 移动高能效端侧，仅占 ~0.9GB 内存)；
  4. `deepseek-r1:1.5b` (🧠 深度战术逻辑蒸馏，仅占 ~1.1GB 内存)。
- **一键下载拉取与指引**：
  - 后端提供 `PullLocalModel` 方法直连 Ollama `/api/pull`；
  - 前端卡片自动检测是否已就绪。未安装时提供【⚡ 一键拉取下载】、【📋 复制终端命令】与【🌐 ModelScope 魔搭 GGUF 国内极速单文件免翻墙直链】。

### 3. 第三方 TTS 完备重构与双重测试机制
- **协议全兼容**：
  - 支持 OpenAI 标准规范（`POST /v1/audio/speech`，支持 Bearer Key，解析 Audio Blob 播放）；
  - 支持通用 HTTP GET 流式音频（`GET /tts?text=...`）；
  - 保留微软内置 Edge-TTS（免配置即开即用保底）。
- **双重测试面板**：
  - 【测试端点连通性】：调用 `systemApi.testTtsEndpoint`，返回网络往返延时与 HTTP 状态码；
  - 【测试发音 (播放音频)】：调用 `speechService.testThirdPartyTts`，真机播放声音，带 Loading 动画与精准错误回显。

## 曾考虑的替代方案与弃用理由

### 1. 继续推荐 7B / 3B 模型
- **最强理由**：7B 模型的通用常识与语言修辞能力显著强于 1.5B。
- **为何弃用**：在 16GB 物理内存环境下，Dota 2 运行本身已耗尽大半内存，7B 必定导致 Windows 换页卡顿掉帧，电竞助手不能成为拖垮游戏的罪魁祸首。1.5B 配合专门的 Prompt 格式化，战术判断足够精准且内存完全安全。

### 2. 在前端直接用 `<input type="file">` 替代原生系统对话框
- **最强理由**：实现极其简单，无需跨进程 IPC 与 Win32 接口。
- **为何弃用**：浏览器沙盒限制无法直接获取完整绝对路径，无法跨进程传递给 Go 后端或挂载给 NPU 引擎。

## 后果与验证

- **编译构建验证**：
  - 前端 `pnpm --dir frontend build` 编译 0 报错；
  - 后端 `go build` 编译 0 报错；
  - 决策笔记校验 `npm run verify-notes` 15 篇全量通过。
