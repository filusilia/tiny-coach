# 神盾教练开发者工作流与验证矩阵 (DEVELOPMENT_GUIDE.md)

本文档规范神盾教练（AegisCoach）日常开发环境配置、编译构建、功能测试及发布打包的标准操作流程。

---

## 1. 开发环境要求

- **操作系统**：Windows 10 / Windows 11
- **前端工具链**：Node.js 20+ / 22+ / 24+，`pnpm` 9+ 或 10+
- **Rust 工具链**：Rust 1.80+ (MSVC 目标架构 `x86_64-pc-windows-msvc`)
- **Tauri CLI**：`@tauri-apps/cli` v2 或 `cargo-tauri`
- **系统辅助**：PowerShell 7 (`pwsh`)、Git for Windows

---

## 2. 核心代码规范红线

1. **严格使用 Windows CRLF (`\r\n`) 换行符**：
   Windows 桌面开发环境下，本仓库全仓一律严格使用 Windows CRLF。禁止混入 Unix LF (`\n`)。
2. **语言优先（简体中文）**：
   UI 文案、错误提示、注释、提交信息与技术文档一律中文优先；API 符号与技术标识符保留英文。
3. **架构纯洁性红线**：
   前端采用 React + TypeScript + Vite，后端采用 Rust + Axum。严禁引入任何弃用的 C# 或 WinUI 依赖。

---

## 3. 日常构建与调试命令

### ① 前端热重载开发调试
```powershell
pnpm dev
```

### ② 完整桌面应用调试 (Tauri)
```powershell
pnpm tauri dev
```

### ③ 前端生产构建验证
```powershell
pnpm build
```

---

## 4. 自动化验证矩阵（代码提交前必跑）

在每次完成代码修改后，必须依次执行以下自动化检查：

```powershell
# 1. 前端全量 TypeScript 类型检查与 Vite 生产构建 (0 错误)
pnpm build

# 2. 全仓换行符规范化检查 (确保所有文件为 CRLF)
pwsh -NoProfile -File .\tools\Normalize-LineEndings.ps1

# 3. Git 空白与冲突检查
git diff --check
```

---

## 5. 自动发布打包命令

完成代码验证后，可通过发布打包脚本生成最终分发包：

```powershell
# 生成便携版免安装独立程序 (release/publish)
pwsh -NoProfile -File .\tools\package-release.ps1

# 生成便携版免安装独立程序并压缩为 ZIP
pwsh -NoProfile -File .\tools\package-release.ps1 -Zip
```

### 发布产物结构：
- `release/publish/神盾教练.exe`（双击即可直接运行）
- `release/publish/dist/`（前端静态电竞 HUD 产物）
- `release/publish/使用说明.txt`（便携运行与 GSI 配置向导）
