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
    participant Store as Vue 3 Reactive Store & UI

    Dota->>GoHTTP: HTTP POST (application/json, ~10Hz)
    GoHTTP->>GoHTTP: 校验 Auth Token & 存入最新快照
    GoHTTP-->>Dota: 立即响应 200 OK (微秒级)
    GoHTTP->>Wails: runtime.EventsEmit("dota_gsi_update", rawPayload)
    Wails->>Engine: 顺序处理快照差分与规则判定
    loop 状态差分与 9 类提醒冷却检查
        Engine->>Engine: 检查血量/蓝量/控制/神符/肉山/出装及独立冷却
        alt 命中确定性战术建议
            Engine->>Store: 派发提醒 (结合三段式开关: 仅文字 / 语音+文字)
            Store->>Store: 触发电竞 TTS 语音合成与文字高亮
        end
    end
    Wails->>Store: 合并最新战局概览 (阶段/时间/比分/经济/建筑)
```

### 关键运行语义：
- **极速应答**：基于 Go 原生 `net/http` 在后台 Goroutine 接收 HTTP POST，在 0.2ms 内返回 200 OK，杜绝影响 Dota 2 客户端网络。
- **单消费者顺序处理**：所有快照由 Go 接收器直接通过 Wails 运行时事件管道派发，前端规则引擎单通道顺序处理，彻底解耦 UI 渲染主线程。
- **断线与心跳管理**：若超过 5 秒未收到新快照，状态自动切换为“已断开连接”，重置瞬态并通知前端展示断线状态。

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
    AiSwitch -- 是 --> Backend{"选择何种 AI 后端？"}
    
    Backend -- "本地 ONNX / NPU 推理" --> NpuRun["本地量化模型推理"]
    Backend -- "云端 API" --> CloudRun["云端 OpenAI 兼容接口 (6 秒超时熔断)"]
    
    NpuRun & CloudRun --> Check{"推理是否成功且输出有效？"}
    Check -- 成功 --> UI_Update["异步平滑更新 UI 建议文本"]
    Check -- 超时 / 失败 / 格式异常 --> UI_Fallback["秒级无缝保持规则原文，记录退化原因"]
```

### 三级降级策略：
1. **三档语音播报滑块**：
   - 档位 0：关闭（静音且不提示）；
   - 档位 1：仅文字播报（界面滚动高亮，不发声）；
   - 档位 2：播报-语音（文字同步 + 电竞 TTS 实时合成播报）。
2. **云端 OpenAI 兼容 API**：轻量 HTTP 请求，设置 6 秒超时熔断，避免网络波动挂起。
3. **规则原文兜底（绝对防线）**：任何 AI 异常、网络中断或超时，实时教练界面均正常展示规则引擎原文，GSI 吞吐量 0 损失。

---

## 4. GSI 配置文件自动安装机制

Dota 2 需在指定目录加载 GSI 配置文件：
- 路径格式：`[Steam安装路径]/steamapps/common/dota 2 beta/game/dota/cfg/gamestate_integration/gamestate_integration_aegis.cfg`
- 服务端口：`http://127.0.0.1:4444/`
- 缓冲区与心跳：`buffer` 设为 `0.1`，`heartbeat` 设为 `30.0`
- 数据订阅节点：`provider`、`map`、`player`、`hero`、`abilities`、`items`、`buildings`。
