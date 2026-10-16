# Agent Note: 视觉模型资源全量拆分外置与全仓 UTF-8 无 BOM / Unix LF 规范统一

Status: implemented
Date: 2026-10-15
Class: architecture

## 问题背景与因果机制分析

1. **打包体积膨胀问题**：
   - 此前引入端侧 YOLOv8 赛前选人视觉识别引擎后，`神盾教练.exe` 体积从原先的 ~15MB 膨胀至 77.07MB。
   - 核心因果机制：Wails v3 采用 `//go:embed all:frontend/dist`，而前端构建时包含端侧权重 `models/yolov8n_hero.onnx` (13.6MB) 以及 ONNX Runtime WebAssembly 引擎 `ort/` (42.5MB)，且 Vite 因 `import * as ort from 'onnxruntime-web'` 将 `ort-wasm-simd-threaded.jsep.wasm` 自动重命名又打包了一份至 `assets/` (28.3MB)，使得构建产物被直接嵌入到 exe 内部只读数据段，严重影响了单文件便携性与冷启动内存开销。
2. **编码与换行符不一致隐患**：
   - 经字节流深度审计，全仓 379 个 Git 追踪文本文件 BOM 数量为 0（无隐藏 BOM），但在换行符方面，历史规范配置为 CRLF，而 Wails v3 自动生成的前端绑定、部分 Vue 组件与 Go 模型文件实际输出了 LF（共 57 个纯 LF 文件与 1 个混用文件），导致跨平台协作与工具链出现混合换行警告。

## 决策方案

1. **大模型与推理引擎全部拆分外置（Full Externalization）**：
   - **移出前端打包**：彻底将 `models/` 与 `ort/` 移出 `frontend/public/`，在前端 Vite 打包阶段通过别名指向无内嵌 wasm 的 `onnxruntime-web/dist/ort.min.mjs`，消除 28.3MB 重复 wasm 打包；
   - **独立外部目录规范**：在项目根目录与 `release/` 发行包中统一设置：
     - `release/models/`：存放 `yolov8n_hero.onnx` (13.6MB) 目标检测模型；
     - `release/ort/`：存放 `ort-wasm-simd-threaded.wasm` (14.2MB) 与加载器；
     - 与现有的 `release/lib/` (`onnxruntime.dll`) 和 `release/melo_weights/` (MeloTTS 离线模型) 保持高度统一的外置资源拓扑；
   - **Go 静态资源分发中间件**：在 `cmd/aegis-coach/main.go` 中注册 `externalAssetMiddleware`，当 WebView2 请求 `/models/` 或 `/ort/` 时，优先直接使用 OS 原生高效流式文件读取提供服务（带正确的 `application/wasm` MIME 头），实现对局使用速度 0 损失与模型独立热更新；
   - **Vite 调试代理**：在 `frontend/vite.config.ts` 中注册 `serveExternalAssets` 插件，确保 `pnpm dev` 本地开发与生产构建无缝对齐。
2. **全仓统一 UTF-8 (无 BOM) 与 Unix LF (`\n`)**：
   - 将全仓所有 Git 追踪的文本文件（共 379 个）全量归一化转换为纯 Unix LF (`\n`)，无 BOM；
   - 同步将 `.editorconfig` 设为 `end_of_line = lf`；
   - 同步将 `.gitattributes` 全局和所有文件扩展名设为 `eol=lf`；
   - 同步更新 `AGENTS.md`、`docs/SYSTEM_LANDSCAPE.md` 与 `README.md`，对齐为全仓标准 LF 规范。

## 曾考虑的替代方案

1. **方案 A：仅在构建层去重，依然将 35MB 模型与 Wasm 嵌入单一 Exe**：
   - **其最强理由**：保持物理上“单个可执行文件”的绝对纯粹，分发给用户时只需要一个孤立的 `神盾教练.exe`，无需携带任何附属文件夹。
   - **为什么不用**：
     1) 当前系统后端早已依赖外部目录 `release/lib/`（`onnxruntime.dll` 14MB）和 `release/melo_weights/`（离线语音模型 177MB），用户本身就是解压后双击运行便携绿色套件，追求“仅单个独立 exe”在物理事实上并不成立；
     2) 将几十兆模型塞进 exe 会使主程序冷启动时 Windows PE 内存映射变重，且后续更新 127 英雄识别模型时无法做到单独覆盖单个 `.onnx` 增量补丁，维护成本高。
2. **方案 B：继续维持 Windows CRLF (`\r\n`) 并将 LF 文件强制转回 CRLF**：
   - **其最强理由**：符合原有的 `.gitattributes` 与 `AGENTS.md` 既定规则，完全贴合 Windows 原生开发习惯。
   - **为什么不用**：现代前端生态（Vite、Prettier、Rollup）与 Go 工具链官方标准推荐使用 LF；且 Wails v3 的绑定代码生成器（`wails3 generate bindings`）默认输出即为 LF，每次重新生成绑定都会反复将绑定文件重置为 LF，与 CRLF 强制规则产生持久冲突。统一为 LF 才是符合现代全栈标准的终极解法。
