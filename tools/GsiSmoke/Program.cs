using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegisCoach.Core.Models;
using AegisCoach.Core.Services;

namespace AegisCoach.Smoke;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static async Task<int> Main()
    {
        Console.WriteLine("== AegisCoach GSI 冒烟测试 ==");
        await TestReceiverAndRulesAsync();
        TestConfigGeneration();
        TestConfigInstall();
        TestMatchSnapshotParsing();
        TestUltimateEdge();
        TestCompositeCoachRules();
        await TestTokenRejection();
        TestSnapshotRecorder();

        Console.WriteLine();
        Console.WriteLine($"通过 {_passed}，失败 {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static async Task TestReceiverAndRulesAsync()
    {
        Console.WriteLine("[1] 接收器 + 规则引擎端到端");
        var receiver = new GsiReceiver(port: 0, token: "smoke-token", disconnectTimeout: TimeSpan.FromSeconds(2));
        var engine = new CoachRuleEngine();
        MatchSnapshot? previous = null;
        var advice = new List<CoachAdvice>();
        receiver.SnapshotReceived += (_, snapshot) =>
        {
            advice.AddRange(engine.Evaluate(previous, snapshot));
            previous = snapshot;
        };

        receiver.Start();
        Check(receiver.IsRunning, "接收器已启动");
        Check(receiver.BoundPort > 0, $"已绑定端口 {receiver.BoundPort}");

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{receiver.BoundPort}/") };
        var token = new Dictionary<string, string> { ["token"] = "smoke-token" };

        // 1. 比赛进行中：满血满魔，大招冷却中
        var snap1 = BuildSnapshot(healthPercent: 100, manaPercent: 100, ultCooldown: 60, alive: true, gameTime: 120);
        await PostAsync(client, snap1, token);
        await Task.Delay(150);
        Check(receiver.SnapshotCount == 1, $"收到第 1 个快照（实际 {receiver.SnapshotCount}）");
        Check(previous is { MatchId: 12345, Phase: GamePhase.InProgress }, "快照解析为对局中且比赛 ID 正确");
        Check(previous!.Hero.HealthPercent == 100, "英雄血量为 100%");

        // 2. 血量降到 20%
        var snap2 = BuildSnapshot(healthPercent: 20, manaPercent: 80, ultCooldown: 40, alive: true, gameTime: 200);
        await PostAsync(client, snap2, token);
        await Task.Delay(150);
        Check(advice.Any(a => a.Id == "low_health" && a.Severity == AdviceSeverity.Critical), "低血量规则触发");
        Check(advice.First(a => a.Id == "low_health").Evidence.Count > 0, "低血量提醒携带证据字段");

        // 3. 大招冷却完成
        var snap3 = BuildSnapshot(healthPercent: 60, manaPercent: 60, ultCooldown: 0, alive: true, gameTime: 300);
        await PostAsync(client, snap3, token);
        await Task.Delay(150);
        Check(advice.Any(a => a.Id == "ult_ready"), "大招就绪规则触发");

        // 4. 阵亡
        var snap4 = BuildSnapshot(healthPercent: 0, manaPercent: 0, ultCooldown: 0, alive: false, gameTime: 420, respawn: 45);
        await PostAsync(client, snap4, token);
        await Task.Delay(150);
        Check(advice.Any(a => a.Id == "died"), "阵亡规则触发");
        Check(advice.First(a => a.Id == "died").Message.Contains("45"), "阵亡提醒包含复活时间");

        // 5. 重复快照去重：连续发送相同内容只处理一次
        var before = receiver.SnapshotCount;
        await PostAsync(client, snap4, token);
        await Task.Delay(150);
        Check(receiver.SnapshotCount == before, "重复快照被去重");

        // 6. 断开检测
        await Task.Delay(2600);
        Check(!receiver.IsConnected, "超过超时时间后连接状态变为断开");

        receiver.Stop();
        Check(!receiver.IsRunning, "接收器已停止");
    }

    private static void TestConfigGeneration()
    {
        Console.WriteLine("[2] GSI 配置生成");
        var cfg = GsiConfigInstaller.GenerateCfg(4444, "test-token-123");
        Check(cfg.Contains("http://127.0.0.1:4444/"), "配置包含监听地址与端口");
        Check(cfg.Contains("test-token-123"), "配置包含 token");
        Check(cfg.Contains("\"hero\" \"1\""), "配置启用 hero 数据");
        Check(GsiConfigInstaller.CfgFileName == "gamestate_integration_aegiscoach.cfg", "配置文件名符合 GSI 规范");
        Console.WriteLine($"    Dota 2 cfg 目录探测结果：{GsiConfigInstaller.LocateDotaCfgDirectory() ?? "未找到"}");
    }

    private static void TestConfigInstall()
    {
        Console.WriteLine("[3] cfg 安装到指定目录");
        var directory = Path.Combine(Path.GetTempPath(), "AegisCoachSmoke", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var result = GsiConfigInstaller.Install(directory, 4455, "guide-token");
            var filePath = Path.Combine(directory, GsiConfigInstaller.CfgFileName);
            Check(result.Success, "指定目录安装成功");
            Check(result.FilePath == filePath && File.Exists(filePath), "安装文件名和路径正确");
            var content = File.ReadAllText(filePath);
            Check(content.Contains("http://127.0.0.1:4455/") && content.Contains("guide-token"), "安装文件包含端口和 token");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestMatchSnapshotParsing()
    {
        Console.WriteLine("[4] 快照解析边界");
        var empty = MatchSnapshot.FromGsi(new GsiSnapshot());
        Check(empty.Phase == GamePhase.Unknown, "空快照解析为未知阶段");
        Check(empty.Abilities.Count == 0 && empty.Items.Count == 0, "空快照无技能与物品");

        var lobby = new GsiSnapshot
        {
            Map = new GsiMap { GameState = "DOTA_GAMERULES_STATE_HERO_SELECTION", MatchId = 0 },
            Player = new GsiPlayer { SteamId = "76561198000000000" },
        };
        var parsed = MatchSnapshot.FromGsi(lobby);
        Check(parsed.Phase == GamePhase.HeroSelection, "选人阶段解析正确");
        Check(parsed.MatchId == 0 && !parsed.InMatch, "未开赛时 InMatch 为 false");
    }

    private static void TestUltimateEdge()
    {
        Console.WriteLine("[5] 大招就绪边界（无 IsUltimate 标记时回退到 ability_3）");
        var engine = new CoachRuleEngine();
        MatchSnapshot? previous = null;
        var advice = new List<CoachAdvice>();

        void Feed(GsiSnapshot snapshot)
        {
            var current = MatchSnapshot.FromGsi(snapshot);
            advice.AddRange(engine.Evaluate(previous, current));
            previous = current;
        }

        Feed(new GsiSnapshot
        {
            Map = new GsiMap { GameState = "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS", MatchId = 1, GameTime = 60 },
            Hero = new GsiHero { Alive = true, Level = 6, MaxHealth = 100, HealthPercent = 100, MaxMana = 100, ManaPercent = 100 },
            Player = new GsiPlayer { UltimateState = "DOTA_ULTIMATE_STATE_LEVEL_READY" },
            Abilities = new Dictionary<string, GsiAbility>
            {
                ["ability_3"] = new() { Name = "antimage_mana_void", Level = 1, AbilityCooldown = 100, CanCast = true },
            },
        });
        Feed(new GsiSnapshot
        {
            Map = new GsiMap { GameState = "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS", MatchId = 1, GameTime = 90 },
            Hero = new GsiHero { Alive = true, Level = 6, MaxHealth = 100, HealthPercent = 100, MaxMana = 100, ManaPercent = 100 },
            Player = new GsiPlayer { UltimateState = "DOTA_ULTIMATE_STATE_READY", UltimateCooldown = 0 },
            Abilities = new Dictionary<string, GsiAbility>
            {
                ["ability_3"] = new() { Name = "antimage_mana_void", Level = 1, AbilityCooldown = 0, CanCast = true },
            },
        });
        Check(advice.Any(a => a.Id == "ult_ready"), "大招从冷却变为就绪时触发");
    }

    private static void TestCompositeCoachRules()
    {
        Console.WriteLine("[6] 复合教练规则");
        var ready = new MatchSnapshot
        {
            Phase = GamePhase.InProgress,
            MatchId = 11,
            Hero = new HeroState { Alive = true, HealthPercent = 90, ManaPercent = 80, Silences = 1 },
            Player = new PlayerState { UltimateState = "DOTA_ULTIMATE_STATE_READY" },
        };
        var readyAdvice = new CoachRuleEngine().Evaluate(null, ready);
        Check(readyAdvice.Any(item => item.Id == "control_locked"), "控制锁定规则触发");
        Check(readyAdvice.Any(item => item.Id == "fight_ready"), "资源完整规则触发");

        var dead = new MatchSnapshot
        {
            Phase = GamePhase.InProgress,
            MatchId = 12,
            Hero = new HeroState { Alive = false, BuybackCost = 1000, BuybackCooldown = 0 },
            Player = new PlayerState { Gold = 1500 },
        };
        Check(new CoachRuleEngine().Evaluate(null, dead).Any(item => item.Id == "buyback_window"), "买活窗口规则触发");
    }

    private static async Task TestTokenRejection()
    {
        Console.WriteLine("[7] token 校验");
        var receiver = new GsiReceiver(port: 0, token: "correct-token");
        var received = 0;
        receiver.SnapshotReceived += (_, _) => received++;
        receiver.Start();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{receiver.BoundPort}/") };

        await PostAsync(client, BuildSnapshot(100, 100, 0, true, 10), new Dictionary<string, string> { ["token"] = "wrong-token" });
        await Task.Delay(150);
        Check(received == 0, "错误 token 的快照被拒绝");

        await PostAsync(client, BuildSnapshot(100, 100, 0, true, 10), new Dictionary<string, string> { ["token"] = "correct-token" });
        await Task.Delay(150);
        Check(received == 1, "正确 token 的快照被接受");
        receiver.Stop();
    }

    private static void TestSnapshotRecorder()
    {
        Console.WriteLine("[8] 原始快照落盘与 token 脱敏");
        var directory = Path.Combine(Path.GetTempPath(), "AegisCoachSmoke", Guid.NewGuid().ToString("N"));
        var recorder = new GsiSnapshotRecorder(directory);
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            provider = new { name = "Dota 2" },
            map = new { matchid = 9876 },
            auth = new { token = "secret-token" },
        });

        Check(recorder.Record(body, new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.FromHours(8))), "快照记录写入成功");
        Check(recorder.LastFilePath is not null && File.Exists(recorder.LastFilePath), "按日期生成 NDJSON 文件");
        var line = File.ReadAllLines(recorder.LastFilePath!)[0];
        var json = JsonNode.Parse(line);
        Check(json?["snapshot"]?["map"]?["matchid"]?.GetValue<int>() == 9876, "记录保留快照字段");
        Check(json?["snapshot"]?["auth"]?["token"]?.GetValue<string>() == "[REDACTED]", "记录中的 token 已脱敏");
    }

    private static GsiSnapshot BuildSnapshot(
        double healthPercent, double manaPercent, double ultCooldown, bool alive, double gameTime, int respawn = 0)
    {
        var health = (int)(1000 * healthPercent / 100);
        var mana = (int)(500 * manaPercent / 100);
        return new GsiSnapshot
        {
            Provider = new GsiProvider { Name = "Dota 2", AppId = 570, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
            Map = new GsiMap
            {
                Name = "start",
                MatchId = 12345,
                GameTime = gameTime,
                ClockTime = gameTime + 1000,
                Daytime = true,
                RadiantScore = 10,
                DireScore = 8,
                GameState = "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS",
            },
            Player = new GsiPlayer
            {
                SteamId = "76561198000000000",
                Name = "SmokePlayer",
                TeamName = "radiant",
                Kills = 5,
                Deaths = 2,
                Assists = 8,
                LastHits = 120,
                Denies = 15,
                Gold = 3200,
                Gpm = 520,
                Xpm = 560,
                UltimateState = ultCooldown > 0 ? "DOTA_ULTIMATE_STATE_READY" : "DOTA_ULTIMATE_STATE_READY",
                UltimateCooldown = (int)ultCooldown,
                RespawnSeconds = respawn,
            },
            Hero = new GsiHero
            {
                Id = 1,
                Name = "npc_dota_hero_antimage",
                Level = 12,
                Xp = 12000,
                Alive = alive,
                RespawnSeconds = respawn,
                Health = health,
                MaxHealth = 1000,
                HealthPercent = healthPercent,
                Mana = mana,
                MaxMana = 500,
                ManaPercent = manaPercent,
            },
            Abilities = new Dictionary<string, GsiAbility>
            {
                ["ability_0"] = new() { Name = "antimage_mana_break", Level = 4, CanCast = true, Passive = true },
                ["ability_1"] = new() { Name = "antimage_blink", Level = 4, CanCast = true, AbilityCooldown = 6 },
                ["ability_2"] = new() { Name = "antimage_counterspell", Level = 1, CanCast = true, AbilityCooldown = 15 },
                ["ability_3"] = new() { Name = "antimage_mana_void", Level = 2, CanCast = true, Ultimate = true, AbilityCooldown = ultCooldown },
            },
            Items = new Dictionary<string, GsiItem>
            {
                ["slot0"] = new() { Name = "item_travel_boots", Purchaser = 1, CanCast = false, Cooldown = 0 },
                ["slot1"] = new() { Name = "item_abyssal_blade", Purchaser = 1, CanCast = true, Cooldown = 20 },
            },
        };
    }

    private static async Task PostAsync(HttpClient client, GsiSnapshot snapshot, Dictionary<string, string> token)
    {
        var payload = new Dictionary<string, object> { ["auth"] = token };
        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty)
        {
            Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["provider"] = snapshot.Provider!,
                ["map"] = snapshot.Map!,
                ["player"] = snapshot.Player!,
                ["hero"] = snapshot.Hero!,
                ["abilities"] = snapshot.Abilities!,
                ["items"] = snapshot.Items!,
                ["auth"] = token,
            }), Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"    POST 失败：{(int)response.StatusCode}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  PASS  {message}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  FAIL  {message}");
        }
    }
}


