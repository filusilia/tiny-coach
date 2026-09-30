# Agent Note: 交付目标收敛与构建流程纯净化（消除 build/bin 冗余镜像）

Status: implemented
Date: 2026-09-22
Class: architecture

## 背景与问题因果机制剖析

在神盾教练原打包脚本 `tools/package-release.ps1` 与构建体系中，存在向 `build/bin/dota-ai-coach.exe` 额外复制一份二进制产物的历史兼容逻辑：
1. **多余拷贝与磁盘冗余**：
   - 客户端编译阶段（`go build`）已直接将可执行文件输出至目标发布目录 `release/神盾教练.exe`；
   - 随后脚本创建 `build/bin` 并通过 `Copy-Item` 将 24MB+ 的 exe 完整拷贝一份为 `build/bin/dota-ai-coach.exe`，导致单次打包产生两份相同的巨型二进制文件；
2. **权责混淆与认知负担**：
   - `build/` 目录本质是构建工程资产配置目录（包含 `build/windows/icon.ico`、`build/windows/wails.exe.manifest`、`build/config.yml` 等源码依赖）；
   - 混入 `build/bin` 导致开发者困惑于哪个才是当前生效的主入口；
   - 当前全仓（VSCode 启动配置、Taskfile、进程管理、开发文档）均已彻底统一使用 `release/神盾教练.exe`，早期 `dota-ai-coach` 名称已无任何现行依赖。

## 决策方案

1. **全面移除向 `build/bin` 的二次镜像复制**：
   - 在 `tools/package-release.ps1` 步骤 3 编译完成后，直接以 `release/神盾教练.exe` 为唯一交付输出，彻底移除对 `build/bin/dota-ai-coach.exe` 的创建与拷贝操作；
2. **纯净化目录边界与自动化清理**：
   - `build/` 目录专职作为**构建输入源资产**（图标、Manifest 清单、info.json 与 Wails 配置），不再产生任何输出二进制；
   - `release/` 目录专职作为**唯一发布输出端**，由编译器直出 `神盾教练.exe`，并由打包流程就地组装 GSI 配置文件与使用说明；
   - 打包脚本首步清理列表中保留对 `build/bin` 的递归扫描清除，防止历史残留文件干扰。

## 曾考虑的替代方案

### 方案 A：继续保留 `build/bin/dota-ai-coach.exe` 作为兼容快捷方式
- **最强理由**：照顾可能习惯从 `build/bin` 打开程序的老习惯或第三方外部快捷方式。
- **放弃原因**：当前仓库为纯单体工程，所有内置任务（`Taskfile.yml`、`npm run package`、VSCode tasks）均已收敛至 `release/`。保留冗余镜像不仅浪费 I/O，还破坏“单一事实来源（SSOT）”原则。

## 后果与验证计划

- **收益**：
  - 减少打包耗时与磁盘占用，构建产物结构更加清晰透明；
  - 明确了 `build/`（输入源资产）与 `release/`（交付输出套件）的分工边界。
- **验证结论**：
  - 执行 `npm run verify-notes`：全量 Agent 决策笔记校验通过；
  - 执行 `pnpm build`：前端构建成功；
  - 执行 `pwsh -NoProfile -File ./tools/package-release.ps1`：成功生成 `release/神盾教练.exe`，且 `build/bin` 未被重新创建。
