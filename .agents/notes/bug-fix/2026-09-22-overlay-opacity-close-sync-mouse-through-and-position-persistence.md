# Agent Note: 独立经济悬浮窗透明度配置迁移、关闭状态双向同步、Win32 穿透修复与位置持久化记忆

Status: implemented
Date: 2026-09-22
Class: bug-fix

## 背景与问题因果机制剖析

在电竞独立置顶经济雷达悬浮窗的交互体验中，存在四个关键体验缺陷与深层机制障碍：

1. **透明度设置归属混乱**：
   - 原悬浮窗卡片底部自带透明度滑动条，破坏了极简 HUD 的信息密度，且悬浮窗透明度应属于全局用户偏好，需统一移至设置主面板持久化存储，并保持跨窗口实时响应。
2. **手动关闭时主页面状态脱节（仍显示“悬浮窗已启动”）**：
   - **因果机制 1（JS 假真值转换漏洞）**：Go 后端在悬浮窗隐藏/关闭时调用 `app.Event.Emit("overlay_state_change", false)`，在 Wails v3 事件总线中传递的是切片形式载荷。前端 `useNativeNavigation.ts` 中原逻辑采用 `Boolean((eventPayload as { data?: boolean })?.data ?? eventPayload)`；当 `data` 为 `[false]` 时，由于 JavaScript 中任何非空数组均为真值，`Boolean([false])` 被错误求值为 `true`！导致前端永远将关闭事件逆向误判为开启状态。
   - **因果机制 2（生命周期事件漏听）**：若用户通过系统任务栏或原生关闭操作，Go 端原未对 `events.Common.WindowClosing` 挂载捕获，未发出状态同步事件。
3. **悬浮窗无法进行物理鼠标穿透**：
   - **因果机制**：Win32 API 底层中，要使窗口完全忽略鼠标点击并直通游戏底层，窗口必须同时具备 `WS_EX_TRANSPARENT (0x00000020)` 与 `WS_EX_LAYERED (0x00080000)`。此前 `internal/overlay/overlay_windows.go` 中的后台保活协程 `EnsureOverlayExtendedStyle` 每 500ms 循环执行一次，其中无差别地执行了 `&^ WS_EX_LAYERED` 剥离逻辑，导致调用穿透后不足半秒即被强制擦除，点击穿透彻底失效。
4. **悬浮窗无法记忆移动位置（重新打开居中归位）**：
   - **因果机制**：`WebviewWindowOptions` 创建悬浮窗时未指定 `X`、`Y`，Wails 默认将无坐标窗口居中。用户拖拽移动后，窗口位置未做任何本地落盘持久化，导致下次启动或重新打开时重置为屏幕中央。

## 决策方案

1. **悬浮窗透明度迁移至设置页面并实现跨窗口响应**：
   - 在 `AppSettings` 中新增 `overlay_opacity?: number`（默认 90%）；
   - 在 `SettingsView.vue` 中新增独立模块“局内经济独立置顶悬浮窗”，提供 30% ~ 100% 精准滑块；
   - 彻底移除 `OverlayView.vue` 底部的冗余滑块；`OverlayView` 通过 `storage` 事件与 Pinia 联动，主窗口拖动滑块时悬浮窗毫秒级实时变色。
2. **关闭状态双向同步机制与事件解析修复**：
   - 在 `useNativeNavigation.ts` 中彻底修复布尔事件载荷解析函数，解包数组并严格按布尔值判定，杜绝 `Boolean([false])` 导致的逻辑颠倒；
   - 在 Go 端（`internal/services/overlay_service.go`）为悬浮窗挂载 `events.Common.WindowClosing` 钩子，窗口关闭或收起时统一发出 `overlay_state_change(false)`。
3. **修复 Win32 鼠标穿透与全局快捷键联动**：
   - 引入 `WS_EX_TRANSPARENT = 0x00000020`；在 `EnsureOverlayExtendedStyle` 中识别若窗口已开启穿透，严密保留 `WS_EX_LAYERED | WS_EX_TRANSPARENT`，不再暴力剥离；
   - 新增 `overlay.ApplyMouseThrough(hwnd, ignore)` 底层 API 与 `OverlayService.ToggleMouseThrough()`；
   - 在 `../../../cmd/aegis-coach/main.go` 中注册全局快捷键 `Alt+F3` 与托盘/系统菜单项，方便玩家随时随地一键切换穿透/交互。
4. **窗口移动位置本地落盘与恢复记忆**：
   - 在数据缓存目录维护 `overlay_position.json`；
   - 监听 `events.Windows.WindowEndMove`、`events.Common.WindowDidMove` 与 `WindowClosing`，实时落盘最新 `(x, y)` 坐标；
   - 悬浮窗无论是由主进程弹出还是以 `--overlay` 子进程启动，创建时均优先读取记忆坐标；
   - 在设置页提供“重置位置”按钮，防止跨多显示器切换后窗口丢失。

## 曾考虑的替代方案

### 方案 A：在前端通过监听拖拽完毕调用 IPC 保存坐标
- **最强理由**：前端对 DOM 拖拽行为更敏感。
- **放弃原因**：Wails 原生拖拽（`--wails-draggable: drag`）由 Windows DWM 与操作系统内核驱动，前端在拖拽释放后无法稳定捕获精确的物理屏幕像素坐标；由 Go 端监听原生 `WindowEndMove` 是最精准、零丢帧的唯一方案。

## 后果与验证计划

- **收益**：
  - 悬浮窗面板大幅精简纯净，透明度设置归于统一首选项；
  - 悬浮窗关闭后主界面按钮秒级同步显示“打开悬浮窗 (Alt+F2)”，状态 100% 准确；
  - 鼠标穿透（`Alt+F3`）真实生效，点击完全直通 Dota 2 战场；
  - 随意拖拽位置后，下次启动软件或关闭重开时精确复位到上次离开的位置。
- **验证矩阵**：
  - `npm run verify-notes`：全量 28 篇 Agent 决策笔记校验 100% 通过；
  - `pnpm build`：前端构建成功，无编译错误；
  - `pwsh -NoProfile -File ./tools/package-release.ps1`：一键打包通过并产出最新免安装客户端。
