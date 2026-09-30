# Agent Note: 神盾教练目标架构全栈重构规范落地基线

Status: implemented
Date: 2026-09-21
Class: architecture

## 问题背景与因果机制分析

在神盾教练以往的演进迭代中，随着实时战术、赛后复盘与模型接入功能的快速扩充，工程逐步显露出以下几处架构硬伤与违规模式：

1. **前后端职责边界与调用混乱**：
   - 视图组件、各 Service 乃至 Store 内部散落着直接针对 `(window as any).go?.main?.App.*` 或 `wailsjs/` 的原生绑定调用；
   - 一旦后端重构方法签名或服务分包，前端多处代码瞬间断裂且缺乏 TypeScript 编译期拦截能力；
   - 部分业务逻辑（如 OpenDota 数据清洗与 TTL 缓存）由前端自作主张，导致前后端权威冲突与重复请求。

2. **GSI 高频数据流与 UI 响应式 Proxy 争抢性能**：
   - Dota 2 GSI 以最高 10Hz 频率持续推送全量战局状态；
   - 旧版直接将大型嵌套状态交由 Vue 的深度响应式 `reactive` 或深度 `ref` 代理，导致高频触发庞大的依赖追踪拓扑计算，在团战激烈时刻引发前端丢帧或内存泄漏；
   - GSI 事件订阅裸写在各个组件内，缺乏生命周期闭环注销机制。

3. **状态管理巨石化与视图路由缺位**：
   - 全局状态集中在单一的 `store/index.ts` 巨石文件中（470+ 行），混杂了应用设置、战况流水、玩家战绩与物品英雄图鉴，破坏单一职责原则；
   - 页面切换依赖 `v-if` 手动判定，缺失标准的现代路由拓扑，不利于多窗口及托盘原生菜单的直觉跳转。

## 决策方案

按照《目标架构规范》全面实施端到端重构：

### 1. Go 后端三层架构收敛与生命周期接管
- **领域数据传输模型 (`internal/models/`)**：创建 `system.go`, `player.go`, `ai.go`, `gsi.go`，严格统一采用小驼峰 JSON Tag（`json:"camelCase"`），提供前后端契约的一致性。
- **业务领域服务分层 (`internal/services/`)**：
  - `SystemService`：集中管理配置持久化、权威默认目录解析（注册表兜底）、原生对话框与本地模型文件扫描；
  - `OverlayService`：管理独立悬浮窗进程状态、置顶保活协程与穿透切换；
  - `GsiService`：4444 端口单消费者监听，使用 `http.MaxBytesReader` 防洪，统一派发 `"gsi_data"` 事件；
  - `PlayerService`：负责 10 分钟 TTL 内存缓存与磁盘落盘，保护 OpenDota API 防限流；
  - `AiService`：负责 Prompt 脱敏拼装、端点网络探测与赛后结构化复盘。
- **生命周期总门面 (`../../../internal/app/app.go`)**：接管 Wails `ServiceStartup` 与 `ServiceShutdown`，提供平滑过渡的委托方法；`../../../cmd/aegis-coach/main.go` 极简化为纯粹宿主装配入口。

### 2. 前端 `src/api/` 强类型隔离层建立
- **完全隔离 Wails 绑定**：严禁组件、页面和 Store 直接触碰 `window.go` 或 `wailsjs`。
- **IPC 调用客户端 (`client.ts`)**：提供 `invokeWails` 统一调度器，具备多候选服务名自动降级重试与统一 `ApiError` 封装。
- **领域模块划分**：划分 `system.ts`, `player.ts`, `ai.ts`, `gsi.ts`, `overlay.ts`，导出强类型 Promise 接口，100% 清除前端 `any`。

### 3. Pinia Stores 领域化与 rAF 帧合并
- **正规化拆分 Store**：
  - `stores/app.ts`：全局 UI 状态、Toast、持久化配置与策略预设；
  - `stores/battle.ts`：战况快照与战术流水，**快照严格使用 `shallowRef`**，仅整体单向替换，消除深层 Proxy 开销；
  - `stores/player.ts`：玩家账号绑定与画像缓存；
  - `stores/hero.ts` 与 `stores/item.ts`：英雄与物品图鉴常数知识库。
- **GSI 流 Composable (`useGsiStream.ts`)**：
  - 监听 `"gsi_data"` 事件，实施 `requestAnimationFrame` (rAF) 节流合并缓冲，将 10Hz 事件合并在浏览器渲染帧刷新；
  - 在 `onUnmounted` 生命周期中彻底解绑事件监听器，闭环杜绝内存泄漏。

### 4. Vue Router Hash 路由与视图解耦
- **Hash 路由拓扑 (`router/index.ts`)**：使用 `createWebHashHistory`，配置 `/coach`, `/analysis`, `/heroes`, `/items`, `/settings`, `/overlay`；
- **原生菜单联动 (`useNativeNavigation.ts`)**：统一监听 Wails 原生菜单 `navigate_to` 事件并平滑驱动 `router.push()`；
- **页面视图解耦 (`views/`)**：建立 `CoachView`, `AnalysisView`, `HeroesView`, `ItemsView`, `SettingsView`, `OverlayView`，净化 `App.vue` 仅保留 `<router-view>` 与外壳框架。

## 曾考虑的替代方案与弃用理由

### 1. 仅在前端补充 TypeScript 声明，保留现有调用习惯
- **最强理由**：修改成本极低，仅需编写若干 `.d.ts` 声明文件。
- **为何弃用**：治标不治本。没有 API 隔离层，组件仍然直接与底层 IPC 耦合，一旦后端重构，前端到处报错，无法建立可靠的企业级架构规范。

### 2. 对 10Hz GSI 快照继续使用深度响应式 `ref`
- **最强理由**：前端读取深层属性（如 `snapshot.hero.health`）更加自然，无需关心浅响应式规则。
- **为何弃用**：GSI 数据结构庞大且包含数十个英雄与技能状态字典，每秒 10 次的深度遍历 Proxy 包装会导致极其严重的 GC 压力与 CPU 峰值，直接违背神盾教练电竞低占用铁律。

### 3. 继续维持单一单例 Store
- **最强理由**：单一文件维护简单，所有状态唾手可得。
- **为何弃用**：状态污染严重，代码高内聚低耦合度全无，多人协同或长效维护极易引入隐蔽状态竞态。

## 后果与验证计划

- **收益**：
  1. 架构层次清澈透亮，前后端契约严密；
  2. 彻底消除组件内直接访问 `window.go` 与裸事件监听导致的内存泄漏；
  3. GSI 渲染帧合并与 shallowRef 保障 60fps 丝滑电竞体验；
  4. 保持了对既有老组件调用的 100% 平滑兼容。
- **验证计划**：
  1. 执行 `pnpm build` 全量构建，确认零类型报错；
  2. 执行 `go build -tags desktop .` 确认前后端打包顺利集成；
  3. 运行 `npm run verify-notes` 保证架构决策笔记与工程基线一致性。
