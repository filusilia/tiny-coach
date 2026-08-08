using System.Text.Json.Serialization;

namespace AegisCoach.Core.Models;

/// <summary>一场已结束对局的紧凑摘要，同时作为“游戏习惯”记录的最小单位。</summary>
public sealed class MatchSessionRecord
{
    public long MatchId { get; set; }
    public int HeroId { get; set; }
    public string HeroName { get; set; } = string.Empty;
    /// <summary>Steam ID 64 位字符串，用于赛后按比赛 ID 从 OpenDota 定位本人在场数据。</summary>
    public string SteamId { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public double DurationSeconds { get; set; }
    public bool? Won { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int LastHits { get; set; }
    public int Denies { get; set; }
    public int Gpm { get; set; }
    public int Xpm { get; set; }
    public int MaxLevel { get; set; }
    public List<string> FinalItems { get; set; } = [];
    public List<string> KeyMoments { get; set; } = [];
    public List<EconomySample> Economy { get; set; } = [];

    [JsonIgnore]
    public string DateText => StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    [JsonIgnore]
    public string ResultText => Won switch
    {
        true => "胜利",
        false => "失败",
        null => "结果未知",
    };

    [JsonIgnore]
    public string DurationText => MatchSnapshot.FormatGameTime(DurationSeconds);

    [JsonIgnore]
    public string KdaText => $"{Kills}/{Deaths}/{Assists}";

    [JsonIgnore]
    public string SummaryLine =>
        $"{DateText}  {HeroName}  {ResultText}  {DurationText}  KDA {KdaText}  GPM {Gpm}";
}

/// <summary>经济曲线采样点（每约 5 分钟一帧）。</summary>
public sealed class EconomySample
{
    public double GameTime { get; set; }
    public int Gpm { get; set; }
    public int Xpm { get; set; }

    [JsonIgnore]
    public string TimeText => MatchSnapshot.FormatGameTime(GameTime);
}

/// <summary>本地游戏习惯数据：历史对局记录 + 汇总统计。</summary>
public sealed class PlayerHabits
{
    public List<MatchSessionRecord> Matches { get; set; } = [];
    public string? SteamId { get; set; }

    [JsonIgnore]
    public int TotalMatches => Matches.Count;

    [JsonIgnore]
    public int WinCount => Matches.Count(match => match.Won == true);

    [JsonIgnore]
    public double WinRate => TotalMatches > 0 ? WinCount * 100d / TotalMatches : 0;

    [JsonIgnore]
    public string WinRateText => TotalMatches > 0 ? $"{WinRate:0.0}%" : "--";

    [JsonIgnore]
    public string MostPlayedHero => Matches
        .GroupBy(match => match.HeroName)
        .OrderByDescending(group => group.Count())
        .Select(group => $"{group.Key}（{group.Count()} 场）")
        .FirstOrDefault() ?? "--";

    [JsonIgnore]
    public string AverageKda
    {
        get
        {
            if (TotalMatches == 0) return "--";
            var kills = Matches.Average(match => match.Kills);
            var deaths = Matches.Average(match => match.Deaths);
            var assists = Matches.Average(match => match.Assists);
            return $"{kills:0.0}/{deaths:0.0}/{assists:0.0}";
        }
    }

    [JsonIgnore]
    public string MostBoughtItem => Matches
        .SelectMany(match => match.FinalItems)
        .GroupBy(name => name)
        .OrderByDescending(group => group.Count())
        .Select(group => $"{group.Key}（{group.Count()} 场）")
        .FirstOrDefault() ?? "--";

    [JsonIgnore]
    public string AverageDuration => TotalMatches == 0
        ? "--"
        : MatchSnapshot.FormatGameTime(Matches.Average(match => match.DurationSeconds));
}

/// <summary>赛后 AI 分析报告（事实摘要 + AI 复盘文本）。</summary>
public sealed class MatchReport
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public string Backend { get; set; } = string.Empty;
    public MatchSessionRecord Match { get; set; } = new();
    public string AiReview { get; set; } = string.Empty;
    /// <summary>从本地 GSI 回放提取的整场操作记录，供 AI 点评与界面展示。</summary>
    public MatchOperationSummary? Operations { get; set; }
    /// <summary>按比赛 ID 从 OpenDota API 拉取的整场数据（双方阵容、装备时间、团战、视野等）。</summary>
    public ApiMatchSummary? Api { get; set; }

    [JsonIgnore]
    public string GeneratedText => GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

/// <summary>
/// 从一局 NDJSON 回放中提取的“操作记录”：
/// 技能/主动道具的施放与使用、购买、阵亡上下文、买活决策、击杀与地图事件。
/// 阶段统计只作为点评背景，点评主体交给模型基于操作记录判断。
/// </summary>
public sealed class MatchOperationSummary
{
    public long MatchId { get; set; }
    public int HeroId { get; set; }
    public string HeroName { get; set; } = string.Empty;
    public bool? Won { get; set; }
    public double DurationSeconds { get; set; }
    public string ScoreText { get; set; } = string.Empty;
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int LastHits { get; set; }
    public int Denies { get; set; }
    public int Gpm { get; set; }
    public int Xpm { get; set; }
    public int MaxLevel { get; set; }
    public List<PhaseStat> Phases { get; set; } = [];
    public List<OperationEvent> Operations { get; set; } = [];
    public List<DeathAnalysis> DeathsDetailed { get; set; } = [];
    public List<EconomySample> Economy { get; set; } = [];
}

/// <summary>阶段背景：每 10 分钟一段，只作点评上下文。</summary>
public sealed class PhaseStat
{
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string Label { get; set; } = string.Empty;
    public int KillsDelta { get; set; }
    public int DeathsDelta { get; set; }
    public int AssistsDelta { get; set; }
    public int LastHitsDelta { get; set; }
    public int DeniesDelta { get; set; }
    public double AvgGpm { get; set; }
    public double AvgXpm { get; set; }
    public double LastHitsPerMinute { get; set; }
    public List<string> ItemsBought { get; set; } = [];

    [JsonIgnore]
    public string TimeText => $"{MatchSnapshot.FormatGameTime(StartSeconds)}-{MatchSnapshot.FormatGameTime(EndSeconds)}";
}

/// <summary>
/// 一条操作事件。Kind 取值：cast（技能施放）、item_use（主动道具使用）、buy（购买）、
/// death（阵亡）、kill（击杀）、buyback（买活）、smoke（使用诡计之雾）、
/// roshan（肉山）、building（建筑）、ult_idle（大招长时间就绪未使用）。
/// </summary>
public sealed class OperationEvent
{
    public double GameTime { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;

    [JsonIgnore]
    public string TimeText => MatchSnapshot.FormatGameTime(GameTime);
}

/// <summary>阵亡复盘上下文：阵亡前约 30 秒内的关键决策信息。</summary>
public sealed class DeathAnalysis
{
    public double GameTime { get; set; }
    public int DeathNumber { get; set; }
    public int Level { get; set; }
    public int Gold { get; set; }
    public double HealthPercentAtWindowStart { get; set; }
    public double HealthPercentAtDeath { get; set; }
    public double MinHealthPercentInWindow { get; set; }
    public double ManaPercentAtDeath { get; set; }
    public bool UltReadyAtDeath { get; set; }
    public bool TpAvailable { get; set; }
    public bool BuybackAvailable { get; set; }
    public bool BuybackUsed { get; set; }
    public int BuybackCost { get; set; }
    public List<string> ReadyActivesUnused { get; set; } = [];
    public List<string> RecentCasts { get; set; } = [];
    public int LastHitsInWindow { get; set; }
    public double? GapSincePreviousDeathSeconds { get; set; }

    [JsonIgnore]
    public string TimeText => MatchSnapshot.FormatGameTime(GameTime);
}

/// <summary>
/// 按比赛 ID 从 OpenDota 拉取的整场复盘数据。
/// 与本地 GSI 操作记录互补：API 提供双方阵容、装备购买时间、团战、视野、经济分位，
/// 但没有阵亡前的逐秒状态（本地回放才有）。
/// </summary>
public sealed class ApiMatchSummary
{
    public long MatchId { get; set; }
    public bool? RadiantWin { get; set; }
    public double DurationSeconds { get; set; }
    public int RadiantScore { get; set; }
    public int DireScore { get; set; }
    public string? GameMode { get; set; }
    public int? RankTier { get; set; }
    public int HeroId { get; set; }
    public int AccountId { get; set; }
    public bool IsRadiant { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int LastHits { get; set; }
    public int Denies { get; set; }
    public int Gpm { get; set; }
    public int Xpm { get; set; }
    public int Level { get; set; }
    public int NetWorth { get; set; }
    public List<string> FinalItems { get; set; } = [];
    public List<ItemTiming> ItemTimings { get; set; } = [];
    public List<KillEvent> KillsLog { get; set; } = [];
    public List<double> DeathTimes { get; set; } = [];
    public List<double> BuybackTimes { get; set; } = [];
    public List<HeroCount> KilledBy { get; set; } = [];
    public List<HeroCount> Killed { get; set; } = [];
    public List<EconomyPoint> Economy { get; set; } = [];
    public List<TeamFightSummary> Teamfights { get; set; } = [];
    public List<TowerKill> TowersTaken { get; set; } = [];
    public int RoshansKilled { get; set; }
    public int ObserversPlaced { get; set; }
    public int SentriesPlaced { get; set; }
    public int ObserverLogCount { get; set; }
    public double TeamfightParticipation { get; set; }
    public double HeroDamage { get; set; }
    public double TowerDamage { get; set; }
    public double HeroHealing { get; set; }
    public double ActionsPerMinute { get; set; }
    public int Pings { get; set; }
    public string? Lane { get; set; }
    public int LaneRole { get; set; }
    public double? LaneEfficiencyPct { get; set; }
    public Dictionary<string, double> Benchmarks { get; set; } = [];
    public List<PlayerSummary> AllPlayers { get; set; } = [];
    public string? Error { get; set; }

    [JsonIgnore]
    public string ScoreText => $"{RadiantScore} : {DireScore}";

    [JsonIgnore]
    public bool Won => RadiantWin == IsRadiant;
}

public sealed class ItemTiming
{
    public double TimeSeconds { get; set; }
    public string ItemName { get; set; } = string.Empty;

    [JsonIgnore]
    public string TimeText => MatchSnapshot.FormatGameTime(TimeSeconds);
}

public sealed class KillEvent
{
    public double TimeSeconds { get; set; }
    public string VictimHero { get; set; } = string.Empty;

    [JsonIgnore]
    public string TimeText => MatchSnapshot.FormatGameTime(TimeSeconds);
}

public sealed class HeroCount
{
    public string HeroName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class EconomyPoint
{
    public int Minute { get; set; }
    public double Gold { get; set; }
    public double Xp { get; set; }
    public int LastHits { get; set; }
}

public sealed class TeamFightSummary
{
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public bool Participated { get; set; }
    public double MyDamage { get; set; }
    public int MyDeaths { get; set; }
    public int TotalDeaths { get; set; }
    public int MyBuybacks { get; set; }
    public int MyDeathPositions { get; set; }
    public double GoldDelta { get; set; }
    public double XpDelta { get; set; }

    [JsonIgnore]
    public string TimeText => $"{MatchSnapshot.FormatGameTime(StartTime)}-{MatchSnapshot.FormatGameTime(EndTime)}";
}

public sealed class TowerKill
{
    public double TimeSeconds { get; set; }
    public string Building { get; set; } = string.Empty;

    [JsonIgnore]
    public string TimeText => MatchSnapshot.FormatGameTime(TimeSeconds);
}

public sealed class PlayerSummary
{
    public int AccountId { get; set; }
    public int HeroId { get; set; }
    public string HeroName { get; set; } = string.Empty;
    public bool IsRadiant { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int Gpm { get; set; }
    public int Xpm { get; set; }
    public int LastHits { get; set; }
    public List<string> Items { get; set; } = [];
}
