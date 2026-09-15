using AegisCoach.Core.Models;
using AegisCoach.Core.Services;

namespace AegisCoach.Native.Services;

/// <summary>实时教练 AI 解释的统一提示词，NPU 与云端后端共用同一格式。</summary>
public static class CoachAiPrompts
{
    public static string BuildPrompt(CoachAdvice advice, MatchSnapshot snapshot, string? customKeywords = null)
    {
        var guidance = string.IsNullOrWhiteSpace(customKeywords)
            ? "用不超过40个中文字符，给出一个可执行动作。只使用下面给出的状态，不要编造敌方信息。"
            : $"指导要求与关键词：{customKeywords.Trim()}。只使用下面给出的状态，不要编造敌方信息。";

        return "你是Dota 2实时教练。只解释这一条规则提醒，不替代规则判断。" + guidance + "\n" +
            "规则：" + advice.Title + "\n提醒：" + advice.Message +
            "\n当前状态：\n" + BuildContext(snapshot);
    }

    /// <summary>把一帧 GSI 归一化快照压缩成 AI 可读的中文战场状态文本。</summary>
    public static string BuildContext(MatchSnapshot snapshot)
    {
        var hero = snapshot.Hero;
        var player = snapshot.Player;
        var lines = new List<string>
        {
            $"时间 {snapshot.GameTimeText}，阶段 {snapshot.PhaseText}，{TeamText(player.TeamName)}，比分 {snapshot.ScoreText}",
            $"我的英雄 等级{hero.Level}，生命 {hero.HealthPercent:0}%（{hero.Health}/{hero.MaxHealth}），魔法 {hero.ManaPercent:0}%（{hero.Mana}/{hero.MaxMana}）",
            $"KDA {player.Kills}/{player.Deaths}/{player.Assists}，击杀连杀 {player.KillStreak}，补刀 {player.LastHits}，反补 {player.Denies}",
            $"金钱 {player.Gold:N0}，GPM {player.Gpm}，XPM {player.Xpm}",
        };
        if (snapshot.Daytime) lines.Add("当前是白天");
        else lines.Add("当前是夜晚，视野缩小，游走与开雾风险高");
        if (snapshot.RoshanAlive.HasValue)
        {
            lines.Add(snapshot.RoshanAlive.Value
                ? "肉山存活"
                : "肉山已被击杀；GSI 未提供准确复活倒计时");
        }
        if (!string.IsNullOrWhiteSpace(snapshot.BuildingSummary)) lines.Add($"建筑：{snapshot.BuildingSummary}");
        if (hero.IsSmoked) lines.Add("我处于诡计之雾中（攻击/施法会现形）");
        if (hero.MagicImmunity) lines.Add("我处于魔法免疫状态");
        var activeItems = snapshot.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => CoachRuleEngine.FriendlyItemName(item.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        if (activeItems.Count > 0) lines.Add("装备：" + string.Join("、", activeItems.Take(6)));
        var skills = snapshot.Abilities
            .Where(ability => ability.Level > 0 && !string.IsNullOrWhiteSpace(ability.Name))
            .Select(ability => $"{CoachRuleEngine.FriendlyAbilityName(ability.Name)}Lv{ability.Level}" + (ability.Cooldown > 0.5 ? $"(CD{ability.Cooldown:0})" : ""))
            .ToList();
        if (skills.Count > 0) lines.Add("技能：" + string.Join("、", skills.Take(6)));
        return string.Join("\n", lines);
    }

    /// <summary>赛后复盘提示词：以真实操作记录（技能/道具/购买/阵亡决策）为输入，要求模型做操作点评。</summary>
    public static string BuildMatchReviewPrompt(
        string heroName,
        bool won,
        double durationSeconds,
        MatchOperationSummary ops,
        IReadOnlyList<string>? finalItems = null,
        ApiMatchSummary? api = null,
        string? customKeywords = null)
    {
        const int maxOperationLines = 260;
        var lines = new List<string>
        {
            "你是Dota 2赛后复盘教练。下面是本局GSI回放中提取的真实操作记录：技能施放、主动道具使用、购买、阵亡前后决策、买活等。",
            "请以“操作点评”为主，不要复述或罗列数据，直接给分析结论。要求：",
            "1) 先给一句总体评价；",
            "2) 找出2-3个最高优先级的操作问题（例如关键道具/大招该用未用、阵亡前的决策失误、装备节奏、买活选择），每条必须写明游戏时间、事实依据和具体可执行的改进动作；",
            "3) 指出1-2个做得好的操作；",
            "4) 严格区分事实与推断，数据不足时写“无法判断”，不要编造英雄技能数值或敌方信息；",
            "5) 总字数控制在500字以内。",
        };
        if (!string.IsNullOrWhiteSpace(customKeywords))
        {
            lines.Add($"6) 重点关注要点与关键词：{customKeywords.Trim()}。");
        }
        lines.Add("");
        lines.Add($"英雄：{heroName} · 结果：{(won ? "胜利" : "失败")} · 时长 {MatchSnapshot.FormatGameTime(durationSeconds)} · 比分 {ops.ScoreText}");
        lines.Add($"总数据：KDA {ops.Kills}/{ops.Deaths}/{ops.Assists} · 补刀 {ops.LastHits} · 反补 {ops.Denies} · GPM {ops.Gpm} · XPM {ops.Xpm} · 最高等级 {ops.MaxLevel}");

        if (finalItems is { Count: > 0 })
        {
            lines.Add($"最终装备：{string.Join("、", finalItems)}");
        }

        if (ops.Phases.Count > 0)
        {
            lines.Add("");
            lines.Add("【阶段背景】（仅作参考，不要复述）");
            foreach (var phase in ops.Phases)
            {
                var bought = phase.ItemsBought.Count > 0 ? $" · 购买：{string.Join("、", phase.ItemsBought)}" : "";
                lines.Add($"{phase.Label} {phase.TimeText}：击杀{phase.KillsDelta} 死亡{phase.DeathsDelta} 助攻{phase.AssistsDelta}，" +
                    $"补刀{phase.LastHitsDelta}（{phase.LastHitsPerMinute:0.#}/分） 反补{phase.DeniesDelta}，均GPM {phase.AvgGpm:0}/XPM {phase.AvgXpm:0}{bought}");
            }
        }

        if (ops.DeathsDetailed.Count > 0)
        {
            lines.Add("");
            lines.Add("【阵亡复盘上下文】");
            foreach (var death in ops.DeathsDetailed)
            {
                var parts = new List<string>
                {
                    $"{death.TimeText} 第{death.DeathNumber}次阵亡：阵亡前窗口血量 {death.HealthPercentAtWindowStart:0}%→{death.HealthPercentAtDeath:0}%（最低{death.MinHealthPercentInWindow:0}%）",
                    $"等级{death.Level} 金钱{death.Gold}",
                    $"大招{(death.UltReadyAtDeath ? "就绪·未使用" : "未就绪")}",
                    death.TpAvailable ? "TP可用·未使用" : "TP不可用",
                };
                if (death.ReadyActivesUnused.Count > 0)
                    parts.Add("关键主动道具就绪未用：" + string.Join("、", death.ReadyActivesUnused));
                if (death.RecentCasts.Count > 0)
                    parts.Add("阵亡前20秒操作：" + string.Join("、", death.RecentCasts));
                parts.Add($"窗口内补刀+{death.LastHitsInWindow}");
                if (death.GapSincePreviousDeathSeconds is not null)
                    parts.Add($"距上次阵亡 {MatchSnapshot.FormatGameTime(death.GapSincePreviousDeathSeconds.Value)}");
                parts.Add(death.BuybackAvailable
                    ? $"可买活（费用{death.BuybackCost}）{(death.BuybackUsed ? "已买活" : "未买活")}"
                    : "不可买活");
                lines.Add(string.Join(" · ", parts));
            }
        }

        if (ops.Operations.Count > 0)
        {
            lines.Add("");
            lines.Add("【操作记录】");
            var highValue = ops.Operations
                .Where(item => item.Kind is "ult_idle" or "buyback" or "smoke" or "roshan" or "building" or "item_use")
                .OrderBy(item => item.GameTime)
                .ToList();
            var lowValue = ops.Operations
                .Where(item => item.Kind is "cast" or "buy" or "kill")
                .OrderBy(item => item.GameTime)
                .ToList();
            var remaining = maxOperationLines - highValue.Count;
            if (remaining > 0 && lowValue.Count > remaining) lowValue.RemoveRange(remaining, lowValue.Count - remaining);
            foreach (var op in highValue.Concat(lowValue).OrderBy(item => item.GameTime))
            {
                lines.Add(FormatOperation(op));
            }
            if (ops.Operations.Count > maxOperationLines)
                lines.Add($"……（另有 {ops.Operations.Count - maxOperationLines} 条低价值操作已省略）");
        }

        if (api is not null)
        {
            lines.Add("");
            lines.AddRange(BuildApiContextText(api));
        }

        return string.Join("\n", lines);
    }

    /// <summary>OpenDota 整场数据摘要，与本地操作记录合并时追加到提示词末尾。</summary>
    public static IReadOnlyList<string> BuildApiContextText(ApiMatchSummary api)
    {
        var lines = new List<string> { "【整场数据（OpenDota）】" };
        if (api.AllPlayers.Count > 0)
        {
            var radiant = api.AllPlayers.Where(player => player.IsRadiant).ToList();
            var dire = api.AllPlayers.Where(player => !player.IsRadiant).ToList();
            lines.Add("阵容：" + string.Join(" | ", radiant.Select(FormatPlayerSummary)) +
                "  VS  " + string.Join(" | ", dire.Select(FormatPlayerSummary)));
        }

        var myLine = $"我方数据：KDA {api.Kills}/{api.Deaths}/{api.Assists} · 补刀 {api.LastHits} · GPM {api.Gpm} · XPM {api.Xpm} · 等级{api.Level}";
        if (api.Benchmarks.TryGetValue("gold_per_min", out var gpmPct)) myLine += $" · GPM处于{FormatPercentile(gpmPct)}";
        if (api.Benchmarks.TryGetValue("xp_per_min", out var xpmPct)) myLine += $" · XPM处于{FormatPercentile(xpmPct)}";
        if (api.Benchmarks.TryGetValue("last_hits_per_min", out var lhPct)) myLine += $" · 补刀效率处于{FormatPercentile(lhPct)}";
        lines.Add(myLine);

        if (api.ItemTimings.Count > 0)
        {
            lines.Add("装备时间：" + string.Join(" · ", api.ItemTimings.Select(item => $"{item.ItemName}@{item.TimeText}")));
        }

        if (api.KillsLog.Count > 0)
        {
            lines.Add("击杀：" + string.Join(" · ", api.KillsLog.Select(kill => $"{kill.TimeText} {kill.VictimHero}")));
        }

        if (api.DeathTimes.Count > 0)
        {
            lines.Add("阵亡：" + string.Join(" · ", api.DeathTimes.Select(MatchSnapshot.FormatGameTime)));
            if (api.KilledBy.Count > 0)
            {
                lines.Add("阵亡来源：" + string.Join("、", api.KilledBy.Select(item => $"{item.HeroName}×{item.Count}")));
            }
        }

        if (api.BuybackTimes.Count > 0)
        {
            lines.Add("买活：" + string.Join("、", api.BuybackTimes.Select(MatchSnapshot.FormatGameTime)));
        }

        if (api.Teamfights.Count > 0)
        {
            var participated = api.Teamfights.Count(fight => fight.Participated);
            var fightDeaths = api.Teamfights.Where(fight => fight.Participated).Sum(fight => fight.MyDeaths);
            var firstFights = api.Teamfights.Where(fight => fight.Participated).Take(8);
            lines.Add($"团战：共{api.Teamfights.Count}场，参与{participated}场，团战内阵亡{fightDeaths}次" +
                (firstFights.Any()
                    ? "；参与场次：" + string.Join(" · ", firstFights.Select(fight => $"{fight.TimeText}(伤害{fight.MyDamage:0}，阵亡{fight.MyDeaths})"))
                    : ""));
        }

        if (api.TowersTaken.Count > 0)
        {
            lines.Add("推塔：" + string.Join(" · ", api.TowersTaken.Select(tower => $"{tower.TimeText} {tower.Building}")));
        }

        if (api.ObserversPlaced > 0 || api.SentriesPlaced > 0)
        {
            lines.Add($"视野：假眼{api.ObserversPlaced}个（含未清除{api.ObserverLogCount}个）· 真眼{api.SentriesPlaced}个");
        }

        if (api.Economy.Count > 0)
        {
            var samples = api.Economy
                .Where((_, index) => index % 5 == 0 || index == api.Economy.Count - 1)
                .Select(point => $"{point.Minute}分 G{point.Gold:0}/X{point.Xp:0}/补{point.LastHits}");
            lines.Add("经济曲线：" + string.Join(" · ", samples));
        }

        if (api.ActionsPerMinute > 0)
        {
            lines.Add($"APM {api.ActionsPerMinute:0} · 英雄伤害 {api.HeroDamage:0} · 塔伤害 {api.TowerDamage:0}");
        }

        return lines;
    }

    private static string FormatPlayerSummary(PlayerSummary player)
    {
        var hero = string.IsNullOrWhiteSpace(player.HeroName) ? $"英雄{player.HeroId}" : player.HeroName;
        return $"{hero}({player.Kills}/{player.Deaths}/{player.Assists} GPM{player.Gpm})";
    }

    private static string FormatPercentile(double percentile) =>
        percentile >= 0 ? $"{percentile:0}%分位" : "未知";

    public static string FormatOperation(OperationEvent op)
    {
        var action = op.Kind switch
        {
            "cast" => $"施放 {op.Detail}",
            "item_use" => $"使用 {op.Detail}",
            "buy" => $"购买 {op.Detail}",
            "kill" => op.Detail,
            "buyback" => "买活",
            "smoke" => "使用诡计之雾",
            "roshan" => "Roshan被击杀",
            "building" => op.Detail,
            "ult_idle" => op.Detail,
            _ => op.Detail,
        };
        return $"{op.TimeText} {action}";
    }

    private static string TeamText(string? team) => team switch
    {
        "radiant" => "天辉",
        "dire" => "夜魇",
        _ => "阵营未知",
    };
}
