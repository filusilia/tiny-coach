# 神盾教练开发者工作流与验证矩阵 (DEVELOPMENT_GUIDE.md)

本文档规范神盾教练（AegisCoach）日常开发环境配置、编译构建、功能测试及发布打包的标准操作流程。

---

## 1. 开发环境要求

- **操作系统**：Windows 10 / Windows 11
- **前端工具链**：Node.js 24+，`pnpm` 11+
- **Go 工具链**：Go 1.27+
- **Wails CLI**：Wails v3 (`go install github.com/wailsapp/wails/v3/cmd/wails3@latest`)
- **系统辅助**：PowerShell 7 (`pwsh`)、Git for Windows

---

## 2. 核心代码规范红线

1. **语言优先（简体中文）**：
   UI 文案、错误提示、注释、提交信息与技术文档一律中文优先；API 符号与技术标识符保留英文。
2. **架构纯洁性红线**：
   前端采用 Vue 3 + TypeScript + Vite + TailwindCSS v4，后端采用 Go + Wails v3。

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

### ③ 桌面客户端完整编译与打包
```powershell
pwsh -NoProfile -File .\tools\package-release.ps1
```

---

## 4. 自动化验证矩阵（代码提交前必跑）

在每次完成代码修改后，必须依次执行以下自动化检查：

```powershell
# 1. 前端全量 TypeScript 类型检查与 Vite 生产构建 (0 错误)
pnpm build

# 2. Agent 决策笔记目录与格式校验
npm run verify-notes

# 3. Git 空白与冲突检查
git diff --check
```

---

## 5. 自动发布打包命令

完成代码验证后，可通过发布打包脚本生成最终分发包（步骤 0 自动清空旧产物与构建缓存，构建产物直接输出至 `release/` 根目录）：

```powershell
# 直接在 release/ 根目录下生成免安装独立便携版程序与配置文件
pwsh -NoProfile -File .\tools\package-release.ps1

```

### 发布产物结构：
- `release/神盾教练.exe`（单文件独立免安装便携程序，双击直接运行）
- `release/使用说明.txt`（便携运行与 GSI 配置向导）
- `release/gamestate_integration_aegis.cfg`（Dota 2 GSI 配置文件）
