# Agent Note: 局内悬浮窗穿透与尺寸收敛、技能展台流畅度与全英雄出装汉化体验加固

Status: implemented
Date: 2026-10-10
Class: bug-fix

## 问题背景与因果机制分析

用户在实测客户端过程中提出 6 个核心体验与功能缺陷：
1. **悬浮窗无法鼠标穿透且实际大小超展示大小约 3 倍长**：
   - 悬浮窗原生窗口配置固定为 `Width: 340, Height: 460`，而未连接 GSI 或折叠状态下内容高度仅约 140~160px，下半区约 300px 虽为透明，但在 Windows 系统底层仍属 WebView2 矩形区域，导致鼠标点击被透明窗口吞没；
   - 切换穿透模式时，原有的 `ApplyMouseThrough` 仅对外层顶层宿主 HWND 设置了 `WS_EX_TRANSPARENT`，而未递归应用到 WebView2 的子窗口（`Chrome_WidgetWin_0` 等），子窗口依然拦截鼠标事件导致穿透失败。
2. **英雄详情切换技能时底部大图跟随切换过慢**：
   - 底部详情区 `SmartImage` 未指定 `:key`，Vue 原地复用 `<img>` DOM 元素并直接修改 `src`；浏览器在后台拉取与解码新图片期间持续展示旧图，产生肉眼可见的切换滞后感；
   - `SmartImage` 默认 `loading="lazy"` 推迟了解码时机。
3. **底部技能详情伤害等包含白色边框**：
   - `ValueElement` 与 `SpecialElement` 带有 `border-b border-white/5` 与 `border border-white/[0.03]`，在暗色背景下呈现突兀的高对比浅色细边框。
4. **所有英雄热门出装千篇一律**：
   - `HeroesView.vue` 直接读取了纯静态文件 `heroGuides.ts`，该文件仅包含 1、8、59 三个英雄的专有出装，其余 124 个英雄全部走主属性兜底模版（全员中期跳刀+BKB+A杖，后期龙心/蝴蝶/希瓦+撒旦+玲珑心）；
   - 后端已具备完善的 OpenDota 大数据真实出装接口 `GetHeroMatchupRecommendations`，但前端图鉴页未予接入。
5. **物品栏固定后点击其他物品未在新物品旁展开**：
   - 展开坐标计算依赖鼠标事件像素，且卡片未绑定 `tooltipRef`，在大尺寸卡片与视口边界翻转计算时易落入固定贴边 Y 坐标；
   - 未基于被点击物品的 DOM 边界盒（`getBoundingClientRect`）动态定位，且固定状态切换未精准重置目标。
6. **物品机制提示包含大段英文**：
   - OpenDota 常量数据 `item_constants.json` 中的 `notes` 为纯英文，前端直接调用 `cleanHtml(item.notes)` 渲染而未做任何本地化翻译。

---

## 决策方案

### 1. 悬浮窗尺寸自适应与真正递归穿透
- **尺寸自适应收敛**：优化悬浮窗卡片容器，未连接对局时窗口尺寸动态收敛（默认高 220px，连接后扩展至 420px），消除下半截 3 倍长的隐形黑洞；
- **子窗口递归穿透**：在 Go 平台层 `ApplyMouseThrough` 中，通过 Win32 API `EnumChildWindows` 递归为宿主 HWND 及其所有子级 WebView2 窗口统一注入 `WS_EX_TRANSPARENT`，并在解除穿透时统一安全剥离，确保点击彻底直通游戏。

### 2. 技能展台切换响应优化
- 在 `AbilityShowcase.vue` 底部大图绑定 `:key="activeSelectedAbility.name"`，强制在切换技能时立刻重置 DOM 并销毁旧图残影；
- 设置 `loading="eager"` 提升解码优先级，杜绝切换迟钝。

### 3. 技能详情去白边与暗黑视觉统一
- 移除 `ValueElement` 的 `border-b border-white/5` 与 `SpecialElement` 的 `border-white/[0.03]`，统一改为无白边自然排版，增强沉浸感。

### 4. 英雄热门出装接入真实大数据
- `HeroesView.vue` 打开英雄详情时，通过 `playerApi.getHeroMatchupRecommendations(heroId, [])` 动态拉取 OpenDota 各阶段真实大数据热门出装；
- 网络离线或超时时，使用基于英雄定位（近战/远程/核/辅）的差异化多套兜底数据，彻底消除千篇一律。

### 5. 物品栏卡片基于 DOM 边界盒智能锚定
- 在 `ItemsView.vue` 中绑定 `ref="tooltipRef"` 获得真实测量尺寸；
- 点击与固定时通过 `e.currentTarget.getBoundingClientRect()` 精准计算卡片相对物品方块的位置（优先右侧展开，不足则左侧，上下居中微调），点击其他物品立即重新锚定并在新物品旁展开。

### 6. 物品机制提示全量汉化解析器
- 在 `frontend/src/utils/itemTextResolver.ts` 中新增 `translateItemNotes(notes: string): string` 模块；
- 覆盖全量 50+ 条常用装备机制提示（包含自我施法、攻击特效叠加规则、对建筑物无效、伤害格挡与反弹机制等），并在 `ItemTooltipCard.vue` 中调用。

---

## 曾考虑的替代方案

### 替代方案 A：在前端完全屏蔽物品机制提示 (notes)
- **最强理由**：直接不显示 `notes` 可以 100% 杜绝任何英文出现，改动仅需删除 10 行模板。
- **为什么不采用**：机制提示（如跳刀双击朝泉水跳、否决可对无敌单位施放等）属于高级玩家极高频查询的硬核对局知识，粗暴删除属于功能严重退化（违背铁律第 11 条）。通过精准字典本地化不仅保留了知识，还提升了中文用户体验。

### 替代方案 B：在 Go 后端对所有物品静态 JSON 数据进行全量离线文本批量替换
- **最强理由**：直接修改 `item_constants.json`，前端无需运行任何正则表达式。
- **为什么不采用**：常量 JSON 来自上游 OpenDota 自动同步机制，后续版本更新或全量重刷时会被覆盖破坏。在文本解析层（`itemTextResolver.ts`）收拢翻译能保证版本升级与动态数据注入时的长久健壮性。

---

## 后果与验证计划

1. **悬浮窗穿透与尺寸验证**：验证在独立悬浮窗下 Alt+F3 穿透后鼠标能否毫无阻碍点击游戏背景；验证未折叠未连接时下方不再有占位黑洞；
2. **展台切换与样式验证**：切换技能时大图毫秒级刷新，技能伤害详情不再有白色细线；
3. **热门出装验证**：切换不同英雄（如影魔、蓝猫、潮汐、暗影萨满），各阶段出装各不相同且吻合真实天梯大数据；
4. **物品固定与交互验证**：固定某物品后，点击另一物品立即在新物品旁精准展开；
5. **门禁校验**：`pnpm run typecheck`、`pnpm --filter frontend lint`、`go test ./...` 与 `npm run verify-notes` 均 100% 保持绿色。
