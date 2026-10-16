# 神盾教练子系统运行语义与降级机制 (SUBSYSTEM_RUNTIMES.md)

本文档详述神盾教练各核心子系统在运行时的状态流转、因果机制、并发模型与容灾降级规则。

---

## 1. GSI 实时接收与单消费者事件管线

Dota 2 Game State Integration（GSI）是系统获取对局状态的唯一实时通道。

```mermaid
sequenceDiagram
    participant Dota as Dota 2 Client
    participant GoHTTP as Go GSI Receiver (HTTP 4444)
    participant Wails as Wails Event Stream
    participant Engine as Rules Engine (gsiRuleEngine.ts)
    participant Store as Pinia Reactive Store (battle.ts)
    participant UI as Vue 3 HUD View (CoachView.vue)

    Dota->>GoHTTP: HTTP POST (application/json, ~10Hz)
    GoHTTP->>GoHTTP: 校验 Auth Token & 存入最新快照
    GoHTTP-->>Dota: 立即响应 200 OK (0.2ms 内微秒级响应)
    GoHTTP->>Wails: app.Event.Emit("gsi_data", rawPayload)
    Wails->>Engine: 顺序处理快照差分与规则判定
    loop 状态差分与 9 类提醒冷却检查
        Engine->>Engine: 检查血量/蓝量/控制/神符/肉山/出装及独立冷却
        alt 命中确定性战术建议
            Engine->>Store: 派发提醒 (结合三段式开关: 仅文字 / 语音+文字)
            Store->>UI: 界面高亮与动画更新
            Store->>GoHTTP: 触发本地 MeloTTS 语音合成与播报
        end
    end
    Wails->>Store: 合并最新战局概览 (阶段/时间/比分/经济/建筑)
```

### 关键运行语义：
- **极速应答**：基于 Go 原生 `net/http` 在后台 Goroutine 接收 HTTP POST，在 0.2ms 内返回 200 OK，杜绝影响 Dota 2 客户端网络。
- **单消费者顺序处理**：所有快照由 Go 接收器直接通过 Wails 运行时事件管道派发，前端规则引擎单通道顺序处理，彻底解耦 UI 渲染主线程。
- **断线与心跳管理**：若超过 3 秒未收到新快照，状态自动切换为“已断开连接”，重置瞬态并通知前端展示断线状态。
- **端口自定义与优雅重启**：支持用户在设置中自定义监听端口，配置更新时平滑重启 HTTP Server。

---

## 2. 确定性规则引擎与独立冷却计算

规则引擎负责产生带证据链（Evidence）的确定性提醒，**拥有最终的告警触发裁定权**。

### 规则分类与触发因果链：
| 规则类别 | 判定条件（基于相邻快照差分） | 证据字段 (Evidence) | 默认独立冷却 |
|---|---|---|:---:|
| **生命危急** | 当前生命值百分比 < 30% 且处于存活状态 | `health_percent`, `alive` | 8 秒 |
| **法力耗尽** | 当前法力值百分比 < 20% 且附近有敌方/处于危险 | `mana_percent`, `alive` | 10 秒 |
| **关键大招就绪** | 终极技能从 CoolingDown 变为 Ready | `ability.name`, `ability.cooldown` | 15 秒 |
| **阵亡与买活** | 英雄从存活变为死亡；买活资金充足且战局关键 | `respawn_seconds`, `buyback_cooldown` | 15 秒 |
| **控制锁定** | 自身获得眩晕 (Stunned) / 沉默 (Silenced) / 缠绕 (Rooted) | `is_stunned`, `is_silenced` | 5 秒 |
| **地图战略节点** | 4分钟圣水符 / 6分钟神符刷新前 15 秒；肉山击杀或刷新窗口 | `clock_time`, `roshan_state` | 单次事件 |
| **痛苦魔方倒计时** | 比赛 20 分钟痛苦魔方刷新倒计时 | `clock_time` | 单次事件 |

### 独立冷却（Per-Rule Cooldown）保证：
每条规则拥有独立的计时器，防止某一高频警告（如持续低血）压制其他重要状态（如关键大招转好）。

---

## 3. 实时教练 AI 旁路解释与降级机制

AI 模型在实时教练中**仅充当可选的自然语言短解释器**，绝不参与规则判决。

```mermaid
flowchart TD
    RuleHit["规则引擎触发带证据提醒"] --> UI_Fast["立即推向 UI 展示确定性规则原文 (0ms 延时)"]
    RuleHit --> AiSwitch{"AI 解释开关是否开启？"}
    AiSwitch -- 否 --> End["保持规则原文展示与语音播报"]
    AiSwitch -- 是 --> Backend{"选择何种 AI 引擎后端？"}
    
    Backend -- "本地 Ollama 引擎" --> OllamaRun["本地端侧轻量模型推理 (Ollama API)"]
    Backend -- "云端 OpenAI / DeepSeek" --> CloudRun["云端兼容接口 (6 秒超时熔断)"]
    
    OllamaRun & CloudRun --> Check{"推理是否成功且输出有效？"}
    Check -- 成功 --> UI_Update["异步平滑更新 UI 建议文本"]
    Check -- 超时 / 失败 / 格式异常 --> UI_Fallback["秒级无缝保持规则原文，记录退化原因"]
```

### 三级降级策略：
1. **三档语音播报滑块**：
   - 档位 0：关闭（静音且不提示）；
   - 档位 1：仅文字播报（界面滚动高亮，不发声）；
   - 档位 2：播报-语音（文字同步 + MeloTTS 纯离线实时合成播报）。
2. **云端 OpenAI 兼容 API**：轻量 HTTP 请求，设置 6 秒超时熔断，避免网络波动挂起。
3. **规则原文兜底（绝对防线）**：任何 AI 异常、网络中断或超时，实时教练界面均正常展示规则引擎原文，GSI 吞吐量 0 损失。

---

## 4. 离线语音子系统（预置 Fast-Path + 本地 MeloTTS 双通道架构）

系统采用双通道混合离线语音方案，兼顾极速 0ms 响应与全场景语义覆盖：

```mermaid
flowchart TD
    Text["输入战术播报文本"] --> Matcher{"Fast-Path 预置离线音频匹配\n(5大类核心基础内容)"}
    Matcher -- "命中预置切片 (地图资源/倒计时/事件/连杀/等级)" --> Preset["读取外置高保真压缩 MP3 (audio/preset/)\n0ms 延迟 · 0% 算力占用"]
    Matcher -- "未命中 (个性化战术出装/AI解释)" --> Lexicon["分词与电竞词表标准化"]
    Lexicon --> CAPI["ONNX Runtime C-API (onnxruntime.dll)"]
    CAPI --> Acoustic["本地 MeloTTS 声学模型推理 (model.onnx)"]
    Acoustic --> WavData["生成标准 PCM / WAV 音频流"]
    Preset & WavData --> Play["前端 HTMLAudioElement 播放\n(音量控制/防堆叠/实时打断)"]
```

### 关键运行保障：
- **0ms 免生成 Fast-Path**：地图资源刷新（赏金符/智慧符/莲花池/功能符/狂石/中立装备）、战略倒计时、系统战场事件（比赛开始/团灭/肉山/塔防/买活）、连杀多杀（双杀至暴走）、等级质变（6/12/15/18/25/30级）等高频固定事件，命中直接播放高音质外置 MP3（仅 ~3.28MB），彻底免除神经网络合成等待与 CPU 占用；
- **全动态语义兜底**：复杂长文本或个性战术建议平滑流转给纯 Go 原生离线 MeloTTS 神经网络合成，实现 100% 语音覆盖；
- **热身启动**：服务初始化时自动进行 1 次极简音频推理热身（Warmup），消除对局中初次播报的冷启动延迟；
- **并发互斥保护**：内部采用互斥锁保护单次模型推理流水线，避免并发重入造成 ONNX Runtime 崩溃；
- **发音纠错**：内置电竞专用词表（如“大晕锤”、“黑皇杖”、“跳刀”、“圣剑”等），确保英雄与物品术语发音准确标准。

---

## 5. 独立悬浮窗生命周期与 Win32 原生置顶保活机制

局内经济悬浮窗具备独立顶级窗口、常驻热备与透明置顶能力：

```mermaid
stateDiagram-v2
    [*] --> Hidden: 主线程启动前预热预建 (Hidden: true)
    Hidden --> Active: 用户触发 Alt+F2 或界面点击打开 (Show/Focus, 0ms瞬开)
    Active --> TopMostRefresher: 启动 500ms 后台保活协程
    TopMostRefresher --> Active: 剥离原生边框标题栏 (WS_POPUP) 并确保 HWND_TOPMOST
    Active --> MouseThrough: Alt+F3 切换鼠标穿透 (叠加 WS_EX_LAYERED 与 WS_EX_TRANSPARENT)
    MouseThrough --> Active: 再次 Alt+F3 恢复响应 (彻底剥离 WS_EX_TRANSPARENT 与 WS_EX_LAYERED)
    Active --> Hidden: 点击关闭 / Hide (保存窗口坐标，保持实例常驻热备)
    Active --> Closing: 主程序退出
    Closing --> [*]: 优雅退出保活循环并释放原生句柄
```

### 关键语义与防死锁设计：
- **无父属主 (Independent Top-Level)**：通过 `DetachWindowOwner` 清空 `GWLP_HWNDPARENT`，主窗口最小化或聚焦切换时，悬浮窗绝不会跟随隐藏或改变 Z 序；
- **DirectComposition 与分层样式解耦**：Wails v3 透明窗口基于 DirectComposition 渲染。正常展示时**严禁**注入 `WS_EX_LAYERED`，否则 DWM 会因缺少分层属性将窗口判定为 0 Alpha 隐形；仅在用户启用 Alt+F3 鼠标穿透时动态叠加 `WS_EX_LAYERED | WS_EX_TRANSPARENT`；
- **主线程常驻预热热备**：悬浮窗在 `main()` 启动阶段随主窗口在主线程预先创建完毕，初始隐藏；关闭时执行 `win.Hide()` 而非 `win.Close()`，彻底消除跨协程创建的 COM/STA 崩溃与二次打开失效；
- **防重入锁设计**：`attachWindowHooksLocked` 与 `attachWindowHooks` 严格隔离持锁与非持锁上下文，彻底杜绝重入死锁。

---

## 6. GSI 配置文件自动安装机制

Dota 2 需在指定目录加载 GSI 配置文件：
- 路径格式：`[Steam安装路径]/steamapps/common/dota 2 beta/game/dota/cfg/gamestate_integration/gamestate_integration_aegis.cfg`
- 服务端口：`http://127.0.0.1:4444/`（或自定义配置端口）
- 缓冲区与心跳：`buffer` 设为 `0.1`，`heartbeat` 设为 `30.0`
- 数据订阅节点：`provider`、`map`、`player`、`hero`、`abilities`、`items`、`draft`、`wearables`、`buildings`。

---

## 7. 原生 Win32 GDI 屏幕抓取与赛前 BP 视觉识别管线

针对 Dota 2 在正式出兵前选人阶段不向 GSI 广播敌方英雄的机制限制，系统建立纯合规外挂级旁路视觉管线：

```mermaid
sequenceDiagram
    participant User as 选手按 F10 盲按
    participant Hotkey as Win32 OS线程消息泵 (hotkey_windows.go)
    participant GDI as Go 原生 GDI 裁剪 (screen_capture_windows.go)
    participant Wails as Wails Event 管道
    participant ONNX as 前端 onnxruntime-web (heroDetector.ts)
    participant Rec as OpenDota 大数据针对出装推荐

    User->>Hotkey: 全局按键 F10 (带 600ms 防抖)
    Hotkey->>GDI: 触发屏幕顶部 18% ROI 局部抓取
    GDI->>GDI: BitBlt 内存位图复制 + 极速 PNG Base64 编码 (耗时 < 10ms)
    GDI->>Wails: app.Event.Emit("vision:capture-completed", payload)
    Wails->>ONNX: 虚拟全屏 Letterbox (640x640) 归一化输入
    ONNX->>ONNX: 端侧 yolov8n_hero.onnx 目标检测推理
    ONNX->>ONNX: 槽位空间几何匹配 (天辉/夜魇双方各 5 槽位)
    ONNX->>Rec: 自动驱动对位英雄大数据克制与针对性出装建议
    Rec->>Wails: 跨窗口实时广播至局内置顶悬浮窗 (OverlayView.vue)
```

### 关键运行保障：
- **无侵入非外挂原则**：严禁注入游戏、严禁 Hook 渲染、严禁读取游戏内存；仅通过标准 Win32 屏幕抓取 API 进行只读分析；
- **ROI 局部截取优化**：仅截取屏幕顶部 18% 双方英雄槽位区域，使像素数据量骤降 82% 以上，单次内存分配降至 ~5MB，完全不影响 Dota 2 客户端帧率；
- **OS 专属线程热键监听**：通过 `runtime.LockOSThread()` 独占系统线程运行 `GetMessage` 消息泵，确保对局全屏游戏中全局 F10 截屏 100% 毫秒级捕获。
