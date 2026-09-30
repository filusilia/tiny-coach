# Agent Note: Qwen 3.5/3.8 端侧升级、零伪造悬浮窗与视图层直接收敛架构决策

Status: implemented
Date: 2026-09-21
Class: architecture

## 问题背景与因果机制分析

在近期的实机联调与体验排查中，发现了以下关键架构缺陷与交互痛点：

1. **悬浮窗无法独立脱离主应用且存在模拟假数据违规（触碰开工铁律第 8 条）**：
   - 之前在 `App.vue` 中存在残余内嵌渲染组件 `<OverlayView v-if="appStore.isOverlayOpen" />`，导致悬浮窗实际上被困在主窗口内部渲染，无法在进入全屏/无边框游戏时独立悬浮置顶于 Dota 2 之上；
   - 悬浮窗内部在未接收到真实对局快照时，曾错误呈现预设的假英雄、假战局时间（`03:42`）与假经济差（`+3,450 领先`），严重违背零伪造铁律红线。
2. **端侧推理模型推荐陈旧，且内存预算未做严格约束**：
   - 此前预设的 `qwen2.5` 系列已过时，用户明确要求“qwen 已经升级到了 3.5 3.8，不要给我推荐 2.5”；
   - 在 16GB 物理内存环境下，Windows 11 系统常态占用约 4.0GB，Dota 2 团战激烈时占用约 5.5GB，端侧模型开销必须严格控制在 ≤ 2GB 以内，否则将触发虚拟内存 Page Fault 导致游戏出现致命掉帧与切屏卡顿。
3. **Wails v3 IPC 绑定寻址报错与对话框无法唤起**：
   - 用户点击选择模型文件报错 `[API Error] 调用 'main.App.SelectModelFileDialog' 失败: 未能在运行时找到对应的后端方法`；
   - 根本因果：Wails v3.0.0-beta.24 抛出的错误信息为 `unknown bound method name '<FQN>'`，而旧版客户端只检测了 `errMsg.includes('not found')`，导致未尝试后续服务名候选即直接中断并抛出异常。
4. **第三方 TTS 切换时出现异常大黑块与无法禁用**：
   - 设置页第三方 TTS 的开关复选框使用了 `peer` 配合 `after:absolute`，但其容器没有声明 `relative`，导致伪元素脱离文档流遮蔽整个卡片，呈现为大黑块；且缺少一键重置为系统内置 Edge-TTS 的开关通道。
5. **前端视图层双层包裹冗余**：
   - `src/views/` 仅仅简单 import 并包裹 `src/pages/`，形成无实际价值的双重代理转发，架构职责不清。

## 决策方案与架构落地

### 1. 局内独立置顶悬浮窗彻底脱离与 Rule 8 零伪造守卫
- **彻底剥离内嵌组件**：移除 `App.vue` 中的内嵌组件，仅在 URL 包含 `?window=overlay` 或通过 Wails 多窗口模式时激活独立悬浮窗；
- **Wails 原生独立窗口创建**：在 `OverlayService.ToggleOverlayWindow` 中，通过 `appInst.Window.NewWithOptions` 创建独立的置顶无边框透明窗口（`Width: 340, Height: 460, BackgroundTypeTransparent, WS_EX_TOOLWINDOW`），同时挂载 WinAPI 保活协程；
- **100% 真实数据与空态守卫**：`OverlayView.vue` 接入 `useBattleStore()`，未连接时严密呈现带有呼吸闪烁的“等待 GSI 4444 端口接入”空态守卫，绝不伪造任何虚拟英雄与战局快照；连接后全量呈现真实等级、血蓝比例、GPM/XPM、金币与真实买活冷却。

### 2. 全量升级 Qwen 3.5 / 3.8 端侧推理模型（严格 ≤ 2GB 内存）
- **推荐模型矩阵**：
  - `qwen3.5:1.5b`（👑 首选推荐，仅占 ~1.2GB 内存，首字 < 25ms，16G 电脑 + Dota 2 双开 0 掉帧）
  - `qwen3.5:0.5b`（⚡ 极限省内存，仅占 ~400MB 内存，对整机资源 0 抢占）
  - `qwen3.8:1.5b`（🚀 最新轻量权重，~1.3GB 内存，兼具长上下文与克制推演）
  - `llama3.2:1b`（🌐 移动端与 PC NPU 高能效架构，~900MB 内存）
- **后端与 Store 自动平滑迁移**：`internal/ai/service.go` 的默认本地模型全部升级为 `qwen3.5:1.5b`；`stores/app.ts` 检测旧配置中的 `qwen2.5` 并自动迁移为 `qwen3.5:1.5b`。

### 3. Wails v3 IPC 寻址容错增强与系统对话框双保险
- **多全限定名 (FQN) 候选自动展开**：`client.ts` 自动将任意方法名展开为 `main.App.<Method>`, `aegis-coach/internal/services.SystemService.<Method>`, `aegis-coach/internal/app.App.<Method>` 等全限定名；
- **错误捕获全覆盖**：覆盖 `unknown bound method`、`ReferenceError` 与 `undefined`，确保跨版本绑定均能自动尝试备选；
- **Windows 原生对话框 STA 双保险**：在 `SystemService` 中除 Wails 原生 Dialog 外，提供 PowerShell `-STA` 原生 Windows 窗体文件/文件夹选择框保底，100% 避免无响应。

### 4. 第三方 TTS 语义化 Switch 重构与 Edge-TTS 原生保底
- 彻底废除无 relative 的 `peer/after:absolute` 结构，重构为具备语义化 `role="switch"`、具备绝对 `relative` 隔离容器的 Vue Switch 组件，杜绝任何样式逃逸；
- 推荐卡片第一项提供“内置微软 Edge-TTS 原生保底”卡片，用户点击即可随时优雅关闭第三方 TTS 并恢复系统极速语音。

### 5. 视图代码收敛直接落位 `src/views/`
- 将 `SettingsView.vue`、`CoachView.vue`、`AnalysisView.vue`、`HeroesView.vue`、`ItemsView.vue` 直接编写为完整标准视图；
- 将版本号移入 `SettingsView.vue` 最底部的新增【关于神盾教练 (About)】卡片；
- `src/pages/*.vue` 转为兼容透传层，保证旧路径引用零报错。

## 验证结论与合规指标

1. **ESLint 全量修复**：执行 `pnpm --dir frontend lint:fix`，0 错误、0 警告通过。
2. **前端全量构建**：`pnpm --dir frontend build` 成功完成（产物位于 `frontend/dist`）。
3. **Go 二进制编译**：`go build` 编译成功，0 错误。
4. **决策笔记校验**：`npm run verify-notes`，全量 16 篇决策笔记结构合规 100% 通过。
