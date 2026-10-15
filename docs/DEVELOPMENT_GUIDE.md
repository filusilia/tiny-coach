# 神盾教练开发者工作流与验证矩阵 (DEVELOPMENT_GUIDE.md)

本文档规范神盾教练（AegisCoach）日常开发环境配置、编译构建、功能测试及发布打包的标准操作流程。

---

## 1. 开发环境要求

- **操作系统**：Windows 10 / Windows 11 (64位)
- **前端工具链**：Node.js 22+ / 24+，`pnpm` 10+ / 11+
- **Go 工具链**：Go 1.23+ / 1.27+
- **Wails CLI**：Wails v3 (`go install github.com/wailsapp/wails/v3/cmd/wails3@latest`)
- **系统辅助**：PowerShell 7 (`pwsh`)、Git for Windows

---

## 2. 核心代码规范红线

1. **语言优先（简体中文）**：
   UI 文案、错误提示、注释、提交信息与技术文档一律中文优先；API 符号与技术标识符保留英文。
2. **架构纯洁性红线**：
   前端采用 Vue 3 + TypeScript + Vite + UnoCSS + Pinia，后端采用 Go + Wails v3。
3. **零伪造与防劫持红线**：
   实时教练与悬浮窗严禁在无对局连接时伪造虚拟英雄；版本探测严禁硬编码默认补丁，未获取到严格置空，并拦截内网防火墙 302 劫持。
4. **防死锁与并发安全红线**：
   `sync.RWMutex` 严禁在同一协程内嵌套持锁重入；系统调用、文件 IO 与原生窗口创建严禁在锁内长时间占用。
5. **文档与代码绝对同步铁律**：
   每次开发更新代码若涉及架构拓扑、发布产物、组件职责、数据契约或运行语义改变，必须在同次修改中同步更新对应文档（`docs/`、`README.md`、`tools/assets/` 及决策笔记），严禁出现代码与文档脱节。

---

## 3. 日常构建与调试命令

### ① 前端热重载开发调试
```powershell
pnpm dev
```

### ② 前端全量构建验证
```powershell
pnpm build
```

### ③ 后端单元测试与静态检查
```powershell
go vet ./...
go test -v ./internal/...
```

### ④ 桌面客户端完整编译与打包
```powershell
pwsh -NoProfile -File .\tools\package-release.ps1
```

---

## 4. 自动化验证矩阵（代码提交前必跑）

在每次完成代码修改后，必须依次执行以下自动化检查：

```powershell
# 1. 前端全量 TypeScript 类型检查与 Vite 生产构建 (0 错误)
pnpm build

# 2. Go 后端静态检查与单元测试
go vet ./...
go test ./internal/...

# 3. Agent 决策笔记目录与格式校验
npm run verify-notes

# 4. Git 空白与冲突检查
git diff --check
```

---

## 5. 自动发布打包命令

完成代码验证后，可通过发布打包脚本生成最终分发包（步骤 0 自动清空旧产物与构建缓存，构建产物直接平铺输出至 `release/` 根目录）：

```powershell
# 直接在 release/ 根目录下生成免安装独立便携版程序与配置文件
pwsh -NoProfile -File .\tools\package-release.ps1
```

### 发布产物完整结构（全部拆分极简绿色便携包）：
- `release/神盾教练.exe`（桌面端轻量主程序，内嵌 Vue 3 电竞 HUD 与 127 英雄官方图鉴，体积仅 ~23MB，双击直接运行）
- `release/models/`（存放端侧赛前选人 YOLOv8 英雄目标检测模型 `yolov8n_hero.onnx`，支持独立热更新）
- `release/ort/`（存放端侧 ONNX Runtime WebAssembly 推理引擎核心文件 `ort-wasm-simd-threaded.wasm` 与加载器）
- `release/lib/`（存放 ONNX Runtime 官方动态链接库 `onnxruntime.dll`，支撑 Go 后端本地 C-API 离线语音合成推理）
- `release/melo_weights/`（存放本地 MeloTTS 离线语音声学模型 `model.onnx`、发音词表 `lexicon.txt` 与分词标记 `tokens.txt`）
- `release/使用说明.txt`（便携运行、Windows SmartScreen 放行与 GSI 配置向导）
- `release/gamestate_integration_aegis.cfg`（Dota 2 GSI 官方配置文件）
