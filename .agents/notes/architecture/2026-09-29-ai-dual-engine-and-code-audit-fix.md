# Agent Note: AI 双引擎分发架构 (Ollama 与 Remote Response API)、设置页预设去污染与内核审查修复

Status: implemented
Date: 2026-09-29
Class: architecture

## 问题背景与因果机制分析

1. **AI 单管道架构与不同协议不兼容**：
   - 原 `ai_service.go` 采用单一管道设计，无论云端还是本地一律走 `/chat/completions`；
   - 然而在现实标准中，本地 Ollama 引擎主要走兼容 OpenAI 的 `/chat/completions`，而远程规范大模型（OpenAI 2025/2026 规范标准）则主要走 `/responses` 协议，输出结构体存在 `output_text` 与 `output[]` 等差异；
   - 依赖 `isLocalEndpoint` 简单判定不仅脆弱，还导致模式语义混淆。

2. **设置页存在自作主张的硬编码预设**：
   - 前端 `AiEngineSection.vue` 之前硬编码了若干预设快捷项（如特定的端口和测试 Key），属于未经用户批准的自作主张行为，污染了设置界面的纯净性；
   - 用户明确要求彻底删除预设按钮，将 AI 严格区分为：本地 Ollama 引擎、远程 Response API 引擎与纯本地规则引擎。

3. **内核代码审查缺陷闭环**：
   - `melo_engine.go`：此前虽有 `Destroy()` 逻辑，但未对外暴露 `Close()` 实现 `TtsEngine` 接口，导致 C 侧 ONNX Runtime Session 无法显式清理；
   - `services_test.go`：原 `TestNormalizeOpenAiBaseUrlChatCompletions` 测试与重构后的 `normalizeBaseUrl` 契约不一致；
   - `system_service.go`：默认设置落盘包含已废弃的冗余旧字段。

## 决策方案

1. **AI 双引擎分发设计 (Dual Engine Architecture)**：
   - 抽取 `AiEngine` 统一接口（[ai_engine.go](file:///D:/develop/demo/internal/services/ai_engine.go)），定义 `Ping`、`Complete` 与 `SetCredentials`；
   - 实现 `OllamaEngine`（[ai_engine_ollama.go](file:///D:/develop/demo/internal/services/ai_engine_ollama.go)）：面向本地服务，走 `/chat/completions`，解析 `choices[0].message.content`；
   - 实现 `RemoteEngine`（[ai_engine_remote.go](file:///D:/develop/demo/internal/services/ai_engine_remote.go)）：面向远程服务，走 `/responses` 协议，解析 `output_text` 与 `output`；
   - `AiService`（[ai_service.go](file:///D:/develop/demo/internal/services/ai_service.go)）根据 `mode`（`ollama` / `remote` / `rule_only`）精准调度对应引擎实例，删除 `isLocalEndpoint` 猜测逻辑。
   - [app.go](file:///D:/develop/demo/internal/app/app.go) 与 [main.go](file:///D:/develop/demo/main.go) 新增 `UpdateAiCredentials` 门面。

2. **默认设置与持久化收敛**：
   - 在 [system_service.go](file:///D:/develop/demo/internal/services/system_service.go) 中将默认 `ai_mode` 收敛为 `ollama`，默认端点为 `http://127.0.0.1:11434/v1`，默认模型为 `qwen3.5:1.5b`；
   - 彻底删除 `npu_endpoint`、`npu_model`、`cloud_api_key`、`cloud_base_url`、`cloud_model` 五组历史冗余字段。

3. **前端设置界面彻底纯净重构**：
   - 在 [AiEngineSection.vue](file:///D:/develop/demo/frontend/src/views/settings/sections/AiEngineSection.vue) 中彻底移除全部预设药丸按钮；
   - 模式三选一：本地 Ollama、远程 Response API、纯规则引擎；各面板按需暴露必要输入框与模型拉取/探测按钮。

4. **TTS 与服务生命周期闭环**：
   - `MeloTtsEngine` 显式提供 `Close() error`，在生命周期结束时调用底层 `m.engine.Destroy()` 彻底释放 ONNX Runtime C 侧资源。
