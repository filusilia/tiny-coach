# Agent Note: 纯 MeloTTS 离线语音架构收敛、Edge TTS 剥离与线程安全串行化合成

Status: implemented
Date: 2026-09-29
Class: architecture

## 问题背景与因果机制分析

在神盾教练早期版本中，语音系统设计了基于抽象引擎接口的双轨制策略：默认使用在线 Edge TTS 云端合成，并提供本地 MeloTTS 离线引擎作为进阶选项。在实际演进与代码审计中，暴露了以下核心架构瓶颈：

1. **架构冗余与网络不可控性**：
   - Edge TTS 严重依赖未经公开的微软云端端点与 WebSocket 握手机制，在高频对局提醒场景下，容易遭遇网络丢包、延迟抖动、跨国路由限流甚至封禁，严重破坏电竞播报的毫秒级时效要求。
   - 为了应对在线引擎的脆弱性，前端代码不得不引入多重降级与复杂的重试状态机（如云端失败降级为系统 Web Speech API），导致播报音色频繁突变、音量忽大忽小，代码维护成本剧增。

2. **并发非安全与 ONNX Runtime 崩溃风险**：
   - 本地 MeloTTS 依赖 `github.com/getcharzp/go-speech` 调用底层 `onnxruntime.dll` 的 C-API 进行神经网络推理。
   - ONNX Runtime 的 `OrtSession` 实例在多线程/多 Goroutine 乱序并发调用 `Run` 时并非并发安全。如果对局中多个高危战况（如肉山刷新、买活警告、团灭预警）在毫秒级内同时并发触发语音合成，极易导致底层 C++ 内存越界或段错误崩溃。

3. **模型文件体积大与非原子下载隐患**：
   - MeloTTS ONNX 模型及其词表文件合计约 150MB。原有的下载机制缺乏临时文件隔离机制，若用户在下载过程中退出应用或遭遇网络异常，磁盘会残留不完整的半截损坏模型文件，导致后续每次启动尝试加载时均发生崩溃报错。

4. **重复 I/O 探测损耗**：
   - 之前的实现中，每次发起单句文本合成都要对 `onnxruntime.dll`、`model.onnx`、`lexicon.txt`、`tokens.txt` 执行全量磁盘 `os.Stat` 探测，带来了无意义的文件系统 I/O 消耗。

## 决策方案

经全面研判，决定执行**纯 MeloTTS 本地原生离线架构**，彻底剥离 Edge TTS 相关代码与依赖，同时保留极简的 `TtsEngine` 接口以备未来扩展。

### 1. 架构拓扑收敛与接口精简

```
┌────────────────────────────────────────┐
│           Wails App 门面               │
│   SynthesizeSpeech / GetTtsStatus      │
└──────────────────┬─────────────────────┘
                   │
┌──────────────────▼─────────────────────┐
│              TtsService                │
│  - 持有单一 MeloTtsEngine 实例          │
│  - 状态快照汇聚 / 模型并发下载互斥保护  │
└──────────────────┬─────────────────────┘
                   │
┌──────────────────▼─────────────────────┐
│            MeloTtsEngine               │
│  - 路径探测缓存 (sync.Once)             │
│  - 引擎懒加载与 C-API Session 串行锁    │
│  - .part 临时文件带重试原子下载与进度推送│
└────────────────────────────────────────┘
```

- 保留精简的 `TtsEngine` 接口（`Name`、`Synthesize`、`IsReady`、`Status`、`Close`）与 `EngineStatus` 实体，为后续潜在接入本地大模型 TTS（如 GPT-SoVITS）预留扩展槽位，但消除无意义的运行时引擎切换逻辑。

### 2. 线程安全串行化与路径缓存

- 在 `MeloTtsEngine` 中使用互斥锁 `m.mu.Lock()` 严格包裹推理全流程，对 `m.engine.SynthesizeToWav` 实施串行化保护，彻底杜绝并发推理引发的底层崩溃。
- 引入 `pathsOnce sync.Once` 对 DLL 和权重目录的查找结果进行常驻内存缓存；在后台下载完成或手动刷新时，通过 `RefreshPaths()` 重置 `pathsOnce` 并安全释放旧引擎句柄。
- 支持 `MODELS_DIR` 环境变量与 `storage.Manager` 统一缓存目录，确保开发环境、独立发布包与自定义缓存路径无缝适配。

### 3. 原子下载与全双工进度推送

- 后台下载权重与 DLL 时，强制先写入 `.part` 临时文件，下载成功并通过校验后再调用 `os.Rename` 原子覆盖目标路径，彻底杜绝半成品破损文件。
- 下载循环内精确计算并防抖推送进度至 Wails 全局事件总线 `tts:download-progress`，供前端状态条实时呈现百分比。

### 4. 前端全栈去 Edge 与播放链路精简

- **API 层**：`frontend/src/api/tts.ts` 明确暴露 `synthesizeSpeech` 与 `getTtsStatus`。
- **播报服务**：重构 `frontend/src/services/speechService.ts`，专注处理电竞黑话规范化预处理（`normalizeTacticalSpeech`）、Audio 实例单例复用、防堆叠静音与倍速/音量控制，彻底剔除 Edge 依赖及 Web Speech 降级代码。
- **设置与交互**：
  - `TtsEngineSection.vue` 与 `TtsTab.vue` 重塑为纯 MeloTTS 状态监控面板，直观展现 ONNX Runtime、模型文件就绪状态，提供一键下载与试听体验。
  - 清理 `SettingsView.vue`、`VoiceBroadcastBar.vue`、`GsiSimulator.vue`、`gsiRuleEngine.ts` 等各处的 Edge 残留设置项。

## 曾考虑的替代方案

1. **保留 Edge TTS 作为模型未下载时的临时在线降级方案**：
   - *最强理由*：在首次启动或模型下载过程中，用户仍可听到在线合成的声音，避免冷启动阶段的静音空窗期。
   - *否决原因*：
     1. Edge TTS 云端服务协议极不稳定，一旦被限流将直接报错甚至卡死，带来极差的用户预期；
     2. 保持双引擎使得前端需要维持两套音色配置、测试接口与错误处理分支，背离了代码精简与纯离线低延迟的核心初衷；
     3. 提示用户一键下载 150MB 模型是一次性且快速的操作，本地纯离线运行才能保证绝佳的电竞抗干扰性。

2. **由前端浏览器调用 Fetch 流式下载模型再写入文件**：
   - *最强理由*：前端有成熟的 Fetch 流式 API，界面进度条绑定更直接。
   - *否决原因*：跨 IPC 往后端传递数百兆的二进制数据块会带来巨额内存开销和 GC 停顿，极易诱发渲染进程卡顿；Go 后端原生通过 `http.Get` 配合缓冲区流式写入磁盘，效率更高且完全不干扰前端渲染。

## 验证与影响面

1. **编译与静态检查**：`go vet ./internal/...` 零报错；`go test -v ./internal/services` 全量测试用例通过。
2. **前后端类型契约**：前端 TypeScript 严格对齐 Go 结构体，无任何旧字段类型报错。
3. **安全闭环**：ONNX C-API 推理全流程互斥锁串行化护航，资源退出时调用 `Close()` 释放动态链接库句柄，杜绝句柄泄漏与崩溃隐患。
