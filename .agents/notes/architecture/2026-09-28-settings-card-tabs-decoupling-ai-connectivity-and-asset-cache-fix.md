# Agent Note: 设置页面卡片化解耦、AI连通性自适应规整与英雄资产缓存修复

Status: implemented
Date: 2026-09-28
Class: architecture

## 问题背景与因果机制分析

1. **资产缓存加载缓慢与破图因果链条**：
   - **英雄列表问题**：虽然用户在设置中触发了一键预缓存，所有英雄头像已成功下载落盘至本地 `<cacheDir>/heroes/<name>.png`，并通过 `/cache/heroes/<name>.png` 提供服务。然而在 `HeroesView.vue` 中，`getHeroDisplayImg` 优先判断了 `officialDetails.value[hero.id]?.crops_img`；一旦官网元数据拉取成功，该字段指向远程 Valve CDN（如 `https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/heroes/crops/...`），导致 `:local-src` 直接被赋值为海外远程 URL，完全跳过了本地 HTTP 缓存服务，127 张图片全部外网排队下载，造成肉眼可见的严重卡顿。
   - **赛后复盘问题**：在 `MatchDetailPanel.vue`、`RecentMatchList.vue` 与 `TopHeroesPanel.vue` 中，英雄头像组件统一绑定了 `:local-src="getHeroIconUrl(String(hero_id))"` 与 `:cdn-src="getHeroCdnUrl(String(hero_id))"`。由于传入的是纯数字 ID（如 `1`），而 `cleanHeroName` 历史实现仅去除了 `npc_dota_hero_` 前缀，未针对纯数字做处理，导致前端请求生成了 `/cache/heroes/1.png` 与 CDN `.../heroes/1.png`。本地文件系统中只有短英文名文件（如 `antimage.png`），不存在 `1.png`；后端 transparent pull 从 CDN 请求 `1.png` 亦返回 HTTP 404；随后前端 SmartImage 捕获 error 降级至 `cdn-src`，再次向 CDN 请求 `1.png` 遭遇二次 404，并等待数秒超时挂起。10 人比赛中数十个头像请求产生雪崩式网络挂起，导致赛后复盘加载极慢且大面积呈现空白占位。
   - **后端下载单线程瓶颈**：在 `internal/services/system_service.go` 中，`CacheAllIcons` 内部采用单线程同步循环下载 120+ 个英雄头像与常用物品，一旦海外网络波动，极易耗时超 30 秒甚至超时中断。

2. **设置页布局体验缺陷与 AI 连通性阻断**：
   - 现行设置页 `SettingsView.vue` 采用单一视口全量 7 个模块垂直无脑堆叠的旧布局，模块冗长不易定位。
   - “战术语音提醒”中策略条目的描述文字使用了 `line-clamp-1`，导致关键战术提醒信息（如倒计时、机制说明）被强制省略号截断；多列紧凑网格中布局挤压严重。
   - 用户使用本地 Cherry Studio 等第三方 OpenAI 兼容 API 时，提供的地址常带有文档路由（如 `http://127.0.0.1:23333/openapi`）或未加 `/v1` 后缀（如 `http://127.0.0.1:23333`）。由于后端直接拼接 `/chat/completions`，导致发送至 `/openapi/chat/completions` 或 `/chat/completions`，被网关返回 HTTP 404，连通性测试直接失败。

## 决策方案

1. **资产缓存工具与组件优先级重构**：
   - 在 `frontend/src/utils/assetUrl.ts` 中增强 `cleanHeroName`：若输入为数字或纯数字字符串，自动通过 `HERO_SHORT_NAMES` 字典权威映射为对应标准短英文名（如 `1` -> `antimage`）；使 `getHeroIconUrl` 与 `getHeroCdnUrl` 支持数字 ID 直接传参，100% 命中本地缓存 `/cache/heroes/<shortName>.png`。
   - 在 `HeroesView.vue` 中重构图片绑定策略：`:local-src` 始终优先使用本地高速缓存路径 `getHeroIconUrl(hero.name)`，`:cdn-src` 使用官方高清图或 CDN 地址，保障毫秒级首屏加载。
   - 在 `internal/services/system_service.go` 的 `CacheAllIcons` 中引入并发 Worker 池（8 并发 goroutine），将百余个图标的预缓存耗时压缩至 2~3 秒内完成。

2. **AI 端点智能自适应规范化**：
   - 在 `internal/services/ai_service.go` 中新增 `normalizeOpenAiBaseUrl`：自动剥离末尾的 `/openapi`、`/openapi/` 等文档路径，对未声明版本路径的根地址智能补齐 `/v1`。
   - 在前端 `AiEngineSection.vue` 中提供本地 Cherry Studio（`http://127.0.0.1:23333/openapi`）一键预设与测试通道，连通性测试自动拉取可用模型并智能激活。

3. **设置页面卡片式 Tab 导航重构与彻底解耦**：
   - 顶部提供现代电竞风格的卡片式 Tab 导航栏，划分四大顶层卡片：
     - **首选项 (Preferences)**：包含独立组件【战术语音提醒】与【个人账号绑定】。
       - 语音提醒彻底消除文字截断，小字完整展现，优化间距与自适应网格；
       - 个人账号绑定强化指定 Steam / 32 位 ID，展示绑定状态与战绩画像链接。
     - **AI 战术推理引擎 (AI Coach)**：独立卡片，包含模型选型、端点配置、Key 输入与连通性即时测试。
     - **第三方拟真 TTS (TTS Engine)**：独立卡片，包含语音引擎与音色设置。
     - **其他选项 (Other Settings)**：独立卡片，解耦容纳【防止缓存/数据存储管理】、【局内置顶悬浮窗控制】与【关于神盾教练合规声明】。
   - 各顶层卡片与子模块彻底拆分为独立 SFC 组件（`views/settings/tabs/` 与 `views/settings/sections/`），禁止巨石组件。

## 曾考虑的替代方案

1. **方案 1：仅在赛后复盘组件内手动转换 `HERO_SHORT_NAMES[p.hero_id]`**
   - 最强理由：改动影响面极其局限，不触碰全局资产解析公共函数。
   - 否决原因：治标不治本。未来任何新功能只要调用 `getHeroIconUrl(heroId)` 依然会发生破图和双重 404；资产工具函数理应具备鲁棒容错与类型自适应能力。

2. **方案 2：保持设置页全量纵向滚动，仅在顶部增加锚点跳转**
   - 最强理由：改动代码量最小，无需拆解顶层视图。
   - 否决原因：单页垂直无节制堆叠在不同分辨率下体验极差，且各功能板块上下文互相干扰；卡片式切换符合专业桌面软件交互心智，各卡片高度内聚独立。

## 后果与验证计划

- **收益**：
  - 英雄列表与赛后复盘图片秒开，彻底消除 404 挂起与请求堵塞；
  - 设置页面模块清晰，语音提醒文字完整，操作流畅直观；
  - 本地与云端 AI 服务（包括 Cherry Studio 等）一键接入且连通性稳定可靠。
- **验证方法**：
  - 执行 `npm run verify-notes` 确保决策笔记合规；
  - 执行 `pnpm --filter frontend build` 验证构建通过；
  - 使用命令行和前端界面针对 `http://127.0.0.1:23333/openapi` 验证连通性测试成功。
