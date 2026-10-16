# ONNX Runtime WebAssembly 运行时穿透修复与教练复盘视觉重构决策笔记

## 1. 产生背景 (Context)

在神盾教练 2.0 运行视觉抓取与端侧目标检测（基于 ONNX Runtime Web 推理 YOLOv8n 模型）时，前端在调用手动抓取画面时报错：
```text
no available backend found. ERR:[wasm] localhost/ort/ort-wasm-simd-threaded.jsep.mjs
```
该错误阻断了端侧视觉识别流程。
同时，用户提出对实战教练视图与赛后分析视图的深度用户体验优化需求：
1. **实时教练页面（CoachView）**：移除物理屏幕、ROI 裁剪、GDI 截屏等底层调试信息；面板标题由“BP 视觉感知与智能克制参谋”改名为“对战识别”，去除长文本介绍；全量重排文字层次，杜绝字号过小或过大；彻底移除实时战术中的提问与对话交互，全面转变为纯自主的“AI 主动战术决策”。
2. **赛后分析页面（AnalysisView）**：将常用高频英雄面板合并吸纳至顶部的玩家资料大盘卡片中；适度紧凑收敛顶部个人卡片高度；在近期比赛列表中，微调缩小“胜利/失败”状态徽章，稍微放大英雄名称，强化选手一眼识别效率。
3. **小地图态势感知 U-Net 接入论证**：向用户系统汇报 `dota2-mentor-pack` 中小地图 U-Net 模型的网络结构、输入输出契约及在本仓库中的落地技术路径。

---

## 2. 根因剖析 (Problem Analysis & Root Cause)

### 2.1 ONNX Runtime Web WASM 后端加载失败机制
1. **Windows 路径分隔符篡改导致静态拦截穿透失败**：
   在 `cmd/aegis-coach/main.go` 中的 `externalAssetMiddleware` 中，使用了 `reqPath := filepath.Clean(r.URL.Path)`。在 Windows 系统上，`filepath.Clean` 会将正斜杠路径转换为 Windows 反斜杠（如 `/ort/ort-wasm-simd-threaded.jsep.mjs` 转换为 `\ort\ort-wasm-simd-threaded.jsep.mjs`）。
   随后的前缀判断 `strings.HasPrefix(reqPath, "/ort/")` 在 Windows 下恒等于 `false`。这导致原本外置在实体文件目录的 `ort/*.mjs` 和 `ort/*.wasm` 请求全部穿透到了只含前端静态代码的嵌入式文件系统，最终返回 404 Not Found。
2. **跨域隔离头（COOP / COEP）缺失导致 Wasm 多线程与共享内存受阻**：
   `onnxruntime-web` 1.30.0 采用 `ort-wasm-simd-threaded.jsep.mjs` 作为 WebAssembly 推理运行模块时，顶层代码尝试探测 `SharedArrayBuffer` 与 `WebAssembly.Memory({ shared: true })`。若 WebView2 / Vite 服务未配置 `Cross-Origin-Opener-Policy: same-origin` 与 `Cross-Origin-Embedder-Policy: require-corp`，浏览器在无隔离环境下会抛出严重安全异常，导致动态模块 `import()` 失败。
3. **WASM 路径相对基准解析隐患**：
   在前端配置 `ort.env.wasm.wasmPaths = '/ort/'` 时，底层 `normalizeUrl` 在执行 `new URL(filename, baseUrl)` 时，因传入的是相对路径而非绝对 URL（如带 `http://`），导致 URL 构造器在部分内核中抛出 `TypeError: Invalid base URL`，进而退化到错误的请求路径。

---

## 3. 架构决策与实施落地 (Decisions & Implementation)

### 3.1 后端资源中间件与跨域头加固
1. 在 `cmd/aegis-coach/main.go` 中，将 URL 路径清洗从 `filepath.Clean` 切换为标准 `path.Clean`（或 `filepath.ToSlash`），确保 URL 路径在任何平台始终保持正斜杠前缀 `/`，严密命中 `/models/`、`/ort/`、`/audio/`。
2. 在 `externalAssetMiddleware` 以及 `vite.config.ts` 中显式注入跨域隔离响应头：
   - `Cross-Origin-Opener-Policy: same-origin`
   - `Cross-Origin-Embedder-Policy: require-corp`
   - `Access-Control-Allow-Origin: *`
3. 确保 `.mjs` 和 `.wasm` 文件具备标准的 `application/javascript` 与 `application/wasm` MIME 标识。

### 3.2 端侧 ONNX Runtime Web 路径标准化
1. 在 `heroDetector.ts` 中，使用 `window.location.origin + '/ort/'` 作为规范绝对基准路径赋值给 `ort.env.wasm.wasmPaths`。
2. 显式声明 `ort.env.wasm.numThreads = 1` 与 `ort.env.wasm.proxy = false`，杜绝后台 Worker 动态生成的资源争用与死锁风险。

### 3.3 实时教练页面（CoachView）纯净重构
1. **更名为对战识别**：`BpVisionCaptureCard.vue` 标题更新为“对战识别”，彻底移除繁杂冗长的 BP 克制宣导标语。
2. **剔除物理屏幕参数**：移除“物理屏幕”、“ROI 裁剪”、“GDI 截屏”等对实战无价值的底层参数，仅保留最简性能指标。
3. **文字规范化（Clarity Standards）**：剔除微小的 `text-2xs`、`text-[10px]`、`text-3xs`，主视图文字以 `text-xs` (12px) 与 `text-sm` (14px) 黄金尺度呈现，保证电竞高频瞥视时的可读性。
4. **移除提问对话，转为 AI 主动战术决策**：
   - 彻底删除用户提问输入框、预置求助按钮、发送按钮与相关交互逻辑；
   - 将整块战术协同区统一命名为“AI 主动战术决策”，由 10Hz GSI 毫秒快照规则引擎与 Solution Hierarchy 思维链主动裁定推送。

### 3.4 赛后分析页面（AnalysisView）紧凑一体化
1. **卡片融合**：将 `TopHeroesPanel` 高频英雄池无缝合并进顶部的 `PlayerProfileBanner`，形成“选手身份 + 战术大盘 + 常用英雄池 + 五维雷达”单卡片全景。
2. **尺寸收敛**：优化顶部资料卡片内部间距与头像规格，减少视口占用，为下方的天梯比赛列表提供更充裕的纵向空间。
3. **文字微调**：
   - 比赛卡片中的英雄中文名稍微放大至 `text-sm font-extrabold`；
   - 比赛卡片中的“胜利 / 失败”标签文字稍微收缩为 `text-[10px] leading-tight px-1.5 py-0.5`，形成精致微型状态徽章。

---

## 4. 被否决的备选方案与强辩驳 (Discarded Alternatives)

### 方案 A：降级使用 CPU 原生 C++ DLL / Go 侧推理目标检测
- **最强理由**：Go 后端可直接调用 OnnxRuntime C-API，无需处理前端浏览器环境下的 WebAssembly、SharedArrayBuffer 与 JSEP MIME 加载问题，架构更简单。
- **否决原因**：Go 侧引入 ONNX C-API 会强依赖 `onnxruntime.dll`（单动态库体积超 150MB，严重违反 23MB 极轻发布包底线），且在多平台（如 Win10/Win11 ARM/NPU）编译分发极度复杂。相比之下，端侧 WebAssembly 运行时纯外置且仅 14MB~28MB，对跨平台与沙箱安全最具优势。

### 方案 B：在实时教练中保留最小化的折叠对话抽屉
- **最强理由**：部分高级玩家在选人或挂机思考时，可能仍有输入复杂出装疑问的诉求，折叠起来不占地方。
- **否决原因**：Dota 2 实战 APM 高达 200~300，实操期间玩家根本没有余力打字提问；真正的电竞辅助必须由系统毫秒级感知局势后主动“推送”决策，用户输入纯属伪需求，保留只会增加状态维护复杂度与 UI 视觉噪点。

---

## 5. 验证与测试策略 (Verification)
1. 运行 `pnpm run typecheck` 验证前端 TypeScript 类型闭环；
2. 运行 `pnpm run build` 验证前端生产构建包；
3. 运行 `pwsh ./tools/package-release.ps1` 验证一键打包与外部资源完整性；
4. 运行 `npm run verify-notes` 确保决策笔记格式规范通过。
