# Agent Note: TTS 超保真拟人参数错位修复与英雄天梯分段选择器防抖动重构

Status: implemented
Date: 2026-09-28
Class: bug-fix

## 问题背景与因果机制分析

用户在体验客户端时反馈了以下两处严重影响交互的核心缺陷：

1. **语音使用超保真拟人无效，依然是 Edge 本机语音**：
   - **因果链路 A（参数错位导致底层抛错降级）**：
     在重构 MeloTTS 原生离线引擎时，Go 后端与前端 API 接口 `ttsApi.synthesizeSpeech(text, speed)` 已经收敛为两个参数。但在 `frontend/src/services/speechService.ts` 的第 241 行与第 342 行中，依然残留着旧三参数签名 `ttsApi.synthesizeSpeech(cleanText, 'melotts', rate)`。
     这导致字符串 `'melotts'` 作为 `speed: number` 传给 Wails 绑定，底层类型反序列化失败抛出异常，进入 catch 分支后将播放状态判为失败，从而**自动静默降级为 Edge 本机语音**，设置页面的“测试发音”亦因此被降级。
   - **因果链路 B（业务层调用未透传 TTS 配置）**：
     在 `gsiRuleEngine.ts` 与 `VoiceBroadcastSection.vue` 中调用 `speechService.speak(text, { mode: 2, ... })` 时，未在 options 中传递 `customTtsEnabled`。而 `speechService.speak` 仅检查了入参 options，未在缺省时自动读取 Pinia Store 中的当前用户全局设置，导致真实战况播报永远走 Edge。

2. **英雄筛选时，选择天梯分段下拉框会发生界面抖动**：
   - **因果链路**：
     天梯分段的文案字数长短不一（例如“传奇”仅 2 字符，而“冠绝一世”为 4 字符）。段位选择触发按钮未指定固定宽度，切换段位时按钮宽度瞬间发生伸缩（从 ~102px 缩减至 ~76px）。
     由于外层导航栏是 Flex `justify-between` 布局，按钮宽度的骤变引发 Flex 子项重新分配宽度与 reflow 重排，同时绝对定位的下拉菜单基准坐标发生横向偏移，导致整行导航条剧烈抖动与跳跃。

## 决策方案

1. **TTS 合成签名与全局 Store 联动闭环**：
   - 彻底修复 `speechService.ts` 中的 `ttsApi.synthesizeSpeech(cleanText, rate)` 调用，剔除多余的 `'melotts'` 参数，确保参数类型与 Go 端 `(string, float64)` 100% 精确契合；
   - 在 `speechService.speak` 中引入 Pinia Store 兜底：若入参 options 未显式声明 `customTtsEnabled` / `customTtsEngine`，自动从 `useAppStore().settings` 中读取当前配置，保证对局实时播报和设置页面试听统一遵从用户设定的引擎。
2. **天梯分段选择器定宽与防抖防漂移重构**：
   - 将天梯分段按钮独立包裹在专用 `relative` 容器中，按钮赋予固定宽度 `w-29`（116px），文本区域使用 `flex-1 text-left truncate`，无论当前选中的是 2 字还是 4 字段位，按钮几何尺寸严格恒定，杜绝触发表头重排与抖动；
   - 下拉浮层以按钮包裹容器为基准左对齐绝对定位，不随父级复合 flex 容器浮动；
   - 增加全局点击外部自动收起下拉面板的事件监听器，组件卸载时及时移除；
   - 在英雄列表滚动容器上应用 `[scrollbar-gutter:stable]`，消除数据重新排序时纵向滚动条切换引起的页面像素抖动。

## 曾考虑的替代方案

1. **在每个业务调用 `speechService.speak` 的地方手动传 `customTtsEnabled`**：
   - *最强理由*：调用方拥有显式控制权。
   - *否决原因*：容易出现遗漏（如 `gsiRuleEngine.ts`、`VoiceBroadcastSection.vue` 等处处需要补充样板代码），一旦有新开发者或新模块接入又会发生“超保真拟人无效”的隐蔽回归。在底层服务缺省时自动对齐全局 Store 是唯一高内聚方案。
2. **改用原生 HTML `<select>` 替换自定义段位选择器**：
   - *最强理由*：原生 select 绝对不会抖动。
   - *否决原因*：原生 select 无法内嵌 Valve 官方精美的段位段位徽章图标（如冠绝一世、超凡入圣原画），严重破坏电竞 HUD 的设计系统质感。通过定宽与定位隔离既能消灭抖动，又能保持极致的视觉呈现。

## 后果与验证计划

- **正面收益**：超保真拟人语音（MeloTTS）在设置试听与真实对局中稳定发音，彻底解决静默降级 Edge 缺陷；天梯分段切换平稳丝滑，零抖动、零跳跃。
- **验证计划**：
  1. 编写独立测试脚本验证 `speechService` 的双参数调用、Store 读取机制与 `HeroesView.vue` 的定宽类名；
  2. 运行 `npm run verify-notes` 校验决策笔记；
  3. 运行 `pnpm --filter frontend lint` 与 `pnpm --filter frontend build`；
  4. 运行 `pwsh tools/package-release.ps1` 产出最终客户端。
