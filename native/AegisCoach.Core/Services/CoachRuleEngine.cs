using AegisCoach.Core.Models;

namespace AegisCoach.Core.Services;

/// <summary>
/// 确定性实时教练规则引擎：对相邻两次快照做差分，产生可回溯证据的提醒。
/// 每条规则有独立冷却时间，避免同一条件反复刷屏。
/// </summary>
public sealed class CoachRuleEngine
{
    private const string ReadyState = "DOTA_ULTIMATE_STATE_READY";
    private readonly CoachRuleOptions _options;
    private readonly Dictionary<string, TimeSpan> _cooldowns;
    private readonly Dictionary<string, DateTimeOffset> _lastFired = new(StringComparer.OrdinalIgnoreCase);

    public CoachRuleEngine(Dictionary<string, TimeSpan>? cooldowns = null, CoachRuleOptions? options = null)
    {
        _options = options ?? new CoachRuleOptions();
        _options.Normalize();
        _cooldowns = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            ["game_state"] = TimeSpan.FromSeconds(30),
            ["low_health"] = TimeSpan.FromSeconds(15),
            ["low_mana"] = TimeSpan.FromSeconds(20),
            ["ult_ready"] = TimeSpan.FromSeconds(30),
            ["died"] = TimeSpan.FromSeconds(10),
            ["control_locked"] = TimeSpan.FromSeconds(12),
            ["buyback_window"] = TimeSpan.FromSeconds(20),
            ["fight_ready"] = TimeSpan.FromSeconds(45),
            ["smoke_purchased"] = TimeSpan.FromSeconds(30),
            ["smoke_used"] = TimeSpan.FromSeconds(45),
            ["smoked_active"] = TimeSpan.FromSeconds(60),
            ["night_fall"] = TimeSpan.FromSeconds(120),
            ["building_attacked"] = TimeSpan.FromSeconds(25),
            ["building_destroyed"] = TimeSpan.FromSeconds(20),
            ["roshan_killed"] = TimeSpan.FromSeconds(300),
            ["roshan_respawned"] = TimeSpan.FromSeconds(120),
            ["item_ready"] = TimeSpan.FromSeconds(20),
            ["ability_ready"] = TimeSpan.FromSeconds(18),
            ["map_awareness"] = TimeSpan.FromSeconds(300),
        };
        foreach (var (key, seconds) in _options.CooldownSeconds)
        {
            _cooldowns[key] = TimeSpan.FromSeconds(seconds);
        }
        if (cooldowns is not null)
        {
            foreach (var (key, value) in cooldowns)
            {
                _cooldowns[key] = value;
            }
        }
    }

    public IReadOnlyDictionary<string, TimeSpan> Cooldowns => _cooldowns;

    public IReadOnlyList<CoachAdvice> Evaluate(MatchSnapshot? previous, MatchSnapshot current)
    {
        var advice = new List<CoachAdvice>();
        var now = DateTimeOffset.UtcNow;

        // 1. 比赛阶段变化
        if (_options.EnableGameState && previous is not null && previous.Phase != current.Phase)
        {
            var (title, message) = PhaseTransition(previous, current);
            Emit(advice, "game_state", AdviceSeverity.Info, title, message, now, current,
            [
                $"map.game_state={current.Phase}",
                $"map.matchid={current.MatchId}",
                $"map.gametime={current.GameTime:0.#}",
            ]);
        }

        // 2. 低血量
        if (_options.EnableLowHealth && current.Phase == GamePhase.InProgress && current.Hero.MaxHealth > 0 && current.Hero.HealthPercent <= _options.LowHealthPercent)
        {
            Emit(advice, "low_health", AdviceSeverity.Critical, "血量危险",
                $"当前血量 {current.Hero.HealthPercent:0}%（{current.Hero.Health}/{current.Hero.MaxHealth}），注意撤退或治疗。",
                now, current,
            [
                $"hero.health_percent={current.Hero.HealthPercent:0.#}",
                $"hero.health={current.Hero.Health}",
                $"hero.max_health={current.Hero.MaxHealth}",
            ]);
        }

        // 3. 低魔法
        if (_options.EnableLowMana && current.Phase == GamePhase.InProgress && current.Hero.MaxMana > 0 && current.Hero.ManaPercent <= _options.LowManaPercent)
        {
            Emit(advice, "low_mana", AdviceSeverity.Warning, "魔法不足",
                $"当前魔法 {current.Hero.ManaPercent:0}%（{current.Hero.Mana}/{current.Hero.MaxMana}），注意控制施法。",
                now, current,
            [
                $"hero.mana_percent={current.Hero.ManaPercent:0.#}",
                $"hero.mana={current.Hero.Mana}",
                $"hero.max_mana={current.Hero.MaxMana}",
            ]);
        }

        // 4. 大招冷却完成（从冷却中变为就绪）
        var ultWasReady = previous is not null && IsUltimateReady(previous);
        var ultIsReady = IsUltimateReady(current);
        if (_options.EnableUltimateReady && current.Phase == GamePhase.InProgress && ultIsReady && !ultWasReady)
        {
            var ultimate = FindUltimate(current);
            Emit(advice, "ult_ready", AdviceSeverity.Info, "大招就绪",
                $"{FriendlyAbilityName(ultimate?.Name)}冷却完成，可以释放。",
                now, current,
            [
                $"player.ultimate_state={current.Player.UltimateState ?? "unknown"}",
                $"player.ultimate_cooldown={current.Player.UltimateCooldown}",
                $"ability={ultimate?.Name ?? "unknown"}",
            ]);
        }

        // 5. 自己阵亡
        if (_options.EnableDeath && current.Hero.Alive == false && previous is { Hero.Alive: true })
        {
            Emit(advice, "died", AdviceSeverity.Critical, "你阵亡了",
                $"{current.GameTimeText} 阵亡，{current.Hero.RespawnSeconds} 秒后复活。",
                now, current,
            [
                "hero.alive=false",
                $"hero.respawn_seconds={current.Hero.RespawnSeconds}",
                $"map.gametime={current.GameTime:0.#}",
            ]);
        }

        // 6. 当前无法执行关键操作
        if (_options.EnableControlLocked && current.Phase == GamePhase.InProgress && current.Hero.Alive &&
            current.Hero.Silences + current.Hero.Stuns + current.Hero.Disables > 0)
        {
            Emit(advice, "control_locked", AdviceSeverity.Warning, "当前受到控制",
                "沉默、眩晕或禁用仍在生效，先不要执行需要精确时机的连招。",
                now, current,
            [
                $"hero.silences={current.Hero.Silences}",
                $"hero.stuns={current.Hero.Stuns}",
                $"hero.disables={current.Hero.Disables}",
            ]);
        }

        // 7. 阵亡后的买活窗口
        if (_options.EnableBuybackWindow && current.Phase == GamePhase.InProgress && !current.Hero.Alive &&
            current.Hero.BuybackCost > 0 && current.Hero.BuybackCooldown <= 0 &&
            current.Player.Gold >= current.Hero.BuybackCost)
        {
            Emit(advice, "buyback_window", AdviceSeverity.Critical, "可以买活",
                $"当前金钱 {current.Player.Gold:N0}，买活需要 {current.Hero.BuybackCost:N0}；是否买活取决于高地、关键资源和复活时间。",
                now, current,
            [
                "hero.alive=false",
                $"hero.buyback_cost={current.Hero.BuybackCost}",
                $"hero.buyback_cooldown={current.Hero.BuybackCooldown}",
                $"player.gold={current.Player.Gold}",
            ]);
        }

        // 8. 资源完整且大招可用，给出一个低频的接团窗口提示
        if (_options.EnableFightReady && current.Phase == GamePhase.InProgress && current.Hero.Alive &&
            current.Hero.HealthPercent >= _options.FightReadyHealthPercent &&
            current.Hero.ManaPercent >= _options.FightReadyManaPercent && ultIsReady)
        {
            Emit(advice, "fight_ready", AdviceSeverity.Info, "资源完整",
                "生命、魔法和大招都已准备好，可以寻找有视野的接团或推进窗口。",
                now, current,
            [
                $"hero.health_percent={current.Hero.HealthPercent:0.#}",
                $"hero.mana_percent={current.Hero.ManaPercent:0.#}",
                $"player.ultimate_state={(current.Player.UltimateState ?? "unknown")}",
            ]);
        }

        // 9. 诡计之雾：购入
        var smokeName = "item_smoke_of_deceit";
        var hasSmoke = current.Items.Any(item => item.Name == smokeName);
        var hadSmoke = previous is not null && previous.Items.Any(item => item.Name == smokeName);
        if (_options.EnableSmoke && current.Phase == GamePhase.InProgress && hasSmoke && !hadSmoke)
        {
            Emit(advice, "smoke_purchased", AdviceSeverity.Info, "已携带诡计之雾",
                "雾到手后尽快规划抓人或偷盾：先确认目标视野和敌方关键技能状态，开雾后约 40 秒内动手。",
                now, current,
            [
                $"item.smoke_of_deceit=present",
                $"map.gametime={current.GameTime:0.#}",
                $"player.gold={current.Player.Gold}",
            ]);
        }

        // 10. 诡计之雾：已使用
        if (_options.EnableSmoke && current.Phase == GamePhase.InProgress && hadSmoke && !hasSmoke)
        {
            Emit(advice, "smoke_used", AdviceSeverity.Warning, "烟雾已使用",
                "隐身效果约持续 40 秒（攻击/施法后现形）。立刻组织抓人、推进或偷盾；如果刚才被打断，尽快撤退重找时机。",
                now, current,
            [
                "item.smoke_of_deceit=consumed",
                $"map.gametime={current.GameTime:0.#}",
            ]);
        }

        // 11. 天黑：敌方游走与开雾风险
        if (_options.EnableNightFall && current.Phase == GamePhase.InProgress && previous is { InMatch: true } &&
            previous.Daytime && !current.Daytime)
        {
            Emit(advice, "night_fall", AdviceSeverity.Warning, "天黑了",
                "夜晚视野缩小，敌方开雾游走与抱团抓人的风险明显升高。及时补真假眼，避免单人带线过河。",
                now, current,
            [
                "map.daytime=true→false",
                $"map.clock_time={current.ClockTime:0.#}",
            ]);
        }

        // 12. 自己处于诡计之雾中
        if (_options.EnableSmokedState && current.Phase == GamePhase.InProgress && current.Hero.IsSmoked &&
            previous is { Hero.IsSmoked: false })
        {
            Emit(advice, "smoked_active", AdviceSeverity.Info, "诡计之雾生效中",
                "你现在处于诡计之雾中。攻击、施法或距离敌方英雄过近都会立刻现形；抓紧时间利用隐身接近目标，行动前先确认对方关键技能。",
                now, current,
            [
                "hero.smoked=true",
                $"map.gametime={current.GameTime:0.#}",
            ]);
        }

        // 13. 建筑被攻击 / 被摧毁
        if (_options.EnableBuildings && current.Phase == GamePhase.InProgress &&
            previous is { InMatch: true } && previous.Buildings.Count > 0)
        {
            var myTeam = current.Player.TeamName;
            foreach (var building in current.Buildings)
            {
                var before = previous.Buildings.FirstOrDefault(item =>
                    item.Team == building.Team && item.Key == building.Key);
                if (before is null) continue;

                if (before.Alive && !building.Alive)
                {
                    var ownership = myTeam is not null && building.Team == myTeam ? "我方" : "敌方";
                    Emit(advice, "building_destroyed", AdviceSeverity.Critical,
                        $"{building.TeamLabel}{building.Label}被摧毁",
                        $"时间 {current.GameTimeText}：{building.TeamLabel}{building.Label}已被摧毁。" +
                        (myTeam is not null && building.Team == myTeam
                            ? "敌方正在推进，注意收缩防守与回防路线。"
                            : "推进优势扩大，注意控制视野、别在敌方塔后深追。"),
                        now, current,
                    [
                        $"buildings.{building.Team}.{building.Key}=destroyed",
                        $"map.gametime={current.GameTime:0.#}",
                    ]);
                    continue;
                }

                if (building.Alive && before.Alive && building.HealthPercent < before.HealthPercent - 0.5)
                {
                    var ownership = myTeam is not null && building.Team == myTeam ? "我方" : "敌方";
                    Emit(advice, "building_attacked", AdviceSeverity.Warning,
                        $"{building.TeamLabel}{building.Label}正在被攻击",
                        myTeam is not null && building.Team == myTeam
                            ? $"你的{building.TeamLabel}{building.Label}正在掉血（剩余 {building.HealthPercent:0}%），有敌方英雄在推线，注意支援或提前回家。"
                            : $"{building.TeamLabel}{building.Label}正在被攻击（剩余 {building.HealthPercent:0}%），队友在推进，跟进时先确认敌方英雄位置。",
                        now, current,
                    [
                        $"buildings.{building.Team}.{building.Key}={building.HealthPercent:0.#}%",
                        $"map.gametime={current.GameTime:0.#}",
                    ]);
                }
            }
        }

        // 14. Roshan 状态变化
        if (_options.EnableRoshan && current.Phase == GamePhase.InProgress)
        {
            if (previous?.RoshanAlive == true && current.RoshanAlive == false)
            {
                Emit(advice, "roshan_killed", AdviceSeverity.Warning, "Roshan 已被击杀",
                    "肉山刚被击杀，预计 8~11 分钟后复活。若无法立刻控制肉山区域，至少补上入口视野并防止敌方蹲守。",
                    now, current,
                [
                    "map.roshan.alive=true→false",
                    $"map.gametime={current.GameTime:0.#}",
                ]);
            }
            else if (previous?.RoshanAlive == false && current.RoshanAlive == true)
            {
                Emit(advice, "roshan_respawned", AdviceSeverity.Info, "Roshan 已复活",
                    "肉山刷新，双方将围绕肉山区展开争夺。开团前确认敌方关键大招和道具状态。",
                    now, current,
                [
                    "map.roshan.alive=false→true",
                    $"map.gametime={current.GameTime:0.#}",
                ]);
            }
        }

        // 15. 主动道具冷却完成
        if (_options.EnableItemReady && current.Phase == GamePhase.InProgress && current.Hero.Alive &&
            previous is { InMatch: true })
        {
            foreach (var item in current.Items)
            {
                if (item.IsPassive || !item.CanCast) continue;
                var before = previous.Items.FirstOrDefault(previousItem => previousItem.Slot == item.Slot);
                if (before is null || before.Cooldown <= 0.5 || item.Cooldown > 0.5) continue;
                var itemName = FriendlyItemName(item.Name);
                Emit(advice, "item_ready", AdviceSeverity.Info, $"{itemName}冷却完成",
                    $"{itemName}已就绪，可以主动使用。使用前先确认目标和己方技能状态，避免浪费关键道具。",
                    now, current,
                [
                    $"items.{item.Slot}={item.Name}",
                    $"items.{item.Slot}.cooldown={before.Cooldown:0.#}→{item.Cooldown:0.#}",
                ]);
                break;
            }
        }

        // 16. 关键技能冷却完成（非大招）
        if (_options.EnableAbilityReady && current.Phase == GamePhase.InProgress && current.Hero.Alive &&
            previous is { InMatch: true })
        {
            foreach (var ability in current.Abilities)
            {
                if (ability.IsPassive || ability.IsUltimate || ability.Level <= 0) continue;
                var before = previous.Abilities.FirstOrDefault(previousAbility => previousAbility.Slot == ability.Slot);
                if (before is null || before.Cooldown < 12 || ability.Cooldown > 0.5) continue;
                var abilityName = FriendlyAbilityName(ability.Name);
                Emit(advice, "ability_ready", AdviceSeverity.Info, $"{abilityName}就绪",
                    $"关键技能冷却完成（{before.Cooldown:0.#} 秒），注意把握释放时机，不要随意交掉。",
                    now, current,
                [
                    $"abilities.{ability.Slot}={ability.Name}",
                    $"abilities.{ability.Slot}.cooldown={before.Cooldown:0.#}→{ability.Cooldown:0.#}",
                ]);
                break;
            }
        }

        // 17. 关键时间点的地图意识提醒（敌方消失/抱团风险）
        if (_options.EnableMapAwareness && current.Phase == GamePhase.InProgress &&
            previous is { InMatch: true })
        {
            if (previous.GameTime < 600 && current.GameTime >= 600)
            {
                Emit(advice, "map_awareness", AdviceSeverity.Warning, "10 分钟：注意敌方动向",
                    "对线期结束，敌方辅助和核心开始游走、抱团或开雾。小地图上看不到敌方英雄时，默认他们可能在抓你、推进或打盾；过河前先确认位置。",
                    now, current,
                [
                    $"map.gametime={current.GameTime:0.#}",
                ]);
            }
            else if (previous.GameTime < 1200 && current.GameTime >= 1200)
            {
                Emit(advice, "map_awareness", AdviceSeverity.Warning, "20 分钟：团战与肉山窗口",
                    "中后期一次失误可能决定比赛。抱团推进或争夺肉山前，先确认敌方关键技能、大招和买活状态；视野外消失的英雄默认在埋伏。",
                    now, current,
                [
                    $"map.gametime={current.GameTime:0.#}",
                ]);
            }
        }

        return advice;
    }

    private static (string Title, string Message) PhaseTransition(MatchSnapshot previous, MatchSnapshot current)
    {
        var from = previous.PhaseText;
        var to = current.PhaseText;
        return current.Phase switch
        {
            GamePhase.InProgress => ("比赛开始", $"比赛开始 · 时间 {current.GameTimeText} · 比分 {current.ScoreText}"),
            GamePhase.PostGame when !string.IsNullOrWhiteSpace(current.WinTeam) =>
                ("比赛结束", $"比赛结束 · 胜方：{TeamText(current.WinTeam)} · 比分 {current.ScoreText}"),
            GamePhase.PostGame => ("比赛结束", $"比赛结束 · 比分 {current.ScoreText}"),
            GamePhase.HeroSelection => ("进入选人阶段", "请根据阵容选择英雄和分路。"),
            _ => ("状态变化", $"{from} → {to}"),
        };
    }

    private static bool IsUltimateReady(MatchSnapshot snapshot)
    {
        if (snapshot.Player.UltimateState == ReadyState) return true;
        var ultimate = FindUltimate(snapshot);
        return ultimate is not null && ultimate.Level > 0 && ultimate.Cooldown <= 0 && !ultimate.IsPassive;
    }

    private static AbilityState? FindUltimate(MatchSnapshot snapshot)
    {
        var flagged = snapshot.Abilities.FirstOrDefault(ability => ability.IsUltimate && ability.Level > 0);
        if (flagged is not null) return flagged;
        return snapshot.Abilities.FirstOrDefault(ability => ability.Slot is "ability_3" or "ability_4" && ability.Level > 0);
    }

    public static string FriendlyAbilityName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "大招";
        var withoutHeroPrefix = name.Contains('_') ? name[(name.IndexOf('_') + 1)..] : name;
        return TitleCase(withoutHeroPrefix.Replace('_', ' ').Trim());
    }

    public static string FriendlyItemName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "道具";
        if (ItemNameMap.TryGetValue(name, out var localized)) return localized;
        var withoutPrefix = name.StartsWith("item_", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;
        return TitleCase(withoutPrefix.Replace('_', ' ').Trim());
    }

    private static string TitleCase(string value)
    {
        var chars = value.ToCharArray();
        var capitalize = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsWhiteSpace(chars[i]))
            {
                capitalize = true;
                continue;
            }

            if (capitalize)
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                capitalize = false;
            }
        }
        return new string(chars);
    }

    private static readonly Dictionary<string, string> ItemNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["item_smoke_of_deceit"] = "诡计之雾",
        ["item_black_king_bar"] = "黑皇杖",
        ["item_blink"] = "闪烁匕首",
        ["item_refresher"] = "刷新球",
        ["item_aeon_disk"] = "永恒之盘",
        ["item_lotus_orb"] = "清莲宝珠",
        ["item_force_staff"] = "原力法杖",
        ["item_cyclone"] = "风之杖",
        ["item_sheepstick"] = "邪恶镰刀",
        ["item_orchid"] = "紫怨",
        ["item_bloodthorn"] = "血棘",
        ["item_manta"] = "幻影斧",
        ["item_heavens_halberd"] = "天堂之戟",
        ["item_abyssal_blade"] = "深渊之刃",
        ["item_satanic"] = "撒旦之邪力",
        ["item_silver_edge"] = "白银之锋",
        ["item_hand_of_midas"] = "点金手",
        ["item_guardian_greaves"] = "卫士胫甲",
        ["item_mekansm"] = "梅肯斯姆",
        ["item_pipe"] = "洞察烟斗",
        ["item_glimmer_cape"] = "微光披风",
        ["item_urn_of_shadows"] = "骨灰盒",
        ["item_spirit_vessel"] = "魂之灵瓮",
        ["item_dust"] = "显影之尘",
        ["item_ward_observer"] = "侦查守卫",
        ["item_ward_sentry"] = "岗哨守卫",
        ["item_tpscroll"] = "回城卷轴",
        ["item_travel_boots"] = "远行鞋",
        ["item_phase_boots"] = "相位鞋",
        ["item_power_treads"] = "动力鞋",
        ["item_arcane_boots"] = "秘法鞋",
        ["item_tranquil_boots"] = "静谧之鞋",
    };

    private static string TeamText(string team) => team switch
    {
        "radiant" => "天辉",
        "dire" => "夜魇",
        _ => team,
    };

    private void Emit(
        List<CoachAdvice> advice,
        string ruleId,
        AdviceSeverity severity,
        string title,
        string message,
        DateTimeOffset now,
        MatchSnapshot current,
        IReadOnlyList<string> evidence)
    {
        if (!_cooldowns.TryGetValue(ruleId, out var cooldown))
        {
            cooldown = TimeSpan.FromSeconds(30);
        }

        if (_lastFired.TryGetValue(ruleId, out var last) && now - last < cooldown) return;
        _lastFired[ruleId] = now;

        advice.Add(new CoachAdvice
        {
            Id = ruleId,
            Severity = severity,
            Title = title,
            Message = message,
            Rule = ruleId,
            Timestamp = now,
            Evidence = evidence,
            MatchId = current.MatchId,
            GameTime = current.GameTime,
        });
    }
}
