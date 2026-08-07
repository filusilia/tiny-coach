# Steam Dota 2 GSI 接入手册

本手册用于把 Steam 版 Dota 2 的 Game State Integration（GSI）快照接入神盾教练。GSI 只通过本机 HTTP 把游戏状态发送到 `127.0.0.1`，不读取游戏进程内存，也不执行游戏操作。

## 1. 准备

- Windows 上已安装 Steam、Dota 2 和神盾教练。
- 先启动神盾教练，再配置 GSI。
- 配置完成后必须重启 Dota 2；Dota 2 不会在运行中自动重新加载修改后的 cfg。

## 2. 在应用中设置

打开神盾教练的“实时教练”页面：

| 设置 | 默认值 | 说明 |
|---|---:|---|
| 端口 | `4444` | 应用监听 `http://127.0.0.1:4444/`；端口被占用时改成其他未占用端口 |
| Token | 空 | 建议填写随机字符串；cfg 和应用必须完全一致，留空表示不校验 |
| cfg 目录 | 自动探测 | 自动扫描 Steam 库；自定义库未找到时手动选择目录 |
| 应用启动时自动监听 GSI | 开启 | 启动神盾教练后自动监听；关闭时需要手动点击“启动监听” |

设置保存到：

`%LOCALAPPDATA%\AegisCoach\settings.json`

安全建议：Token 只用于本机 GSI 校验，不要发布到截图、日志或聊天中。原始快照写入 `%LOCALAPPDATA%\AegisCoach\gsi\` 时会把 token 脱敏为 `[REDACTED]`。

## 3. 方式 A：应用自动安装

1. 在“实时教练”页面填写端口和 Token。
2. cfg 目录留空，点击“打开 cfg 目录”确认自动探测到的目录；如果没有找到，使用“选择目录”。
3. 点击“安装配置到 Dota 2”。
4. 确认提示中的文件路径，文件名必须是 `gamestate_integration_aegiscoach.cfg`。
5. 完全退出 Dota 2，再重新启动 Dota 2。
6. 回到神盾教练，确认状态为“GSI 监听中”；进入一场游戏后，连接状态应变为“已连接”，快照数量和当前比赛 ID 会更新。

自动探测的新版目录通常是：

`<Steam 库>\steamapps\common\dota 2 beta\game\dota\cfg\`

少数旧版布局使用：

`<Steam 库>\steamapps\common\dota 2 beta\dota\cfg\`

## 4. 方式 B：手动导出或复制

当 Steam 安装在非标准位置或自动探测失败时：

1. 在应用中填写端口、Token。
2. 点击“导出配置文件”，或点击“复制配置”。
3. 如果使用复制方式，在记事本中粘贴为 `gamestate_integration_aegiscoach.cfg`。
4. 从 Steam 打开 Dota 2 安装目录：库中右键 Dota 2 →“管理”→“浏览本地文件”。
5. 进入 `game\dota\cfg\`；如果该路径不存在，再检查 `dota\cfg\`。
6. 把 cfg 文件放入该目录，覆盖同名的旧文件。
7. 完全退出并重新启动 Dota 2。

如果不确定 Steam 库位置，不要猜盘符。使用 Steam 的“浏览本地文件”打开的 `dota 2 beta` 目录作为起点，再进入 `game\dota\cfg\`。

## 5. 验证链路

按下面的因果顺序检查：

1. 应用监听成功：页面显示“GSI 监听中”，端点为 `127.0.0.1:端口`。
2. Dota 2 读取 cfg：修改 cfg 后重启游戏，否则不会产生 POST。
3. URI 一致：cfg 中的 `uri` 必须和页面端口一致，例如 `http://127.0.0.1:4444/`。
4. Token 一致：cfg 的 `auth.token` 和页面 Token 必须逐字一致；若应用 Token 留空，cfg 也应由应用重新生成。
5. 数据到达：进入对局后连接状态变为“已连接”，快照数量增加。
6. 规则生效：血量、魔量或阶段变化后，页面出现带证据字段的教练提醒。

## 6. 常见问题

### 页面一直是“未连接”

先确认 Dota 2 已完全重启，再检查 cfg 的 `uri` 端口和应用端口。之后确认 Dota 2 正在对局或选人阶段；应用没有收到任何 POST 时不会显示“已连接”。

### 日志显示 token 校验失败

cfg 与应用 Token 不一致。回到页面重新填写 Token，点击“安装配置到 Dota 2”，再重启 Dota 2。不要手工只改一处。

### 安装按钮提示找不到目录

点击 Steam 中 Dota 2 的“浏览本地文件”，把路径指定到 `game\dota\cfg`，再点击“安装配置到 Dota 2”。选择的是目录，不是 `dota2.exe` 或 `dota 2 beta` 根目录。

### 端口启动失败

端口可能被其他程序或另一个神盾教练实例占用。关闭重复实例，或把端口改为 `4445`，然后重新安装 cfg；cfg 的 `uri` 也必须随之更新。

### cfg 已存在但没有生效

确认文件名没有变成 `gamestate_integration_aegiscoach.cfg.txt`，确认文件位于 `game\dota\cfg`，并完全退出重启 Dota 2。Steam 更新或校验游戏文件后，也应重新检查 cfg 是否仍在目标目录。

## 7. 本地记录位置

- GSI 设置：`%LOCALAPPDATA%\AegisCoach\settings.json`
- 原始快照：`%LOCALAPPDATA%\AegisCoach\gsi\snapshots-YYYY-MM-DD.ndjson`
- 安装失败时的导出 cfg：`%LOCALAPPDATA%\AegisCoach\gamestate_integration_aegiscoach.cfg`

操作完成后，将实际使用的目录、端口、是否重启 Dota 2 和页面最终状态追加到 `docs/OPERATION_LOG.md`；不要记录真实 Token。
