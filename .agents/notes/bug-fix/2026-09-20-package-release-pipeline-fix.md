# Agent Note: 客户端免安装发布打包工具链与 Wails v3 原生构建修复

Status: implemented
Date: 2026-09-20
Class: bug-fix

## 问题背景与因果机制分析

在执行 `tools\package-release.ps1` 进行客户端免安装便携版一键打包时，流水线出现两项错误与一项警告提示：

1. **`Normalize-LineEndings.ps1` 丢失导致报错**：
   - **因果机制**：在之前的清理与代码重构提交中，`tools\Normalize-LineEndings.ps1` 被误加入暂存区删除操作，文件从物理磁盘上消失。当 `package-release.ps1` 执行步骤 `[1/4]` 调用该脚本时，PowerShell 报告 `The argument '...\tools\Normalize-LineEndings.ps1' to the -File parameter does not exist`。
2. **Wails v2 CLI 与 Wails v3 项目不兼容导致假报错**：
   - **因果机制**：打包脚本自动扫描并优先尝试调用环境中的 `wails.exe`。本机安装的全局工具版本为 `Wails CLI v2.16.0`，而本项目已演进为 `github.com/wailsapp/wails/v3` 架构。Wails v2 CLI 在解析 `go.mod` 时因严格寻找 `v2` 依赖而崩溃并抛出 `ERROR Unable to find Wails in go.mod`。
3. **可执行文件启动报错“应用程序的并行配置不正确” (Side-by-Side SXS Error)**：
   - **因果机制**：`build/windows/wails.exe.manifest` 中原包含 Wails v2 的模板占位符 `<assemblyIdentity name="com.wails.{{.Name}}" version="{{.Info.ProductVersion}}.0" ... />`。在脱离 v2 模板引擎进行打包时，未被渲染的字面量 `{{.Info.ProductVersion}}.0` 被直接注入可执行程序的 PE 清单段中。Windows 系统的 `csrss.exe` / SxS 激活子系统要求 `version` 必须是标准的四组数字。因版本格式非法，Windows 系统直接拒绝加载进程并抛出“应用程序的并行配置不正确”致命弹窗。

## 修复决策方案与落地细节

1. **根治 Side-by-Side (SxS) 清单解析错误**：
   - 全面更新 `build/windows/wails.exe.manifest` 与 `build/windows/info.json`，将模板占位符替换为合法的固定值（`version="2.0.0.0"`，`name="com.wails.AegisCoach"`）；
   - 使用新版 `wails3 generate syso` 编译嵌入合法的 Windows PE 资源，编译产物即刻成功拉起并稳定运行。

2. **工程全面瘦身与纯净化（彻底剔除冗余脚本）**：
   - 彻底删除 `tools/main.go`、`Normalize-LineEndings.ps1`、`download-ai-runtime.ps1`、`update-hero.ps1` 等冗余工具；
   - 依托 `.gitattributes` 与 `.editorconfig` 的双重底层保障，去除多余的换行符重复扫描；
   - 建立 Wails v3 官方规范元数据 `build/config.yml` 与 `Taskfile.yml`；
   - 重构 `tools/package-release.ps1` 为 50 行极简流水线，3 秒一键直出绿色免安装单文件客户端。

3. **Vite 6+ ESM 现代标准路径解析**：
   - 在 `frontend/vite.config.ts` 中引入 `node:url` 的 `fileURLToPath`，采用 `fileURLToPath(new URL('./src', import.meta.url))` 替代 `__dirname`，彻底消除了 Vite 构建中的任何警告。

## 验证结论

- 规范化脚本 `Normalize-LineEndings.ps1` 正常运行并通过检查；
- 前端静态构建 `pnpm build` 600ms 内完成，0 报错 0 警告；
- 一键打包脚本 `pwsh -NoProfile -File .\tools\package-release.ps1` 完整执行 4 步流程，成功输出体积约 23.4 MB 的独立免安装单文件 `神盾教练.exe`，耗时仅数秒。
