using AegisCoach.Core.Models;

namespace AegisCoach.Core.Services;

/// <summary>
/// 确定性实时教练规则引擎：对相邻两次快照做差分，产生可回溯证据的提醒。
/// 每条规则有独立冷却时间，避免同一条件反复刷屏。
/// </summary>
public sealed class CoachRuleEngine
{
    private const string ReadyState = "DOTA_ULTIMATE_STATE_READY";
    private readonly Dictionary<string, TimeSpan> _cooldowns;
    private readonly Dictionary<string, DateTimeOffset> _lastFired = new(StringComparer.OrdinalIgnoreCase);

    public CoachRuleEngine(Dictionary<string, TimeSpan>? cooldowns = null)
    {
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
        };
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
        if (previous is not null && previous.Phase != current.Phase)
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
        if (current.Phase == GamePhase.InProgress && current.Hero.MaxHealth > 0 && current.Hero.HealthPercent <= 25)
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
        if (current.Phase == GamePhase.InProgress && current.Hero.MaxMana > 0 && current.Hero.ManaPercent <= 20)
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
        if (current.Phase == GamePhase.InProgress && ultIsReady && !ultWasReady)
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
        if (current.Hero.Alive == false && previous is { Hero.Alive: true })
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
        if (current.Phase == GamePhase.InProgress && current.Hero.Alive &&
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
        if (current.Phase == GamePhase.InProgress && !current.Hero.Alive &&
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
        if (current.Phase == GamePhase.InProgress && current.Hero.Alive &&
            current.Hero.HealthPercent >= 70 && current.Hero.ManaPercent >= 50 && ultIsReady)
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

    private static string FriendlyAbilityName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "大招";
        return name.Replace("npc_dota_hero_", string.Empty).Replace('_', ' ').Trim();
    }

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
