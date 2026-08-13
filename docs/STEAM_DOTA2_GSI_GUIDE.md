# Steam Dota 2 实时连接

神盾教练通过 Dota 2 Game State Integration（GSI）接收本机游戏状态。它只监听本机连接，不读取游戏进程内存，也不会执行游戏操作。

## 首次配置

1. 第一次打开应用时，先完成“首次设置”。此时应用不会加载资料、启动智能服务或连接 Dota 2。
2. 点击“保存并开始”。应用会自动查找 Steam 库并安装 `gamestate_integration_aegiscoach.cfg`。
3. 完全退出并重新启动 Dota 2。Dota 2 只在启动时读取 GSI 配置。
4. 进入选人或对局后，实时教练状态应从“未连接”变为“已连接”。主菜单通常不会发送 GSI 数据。

应用内部固定使用本机监听参数，普通用户不需要配置端口或 Token。配置文件与监听器由应用一起生成，避免两边不一致。

## 自动查找失败

在设置中展开“高级设置”，点击“Dota 2 安装位置”右侧的“选择”，定位到以下目录之一：

`<Steam 库>\steamapps\common\dota 2 beta\game\dota\cfg\gamestate_integration\`

少数旧布局使用：

`<Steam 库>\steamapps\common\dota 2 beta\dota\cfg\gamestate_integration\`

选择后再次点击“自动配置 Dota 2”。配置文件必须位于 `cfg\gamestate_integration\` 子目录，放在 `cfg\` 根目录不会被游戏读取。

## 常见问题

### 一直显示“未连接”

先完全重启 Dota 2，再进入选人或对局。仍未连接时，在设置中点击“打开配置目录”，确认存在 `gamestate_integration_aegiscoach.cfg`。不要手工修改文件内容，重新执行“自动配置 Dota 2”即可覆盖修复。

### 提示找不到 Dota 2

从 Steam 库中右键 Dota 2，选择“管理”与“浏览本地文件”，再按上面的路径进入 `cfg\gamestate_integration`，在高级设置中选择该目录。

### 提示监听启动失败

通常是另一个神盾教练实例仍在运行。关闭重复实例后重新启动应用。监听参数由应用内部管理，不需要手工换端口。

## 本地文件

应用数据统一保存在用户的 `文档\AegisCoach\`：

- `settings.json`：应用设置和首次设置完成状态。
- `cache\`：英雄、物品和统计缓存。
- `gsi\snapshots-YYYY-MM-DD.ndjson`：脱敏后的对局快照。
- `analysis\`：赛后分析报告。
- `habits\player-habits.json`：本地对局习惯记录。
- `models\`：本地智能解释模型。

应用不读取旧 AppData 配置，也不会把缓存写入 AppData。
