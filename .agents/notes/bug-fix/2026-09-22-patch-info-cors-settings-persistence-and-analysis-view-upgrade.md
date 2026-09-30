# Agent Note: 补丁接口跨域穿透、基础设置容灾持久化与赛后分析雷达图大盘重构

Status: implemented
Date: 2026-09-22
Class: bug-fix

## 问题背景与因果机制分析

1. **“获取 Dota 2 最新版本号与更新日志失败，请检查网络连接”报错**：
   - **因果机制分析**：
     - 在 `frontend/src/services/gameDataService.ts` 中，前端在浏览器/WebView2 渲染器中直接发起 `fetch('https://www.dota2.com/datafeed/patchnoteslist?language=schinese')`；
     - Valve 官方 CDN（`www.dota2.com`）的响应头**未配置 `Access-Control-Allow-Origin: *`** 跨域响应头；
     - WebView2 运行在自定义应用 Origin 下，遵循浏览器标准同源策略（Same-Origin Policy），网络请求被浏览器安全策略静默拦截并产生 CORS 拒绝错误；
     - 降级分支虽然能从 OpenDota 拿到版本号，但随后拉取版本具体更新日志再次向 `www.dota2.com/datafeed/patchnotes` 发起 `fetch`，再次被 CORS 拦截阻断；
     - 两个阶段在前端直接请求全部被阻断，直接进入 catch 分支触发了 `appStore.showToast('获取 Dota 2 最新版本号与更新日志失败，请检查网络连接', 'error')`。

2. **基础设置未持久化（段位环境、出场最多/胜率排序）且设置读取路径逻辑脆弱**：
   - **因果机制分析**：
     - `HeroesView.vue` 中的 `selectedRank`（天梯段位环境，如超凡入圣 7）与 `sortMode`（出场最多 `pick_desc` 还是胜率 `win_desc`）此前作为组件局部 `ref` 存在，未接入 `AppSettings` 与持久化流水线，换页或重启即重置；
     - 软件虽然具备 `settings.json` 落盘，但 `GetSettingsFilePath()` 错误绑定到了动态可变的 `GetCacheDirectory()`；若用户将“缓存目录”自定义迁移至其他分区（如 `D:\DotaCache`），重启时后端内存中的 `customCacheDir` 尚为空，系统从默认文档目录找不到 `settings.json`，便误判为“文件不存在”，错误地触发了“新建默认设置”，覆盖了用户的既有配置；
     - 未建立“权威主设置目录（固定位于用户文档目录）+ 多源容灾探测”的可靠架构。

3. **赛后分析页面功能单一、缺乏英雄头像、战术雷达图与常用英雄统计**：
   - **因果机制分析**：
     - `AnalysisView.vue` 此前的近期对局历史仅为单行文字胶囊，缺少英雄头像、KDA、持续时长、对局开始时间与经济数据；
     - 缺乏玩家维度的战术全景呈现，无法直观了解近期综合胜率、场均击杀/助攻/死亡比率；
     - 缺乏五维战术能力雷达图，无法量化展现玩家在“参战、发育、击杀、生存、推进”五大层面的战术偏向与短板；
     - 缺少近期出场 Top 3 常用英雄统计与胜率卡片。

---

## 决策方案

### 1. Go 后端代理穿透官方补丁数据流，根除 CORS 拦截
- 在 Go 端 `SystemService` 增加 `FetchLatestPatchInfo() (string, error)`：
  - 使用 Go 原生 `http.Client` 请求 Valve 官方 `datafeed/patchnoteslist` 与 `datafeed/patchnotes`，完全不受浏览器 CORS 限制；
  - 若官方接口偶发超时，平滑降级至 OpenDota 获取版本号；
  - 成功解析后，自动结构化落盘至 `<cacheDir>/data/patch_info.json`，并返回给前端；
- 前端 `gameDataService.ts` 优先读取本地磁盘缓存，缓存未命中或强制刷新时调用 Go 原生 `systemApi.fetchLatestPatchInfo()`；彻底移除前端带 CORS 风险的直接 fetch，避免启动时报错 Toast 干扰。

### 2. 基础设置持久化与设置文件夹第一权威发现
- **设置目录与缓存目录职责分离**：
  - 主设置目录固定为 `Documents/DotaAICoach/settings.json`，任何时候不随缓存目录迁移而改变；
  - `LoadSettings()` 实施多源容灾探测：优先读取主设置目录，若存在自定义 `cache_directory` 立即注入 `storage.Manager` 单例生效；若主目录未命中，探测当前生效缓存目录下是否存在历史 `settings.json`；只有全部候选均不存在时，才创建默认设置并落盘；
  - `SaveSettings()` 确保写入主设置文件，并向自定义缓存目录镜像写入，杜绝设置丢失。
- **段位与排序持久化**：
  - `AppSettings` 扩展 `hero_rank_tier`（默认 7）与 `hero_sort_mode`（默认 `'win_desc'`）；
  - `HeroesView.vue` 与 `appStore.settings` 实施双向绑定，在用户切换段位或排序时即时调用 `updateSettings` 自动持久化落盘。

### 3. 赛后分析页面全景重构与战术雷达图
- **近期战绩卡片化增强**：
  - 展现英雄头像、中文名称、胜利（翠绿）/ 失败（绯红）徽章、KDA 数值高亮、比赛持续时间、对局发生相对时间与 GPM 经济；
  - 点击比赛卡片秒级载入十人阵容大盘。
- **个人战术大盘与纯 SVG 五维雷达图**：
  - 概况统计栏展示近期总场次、胜场/负场、综合胜率与场均 KDA；
  - 基于真实比赛战绩均值，百分制标准化计算“参战、发育、击杀、生存、推进”五维战术评分；
  - 采用轻量化纯 SVG 渲染五边形雷达网格、渐变能量填充区与顶点数值标签，零第三方重型图表库依赖，电竞深色质感；
- **Top 3 常用英雄池卡片**：
  - 自动聚合计算出场频次前三的常用英雄，展示头像、出场场次、专属胜率与平均 KDA。
- **严格遵守零伪造原则**：
  - 未绑定账号时呈现真实空态引导；战绩数据完全基于真实战报计算，严禁虚构英雄与胜率。

---

## 曾考虑的替代方案

### 1. 前端配置代理服务器或在 Vite 开发服务器中配置 proxy
- **为何最强**：在前端开发阶段配置 `vite.config.ts` 的 `server.proxy` 即可避开跨域。
- **为何放弃**：神盾教练打包后是桌面原生 Wails 应用，生产环境下使用内置的 WebView2 渲染本地打包后的静态资产，完全不经过 Vite 开发服务器，`proxy` 在生产环境彻底失效；且通过 Go 后端拉取能统一管理连接池与本地落盘缓存。

### 2. 引入 ECharts / Chart.js 等前端图表库渲染雷达图
- **为何最强**：图表库功能丰富、自带动画与交互配置。
- **为何放弃**：ECharts 体积庞大（数百 KB 至数 MB），对极轻量的 Wails 架构带来不必要的打包膨胀与运行期内存负担；纯 SVG 仅需数十行轻量代码即可实现完美的深色电竞高对比五边形雷达图，性能极致且完全可控。
