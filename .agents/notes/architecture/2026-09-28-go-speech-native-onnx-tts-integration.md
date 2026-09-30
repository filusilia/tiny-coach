# Agent Note: 基于 ModelScope go-speech 的 Go 原生端侧离线 TTS 引擎接入决策笔记

Status: implemented
Date: 2026-09-28
Class: architecture

## 问题背景与因果机制分析

在神盾教练原有的战术语音播报体系中，语音输出依赖两大通道：
1. **系统在线 Web Speech API (Edge-TTS)**：虽然免配置开箱即用，但强依赖 Windows Edge 在线合成连接与公网质量；在弱网、丢包或网络波动时可能出现首字延迟或偶发无声；
2. **第三方外部 HTTP TTS (如 OpenAI 规范、CosyVoice、ChatTTS)**：虽然支持高质量拟真，但要求用户在本地独立部署复杂的 Python/Torch 运行环境、独立开启额外的后台进程并监听本地端口（如 9233、9966）。这带来了两大严重痛点：
   - **内存与电竞帧率风险**：基于 Python/Torch 的 TTS 进程动辄占用 1.5GB ~ 3GB 显存或物理内存，叠加 Dota 2 本身 6GB+ 内存，极易击穿 16GB 物理内存红线导致掉帧；
   - **部署门槛极高**：绝大多数普通玩家无法顺利配置 Python 环境、CUDA 驱动与 HuggingFace 下载通道。

为实现真正的**“零 Python 依赖、零外部进程、毫秒级响应、完全离线”**的高清电竞解说发音，本项目决定正式引入并接入 ModelScope 开源的 [`getcharzp/go-speech`](https://www.modelscope.cn/models/getcharzp/go-speech) 原生 Go 语音库。

## 架构拓扑与因果链路机制

```
+--------------------------------------------------------------------------+
| 前端电竞 HUD / 战术播报 (Vue 3 / TypeScript)                             |
|  - TtsEngineSection: 状态感应卡片 (展示 ONNX dll 状态、模型就绪状态、进度) |
|  - speechService: 优先走 go-speech 原生流，失败优雅降级至系统内置 Edge   |
+--------------------------------------------------------------------------+
                                    | Wails v3 强类型绑定
                                    v
+--------------------------------------------------------------------------+
| 后端 Go 门面与原生服务层 (internal/services/tts_service.go)               |
|  - GetGoSpeechStatus(): 探测 lib/onnxruntime.dll 与模型权重齐全度        |
|  - DownloadGoSpeechModel(): 直连 ModelScope 国内镜像免翻墙高速多线程下载  |
|  - SynthesizeSpeech(): 线程安全会话复用，将战术文本推理生成 WAV 二进制    |
+--------------------------------------------------------------------------+
                                    | purego 系统调用 (零 CGO 编译依赖)
                                    v
+--------------------------------------------------------------------------+
| 运行时与模型硬件加速层 (本地环境)                                        |
|  - lib/onnxruntime.dll (14.18MB，Microsoft 官方预编译运行时)             |
|  - models/pipertts (zh_CN-xiao_ya-medium.onnx 60MB + json，极速纯中文)   |
|  - models/melotts (model.onnx 170MB + lexicon + tokens，高质量中英混合)  |
+--------------------------------------------------------------------------+
```

1. **零 CGO 的 PureGo 动态绑定**：
   - `go-speech` 底层通过 `github.com/getcharzp/onnxruntime_purego` 和 `ebitengine/purego` 实现动态加载 Windows `onnxruntime.dll`，无需安装 MinGW/GCC 编译器，彻底保护了 Go 原生极速编译和一键免安装便携发布的工程闭环。
2. **双模型按需矩阵**：
   - **Piper (推荐首选)**：晓雅中文音色，单模型仅 60.3MB，内存常驻 < 120MB，端到端合成延迟 < 40ms，完美匹配电竞即时战术告警；
   - **MeloTTS**：中英混合合成，模型体积 170MB，发音饱满自然。
3. **ModelScope 国内镜像源一键高速拉取**：
   - 后端直连阿里云 ModelScope 极速下载直链（`https://www.modelscope.cn/models/getcharzp/go-speech/resolve/master/...`），提供断点检测与实时进度广播（`app.Event.Emit("tts:download_progress", ...)`），用户无需繁琐的命令行或魔法代理，界面一键拉取即刻启用。
4. **单例会话缓存与 Base64 流转**：
   - ONNX 推理会话在首次使用时初始化并常驻复用，避免每次合成重复加载几十兆模型；
   - 输出以 Base64 Data URL (`data:audio/wav;base64,...`) 形式返回，前端 `new Audio(url).play()` 即点即播，零临时文件落盘与并发文件锁风险。

## 曾考虑的替代方案与弃用理由

### 1. 继续通过 HTTP 接入外部 CosyVoice / ChatTTS 服务
- **最强理由**：CosyVoice 2.0 等大模型的音色逼真度和拟人情感表现力处于顶尖水平。
- **为何不用**：部署成本高昂（需数 GB Python 环境、GPU 显卡与 PyTorch），在普通游戏电脑上运行会严重挤占 Dota 2 显存与 CPU 资源，违反项目“轻量级辅助助手、绝不影响游戏帧率”的第一原则。

### 2. 使用 CGO 编译原生 C++ onnxruntime SDK
- **最强理由**：CGO 理论性能更贴近底层 C/C++，兼容性更广。
- **为何不用**：Windows 下引入 CGO 会严重劣化构建工具链，要求必须安装配置 MinGW-w64 GCC，使得 GitHub Actions CI 及本地一键打包脚本（`package-release.ps1`）复杂度成倍上升。`purego` 经过验证性能完全满足实时语音合成，且完全摆脱 CGO 依赖。

## 验证与测试策略

1. **动态库载入验证**：编写探测逻辑确认 `lib/onnxruntime.dll` 正常初始化 ONNX Engine 与 SessionOptions；
2. **模型状态与下载流程测试**：验证 ModelScope API 直链文件拉取与进度事件推送；
3. **合成与回放测试**：实测文本合成为规范 WAV 格式，验证采样率与音质无破音；
4. **降级与防崩溃守卫**：当缺少 dll 或模型未就绪时，安全报错并不阻断界面，前端平滑降级至 Edge-TTS。
