# Agent Note: 悬浮窗生命周期闭环、GSI研判守卫、赛后深度复盘与全站电竞UI/UX全面重构

Status: implemented
Date: 2026-09-29
Class: architecture

## 问题背景与因果机制分析

1. **悬浮窗无法关闭与启动状态未检测到**：
   - 现象：悬浮窗打开后无法关闭；在主窗口点击打开悬浮窗后，按钮未检测到悬浮窗打开，仍然显示“打开悬浮窗 (Alt+F2)”。
   - 根因链路：
     - 在独立子进程模式（`--overlay`）下，悬浮窗中的关闭按钮仅调用了 `toggleOverlayWindow()` 将窗口 Hide，但未退出子进程（`app.Quit()`），子进程在后台永久运行；
     - 主进程的 `overlayCmd` 一直处于存活状态，主进程无法释放句柄；
     - 主窗口与悬浮窗属于不同 Webview 实例，前端 Vue 的 Pinia Store 内存隔离，无法自动跨窗口同步；
     - 前端 `overlayApi` 未暴露 `CloseOverlay()` 门面方法，关闭时缺少双保险同步机制。

2. **GSI 未接入时的 AI 研判误触发**：
   - 现象：在尚未进入 Dota 2 对局或 GSI 未连接时，点击 AI 研判依然向模型发起请求。
   - 违规与根因：违反铁律第 8 条（零伪造原则）；前端在 `triggerPresetQuestion` 与 `handleCustomAsk` 中未守卫 `battle.isGsiActive`。

3. **赛后分析页面排版与体验缺陷**：
   - 仅能展示 20 场近期比赛，缺乏分页机制；
   - 顶部包含大尺寸标题栏，搜索框包含厚重外层卡片，空间极其浪费；
   - 十人战报英雄头像过小，阿哈利姆神杖与魔晶显示为字符 "A" 和 "S"，参数高低不一缺乏列对齐；
   - “天辉防御塔”和“夜魇防御塔”直接展示官方原始 11 位二进制整数（如 2047），导致用户误解；
   - 点击比赛记录缺少即时反馈与加载动画；
   - 获胜阵营与 Tab 标题残留冗余括号英文和 `stats` 标识；
   - AI 复盘内容维度简陋，未充分结合十人装备与关键指标。

4. **英雄资料技能文字对齐缺陷**：
   - 现象：卡尔等英雄的技能特殊属性与文字描述参差错乱；
   - 根因：`AbilityShowcase.vue` 中 `SpecificValues` 采用 `grid-cols-2` 且对多等级属性应用了 `col-span-2` 和额外 padding，导致长短属性交错时内边距与网格起始线不一致，形成大面积空洞与错位。

5. **物品栏悬浮卡内容超出视口被截断**：
   - 现象：部分多技能、长机制的物品悬浮窗底部内容被屏幕切断；
   - 根因：`tooltipRef` 宽度写死为 `w-90` (360px)，且缺少 `max-height` 与 `overflow-y`，导致卡片超出屏幕视口无法滚动查看。

6. **设置页远程 API 模型拉取与选择交互缺失**：
   - 现象：远程模式使用复杂的探测微型标签，用户要求“远程api也使用拉取模型按钮，select模型即可”。

7. **英雄列表排序与段位环境样式不一致**：
   - 段位环境使用现代化暗黑自定义下拉框，而排序选择使用系统原生 `<select>`，视觉割裂。

## 决策方案与架构落地

1. **悬浮窗生命周期与状态双向保活闭环**：
   - 在 [overlay_service.go](file:///D:/develop/demo/internal/services/overlay_service.go) 中，`CloseOverlay()` 与 `ToggleOverlayWindow()` 检测独立进程模式（`isOverlay`），若需关闭则显式执行 `application.Get().Quit()` 彻底销毁子进程；
   - 在 [overlay.ts](file:///D:/develop/demo/frontend/src/api/overlay.ts) 中导出 `closeOverlay()`，并在 [OverlayView.vue](file:///D:/develop/demo/frontend/src/views/OverlayView.vue) 中直接调用；
   - 引入 `localStorage` 广播 `aegis_overlay_status`，配合 Wails 事件总线实现毫秒级双窗口状态对齐与多次静默校准。

2. **GSI 状态严格空态守卫**：
   - 在 [CoachView.vue](file:///D:/develop/demo/frontend/src/views/CoachView.vue) 中拦截所有 AI 研判入口，当 `!battle.isGsiActive` 时统一 Toast 警告“GSI 战局尚未接入，请在进入游戏或连接 GSI 后发起战术研判”，杜绝空发与假数据。

3. **赛后分析全景紧凑重构与分页扩展**：
   - [playerProfileService.ts](file:///D:/develop/demo/frontend/src/services/playerProfileService.ts)：支持请求更多比赛（如 60~100 场）；
   - [RecentMatchList.vue](file:///D:/develop/demo/frontend/src/views/analysis/RecentMatchList.vue)：实现前端多页分页组件（每页 12 场），点击条目即时显示加载动画；
   - [AnalysisView.vue](file:///D:/develop/demo/frontend/src/views/AnalysisView.vue)：移除顶部大标题栏，搜索框无外部卡片紧凑呈现；
   - [MatchDetailPanel.vue](file:///D:/develop/demo/frontend/src/views/analysis/MatchDetailPanel.vue)：
     - 放大英雄图标至 `h-11 w-15`；
     - 装备栏阿哈利姆神杖与魔晶替换为官方高保真微型图标；
     - 将防御塔 Bitmask 解析为存活塔数（如 `11/11 座存活`）并附加详细推塔状态提示；
     - 去除所有括号英文与无用 stats 标签；
     - 大幅扩充 AI 复盘 Prompt 与离线规则复盘内容。

4. **英雄技能属性通用对齐重构**：
   - 在 [AbilityShowcase.vue](file:///D:/develop/demo/frontend/src/views/heroes/components/AbilityShowcase.vue) 中消除破坏流的 `col-span-2` 与非对称 padding，采用统一标准网格，使所有英雄的属性列表严格水平对齐。

5. **物品栏悬浮窗滚动与视口自适应**：
   - 在 [ItemsView.vue](file:///D:/develop/demo/frontend/src/views/ItemsView.vue) 中，给悬浮卡增加 `max-h-[calc(100vh-32px)] overflow-y-auto` 与宽度弹性自适应，彻底解决内容溢出截断。

6. **设置页远程 API 拉取模型与 Select 交互统一**：
   - 在 [AiEngineSection.vue](file:///D:/develop/demo/frontend/src/views/settings/sections/AiEngineSection.vue) 中，远程 API 模式统一提供“拉取模型”按钮与优雅的 `<select>` 下拉选择器。

7. **英雄列表排序与段位环境视觉统一**：
   - 在 [HeroesView.vue](file:///D:/develop/demo/frontend/src/views/HeroesView.vue) 中，将排序选择重构为与段位环境一致的暗黑高级自定义下拉框。
