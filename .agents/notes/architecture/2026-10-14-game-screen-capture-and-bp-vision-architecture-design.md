# Agent Note: 游戏原生截图与 BP 阶段视觉辅助架构规划

Status: implemented
Date: 2026-10-14
Class: architecture

## 问题背景与因果机制分析

1. **BP 阶段状态盲区痛点**：
   神盾教练现行架构高度依赖 Valve 官方 Game State Integration (GSI) 4444 端口提供 10Hz 实时状态。然而，Valve 为防止对局作弊与自动化脚本，在游戏正式开始前的 **BP（挑选/禁用英雄）阶段，GSI 协议坚决不向客户端广播对手已选定的英雄信息**。这导致神盾教练在整个 BP 关键期处于“视界盲区”，玩家必须脱离当前游戏界面，在教练端手动逐一检索并点击选择对手的 5 名英雄。在倒计时紧张的选人阶段，这一操作极易延误对位战术克制与出装决策的最佳推导时机。

2. **合规红线与技术约束**：
   根据本项目开工铁律第 7 条（合规红线与非外挂原则），系统严禁使用任何形式的 DLL 注入、内存扫描读取、DirectX/Vulkan 渲染管线 Hook 或模拟键鼠操作。因此，**无侵入式的外挂级旁路屏幕画面捕获与端侧视觉分析**是突破 BP 阶段数据盲区的唯一合规合法路径。

3. **性能与延迟挑战**：
   电竞玩家对于后台辅助工具的性能损耗极其敏感。若每次捕获全屏 2K/4K 甚至带鱼屏画面并进行未经优化的编码传输，将消耗数十毫秒并瞬时占用大量 CPU/内存，造成对局掉帧；同时必须兼顾无边框窗口模式与多显示器 DPI 缩放适配。

---

## 决策方案

经权衡与论证，确立**“Go 原生 Win32 GDI ROI 局部截图 + Wails v3 事件中继 + 前端 ONNX Runtime Web 端侧推理”**的分层架构：

### 1. 架构拓扑与职责边界
```text
[ 用户按下全局快捷键 F10 ]
         │
         ▼
[ Go 后端 VisionService (internal/services/vision) ]
  1. 调用 Win32 GDI 原生 BitBlt 仅截取屏幕顶部 18% ROI 区域 (针对 10 英雄槽位)
  2. 极速内存压缩为 WebP/JPEG Base64 格式 (耗时 < 8ms)
  3. 通过 Wails v3 app.Event.Emit("vision:capture-completed", payload) 推送
         │
         ▼
[ 前端 Vue 3 / Pinia 状态层 ]
  1. 接收截图数据，送入 @microsoft/onnxruntime-web
  2. 加载轻量化 yolov8n_hero.onnx 模型进行端侧目标检测
  3. 结合 slot-overrides.json 多分辨率槽位坐标匹配双方 10 英雄
  4. 自动填充阵容推荐 Store，触发已有的 OpenDota 对抗推荐流水线
```

### 2. 核心技术契约与实现细节
* **服务注册与门面**：
  在 Go 端建立 `VisionService`，采用 `application.NewService()` 注册为标准 Wails v3 服务，并在 `main.App` 显式转发 `CaptureTopScreen()`、`RegisterVisionHotkey()`，通过 `wails3 generate bindings` 输出强类型绑定。
* **ROI 局部截取优化（Region of Interest）**：
  DOTA 2 双方已选英雄头像统一排列在屏幕顶部居中区域。截取范围限制为 `(0, 0, screenWidth, screenHeight * 0.18)`，使像素处理量减少 82% 以上，内存开销从全屏 ~30MB 降低至 ~5MB，大幅消除 GC 压力。
* **错误处理与取消语义统一**：
  若玩家未运行游戏或截屏失败，Go 端返回规范的错误提示，前端捕获后以 Toast 提示“未检测到游戏画面”，禁止静默失败或抛出未捕获 Promise rejection。

---

## 曾考虑的替代方案

### 替代方案一：前端调用 WebRTC 标准 `navigator.mediaDevices.getDisplayMedia`
* **最强理由**：纯浏览器原生 Web API，无需在 Go 后端编写任何 Windows 平台调用，前端具备完全的自主控制权。
* **为什么不用**：
  1. **系统弹窗干扰**：浏览器标准规定调用 `getDisplayMedia` 时必须强制弹出系统模态窗口让用户确认“选择共享哪块屏幕”，在紧张的电竞倒计时中每一次选人都弹窗属于灾难级用户体验。
  2. **无法监听后台全局热键**：当玩家处于 DOTA 2 游戏全屏窗口中时，前端 WebView 失去焦点，无法捕获页面内的键盘按键，玩家无法通过盲按 `F10` 一键静默截图。

### 替代方案二：直接在 Go 后端通过 CGO 链接 `onnxruntime.dll`
* **最强理由**：推理计算完全留在 Go 后端独立 goroutine 执行，计算过程完全不占用前端 UI 渲染线程。
* **为什么不用**：
  1. **破坏极简构建链**：引入 CGO 将导致跨平台编译复杂化，必须在宿主机安装指定版本的 MSVC/MinGW 和庞大的 C++ 运行库，严重违背当前纯 Go 静态编译与快速构建基线。
  2. **发布包体积膨胀**：C++ ONNX Runtime 的动态库随附将增加几十兆甚至上百兆体积。相比之下，前端 WebView2 自带 WebAssembly 和 WebGPU 引擎，只需轻量的 `@microsoft/onnxruntime-web` npm 依赖即可跑通模型。

### 替代方案三：沿用外部项目做法，启动外部 Node.js/Python 进程做截图识别
* **最强理由**：外部项目的 Node 脚本已经经过实际验证，现成代码最多。
* **为什么不用**：
  1. **严重违背轻量化铁律**：随附便携版 Node/Python 会使整个应用体积暴增至 300MB~1GB，内存占用增加 10 倍。
  2. **进程生命周期风险**：外部子进程容易引发僵尸进程驻留和句柄竞争，不符合现代桌面软件工程标准。

---

## 后果与验证计划

### 正面收益
1. 彻底攻克 BP 选人阶段的数据空白，构建“F10 一键截图 -> 双方英雄自动识别 -> 毫秒级克制出装与对位推导”的完整体验。
2. 保持纯 Go + Wasm 架构，二进制体积与内存占用维持极致克制。

### 潜在风险与约束
1. **独占全屏黑屏风险**：Windows GDI `BitBlt` 在极少数“独占全屏（Exclusive Fullscreen）”下可能抓取到黑屏。需在 UI 处给出明确的指引：“建议将游戏显示模式设置为【无边框窗口模式】”。
2. **多显示器与高 DPI 缩放**：在多屏环境与 125%/150% DPI 缩放下，必须显式调用 Win32 `SetProcessDpiAwarenessContext` 确保获取正确的真实物理像素坐标。

### 验证计划
1. **单元测试与集成测试**：编写 Go 端截屏基准测试（Benchmark），验证顶部 ROI 截图在 1080P/2K 下的耗时是否稳定在 10ms 以内。
2. **前端模拟测试**：提供静态图片测试管道，验证不同分辨率（16:9 与 21:9）下槽位裁剪与模型输出的准确率。
3. **全量构建验证**：执行 `pnpm build` 与 `verify-notes` 确保零告警通过。

---

## 落地实现与踩坑沉淀 (2026-10-14 更新)

1. **Windows GDI 64位句柄符号扩展陷阱**：
   在 Windows x64 系统中，GDI 函数（如 `GetDC`、`CreateCompatibleDC`、`CreateCompatibleBitmap`）返回的是 32 位句柄值（放于 RAX 低 32 位）。若句柄最高位为 1，Go 的 `Syscall.Call` 会把整型返回值符号扩展为负数（例如 `0xffffffff8e012e54`），直接作为 `uintptr` 传给后续 GDI 函数时会导致 Windows 内核报 `The handle is invalid`。解决方案：必须使用 `uintptr(uint32(hDC))` 严格截断符号位，消除高位脏数据。
2. **锁屏/睡眠待机受控错误保护**：
   当操作系统处于锁屏保护（Win+L）或显示器进入节能休眠状态时，DWM 会断开物理显示表面，`BitBlt` 会正常报错。系统按照开工铁律第 7 条规范返回 `CaptureResult{Success: false, ErrorMessage: ...}`，严禁抛出 panic 导致后端崩溃。
3. **BGRA 与 RGBA 字节流通道反转**：
   Win32 `GetDIBits` 在 32bpp 下输出的像素排布默认为 BGRA。若直接送入后续模型或 Canvas 渲染，会出现红蓝反转问题（如蓝胖变红胖）。底层在复制阶段高效原地交换 `row[x]` 与 `row[x+2]`，实现零成本 BGRA->RGBA 转换。
4. **消息循环与 OS 线程绑定**：
   Win32 全局热键必须在注册它的同一个 OS 线程的消息队列中派发。若 Go 运行时调度器将 Goroutine 迁往其他物理线程，`GetMessageW` 将永远无法接收到 `WM_HOTKEY` 消息。在热键监听循环中显式调用 `runtime.LockOSThread()` 彻底杜绝漏消息隐患。
5. **分层闭环与生命周期防御**：
   前端在 `BpVisionCaptureCard.vue` 挂载时监听 `vision:capture-completed`，组件卸载时调用 `unlisten` 彻底注销，杜绝事件与内存泄漏。
6. **虚拟全屏 Letterbox 几何坐标还原算法**：
   为避免对局时全屏 4K/2K 截图带来的巨大开销，截图底层只捕获顶部 18% ROI 画面。但在 640×640 预处理时，若简单拉伸顶部图片，会导致模型输入的空间几何关系与全屏训练样本产生根本性偏离。通过以全屏物理尺寸 `(screenWidth, screenHeight)` 计算全局等比 `scale = Math.min(640/W, 640/H)` 与居中偏移 `(padX, padY)`，将顶部图像精确贴合在 `(padX, padY)` 并在 `rgb(114,114,114)` 灰色画布上展开，输出检测框直接反算回真实全屏绝对像素，实现与 `slot-overrides.json` 1:1 无损槽位判定。
7. **ONNX Runtime Web 静态路径与线程安全**：
   在 Vite SPA 架构中，Wasm 二进制如果未配置绝对静态路径，路由跳转或 fallback 会返回 `index.html` 导致 Wasm 报非法魔数错误。通过配置 `ort.env.wasm.wasmPaths = '/ort/'` 与 `numThreads = 1`，并在 `public/ort` 预置运行时，既杜绝了路由回退，又规避了 Windows WebView2 默认缺少 `SharedArrayBuffer` 跨域隔离引发的初始化崩溃。
8. **127 英雄类别 100% 字典闭环**：
   针对 `hero_classes.txt` 与游戏显示短名的两处官方历史别名（`obsidian_destroyer` -> 76 殁境神蚀者，`rattletrap` -> 51 发条技师）建立双向映射索引，保证模型输出的 127 个类别 100% 映射到中文译名与官方资源。
9. **智能阵营视角判定与一键切换**：
   优先读取 GSI `player.team_name` 动态裁定当前玩家归属阵营（天辉/夜魇），并提供 UI 级一键阵营颠倒切换，切换后毫秒级重算克制出装。
10. **分轮次 BP 容错与手动微调**：
   支持实战中 1~5 人的非完整阵容持续推导，并提供槽位级交互浮层，允许用户随时增删或纠错单个槽位的英雄。
11. **置顶悬浮窗 (Overlay) 跨进程实时同步**：
   解决游戏内盲按 F10 无法感知主界面的问题，通过 `overlay:bp-update` 事件与 Storage 缓存，将识别到的敌方阵容与核心针对装秒级推送至独立悬浮雷达。
12. **端到端两级 600ms 防抖保护**：
   在 Go 消息泵处理 `WM_HOTKEY` 与前端 `triggerCapture` 中建立双重 600ms 阈值与并发互斥锁，彻底杜绝快速连按导致的推理队列堆积与卡顿。



