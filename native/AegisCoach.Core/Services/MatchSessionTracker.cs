using System.Text.Json;
using AegisCoach.Core.Models;

namespace AegisCoach.Core.Services;

/// <summary>
/// 对局会话跟踪器：消费 GSI 归一化快照，累积一局内的事件（阵亡/击杀/买装备/拆塔/肉山），
/// 在进入赛后阶段时产出 <see cref="MatchSessionRecord"/>，并负责把“游戏习惯”与
/// 赛后报告持久化到本地 JSON。
/// </summary>
public sealed class MatchSessionTracker
{
    private const int MaxEconomySamples = 24;
    private const int MaxMoments = 40;
    private MatchSessionRecord? _current;
    private MatchSnapshot? _previous;

    /// <summary>游戏习惯库目录，可在设置页自定义。</summary>
    public static string HabitsDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AegisCoach",
        "habits");

    /// <summary>赛后分析报告保存目录，可在设置页自定义。</summary>
    public static string AnalysisDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AegisCoach",
        "analysis");

    private static string HabitsPath => Path.Combine(
        HabitsDirectory,
        "player-habits.json");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>消费一帧快照。返回非 null 表示本局结束并产出了对局记录。</summary>
    public MatchSessionRecord? OnSnapshot(MatchSnapshot snapshot)
    {
        var previous = _previous;
        _previous = snapshot;

        if (snapshot.Phase != GamePhase.InProgress)
        {
            if (snapshot.Phase == GamePhase.PostGame && _current is not null)
            {
                return Finalize(snapshot);
            }

            if (snapshot.Phase != GamePhase.HeroSelection && snapshot.Phase != GamePhase.StrategyTime)
            {
                _current = null;
            }
            return null;
        }

        if (snapshot.MatchId <= 0 || snapshot.Hero.Id <= 0)
        {
            _current = null;
            return null;
        }

        if (_current is null || _current.MatchId != snapshot.MatchId)
        {
            _current = new MatchSessionRecord
            {
                MatchId = snapshot.MatchId,
                HeroId = snapshot.Hero.Id,
                StartedAt = DateTimeOffset.UtcNow,
            };
        }

        var record = _current;
        record.HeroId = snapshot.Hero.Id;
        if (!string.IsNullOrWhiteSpace(snapshot.Player.SteamId))
        {
            record.SteamId = snapshot.Player.SteamId;
        }
        record.DurationSeconds = snapshot.GameTime;
        record.Kills = snapshot.Player.Kills;
        record.Deaths = snapshot.Player.Deaths;
        record.Assists = snapshot.Player.Assists;
        record.LastHits = snapshot.Player.LastHits;
        record.Denies = snapshot.Player.Denies;
        record.Gpm = snapshot.Player.Gpm;
        record.Xpm = snapshot.Player.Xpm;
        if (snapshot.Hero.Level > record.MaxLevel) record.MaxLevel = snapshot.Hero.Level;
        record.FinalItems = snapshot.Items
            .Where(item => item.SlotKind == ItemSlotKind.Main && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => CoachRuleEngine.FriendlyItemName(item.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        if (previous is { InMatch: true } && previous.MatchId == snapshot.MatchId)
        {
            CaptureEvents(record, previous, snapshot);
        }

        // 经济曲线：每 5 分钟采样一次。
        var lastSample = record.Economy.LastOrDefault();
        if (lastSample is null || snapshot.GameTime - lastSample.GameTime >= 300)
        {
            if (record.Economy.Count >= MaxEconomySamples) record.Economy.RemoveAt(0);
            record.Economy.Add(new EconomySample
            {
                GameTime = snapshot.GameTime,
                Gpm = snapshot.Player.Gpm,
                Xpm = snapshot.Player.Xpm,
            });
        }

        return null;
    }

    private MatchSessionRecord Finalize(MatchSnapshot snapshot)
    {
        var record = _current!;
        record.EndedAt = DateTimeOffset.UtcNow;
        record.DurationSeconds = Math.Max(record.DurationSeconds, snapshot.GameTime);
        if (!string.IsNullOrWhiteSpace(snapshot.WinTeam) && !string.IsNullOrWhiteSpace(snapshot.Player.TeamName))
        {
            record.Won = string.Equals(snapshot.WinTeam, snapshot.Player.TeamName, StringComparison.OrdinalIgnoreCase);
        }

        // 赛后快照通常不再携带完整装备，若为空则保留最后一帧 InProgress 的装备。
        var postItems = snapshot.Items
            .Where(item => item.SlotKind == ItemSlotKind.Main && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => CoachRuleEngine.FriendlyItemName(item.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        if (postItems.Count > 0) record.FinalItems = postItems;

        _current = null;
        return record;
    }

    private static void CaptureEvents(MatchSessionRecord record, MatchSnapshot previous, MatchSnapshot current)
    {
        var time = current.GameTimeText;
        if (previous.Hero.Alive && !current.Hero.Alive)
        {
            AddMoment(record, $"{time} 阵亡（第 {current.Player.Deaths} 次）");
        }

        if (current.Player.Kills > previous.Player.Kills)
        {
            var streak = current.Player.KillStreak > 1 ? $"，连杀 {current.Player.KillStreak}" : string.Empty;
            AddMoment(record, $"{time} 击杀{streak}");
        }

        var previousItemNames = previous.Items
            .Where(item => item.SlotKind == ItemSlotKind.Main)
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in current.Items.Where(item => item.SlotKind == ItemSlotKind.Main))
        {
            if (string.IsNullOrWhiteSpace(item.Name) || previousItemNames.Contains(item.Name)) continue;
            AddMoment(record, $"{time} 购买 {CoachRuleEngine.FriendlyItemName(item.Name)}");
        }

        foreach (var building in current.Buildings)
        {
            var before = previous.Buildings.FirstOrDefault(item => item.Team == building.Team && item.Key == building.Key);
            if (before is { Alive: true } && !building.Alive)
            {
                AddMoment(record, $"{time} {building.TeamLabel}{building.Label}被摧毁");
            }
        }

        if (previous.RoshanAlive == true && current.RoshanAlive == false)
        {
            AddMoment(record, $"{time} Roshan 被击杀");
        }

        if (current.Hero.Level > previous.Hero.Level)
        {
            AddMoment(record, $"{time} 升到 {current.Hero.Level} 级");
        }
    }

    private static void AddMoment(MatchSessionRecord record, string moment)
    {
        record.KeyMoments.Add(moment);
        if (record.KeyMoments.Count > MaxMoments) record.KeyMoments.RemoveAt(0);
    }

    /// <summary>把一条对局记录追加到本地习惯库（保留最近 300 场）。</summary>
    public static PlayerHabits AppendHabit(MatchSessionRecord record)
    {
        var habits = LoadHabits();
        if (record.MatchId > 0 && habits.Matches.Any(existing => existing.MatchId == record.MatchId)) return habits;
        if (habits.SteamId is null && !string.IsNullOrWhiteSpace(record.SteamId))
        {
            habits.SteamId = record.SteamId;
        }
        habits.Matches.Insert(0, record);
        if (habits.Matches.Count > 300) habits.Matches.RemoveRange(300, habits.Matches.Count - 300);
        SaveHabits(habits);
        return habits;
    }

    public static PlayerHabits LoadHabits()
    {
        try
        {
            if (File.Exists(HabitsPath))
            {
                var habits = JsonSerializer.Deserialize<PlayerHabits>(File.ReadAllText(HabitsPath), JsonOptions);
                if (habits is not null) return habits;
            }
        }
        catch
        {
            // 损坏的习惯数据不阻塞功能，重新开始记录。
        }

        return new PlayerHabits();
    }

    public static void SaveHabits(PlayerHabits habits)
    {
        try
        {
            var directory = Path.GetDirectoryName(HabitsPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(HabitsPath, JsonSerializer.Serialize(habits, JsonOptions));
        }
        catch
        {
            // 本地记录失败只影响习惯统计，不影响实时教练。
        }
    }

    public static string SaveReport(MatchReport report)
    {
        try
        {
            Directory.CreateDirectory(AnalysisDirectory);
            var path = Path.Combine(AnalysisDirectory, $"{report.Id}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
            return path;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static IReadOnlyList<MatchReport> LoadReports()
    {
        var reports = new List<MatchReport>();
        if (!Directory.Exists(AnalysisDirectory)) return reports;
        foreach (var file in Directory.EnumerateFiles(AnalysisDirectory, "*.json"))
        {
            try
            {
                var report = JsonSerializer.Deserialize<MatchReport>(File.ReadAllText(file), JsonOptions);
                if (report is not null) reports.Add(report);
            }
            catch
            {
                // 跳过损坏的报告文件。
            }
        }

        return reports
            .OrderByDescending(report => report.GeneratedAt)
            .Take(200)
            .ToList();
    }
}
