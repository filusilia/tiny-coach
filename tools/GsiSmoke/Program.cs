using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegisCoach.Core.Models;
using AegisCoach.Core.Services;
using AegisCoach.Native.Services;

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
        TestApplicationPaths();
        TestAtomicFile();
        TestConfigInstall();
        TestMatchSnapshotParsing();
        TestUltimateEdge();
        TestCompositeCoachRules();
        TestConfigurableCoachRules();
        TestNewCoachRules();
        TestCoachAiPromptEvidenceBoundary();
        await TestTokenRejection();
        TestSnapshotRecorder();
        TestSnapshotReplay();
        TestMatchOperationExtractor();
        TestApiMatchAnalyzer();
        await TestApiMatchAnalyzerRetryAsync();
        TestApiMatchAnalyzerRealFile();
        TestItemRecommendation();
        TestMatchSessionTracker();

        Console.WriteLine();
        Console.WriteLine($"通过 {_passed}，失败 {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void TestApplicationPaths()
    {
        Console.WriteLine("[2a] 应用数据目录");
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Check(AegisCoachPaths.Root == Path.Combine(documents, "AegisCoach"), "根目录位于用户文档文件夹");
        Check(AegisCoachPaths.Cache.StartsWith(AegisCoachPaths.Root, StringComparison.OrdinalIgnoreCase), "缓存位于应用文档目录");
        Check(AegisCoachPaths.SettingsFile.StartsWith(AegisCoachPaths.Root, StringComparison.OrdinalIgnoreCase), "设置位于应用文档目录");
        Check(!AegisCoachPaths.Root.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase), "不使用 AppData 目录");
    }

    private static void TestAtomicFile()
    {
        Console.WriteLine("[2b] 原子文件写入");
        var directory = Path.Combine(Path.GetTempPath(), "AegisCoachAtomicSmoke", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            AtomicFile.WriteAllText(path, "first");
            AtomicFile.WriteAllText(path, "second");
            Check(File.ReadAllText(path) == "second", "原子写入可安全覆盖旧文件");
            Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "成功后没有遗留临时文件");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
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
        Check(cfg.Contains("\"buildings\" \"1\""), "配置启用 buildings 数据");
        Check(cfg.Contains("\"roshan\" \"1\""), "配置启用 roshan 数据");
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

    private static void TestCoachAiPromptEvidenceBoundary()
    {
        Console.WriteLine("[3a] AI 提示词证据边界");
        var snapshot = new MatchSnapshot
        {
            Phase = GamePhase.InProgress,
            MatchId = 12345,
            GameTime = 900,
            RoshanAlive = false,
            Player = new PlayerState { TeamName = "radiant" },
            Hero = new HeroState { Alive = true },
        };

        var first = CoachAiPrompts.BuildContext(snapshot);
        var second = CoachAiPrompts.BuildContext(snapshot);
        Check(first == second, "相同快照生成确定性提示词");
        Check(first.Contains("GSI 未提供准确复活倒计时", StringComparison.Ordinal),
            "肉山死亡状态明确标注倒计时缺失");
        Check(!first.Contains("预计 8", StringComparison.Ordinal) &&
              !first.Contains("预计 9", StringComparison.Ordinal) &&
              !first.Contains("预计 10", StringComparison.Ordinal) &&
              !first.Contains("预计 11", StringComparison.Ordinal),
            "提示词不伪造肉山复活时间");
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
    private static void TestConfigurableCoachRules()
    {
        Console.WriteLine("[7] 可配置规则设置");
        var options = new CoachRuleOptions
        {
            EnableLowHealth = false,
            LowManaPercent = 40,
            CooldownSeconds = new Dictionary<string, int> { ["low_mana"] = 1 },
        };
        var engine = new CoachRuleEngine(options: options);
        var snapshot = new MatchSnapshot
        {
            Phase = GamePhase.InProgress,
            Hero = new HeroState { Alive = true, MaxHealth = 100, HealthPercent = 10, MaxMana = 100, ManaPercent = 35 },
        };
        var advice = engine.Evaluate(null, snapshot);
        Check(advice.All(item => item.Id != "low_health"), "关闭低血规则后不提醒");
        Check(advice.Any(item => item.Id == "low_mana"), "低魔阈值可调整");
        Check(engine.Cooldowns["low_mana"] == TimeSpan.FromSeconds(1), "规则冷却可以调整");
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

    private static void TestNewCoachRules()
    {
        Console.WriteLine("[7.5] 烟雾/天黑/建筑/肉山/道具技能就绪/关键时间点");
        var baseSnapshot = BuildMatchSnapshot(gameTime: 300);

        var withSmoke = BuildMatchSnapshot(gameTime: 300, items: new[]
        {
            new ItemState { Slot = "slot0", SlotKind = ItemSlotKind.Main, Name = "item_smoke_of_deceit" },
        });
        Check(AdviceIds(new CoachRuleEngine(), baseSnapshot, withSmoke).Contains("smoke_purchased"), "购入烟雾触发提醒");
        Check(AdviceIds(new CoachRuleEngine(), withSmoke, baseSnapshot).Contains("smoke_used"), "烟雾消失触发提醒");

        var smoked = BuildMatchSnapshot(gameTime: 300, smoked: true);
        Check(AdviceIds(new CoachRuleEngine(), baseSnapshot, smoked).Contains("smoked_active"), "处于烟雾状态触发提醒");

        var night = BuildMatchSnapshot(gameTime: 300, daytime: false);
        Check(AdviceIds(new CoachRuleEngine(), baseSnapshot, night).Contains("night_fall"), "天黑触发视野提醒");

        var withTower = BuildMatchSnapshot(gameTime: 300, team: "radiant", buildings: new[]
        {
            new BuildingState { Team = "radiant", TeamLabel = "天辉", Key = "radiant_top_tower1", Label = "上一塔", Alive = true, HealthPercent = 100 },
        });
        var towerGone = BuildMatchSnapshot(gameTime: 300, team: "radiant", buildings: new[]
        {
            new BuildingState { Team = "radiant", TeamLabel = "天辉", Key = "radiant_top_tower1", Label = "上一塔", Alive = false, HealthPercent = 0 },
        });
        Check(AdviceIds(new CoachRuleEngine(), withTower, towerGone).Contains("building_destroyed"), "建筑被摧毁触发提醒");

        var roshanAlive = BuildMatchSnapshot(gameTime: 300, roshanAlive: true);
        var roshanDead = BuildMatchSnapshot(gameTime: 300, roshanAlive: false);
        Check(AdviceIds(new CoachRuleEngine(), roshanAlive, roshanDead).Contains("roshan_killed"), "肉山被击杀触发提醒");
        Check(AdviceIds(new CoachRuleEngine(), roshanDead, roshanAlive).Contains("roshan_respawned"), "肉山复活触发提醒");

        var itemCooling = BuildMatchSnapshot(gameTime: 300, items: new[]
        {
            new ItemState { Slot = "slot0", SlotKind = ItemSlotKind.Main, Name = "item_blink", CanCast = true, Cooldown = 14 },
        });
        var itemReady = BuildMatchSnapshot(gameTime: 300, items: new[]
        {
            new ItemState { Slot = "slot0", SlotKind = ItemSlotKind.Main, Name = "item_blink", CanCast = true, Cooldown = 0 },
        });
        Check(AdviceIds(new CoachRuleEngine(), itemCooling, itemReady).Contains("item_ready"), "主动道具冷却完成触发提醒");

        var abilityCooling = BuildMatchSnapshot(gameTime: 300, abilities: new[]
        {
            new AbilityState { Slot = "ability_1", Name = "antimage_blink", Level = 4, CanCast = true, Cooldown = 15 },
        });
        var abilityReady = BuildMatchSnapshot(gameTime: 300, abilities: new[]
        {
            new AbilityState { Slot = "ability_1", Name = "antimage_blink", Level = 4, CanCast = true, Cooldown = 0 },
        });
        Check(AdviceIds(new CoachRuleEngine(), abilityCooling, abilityReady).Contains("ability_ready"), "关键技能就绪触发提醒");

        var before10 = BuildMatchSnapshot(gameTime: 590);
        var after10 = BuildMatchSnapshot(gameTime: 610);
        Check(AdviceIds(new CoachRuleEngine(), before10, after10).Contains("map_awareness"), "10 分钟敌方动向提醒触发");

        // 槽位分类：背包 / 中立 / 回城
        var classified = MatchSnapshot.FromGsi(new GsiSnapshot
        {
            Map = new GsiMap { GameState = "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS", MatchId = 1 },
            Items = new Dictionary<string, GsiItem>
            {
                ["slot6"] = new() { Name = "item_dust" },
                ["neutral0"] = new() { Name = "item_trusty_shovel" },
                ["teleport"] = new() { Name = "item_tpscroll" },
            },
        });
        Check(classified.Items.Any(item => item.Slot == "slot6" && item.SlotKind == ItemSlotKind.Backpack), "slot6 归类为背包");
        Check(classified.Items.Any(item => item.Slot == "neutral0" && item.SlotKind == ItemSlotKind.Neutral), "neutral0 归类为中立栏");
        Check(classified.Items.Any(item => item.Slot == "teleport" && item.SlotKind == ItemSlotKind.Teleport), "teleport 归类为回城栏");

        var disabled = new CoachRuleEngine(options: new CoachRuleOptions
        {
            EnableSmoke = false,
            EnableSmokedState = false,
            EnableNightFall = false,
            EnableBuildings = false,
            EnableRoshan = false,
            EnableItemReady = false,
            EnableAbilityReady = false,
            EnableMapAwareness = false,
        });
        Check(AdviceIds(disabled, withSmoke, baseSnapshot).All(id => id is not ("smoke_used" or "night_fall")), "关闭新规则后不触发");
    }

    private static MatchSnapshot BuildMatchSnapshot(
        double gameTime, bool daytime = true, bool? roshanAlive = null, string? team = "radiant",
        IReadOnlyList<ItemState>? items = null, IReadOnlyList<AbilityState>? abilities = null,
        IReadOnlyList<BuildingState>? buildings = null, bool smoked = false)
    {
        return new MatchSnapshot
        {
            Phase = GamePhase.InProgress,
            MatchId = 12345,
            GameTime = gameTime,
            Daytime = daytime,
            RoshanAlive = roshanAlive,
            Hero = new HeroState { Id = 1, Alive = true, IsSmoked = smoked, MaxHealth = 1000, HealthPercent = 100, MaxMana = 500, ManaPercent = 100 },
            Player = new PlayerState { TeamName = team, Gold = 3000 },
            Items = items ?? [],
            Abilities = abilities ?? [],
            Buildings = buildings ?? [],
        };
    }

    private static List<string> AdviceIds(CoachRuleEngine engine, MatchSnapshot previous, MatchSnapshot current)
    {
        return engine.Evaluate(previous, current).Select(item => item.Id).ToList();
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

    private static void TestSnapshotReplay()
    {
        Console.WriteLine("[9] NDJSON 离线回放");
        var directory = Path.Combine(Path.GetTempPath(), "AegisCoachSmoke", Guid.NewGuid().ToString("N"));
        var recorder = new GsiSnapshotRecorder(directory);
        var first = BuildSnapshot(100, 100, 60, true, 120);
        var second = BuildSnapshot(20, 80, 40, true, 180);
        var baseTime = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

        try
        {
            Check(recorder.Record(JsonSerializer.SerializeToUtf8Bytes(first), baseTime), "回放样本 1 写入成功");
            Check(recorder.Record(JsonSerializer.SerializeToUtf8Bytes(second), baseTime.AddSeconds(2)), "回放样本 2 写入成功");

            var result = new GsiReplayService().Replay(directory);
            Check(result.FilesRead == 1 && result.Frames.Count == 2, "回放读取两条有效快照");
            Check(result.Issues.Count == 0, "回放没有解析错误");
            Check(result.Advice.Any(item => item.Id == "low_health"), "回放重新触发低血量规则");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestMatchOperationExtractor()
    {
        Console.WriteLine("[9a] 回放操作提取（技能/道具/购买/阵亡/买活）");
        var directory = Path.Combine(Path.GetTempPath(), "AegisCoachSmoke", Guid.NewGuid().ToString("N"));
        var recorder = new GsiSnapshotRecorder(directory);
        var baseTime = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

        try
        {
            var frames = new List<GsiSnapshot>();
            frames.Add(BuildOpFrame(60, alive: true, kills: 0, deaths: 0, lh: 20, gold: 2500, hp: 100, blinkCd: 0, bkbCd: 0, ultCd: 0, hasBkb: false));
            frames.Add(BuildOpFrame(61, alive: true, kills: 0, deaths: 0, lh: 20, gold: 2500, hp: 100, blinkCd: 15, bkbCd: 0, ultCd: 0, hasBkb: false));
            frames.Add(BuildOpFrame(65, alive: true, kills: 0, deaths: 0, lh: 21, gold: 1900, hp: 100, blinkCd: 12, bkbCd: 0, ultCd: 0, hasBkb: true));
            frames.Add(BuildOpFrame(70, alive: true, kills: 0, deaths: 0, lh: 21, gold: 1900, hp: 100, blinkCd: 9, bkbCd: 90, ultCd: 0, hasBkb: true));
            frames.Add(BuildOpFrame(75, alive: true, kills: 0, deaths: 0, lh: 22, gold: 1900, hp: 100, blinkCd: 6, bkbCd: 85, ultCd: 70, hasBkb: true));
            frames.Add(BuildOpFrame(90, alive: true, kills: 0, deaths: 0, lh: 24, gold: 2600, hp: 25, blinkCd: 0, bkbCd: 70, ultCd: 0, hasBkb: true));
            frames.Add(BuildOpFrame(95, alive: false, kills: 0, deaths: 1, lh: 24, gold: 2600, hp: 0, blinkCd: 0, bkbCd: 70, ultCd: 0, hasBkb: true, buybackCost: 1200, buybackCd: 0));
            frames.Add(BuildOpFrame(100, alive: false, kills: 0, deaths: 1, lh: 24, gold: 1300, hp: 0, blinkCd: 0, bkbCd: 70, ultCd: 0, hasBkb: true, buybackCost: 1200, buybackCd: 480));
            frames.Add(BuildOpFrame(105, alive: true, kills: 0, deaths: 1, lh: 24, gold: 1300, hp: 100, blinkCd: 0, bkbCd: 70, ultCd: 0, hasBkb: true, buybackCost: 1200, buybackCd: 480));
            frames.Add(BuildOpFrame(120, alive: true, kills: 1, deaths: 1, lh: 30, gold: 1500, hp: 100, blinkCd: 0, bkbCd: 60, ultCd: 70, hasBkb: true, streak: 2));
            frames.Add(BuildOpFrame(300, alive: true, kills: 1, deaths: 1, lh: 60, gold: 3000, hp: 100, blinkCd: 0, bkbCd: 30, ultCd: 0, hasBkb: true, level: 18));

            // 跨局过滤：另一场比赛的记录不应进入提取结果。
            var otherMatch = BuildOpFrame(30, alive: true, kills: 9, deaths: 0, lh: 99, gold: 9999, hp: 100, blinkCd: 0, bkbCd: 0, ultCd: 0, hasBkb: true);
            otherMatch.Map!.MatchId = 99999;
            frames.Add(otherMatch);

            for (var index = 0; index < frames.Count; index++)
            {
                Check(recorder.Record(JsonSerializer.SerializeToUtf8Bytes(frames[index]), baseTime.AddSeconds(index * 2)),
                    $"操作样本 {index + 1} 写入成功");
            }

            var summary = new MatchOperationExtractor().Extract(
                directory, 12345, baseTime, baseTime.AddMinutes(10));
            Check(summary is not null, "提取到对局操作摘要");
            Check(summary!.MatchId == 12345, "按 matchid 过滤正确");
            Check(summary.Kills == 1 && summary.Deaths == 1 && summary.LastHits == 60, "汇总数据正确");
            Check(summary.Operations.Any(item => item.Kind == "cast" && item.Detail.Contains("Blink")), "提取到闪烁施放");
            Check(summary.Operations.Any(item => item.Kind == "cast" && item.Detail.Contains("Mana Void")), "提取到大招施放");
            Check(summary.Operations.Any(item => item.Kind == "item_use" && item.Detail.Contains("黑皇杖")), "提取到黑皇杖使用");
            Check(summary.Operations.Any(item => item.Kind == "buy" && item.Detail.Contains("黑皇杖")), "提取到购买黑皇杖");
            Check(summary.Operations.Any(item => item.Kind == "buyback"), "提取到买活");
            Check(summary.Operations.Any(item => item.Kind == "kill"), "提取到击杀");
            Check(summary.DeathsDetailed.Count == 1, "提取到一条阵亡上下文");
            var death = summary.DeathsDetailed[0];
            Check(death.DeathNumber == 1 && death.HealthPercentAtDeath == 25, "阵亡信息正确（次数与阵亡前血量）");
            Check(death.UltReadyAtDeath && death.TpAvailable, "阵亡时大招就绪且 TP 可用");
            Check(death.BuybackAvailable && death.BuybackUsed, "买活可用且已使用");
            Check(death.ReadyActivesUnused.Any(name => name.Contains("深渊之刃")), "阵亡时深渊之刃就绪未使用");
            Check(summary.Phases.Count >= 1 && summary.Phases[0].KillsDelta == 1 && summary.Phases[0].DeathsDelta == 1, "阶段统计正确");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static GsiSnapshot BuildOpFrame(
        double gameTime,
        bool alive,
        int kills,
        int deaths,
        int lh,
        int gold,
        double hp,
        double blinkCd,
        double bkbCd,
        double ultCd,
        bool hasBkb,
        int buybackCost = 0,
        int buybackCd = 0,
        int level = 12,
        int streak = 0)
    {
        var snapshot = BuildSnapshot(hp, 100, ultCd, alive, gameTime);
        snapshot.Player!.Kills = kills;
        snapshot.Player.Deaths = deaths;
        snapshot.Player.LastHits = lh;
        snapshot.Player.Gold = gold;
        snapshot.Player.KillStreak = streak;
        snapshot.Hero!.Level = level;
        snapshot.Hero.BuybackCost = buybackCost;
        snapshot.Hero.BuybackCooldown = buybackCd;
        snapshot.Abilities!["ability_1"].AbilityCooldown = blinkCd;
        snapshot.Abilities!["ability_3"].AbilityCooldown = ultCd;
        snapshot.Items!["slot1"].Cooldown = 0;
        if (hasBkb)
        {
            snapshot.Items!["slot2"] = new GsiItem
            {
                Name = "item_black_king_bar",
                Purchaser = 1,
                CanCast = true,
                Cooldown = bkbCd,
            };
        }
        return snapshot;
    }

    private static void TestApiMatchAnalyzer()
    {
        Console.WriteLine("[9b] OpenDota 整场数据分析（按比赛 ID）");
        using var document = JsonDocument.Parse(BuildApiFixtureJson());
        var summary = new ApiMatchAnalyzer().Analyze(
            document.RootElement,
            matchId: 8888888888,
            steamId64: "76561198000000000",
            expectedHeroId: 0,
            heroInternalNameResolver: heroId => heroId == 1 ? "npc_dota_hero_antimage" : string.Empty,
            itemNameResolver: key => key,
            itemIdResolver: id => id == 29 ? "item_29_mapped" : string.Empty);

        Check(summary is not null && string.IsNullOrWhiteSpace(summary!.Error), "按比赛 ID 解析出整场数据");
        Check(summary!.HeroId == 1 && summary.AccountId == 39734272 && summary.IsRadiant, "正确识别本人玩家（Steam ID 换算）");
        Check(summary.Kills == 5 && summary.Deaths == 3 && summary.LastHits == 200, "汇总 KDA/补刀正确");
        Check(summary.DeathTimes.SequenceEqual(new[] { 500d, 850d }), "从全场 kills_log 反推阵亡时间");
        Check(summary.KillsLog.Count == 2 && summary.KillsLog[0].TimeSeconds == 258, "击杀时间线正确");
        Check(summary.ItemTimings.Any(item => item.ItemName == "blink" && item.TimeSeconds == 400), "提取购买时间（闪烁匕首）");
        Check(summary.ItemTimings.Any(item => item.ItemName == "black_king_bar" && item.TimeSeconds == 720), "提取购买时间（黑皇杖）");
        Check(summary.ItemTimings.All(item => item.ItemName is not ("tango" or "ward_observer" or "recipe_blink")), "过滤消耗品/卷轴/初始物品");
        Check(summary.BuybackTimes.SequenceEqual(new[] { 900d }), "提取买活时间");
        Check(summary.KilledBy.First().HeroName == "Pudge" && summary.KilledBy.First().Count == 2, "阵亡来源统计正确");
        Check(summary.Teamfights.Count == 1 && summary.Teamfights[0].Participated
            && summary.Teamfights[0].MyDamage == 1200 && summary.Teamfights[0].MyDeaths == 1, "团战参与与伤害正确");
        Check(summary.TowersTaken.Count == 1 && summary.TowersTaken[0].TimeSeconds == 600, "本人推塔事件正确");
        Check(summary.Benchmarks.TryGetValue("gold_per_min", out var pct) && Math.Abs(pct - 82) < 0.01, "经济分位正确");
        Check(summary.ObserversPlaced == 8 && summary.ObserverLogCount == 2, "视野统计正确");
        Check(summary.Economy.Count == 25, "每分钟经济曲线正确");
        Check(summary.AllPlayers.Count == 2, "双方阵容汇总正确");
        Check(summary.FinalItems.Contains("item_29_mapped"), "数字 ID 装备也能解析");
    }

    private static async Task TestApiMatchAnalyzerRetryAsync()
    {
        Console.WriteLine("[9c] OpenDota 瞬时失败重试");
        var handler = new RetryFixtureHandler(BuildApiFixtureJson());
        using var client = new HttpClient(handler);
        var summary = await new ApiMatchAnalyzer(client).FetchAsync(
            8888888888,
            "76561198000000000",
            0,
            heroId => heroId == 1 ? "npc_dota_hero_antimage" : string.Empty,
            key => key,
            itemId => $"item_{itemId}_mapped",
            CancellationToken.None);
        Check(handler.RequestCount == 2, "HTTP 429 后执行一次退避重试");
        Check(summary is { HeroId: 1 }, "重试成功后正常解析比赛数据");
    }

    private static void TestApiMatchAnalyzerRealFile()
    {
        var realFile = Environment.GetEnvironmentVariable("AEGISCOACH_REAL_MATCH");
        if (string.IsNullOrWhiteSpace(realFile) || !File.Exists(realFile)) return;
        Console.WriteLine("[9c] 真实 OpenDota 数据验证（可选）");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(realFile));
            var summary = new ApiMatchAnalyzer().Analyze(
                document.RootElement,
                matchId: 0,
                steamId64: "76561198064843317",
                expectedHeroId: 0,
                heroInternalNameResolver: heroId => heroId == 102 ? "npc_dota_hero_abaddon" : string.Empty,
                itemNameResolver: key => key,
                itemIdResolver: id => id.ToString());
            Check(summary is not null && summary!.HeroId > 0, "真实数据能定位玩家并解析");
            Console.WriteLine($"    英雄 {summary?.HeroId} KDA {summary?.Kills}/{summary?.Deaths}/{summary?.Assists} · " +
                $"阵亡时间{summary?.DeathTimes.Count} · 装备时间{summary?.ItemTimings.Count} · " +
                $"团战{summary?.Teamfights.Count} · 推塔{summary?.TowersTaken.Count} · 经济点{summary?.Economy.Count}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"    真实数据验证失败：{exception.Message}");
        }
    }

    private sealed class RetryFixtureHandler(string json) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests));
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static string BuildApiFixtureJson() =>
        """
        {
          "radiant_win": true,
          "duration": 1500,
          "radiant_score": 30,
          "dire_score": 25,
          "game_mode": 22,
          "players": [
            {
              "player_slot": 0,
              "account_id": 39734272,
              "hero_id": 1,
              "kills": 5,
              "deaths": 3,
              "assists": 10,
              "last_hits": 200,
              "denies": 12,
              "gold_per_min": 520,
              "xp_per_min": 460,
              "level": 18,
              "net_worth": 12000,
              "rank_tier": 60,
              "obs_placed": 8,
              "sen_placed": 4,
              "teamfight_participation": 0.55,
              "hero_damage": 15000,
              "tower_damage": 2000,
              "hero_healing": 500,
              "actions_per_min": 120,
              "pings": 30,
              "lane": "mid",
              "lane_role": 2,
              "lane_efficiency_pct": 0.8,
              "roshans_killed": 1,
              "item_0": "item_power_treads",
              "item_1": "item_blink",
              "item_2": "item_black_king_bar",
              "item_3": 29,
              "item_4": "",
              "item_5": "",
              "purchase_time": {
                "tango": -59,
                "branches": -118,
                "ward_observer": 300,
                "recipe_blink": 380,
                "blink": 400,
                "black_king_bar": 720,
                "tpscroll": 800
              },
              "kills_log": [
                {"time": 258, "key": "npc_dota_hero_silencer"},
                {"time": 500, "key": "npc_dota_hero_pudge"}
              ],
              "buyback_log": [{"time": 900}],
              "gold_t": [100,200,300,400,500,600,700,800,900,1000,1100,1200,1300,1400,1500,1600,1700,1800,1900,2000,2100,2200,2300,2400,2500],
              "lh_t": [5,10,15,20,25,30,35,40,45,50,55,60,65,70,75,80,85,90,95,100,105,110,115,120,125],
              "xp_t": [100,200,300,400,500,600,700,800,900,1000,1100,1200,1300,1400,1500,1600,1700,1800,1900,2000,2100,2200,2300,2400,2500],
              "killed_by": {"npc_dota_hero_pudge": 2, "npc_dota_hero_witch_doctor": 1},
              "killed": {"npc_dota_hero_silencer": 3, "npc_dota_hero_pudge": 2},
              "benchmarks": {"gold_per_min": {"raw": 520, "pct": 82}, "xp_per_min": {"raw": 460, "pct": 70}},
              "obs_log": [{"time": 67}, {"time": 322}]
            },
            {
              "player_slot": 1,
              "account_id": 111,
              "hero_id": 2,
              "kills": 1,
              "deaths": 5,
              "assists": 2,
              "last_hits": 50,
              "denies": 3,
              "gold_per_min": 300,
              "xp_per_min": 350,
              "level": 12,
              "item_0": "item_tango",
              "purchase_time": {},
              "kills_log": [
                {"time": 500, "key": "npc_dota_hero_antimage"},
                {"time": 850, "key": "npc_dota_hero_antimage"}
              ]
            }
          ],
          "teamfights": [
            {
              "start": 300,
              "end": 340,
              "deaths": 2,
              "players": [
                {"deaths_pos": [], "deaths": 1, "buybacks": 0, "damage": 1200, "gold_delta": 300, "xp_delta": 400},
                {"deaths_pos": [], "deaths": 0, "buybacks": 0, "damage": 500, "gold_delta": 100, "xp_delta": 100}
              ]
            }
          ],
          "objectives": [
            {"time": 24, "type": "CHAT_MESSAGE_FIRSTBLOOD", "key": "2", "player_slot": 129},
            {"time": 600, "type": "building_kill", "key": "npc_dota_badguys_tower1_top", "player_slot": 0},
            {"time": 610, "type": "building_kill", "key": "npc_dota_goodguys_tower1_top", "player_slot": 129}
          ]
        }
        """;

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

    private static void TestItemRecommendation()
    {
        Console.WriteLine("[10] 全场英雄出装推荐");
        var profiles = new Dictionary<int, HeroProfile>
        {
            [1] = new("npc_dota_hero_antimage", "敌法", "agi"),
            [2] = new("npc_dota_hero_zuus", "宙斯", "int"),
            [3] = new("npc_dota_hero_lina", "莉娜", "int"),
            [4] = new("npc_dota_hero_dazzle", "戴泽", "int"),
        };
        var heroes = new List<MatchHeroRef>
        {
            new(1, true),
            new(2, true),
            new(3, true),
            new(4, true),
        };
        var suggestions = ItemRecommendationEngine.Recommend(
            new ItemRecommendationContext(heroes, profiles, 1200, 1500));
        Check(suggestions.Any(item => item.ItemName.Contains("黑皇杖")), "法术威胁多时推荐黑皇杖");
        Check(suggestions.Any(item => item.ItemName.Contains("否决")), "有治疗英雄时推荐否决挂穗");
        Check(suggestions.All(item => !string.IsNullOrWhiteSpace(item.Evidence)), "每条建议都带证据");
    }

    private static void TestMatchSessionTracker()
    {
        Console.WriteLine("[11] 对局会话跟踪（习惯记录 / 赛后分析数据源）");
        var tracker = new MatchSessionTracker();

        var before = new MatchSnapshot
        {
            Phase = GamePhase.InProgress,
            MatchId = 12345,
            GameTime = 600,
            RoshanAlive = true,
            Hero = new HeroState
            {
                Id = 1,
                Alive = true,
                Level = 12,
                MaxHealth = 1000,
                HealthPercent = 100,
                MaxMana = 500,
                ManaPercent = 100,
            },
            Player = new PlayerState
            {
                TeamName = "radiant",
                Kills = 5,
                Deaths = 2,
                Assists = 8,
                Gpm = 520,
                Xpm = 560,
                Gold = 3000,
            },
            Items =
            [
                new ItemState { Slot = "slot0", SlotKind = ItemSlotKind.Main, Name = "item_travel_boots" },
                new ItemState { Slot = "slot1", SlotKind = ItemSlotKind.Main, Name = "item_abyssal_blade" },
            ],
            Buildings =
            [
                new BuildingState { Team = "radiant", TeamLabel = "天辉", Key = "top_1", Label = "上一塔", Alive = true, HealthPercent = 60 },
            ],
        };

        Check(tracker.OnSnapshot(before) is null, "对局中快照不产生结束记录");

        var after = new MatchSnapshot
        {
            Phase = GamePhase.InProgress,
            MatchId = 12345,
            GameTime = 1200,
            RoshanAlive = false,
            Hero = new HeroState
            {
                Id = 1,
                Alive = false,
                Level = 13,
                MaxHealth = 1000,
                HealthPercent = 0,
                MaxMana = 500,
                ManaPercent = 0,
            },
            Player = new PlayerState
            {
                TeamName = "radiant",
                Kills = 6,
                Deaths = 3,
                Assists = 8,
                Gpm = 540,
                Xpm = 590,
                Gold = 3200,
            },
            Items =
            [
                new ItemState { Slot = "slot0", SlotKind = ItemSlotKind.Main, Name = "item_travel_boots" },
                new ItemState { Slot = "slot1", SlotKind = ItemSlotKind.Main, Name = "item_abyssal_blade" },
                new ItemState { Slot = "slot2", SlotKind = ItemSlotKind.Main, Name = "item_black_king_bar" },
            ],
            Buildings =
            [
                new BuildingState { Team = "radiant", TeamLabel = "天辉", Key = "top_1", Label = "上一塔", Alive = false, HealthPercent = 0 },
            ],
        };

        Check(tracker.OnSnapshot(after) is null, "第二帧对局中快照不产生结束记录");

        var post = new MatchSnapshot
        {
            Phase = GamePhase.PostGame,
            MatchId = 12345,
            GameTime = 1800,
            WinTeam = "radiant",
            Player = new PlayerState { TeamName = "radiant" },
        };
        var finished = tracker.OnSnapshot(post);
        Check(finished is not null, "赛后快照产出对局记录");
        if (finished is null) return;

        Check(finished.MatchId == 12345 && finished.HeroId == 1, "记录保留比赛与英雄 ID");
        Check(finished.Won == true, "根据胜方阵营判定胜负");
        Check(finished.Kills == 6 && finished.Deaths == 3 && finished.Assists == 8, "记录最终 KDA");
        Check(finished.MaxLevel == 13, "记录最高等级");
        Check(finished.FinalItems.Contains("黑皇杖"), "记录最终装备（含新购买物品）");
        Check(finished.KeyMoments.Any(moment => moment.Contains("阵亡")), "记录阵亡节点");
        Check(finished.KeyMoments.Any(moment => moment.Contains("击杀")), "记录击杀节点");
        Check(finished.KeyMoments.Any(moment => moment.Contains("购买")), "记录购买节点");
        Check(finished.KeyMoments.Any(moment => moment.Contains("上一塔")), "记录建筑被摧毁节点");
        Check(finished.KeyMoments.Any(moment => moment.Contains("Roshan")), "记录 Roshan 被击杀节点");
        Check(finished.Economy.Count >= 2, "经济曲线有多个采样点");
        Check(finished.DurationText == "30:00", "时长格式化为分:秒");
    }
}


