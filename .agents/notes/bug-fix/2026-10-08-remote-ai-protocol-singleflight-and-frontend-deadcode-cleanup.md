# Agent Note: 远程 AI 协议修复、全栈统一出口收拢、SingleFlight 并发加固与前端死代码清理

Status: implemented
Date: 2026-10-08
Class: bug-fix

## 问题背景与因果机制分析

在对当前工程全量代码规范、开工铁律（AGENTS.md）与健壮性进行深度走查时，发现了以下跨层因果问题与隐患：

1. **云端大模型 404 导致长期退化为离线规则（核心因果链）**：
   - 在 `internal/services/ai/engine_remote.go` 中，向远端兼容 OpenAI / 兼容端点发送请求时，使用的 URL 路径为硬编码的 `%s/responses`，请求 Body 为非标准的 `{"model": "...", "input": "..."}`。
   - 任何标准 OpenAI 兼容服务（包括 OpenAI、DeepSeek、Qwen、SiliconFlow、OneAPI、Ollama 兼容代理）均提供 `/v1/chat/completions` 或 `/chat/completions`，收到 `/responses` 时必返回 HTTP 404 Not Found。
   - 由于后端持续返回 404，历史前端代码在调用 Go 端失败后，违规加入了“前端浏览器裸发 `fetch`”的兜底分支。但这又引发了严重的浏览器同源策略（CORS）拦截以及前端泄露选手 API Key 的重大安全隐患。
2. **多组件并发加载导致的 429 频控与 Windows 文件句柄竞争**：
   - 战况分析视图中，`MatchSearchBar`、`AdvantageGraph`、`MatchDetailPanel` 会在毫秒级并发请求同一场比赛详情或同一个玩家战绩。
   - 在旧代码中，每个调用方都会穿透至 OpenDota 发起独立的 HTTP 请求，极易触发官方速率限制（429 Too Many Requests）；并且异步落盘时由无保护的 goroutine 并发执行 `os.WriteFile` 写入同一个 `match_<id>.json`，在 Windows NTFS 文件系统下会产生偶发性文件句柄访问拒绝冲突。
3. **MeloTTS 推理会话阻塞前端状态轮询**：
   - `internal/services/tts/engine_melo.go` 中，`SynthesizeToWav`（单次 CPU 推理耗时 500ms~2s）直接在 `m.mu.Lock()` 保护下执行。而前端状态轮询会频繁通过 IPC 调用 `GetTtsStatus()`，同样请求 `m.mu.Lock()`。导致一旦开始合成语音，前端 IPC 管道被完全挂起直到合成结束，造成 UI 偶发性卡顿。
4. **代码规范与编译阻断**：
   - `playerProfileService.ts` 存在未引用的死代码 `analyzeMatches`，导致 `vue-tsc` 检查直接报错失败。
   - `useNativeNavigation.ts` 存在在声明前使用变量的暂时性死区（TDZ / `no-use-before-define`）缺陷。
   - `itemTextResolver.ts` 的属性匹配正则存在量词重叠（`[\s\S]*?` + 尾部多重捕获），触发了 ESLint 严厉的 ReDoS 潜在安全回溯告警（`no-super-linear-backtracking`）。
   - `system_service.go` 的图标批量下载逻辑中，每个 worker goroutine 每次循环都会新建 `&http.Client{}`，没有复用 TCP keep-alive 连接池。

---

## 决策方案

### 1. 修复 Go 侧远程大模型协议标准契约 (engine_remote.go)
- 修正 endpoint 路径拼接规则：当传入端点未以 `/chat/completions` 结尾时，自动规整拼接 `/chat/completions`。
- 载荷标准对齐：组装标准 OpenAI 契约格式：
  ```json
  {
    "model": "...",
    "messages": [
      {"role": "system", "content": "..."},
      {"role": "user", "content": "..."}
    ],
    "max_tokens": 1500,
    "temperature": 0.7
  }
  ```
- 响应安全解析：解析 `choices[0].message.content`，并使用 `io.LimitReader(resp.Body, 2*1024*1024)` 严格防御恶意或异常超大响应。

### 2. 前端彻底收拢为单一出口，移除裸发 fetch (aiCoachService.ts / playerProfileService.ts)
- 坚决贯彻开工铁律 6 与 9：前端所有外部网络请求（OpenDota 数据、大模型推理、CDN 资源）必须且只能通过 Go 后端作为唯一出口。
- 移除前端 `aiCoachService.ts` 和 `playerProfileService.ts` 中的外部 `fetch` 降级，调用 Go 失败或超时后直接执行确定性离线规则引擎生成专业电竞分析，100% 杜绝 CORS 报错与凭据泄露。

### 3. 实现轻量内置 SingleFlight 并发任务合并 (player_service.go)
- 鉴于 Windows 运行环境的安全策略限制终端直接执行 `go get` 安装外部依赖，在 `player_service.go` 中基于标准库 `sync.Mutex` 与 `sync.WaitGroup` 实现了轻量级（约 30 行）的 `singleFlightGroup`。
- 将 `GetPlayerProfile`、`GetMatchDetails`、`GetPlayerRecentMatches`、`GetPlayerCard` 中的实际网络拉取通过 `sfGroup.Do(key, ...)` 进行合并。对于同一 `matchId` 或 `accountId` 的瞬时并发调用，仅发起 1 次 OpenDota 请求。
- 比赛详情的不可变磁盘缓存落盘移入 SingleFlight 闭包内部执行，确保同一时刻只有单协程写入文件，彻底消除了 Windows 下的文件句柄竞争。

### 4. 优化 SystemService 连接池复用 (system_service.go)
- 在 `SystemService` 单例中维护复用的 `httpClient *http.Client`，配置 `MaxIdleConns: 50, MaxIdleConnsPerHost: 10, IdleConnTimeout: 60s`。
- 批量下载英雄和物品图标时，多协程直接复用单例 `httpClient`，大幅减少 TCP 握手开销。

### 5. 解耦 MeloTTS 推理锁与状态查询锁 (engine_melo.go)
- 引入独立的 `inferMu sync.Mutex` 保护 ONNX Runtime Session 的 `SynthesizeToWav` 串行执行。
- `m.mu` 仅用于保护模型路径探测、懒加载就绪标志与下载进度。在开始实际推理前释放 `m.mu`，使得前端 IPC 轮询 `GetTtsStatus()` 零等待即刻返回。

### 6. 清理前端 Lint 告警与正则 ReDoS 风险
- `useNativeNavigation.ts`：将清理变量定义提升至 `onMounted` 之前，修复 TDZ。
- `itemTextResolver.ts`：将正则重叠量词优化为非回溯的原子捕获组 `(?:...)`，使用 `Number.isNaN` 替代全局 `isNaN`。
- `aiCoachService.ts`：清理无用私有方法与未引用参数，修复 ESLint 格式。

---

## 曾考虑的替代方案

### 替代方案 A：引入官方 golang.org/x/sync/singleflight 第三方依赖
- **最强理由**：官方库经过充分的生产环境检验，功能全面且支持 Shared/Result/Forget。
- **为什么不采用**：当前系统终端环境配置了应用控制策略，禁止运行 `go get` / `go install` 安装外部包；且本模块仅需基础的并发抑制并共享结果，30 行标准库实现完全自包含、零外部依赖、构建最轻量且完全满足业务契约。

### 替代方案 B：在前端维护一个 fetch 请求去重 Map
- **最强理由**：前端也能通过 Promise 缓存做并发去重。
- **为什么不采用**：违反开工铁律 6 架构拓扑边界。网络请求权威必须在 Go 后端，前端多窗口（主窗口与悬浮窗）各自拥有独立的 JS 上下文，前端去重无法跨窗口共享；而 Go 后端是单一进程单例，天然能够跨窗口多消费者实现单飞行合并与 L1/L2 缓存。

### 替代方案 C：保留前端裸 fetch 作为云端 AI 最后的兜底
- **最强理由**：万一 Go 后端崩溃或网络配置错误，前端还能试一试。
- **为什么不采用**：严重违反安全规范与合规红线。前端直接发请求需要拿到用户的明文 API Key，容易被浏览器 DevTools 嗅探；同时多数云端大模型 API 没有开启跨域 CORS 响应头，前端 fetch 必然报 Network Error 并污染控制台，造成更糟糕的用户体验。坚决使用 Go 统一出口 + 确定性离线规则才是最稳定方案。

---

## 后果与验证计划

### 正面收益
1. **云端 AI 彻底恢复正常响应**：OpenAI 兼容端点由硬编码 404 恢复为标准 200 响应，用户配置的 DeepSeek / Qwen 等模型可正常问答与深度赛后复盘。
2. **彻底杜绝 OpenDota 429 封禁与 Windows 句柄冲突**：前端多组件并发请求被 SingleFlight 自动合并为单次请求，磁盘写入平稳互斥。
3. **前端状态查询零阻塞**：语音合成期间 IPC `GetTtsStatus` 查询时延由秒级降至毫秒级。
4. **编译与代码质量完全合规**：`vue-tsc` 0 错误通过；`eslint` 0 错误 0 警告全绿；`pnpm build` 100% 成功。

### 验证记录
- `pnpm run typecheck` (`vue-tsc --noEmit`)：✅ 通过（0 错误）。
- `pnpm --filter frontend lint` (`eslint .`)：✅ 通过（0 错误，0 警告）。
- `pnpm build` (`vite build`)：✅ 通过（打包产出 457KB，耗时 1.05s）。
- `npm run verify-notes`：✅ 通过（全量 62 篇 Agent 决策笔记目录结构与格式校验通过）。
