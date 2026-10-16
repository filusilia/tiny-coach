# Agent Note: Wails v3 beta.30 全局 CLI 对齐与 onnxruntime_purego 兼容性锁定

Status: implemented
Date: 2026-10-16
Class: bug-fix

## 问题背景与因果机制分析

1. **Wails v3 双轨架构版本不一致现象**：
   - 现象：在项目 `go.mod` 中升级 `github.com/wailsapp/wails/v3` 至 `v3.0.0-beta.30` 后，执行打包脚本 `tools/package-release.ps1` 时，控制台在执行 `[1/4] bindings` 时输出的依然是旧版 `Wails v3.0.0-beta.26 › Generate Bindings`。
   - 因果链路机制：
     - Wails v3 采用“宿主系统全局 CLI 二进制程序 + 模块源码 SDK 库”的双轨解耦架构；
     - `go.mod` 仅声明并作用于当前项目（`module aegis-coach`）代码在 `go build` 编译时的包解析依赖（决定最终打包产物内核）；
     - 打包脚本中的 `wails3 generate bindings` 和 `wails3 generate syso` 调用的是系统环境变量中全局安装的独立编译产物（位于 `C:\Users\AkoAko\go\bin\wails3.exe`）；
     - 全局 CLI 工具不会跟随项目的 `go.mod` 变动自动更新，其版本号由编译该 CLI 时的代码版本固化。因此当全局 CLI 停留在 `beta.26` 时，打包脚本打印的便是 CLI 自身的旧版本号。

2. **升级依赖引发的间接传递依赖断裂 (Breaking Change)**：
   - 现象：在将全局依赖更新为最新版本或执行 `go get -u` 类操作时，`github.com/getcharzp/onnxruntime_purego` 被意外从 `v1.24.0` 升级到了 `v1.26.0`。
   - 因果链路机制：
     - `v1.26.0` 的 `onnxruntime_purego` 对 `options.EnableCUDA` 增加了必填参数（由 `EnableCUDA()` 变更为 `EnableCUDA(map[string]string)`）；
     - 本地 ONNX 语音合成依赖的 `github.com/getcharzp/go-speech` 依赖旧版签名；
     - 导致执行 `go build` 时抛出致命错误：`not enough arguments in call to options.EnableCUDA, have (), want (map[string]string)`，破坏了项目构建闭环。

## 决策方案

1. **全局 CLI 工具版本强对齐**：
   - 执行 `go install github.com/wailsapp/wails/v3/cmd/wails3@v3.0.0-beta.30`，将宿主环境全局 `wails3.exe` 与项目 `go.mod` 严格对齐至 `v3.0.0-beta.30`；
   - 消除 AST 语法分析、绑定生成与资源链接潜在的跨版本兼容隐患，确保构建控制台版本输出与底层 SDK 100% 同步。

2. **锁定传递依赖兼容版本**：
   - 在 `go.mod` 中显式固定 `github.com/getcharzp/onnxruntime_purego v1.24.0 // indirect`；
   - 杜绝隐式拉取 `v1.26.0` 导致 `go-speech` 的 CUDA 初始化函数签名断裂，保障原生离线 TTS 引擎稳定编译。

## 曾考虑的替代方案

1. **方案 1：在 package-release.ps1 打包脚本中每次动态调用 `go run github.com/wailsapp/wails/v3/cmd/wails3@v3.0.0-beta.30`**：
   - *最强理由*：无需开发者在系统环境手动维护全局 CLI，脚本自动按版本拉取并执行，绝对不会出现版本不一致。
   - *为什么不用*：`go run` 每次执行都会经历网络元数据检查或临时缓存编译，导致生成 bindings 和 syso 的速度从毫秒级暴跌至数秒甚至网络超时；此外，对于离线环境完全不可行。因此采用“预先安装强对齐 CLI + 脚本优雅检测调用”是最佳工程实践。
2. **方案 2：修改 go-speech 库源码以适配 onnxruntime_purego v1.26.0 的新签名**：
   - *最强理由*：能跟随依赖库始终使用最新版本。
   - *为什么不用*：`go-speech` 属于第三方开源库，且神盾教练定位为纯 CPU/NPU 本地离线秒级运行，无需向 ONNX 传入 CUDA 动态配置字典；锁定兼容稳定的 `v1.24.0` 是破坏性最小、零运行时风险的最优解。

## 后果与验证计划

- **收益**：
  - `wails3 version` 确认输出 `v3.0.0-beta.30`，与 `go.mod` 保持严格一致；
  - 打包脚本 `package-release.ps1` 5 大构建与安全加固阶段全量跑通，单文件便携客户端 `release/神盾教练.exe` 成功产出（体积 23.41 MB）；
  - `npm run verify-notes` 验证决策笔记 100% 格式合规。
