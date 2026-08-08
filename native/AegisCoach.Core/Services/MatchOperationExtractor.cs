using System.Text.Json;
using AegisCoach.Core.Models;

namespace AegisCoach.Core.Services;

/// <summary>
/// 从本地 NDJSON 回放中按 matchid 提取一局比赛的“操作记录”：
/// 技能施放、主动道具使用、购买、阵亡上下文、买活决策、击杀与地图事件。
/// 数据边界：GSI 没有英雄坐标、敌方状态与视野，只能点评自身操作时机与决策，
/// 不能点评走位、视野与团战细节。
/// </summary>
public sealed class MatchOperationExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const double DeathWindowSeconds = 35;
    private const double RecentCastWindowSeconds = 20;
    private const double MinFrameCount = 10;
    private const double PhaseBucketSeconds = 600;

    /// <summary>
    /// 在 replayDirectory 中查找 matchId 对应的对局回放。
    /// 返回 null 表示未找到足够帧（本局没有开启记录或记录缺失）。
    /// </summary>
    public MatchOperationSummary? Extract(
        string replayDirectory,
        long matchId,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt)
    {
        if (matchId <= 0 || string.IsNullOrWhiteSpace(replayDirectory) || !Directory.Exists(replayDirectory))
            return null;

        var frames = LoadMatchFrames(replayDirectory, matchId, startedAt, endedAt);
        if (frames.Count < MinFrameCount) return null;
        return Analyze(frames);
    }

    private static List<MatchSnapshot> LoadMatchFrames(
        string replayDirectory,
        long matchId,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt)
    {
        var startDate = startedAt.ToLocalTime().Date.AddDays(-1);
        var endDate = endedAt.ToLocalTime().Date.AddDays(1);
        var files = Directory.EnumerateFiles(replayDirectory, "snapshots-*.ndjson", SearchOption.TopDirectoryOnly)
            .Where(file => IsInDateRange(file, startDate, endDate))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byGameTime = new Dictionary<double, MatchSnapshot>();
        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var snapshot = TryParseMatchLine(line, matchId);
                if (snapshot is null || snapshot.Phase != GamePhase.InProgress) continue;
                byGameTime[snapshot.GameTime] = snapshot;
            }
        }

        return byGameTime.Values
            .OrderBy(snapshot => snapshot.GameTime)
            .ToList();
    }

    private static bool IsInDateRange(string file, DateTime startDate, DateTime endDate)
    {
        var name = Path.GetFileName(file);
        var prefix = "snapshots-";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var dateText = name[prefix.Length..];
        var separator = dateText.IndexOf('.');
        if (separator > 0) dateText = dateText[..separator];
        return DateTime.TryParseExact(
            dateText,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var date) && date >= startDate && date <= endDate;
    }

    private static MatchSnapshot? TryParseMatchLine(string line, long matchId)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("snapshot", out var snapshotElement)) return null;
            if (!snapshotElement.TryGetProperty("map", out var mapElement)
                || !mapElement.TryGetProperty("matchid", out var matchIdElement)
                || matchIdElement.GetInt64() != matchId)
            {
                return null;
            }

            var raw = snapshotElement.Deserialize<GsiSnapshot>(JsonOptions);
            return raw is null ? null : MatchSnapshot.FromGsi(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MatchOperationSummary Analyze(List<MatchSnapshot> frames)
    {
        var summary = new MatchOperationSummary
        {
            MatchId = frames[0].MatchId,
            HeroId = frames[0].Hero.Id,
            Won = frames[^1].WinTeam is not null && frames[^1].Player.TeamName is not null
                ? string.Equals(frames[^1].WinTeam, frames[^1].Player.TeamName, StringComparison.OrdinalIgnoreCase)
                : null,
            DurationSeconds = frames[^1].GameTime,
            ScoreText = frames[^1].ScoreText,
        };

        var lastFrame = frames[^1];
        summary.Kills = lastFrame.Player.Kills;
        summary.Deaths = lastFrame.Player.Deaths;
        summary.Assists = lastFrame.Player.Assists;
        summary.LastHits = lastFrame.Player.LastHits;
        summary.Denies = lastFrame.Player.Denies;
        summary.Gpm = lastFrame.Player.Gpm;
        summary.Xpm = lastFrame.Player.Xpm;
        summary.MaxLevel = frames.Max(frame => frame.Hero.Level);

        var window = new List<MatchSnapshot>();
        MatchSnapshot? previous = null;
        double? ultReadySince = null;
        double? lastDeathTime = null;
        int lastKills = 0;

        foreach (var current in frames)
        {
            var time = current.GameTime;
            window.RemoveAll(frame => time - frame.GameTime > DeathWindowSeconds);

            if (previous is not null)
            {
                DetectAbilityCasts(summary, previous, current, ref ultReadySince);
                DetectItemUses(summary, previous, current);
                DetectPurchases(summary, previous, current);
                DetectSmokeUse(summary, previous, current);
                DetectDeaths(summary, previous, current, window, ref lastDeathTime);
                DetectKills(summary, current, ref lastKills);
                DetectBuyback(summary, previous, current);
                DetectMapEvents(summary, previous, current);
                TrackUltIdle(summary, previous, current, ref ultReadySince);
            }

            window.Add(current);
            previous = current;
        }

        // 对局结束时，若大招已就绪超过 3 分钟，补一条 idle 记录。
        if (ultReadySince is not null && lastFrame.GameTime - ultReadySince.Value >= 180)
        {
            summary.Operations.Add(new OperationEvent
            {
                GameTime = lastFrame.GameTime,
                Kind = "ult_idle",
                Detail = $"大招从 {MatchSnapshot.FormatGameTime(ultReadySince.Value)} 起就绪，至对局结束未使用",
            });
        }

        BuildPhaseStats(summary, frames);
        BuildEconomySamples(summary, frames);
        return summary;
    }

    private static void DetectAbilityCasts(
        MatchOperationSummary summary,
        MatchSnapshot previous,
        MatchSnapshot current,
        ref double? ultReadySince)
    {
        foreach (var ability in current.Abilities)
        {
            if (ability.Level <= 0 || ability.IsPassive) continue;
            var before = previous.Abilities.FirstOrDefault(item => item.Slot == ability.Slot);
            if (before is null || before.Cooldown > 0.5 || ability.Cooldown <= 2) continue;

            summary.Operations.Add(new OperationEvent
            {
                GameTime = current.GameTime,
                Kind = "cast",
                Detail = CoachRuleEngine.FriendlyAbilityName(ability.Name),
            });
            if (ability.IsUltimate) ultReadySince = null;
        }
    }

    private static void DetectItemUses(
        MatchOperationSummary summary,
        MatchSnapshot previous,
        MatchSnapshot current)
    {
        foreach (var item in current.Items)
        {
            if (item.IsPassive || !item.CanCast) continue;
            if (item.Name == "item_tpscroll") continue;
            var before = previous.Items.FirstOrDefault(previousItem => previousItem.Slot == item.Slot);
            if (before is null || before.Cooldown > 0.5 || item.Cooldown <= 2) continue;
            summary.Operations.Add(new OperationEvent
            {
                GameTime = current.GameTime,
                Kind = "item_use",
                Detail = CoachRuleEngine.FriendlyItemName(item.Name),
            });
        }

        // 回城卷轴/远行鞋使用后从物品栏消失。
        foreach (var before in previous.Items.Where(item =>
                     item.SlotKind == ItemSlotKind.Teleport &&
                     item.Name == "item_tpscroll"))
        {
            var after = current.Items.FirstOrDefault(item => item.Slot == before.Slot);
            if (after is not null && !string.IsNullOrWhiteSpace(after.Name)) continue;
            summary.Operations.Add(new OperationEvent
            {
                GameTime = current.GameTime,
                Kind = "item_use",
                Detail = "回城卷轴",
            });
        }
    }

    private static bool IsTeleportItem(string name) =>
        name is "item_tpscroll" or "item_travel_boots" or "item_travel_boots_2"
            or "item_travel_boots_3" or "item_travel_boots_4";

    private static void DetectPurchases(
        MatchOperationSummary summary,
        MatchSnapshot previous,
        MatchSnapshot current)
    {
        if (current.GameTime < 60) return;
        foreach (var item in current.Items.Where(item => item.SlotKind == ItemSlotKind.Main))
        {
            if (string.IsNullOrWhiteSpace(item.Name)) continue;
            var before = previous.Items.FirstOrDefault(previousItem => previousItem.Slot == item.Slot);
            if (before is not null && before.Name == item.Name) continue;
            summary.Operations.Add(new OperationEvent
            {
                GameTime = current.GameTime,
                Kind = "buy",
                Detail = CoachRuleEngine.FriendlyItemName(item.Name),
            });
        }
    }

    private static void DetectSmokeUse(
        MatchOperationSummary summary,
        MatchSnapshot previous,
        MatchSnapshot current)
    {
        if (current.GameTime < 60) return;
        const string smoke = "item_smoke_of_deceit";
        var had = previous.Items.Any(item => item.Name == smoke);
        var has = current.Items.Any(item => item.Name == smoke);
        if (had && !has)
        {
            summary.Operations.Add(new OperationEvent
            {
                GameTime = current.GameTime,
                Kind = "smoke",
                Detail = "使用诡计之雾",
            });
        }
    }

    private static void DetectDeaths(
        MatchOperationSummary summary,
        MatchSnapshot previous,
        MatchSnapshot current,
        IReadOnlyList<MatchSnapshot> window,
        ref double? lastDeathTime)
    {
        if (!previous.Hero.Alive || current.Hero.Alive) return;
        var time = current.GameTime;
        var windowStart = window.Count > 0 ? window[0] : previous;
        var deathNumber = current.Player.Deaths;
        var readyActives = previous.Items
            .Where(item => item.CanCast && !item.IsPassive && item.Cooldown <= 0.5)
            .Where(item => !IsTeleportItem(item.Name))
            .Where(item => windowStart.Items.Any(start => start.Slot == item.Slot && start.Name == item.Name))
            .Select(item => CoachRuleEngine.FriendlyItemName(item.Name))
            .Distinct()
            .ToList();
        var tpAvailable = previous.Items.Any(item => IsTeleportItem(item.Name) && item.Cooldown <= 0.5);

        summary.DeathsDetailed.Add(new DeathAnalysis
        {
            GameTime = time,
            DeathNumber = deathNumber,
            Level = previous.Hero.Level,
            Gold = previous.Player.Gold,
            HealthPercentAtWindowStart = windowStart.Hero.HealthPercent,
            HealthPercentAtDeath = previous.Hero.HealthPercent,
            MinHealthPercentInWindow = window.Count > 0 ? window.Min(frame => frame.Hero.HealthPercent) : previous.Hero.HealthPercent,
            ManaPercentAtDeath = previous.Hero.ManaPercent,
            UltReadyAtDeath = IsUltimateReady(previous),
            TpAvailable = tpAvailable,
            BuybackAvailable = current.Hero.BuybackCost > 0
                && current.Hero.BuybackCooldown <= 0
                && current.Player.Gold >= current.Hero.BuybackCost,
            BuybackCost = current.Hero.BuybackCost,
            ReadyActivesUnused = readyActives,
            RecentCasts = summary.Operations
                .Where(item => item.Kind is "cast" or "item_use" && time - item.GameTime <= RecentCastWindowSeconds)
                .Select(item => $"{item.TimeText} {item.Detail}")
                .TakeLast(6)
                .ToList(),
            LastHitsInWindow = Math.Max(0, current.Player.LastHits - windowStart.Player.LastHits),
            GapSincePreviousDeathSeconds = lastDeathTime is null ? null : time - lastDeathTime,
        });

        summary.Operations.Add(new OperationEvent
        {
            GameTime = time,
            Kind = "death",
            Detail = $"第 {deathNumber} 次阵亡",
        });
        lastDeathTime = time;
    }

    private static void DetectKills(
        MatchOperationSummary summary,
        MatchSnapshot current,
        ref int lastKills)
    {
        if (current.Player.Kills <= lastKills) return;
        lastKills = current.Player.Kills;
        summary.Operations.Add(new OperationEvent
        {
            GameTime = current.GameTime,
            Kind = "kill",
            Detail = current.Player.KillStreak > 1
                ? $"击杀（连杀 {current.Player.KillStreak}）"
                : "击杀",
        });
    }

    private static void DetectBuyback(
        MatchOperationSummary summary,
        MatchSnapshot previous,
        MatchSnapshot current)
    {
        if (current.Hero.Alive || previous.Hero.Alive) return;
        if (current.Hero.BuybackCooldown <= 0 || previous.Hero.BuybackCooldown > 0) return;
        summary.Operations.Add(new OperationEvent
        {
            GameTime = current.GameTime,
            Kind = "buyback",
            Detail = "买活",
        });
        var recentDeath = summary.DeathsDetailed
            .Where(death => current.GameTime - death.GameTime <= 120)
            .OrderByDescending(death => death.GameTime)
            .FirstOrDefault();
        if (recentDeath is not null) recentDeath.BuybackUsed = true;
    }

    private static void DetectMapEvents(
        MatchOperationSummary summary,
        MatchSnapshot previous,
        MatchSnapshot current)
    {
        if (previous.RoshanAlive == true && current.RoshanAlive == false)
        {
            summary.Operations.Add(new OperationEvent
            {
                GameTime = current.GameTime,
                Kind = "roshan",
                Detail = "Roshan 被击杀",
            });
        }

        foreach (var building in current.Buildings)
        {
            var before = previous.Buildings.FirstOrDefault(item => item.Team == building.Team && item.Key == building.Key);
            if (before is { Alive: true } && !building.Alive)
            {
                summary.Operations.Add(new OperationEvent
                {
                    GameTime = current.GameTime,
                    Kind = "building",
                    Detail = $"{building.TeamLabel}{building.Label}被摧毁",
                });
            }
        }
    }

    private static void TrackUltIdle(
        MatchOperationSummary summary,
        MatchSnapshot previous,
        MatchSnapshot current,
        ref double? ultReadySince)
    {
        var wasReady = IsUltimateReady(previous);
        var isReady = IsUltimateReady(current);
        if (isReady && !wasReady && ultReadySince is null)
        {
            ultReadySince = current.GameTime;
            return;
        }

        if (!isReady && ultReadySince is not null)
        {
            var idleSeconds = current.GameTime - ultReadySince.Value;
            if (idleSeconds >= 180)
            {
                summary.Operations.Add(new OperationEvent
                {
                    GameTime = current.GameTime,
                    Kind = "ult_idle",
                    Detail = $"大招从 {MatchSnapshot.FormatGameTime(ultReadySince.Value)} 起就绪 {MatchSnapshot.FormatGameTime(idleSeconds)} 未使用",
                });
            }
            ultReadySince = null;
        }
    }

    private static void BuildPhaseStats(MatchOperationSummary summary, IReadOnlyList<MatchSnapshot> frames)
    {
        var labels = new[] { "对线期", "中期", "中后期", "后期" };
        var bucketCount = (int)Math.Ceiling(frames[^1].GameTime / PhaseBucketSeconds);
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var start = bucket * PhaseBucketSeconds;
            var end = Math.Min((bucket + 1) * PhaseBucketSeconds, frames[^1].GameTime);
            var bucketFrames = frames.Where(frame => frame.GameTime >= start && frame.GameTime < end).ToList();
            if (bucketFrames.Count == 0) continue;

            var first = bucketFrames[0];
            var last = bucketFrames[^1];
            var startSeconds = bucket == 0 ? 0 : start;
            var durationMinutes = Math.Max(1, (last.GameTime - startSeconds) / 60d);
            var phase = new PhaseStat
            {
                StartSeconds = startSeconds,
                EndSeconds = end,
                Label = bucket < labels.Length ? labels[bucket] : $"第{bucket + 1}阶段",
                KillsDelta = last.Player.Kills - first.Player.Kills,
                DeathsDelta = last.Player.Deaths - first.Player.Deaths,
                AssistsDelta = last.Player.Assists - first.Player.Assists,
                LastHitsDelta = last.Player.LastHits - first.Player.LastHits,
                DeniesDelta = last.Player.Denies - first.Player.Denies,
                AvgGpm = bucketFrames.Average(frame => frame.Player.Gpm),
                AvgXpm = bucketFrames.Average(frame => frame.Player.Xpm),
                LastHitsPerMinute = (last.Player.LastHits - (bucket == 0 ? 0 : first.Player.LastHits)) / durationMinutes,
                ItemsBought = summary.Operations
                    .Where(item => item.Kind == "buy" && item.GameTime >= start && item.GameTime < end)
                    .Select(item => $"{item.Detail}@{item.TimeText}")
                    .ToList(),
            };
            summary.Phases.Add(phase);
        }
    }

    private static void BuildEconomySamples(MatchOperationSummary summary, IReadOnlyList<MatchSnapshot> frames)
    {
        double lastSampleTime = -300;
        foreach (var frame in frames)
        {
            if (frame.GameTime - lastSampleTime < 300) continue;
            lastSampleTime = frame.GameTime;
            summary.Economy.Add(new EconomySample
            {
                GameTime = frame.GameTime,
                Gpm = frame.Player.Gpm,
                Xpm = frame.Player.Xpm,
            });
        }
    }

    private static bool IsUltimateReady(MatchSnapshot snapshot)
    {
        var ultimate = snapshot.Abilities.FirstOrDefault(ability => ability.IsUltimate && ability.Level > 0)
            ?? snapshot.Abilities.FirstOrDefault(ability => ability.Slot is "ability_3" or "ability_4" && ability.Level > 0);
        return ultimate is not null && ultimate.Cooldown <= 0.5 && !ultimate.IsPassive;
    }
}
