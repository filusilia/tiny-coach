# Agent Note: 三位一体重构收口、构建链路修复与前端类型门禁

Status: implemented
Date: 2026-09-30
Class: architecture

## 问题背景与因果机制分析

在「Go 服务拆包（`internal/services/*.go` → `internal/services/<name>/`）+ 入口迁移（根 `main.go` → `cmd/aegis-coach/main.go`）+ 设置页 tabs → sections」三项重构并行推进后，工作树处于**未收口**状态。审计（只读全量，148 项未提交改动）定位到完整的因果链：

1. **入口迁移只改了一半，构建链路被单边击穿**：
   - `Taskfile.yml:30` 已更新为 `go build ... ./cmd/aegis-coach`；
   - 但 `tools/package-release.ps1:136` 的 `go build` **没有包路径参数**，隐式指向「当前目录 = 仓库根」。入口迁移后仓库根已 **0 个 `.go` 文件**，该命令必然报 `no Go files in D:\develop\demo`；
   - 由于脚本首行设 `$ErrorActionPreference='Stop'` 且失败即 `throw`，执行会在 **第 [4/4] 步资源汇总之前中断**，`release/lib`、`release/melo_weights` 也不再产出；
   - 时间戳佐证该路径自迁移后从未成功：`release/神盾教练.exe` = 09-29 17:29，而 `cmd/aegis-coach/main.go` = 09-30 09:54。

2. **`.syso` 资源产出于错误的包目录**：
   - `Taskfile.yml:22` 与 `package-release.ps1:109,115` 均把 `rsrc_windows_amd64.syso` 输出到**仓库根**，而编译目标是 `./cmd/aegis-coach`；
   - Go 只链接**被编译包自身目录**下的 `.syso`，因此 `build/windows/icon.ico`、`wails.exe.manifest`、`info.json` 自入口迁移后**静默失效**——不报错、无提示，产物只是丢了图标与 manifest。

3. **前端完全没有类型门禁，导致可编译期拦截的缺陷上线**：
   - `frontend/package.json:8` 的 `build` 仅为 `vite build`，**不含 `vue-tsc`**；`tsconfig.json` 的 `strict`/`noUnusedLocals` 从未执行；
   - 直接后果一：`frontend/src/types/index.ts:454-456` 存在**真实语法错误**（`:453` 已闭合 `AppSettings`，其后又重复一个 `user_context` 与多余 `}`，全文花括号 10:11 不平衡）；因所有消费方均为 `import type`，esbuild 直接擦除类型层，模块从未被真正解析，故构建不报错；
   - 直接后果二：`CoachView.vue:49` 的 `const battle = activeBattleContext`（`gsiRuleEngine` 的 reactive 对象）被访问了并不存在的 `battle.isGsiActive`（该字段只存在于 `stores/battle.ts:22` 的 computed）→ `:108,:120` 守卫恒真直接 return、`:657` 提交按钮 `:disabled` 恒 true，**AI 战术提问功能实际不可用**；
   - 直接后果三：`AiEngineSection.vue:127` 调用 `aiCoachService.pullLocalModel()`，而真实方法名是 `pullOllamaModel`（`aiCoachService.ts:259`）→ 点击「拉取模型」必抛 `TypeError`，被捕获后弹出误导性「模型拉取失败」。

4. **既有决策笔记被现方案证伪却仍标 `implemented`**：
   - `2026-09-20-go-standard-internal-package-layout-refactor.md:45` 的核心论证是「`//go:embed` 禁止 `..`，把入口移入深子目录会破坏前端资源嵌入并改变 `package-release.ps1` 构建上下文，带来严重破坏性风险」；
   - 现方案恰恰移入了 `cmd/aegis-coach/`，并以「把 embed 声明下沉到资源所在目录」的方式（`frontend/assets.go` 声明 `//go:embed all:dist`、`build/assets.go` 声明 `//go:embed windows/icon.ico`）**绕开了该约束**——即旧论证的前提被解除，而非被无视；
   - 该篇仍标 `implemented` 且未归档，与铁律 2「新方案彻底取代旧决策时，同批归档旧篇并标明被谁取代」相违。

5. **凭据入库**：`scripts/test-settings-tabs-and-cache-fix.cjs:95` 硬编码真实 API Key `cs-sk-…` 并提交入库，违反铁律 13。

## 决策方案

1. **构建路径归位与 `.syso` 就近产出**：
   - `tools/package-release.ps1` 的 `go build` 显式补上 `./cmd/aegis-coach`；
   - `Taskfile.yml` 与 `package-release.ps1` 的 `generate syso -out` 目标统一改为 `cmd/aegis-coach/rsrc_windows_amd64.syso`（与编译包同目录），临时文件清理路径同步更新；
   - 保留「`release/` 为唯一交付输出端」的既有边界（承接 `2026-09-22-packaging-workflow-direct-release-output.md`）。

2. **建立前端类型门禁**：
   - 新增 `typecheck` 脚本（`vue-tsc --noEmit`），并把它前置到 `build`（`vue-tsc --noEmit && vite build`），使类型错误重新成为**编译期**问题；
   - 修复 `types/index.ts:454-456` 的语法错误（删除重复字段与多余闭括号）。

3. **修复两处功能性缺陷**（按最小改动原则，不顺手重构）：
   - `CoachView.vue`：把对 `activeBattleContext` 的属性误用改为读取 `battleStore.isGsiActive`（单一事实来源仍是 `stores/battle.ts:22` 的 computed）；
   - `AiEngineSection.vue`：调用名对齐为 `aiCoachService.pullOllamaModel`。

4. **凭据出库**：删除 `scripts/test-settings-tabs-and-cache-fix.cjs` 中的硬编码 Key，改为从环境变量读取（未设置时跳过真实连通性测试而非失败）；已泄露的 Key 必须由持有人在服务侧吊销并轮换（代码侧删除不等于作废）。

5. **归档被证伪的旧决策**：将 `2026-09-20-go-standard-internal-package-layout-refactor.md` 移入 `.agents/notes/archive/`，`Status` 改为 `archived`，并在篇首标明被本篇取代。

## 曾考虑的替代方案

### 方案 A：仅回滚入口迁移，把 `main.go` 搬回仓库根
- **最强理由**：一次性同时解决打包脚本失效、`.syso` 失效、旧笔记被证伪三个问题，改动面最小（一次 `git mv`），且无需新增任何 embed 声明文件；旧笔记与旧脚本全部重新成立，零学习成本。
- **放弃原因**：与「标准 Go 项目布局」的既定方向背道而驰，等于用倒退换取省事；且入口留在根目录会持续把「包 main 与业务同层」的问题带进后续演进。真正的问题只是**迁移未收口**，收口成本是一行包路径 + 一行 `-out` 路径。

### 方案 B：把 `.syso` 留在仓库根，改用 `go build` 时显式 `-C` 或在根目录建一个只做转发的 `main` 包
- **最强理由**：避免改动构建脚本中已经「看起来能用」的资源生成路径，风险面更小。
- **放弃原因**：根目录转发包会重新引入「根目录存在 main 包」的认知负担，与本次收敛方向冲突；`-C` 语义会改变相对路径解析，反而更容易错。

### 方案 C：把 `vue-tsc` 作为独立脚本，暂不并入 `build`
- **最强理由**：当前仓库存在 120 处 `any` 与 7 处未使用 import，直接并入 `build` 可能一次性暴露大量历史类型错误，阻塞交付节奏。
- **放弃原因**：**独立脚本必然被绕过**——本次三处缺陷（含一处语法错误）长期潜伏，正是因为门禁「存在但不在必经路径上」。故选择并入 `build`；若类型错误过多，则以「先修 P0 级错误、允许后续分批清零」的方式推进，而不是把门禁降格为可选。

## 后果与验证计划

- **收益**：
  - `pnpm run package` 恢复可用，且产物重新带上图标与 manifest；
  - 类型错误从「运行期盲试」回归「编译期拦截」，`battle.isGsiActive` 这类属性误用不再可能静默上线；
  - AI 战术提问与模型拉取两个用户可见功能恢复；
  - 决策笔记的 `Status` 与真实架构重新一致。
- **代价与约束**：
  - `build` 变慢（新增一次全量类型检查）；
  - 仓库中历史遗留的类型问题会被门禁暴露，需要分批清零，短期内可能在 `build` 处形成摩擦。
- **验证计划**：
  1. `npm run verify-notes`（笔记目录与格式校验）；
  2. `pnpm --filter frontend typecheck`（0 错误）；
  3. `pnpm build`（前端全量构建）；
  4. `go vet ./...` 与 `go test ./internal/...`；
  5. `pwsh -NoProfile -File ./tools/package-release.ps1`（确认 `release/神盾教练.exe` 产出，且 `cmd/aegis-coach/rsrc_windows_amd64.syso` 生成后已被清理）。
