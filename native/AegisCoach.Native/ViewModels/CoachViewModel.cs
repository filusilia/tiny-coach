using System.Collections.ObjectModel;
using AegisCoach.Core.Models;
using AegisCoach.Core.Services;
using AegisCoach.Native.Models;
using AegisCoach.Native.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AegisCoach.Native.ViewModels;

/// <summary>
/// 实时教练页 ViewModel：管理 GSI 状态、自身英雄数值、装备栏、教练提醒列表、
/// 全场阵容、出装推荐与克制选人推荐。
/// </summary>
public sealed partial class CoachViewModel : ObservableObject
{
    private readonly Dictionary<int, BitmapImage> _heroImageCache = new();
    private List<string> _lastCoachItems = [];

    [ObservableProperty]
    public partial string StatusText { get; set; } = "未连接";

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string OverviewText { get; set; } = "等待对局数据…";

    [ObservableProperty]
    public partial string AdviceCountText { get; set; } = "0 条提醒";

    [ObservableProperty]
    public partial bool IsHeroPanelVisible { get; set; }

    [ObservableProperty]
    public partial string HeroName { get; set; } = "等待英雄数据…";

    [ObservableProperty]
    public partial string HeroLevelText { get; set; } = "Lv1";

    [ObservableProperty]
    public partial ImageSource? HeroImage { get; set; }

    [ObservableProperty]
    public partial double HeroHp { get; set; }

    [ObservableProperty]
    public partial double HeroMaxHp { get; set; } = 100;

    [ObservableProperty]
    public partial string HeroHpText { get; set; } = "--";

    [ObservableProperty]
    public partial double HeroMana { get; set; }

    [ObservableProperty]
    public partial double HeroMaxMana { get; set; } = 100;

    [ObservableProperty]
    public partial string HeroManaText { get; set; } = "--";

    [ObservableProperty]
    public partial string HeroKdaText { get; set; } = "KDA --/--/--";

    [ObservableProperty]
    public partial string HeroStatsText { get; set; } = "--";

    [ObservableProperty]
    public partial string AllyHeroesCountText { get; set; } = "";

    [ObservableProperty]
    public partial string EnemyHeroesCountText { get; set; } = "";

    [ObservableProperty]
    public partial string HeroesStatusText { get; set; } = "未启用";

    [ObservableProperty]
    public partial string ItemRecommendText { get; set; } = "--";

    [ObservableProperty]
    public partial string CounterStatusText { get; set; } = "等待全场英雄数据…";

    public ObservableCollection<CoachAdvice> Advices { get; } = [];
    public ObservableCollection<HeroChip> AllyHeroes { get; } = [];
    public ObservableCollection<HeroChip> EnemyHeroes { get; } = [];
    public ObservableCollection<CounterMatchupItem> CountersMe { get; } = [];
    public ObservableCollection<CounterMatchupItem> CountersTarget { get; } = [];
    public ObservableCollection<CounterPickRecommendation> PickRecommendations { get; } = [];
    public ObservableCollection<CoachItemSlot> HeroItems { get; } = [];

    public event Action? OpenSettingsRequested;

    [RelayCommand]
    private void ClearAdvices()
    {
        Advices.Clear();
        AdviceCountText = "0 条提醒";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        OpenSettingsRequested?.Invoke();
    }

    private int _lastHeroId;
    private int _lastGameTimeSec = -1;
    private double _lastHp = -1;
    private double _lastMaxHp = -1;
    private double _lastMana = -1;
    private double _lastMaxMana = -1;
    private int _lastLevel = -1;
    private int _lastKills = -1;
    private int _lastDeaths = -1;
    private int _lastAssists = -1;
    private int _lastGold = -1;
    private int _lastLastHits = -1;
    private string _lastPhaseText = string.Empty;
    private string _lastScoreText = string.Empty;
    private bool? _lastDaytime;
    private string _lastBuildingSummary = string.Empty;

    public void UpdateStatus(bool running, bool isConnected, string? lastError)
    {
        if (IsRunning != running) IsRunning = running;
        if (IsConnected != isConnected) IsConnected = isConnected;

        var newStatus = running
            ? (lastError is null ? (isConnected ? "已连接" : "未连接") : "GSI 启动失败")
            : "已停止";

        if (StatusText != newStatus) StatusText = newStatus;
    }

    public void UpdateSnapshot(MatchSnapshot snapshot, Func<int, string> getHeroName, Func<int, string> getHeroIcon)
    {
        var currentGameSec = (int)Math.Floor(snapshot.GameTime);
        var overviewDirty = currentGameSec != _lastGameTimeSec
            || snapshot.PhaseText != _lastPhaseText
            || (snapshot.InMatch && snapshot.ScoreText != _lastScoreText)
            || snapshot.Daytime != _lastDaytime
            || (snapshot.BuildingSummary ?? string.Empty) != _lastBuildingSummary;

        if (overviewDirty)
        {
            _lastGameTimeSec = currentGameSec;
            _lastPhaseText = snapshot.PhaseText;
            _lastScoreText = snapshot.ScoreText;
            _lastDaytime = snapshot.Daytime;
            _lastBuildingSummary = snapshot.BuildingSummary ?? string.Empty;

            var matchParts = new List<string>(5) { snapshot.PhaseText, snapshot.GameTimeText };
            if (snapshot.InMatch) matchParts.Add($"比分 {snapshot.ScoreText}");
            matchParts.Add(snapshot.Daytime ? "白天" : "夜晚");
            if (!string.IsNullOrWhiteSpace(snapshot.BuildingSummary)) matchParts.Add("建筑 " + snapshot.BuildingSummary);
            OverviewText = string.Join(" · ", matchParts);
        }

        if (snapshot.Hero.Id > 0)
        {
            if (!IsHeroPanelVisible) IsHeroPanelVisible = true;

            // 英雄更换时才更新英雄名称和图标
            if (snapshot.Hero.Id != _lastHeroId)
            {
                _lastHeroId = snapshot.Hero.Id;
                HeroName = getHeroName(snapshot.Hero.Id);

                var iconUrl = getHeroIcon(snapshot.Hero.Id);
                if (!string.IsNullOrWhiteSpace(iconUrl) && !_heroImageCache.TryGetValue(snapshot.Hero.Id, out _))
                {
                    try
                    {
                        _heroImageCache[snapshot.Hero.Id] = new BitmapImage(ImageCacheService.Instance.Resolve(iconUrl));
                    }
                    catch
                    {
                        /* 图标加载失败不影响状态卡 */
                    }
                }

                if (_heroImageCache.TryGetValue(snapshot.Hero.Id, out var heroImage))
                {
                    HeroImage = heroImage;
                }
                else
                {
                    HeroImage = null;
                }
            }

            if (snapshot.Hero.Level != _lastLevel)
            {
                _lastLevel = snapshot.Hero.Level;
                HeroLevelText = $"Lv{snapshot.Hero.Level}";
            }

            if (snapshot.Hero.MaxHealth > 0 &&
                (Math.Abs(snapshot.Hero.Health - _lastHp) > 0.01 || Math.Abs(snapshot.Hero.MaxHealth - _lastMaxHp) > 0.01))
            {
                _lastHp = snapshot.Hero.Health;
                _lastMaxHp = snapshot.Hero.MaxHealth;
                HeroMaxHp = snapshot.Hero.MaxHealth;
                HeroHp = snapshot.Hero.Health;
                HeroHpText = $"{snapshot.Hero.Health:0}/{snapshot.Hero.MaxHealth:0}（{snapshot.Hero.HealthPercent:0}%）";
            }

            if (snapshot.Hero.MaxMana > 0 &&
                (Math.Abs(snapshot.Hero.Mana - _lastMana) > 0.01 || Math.Abs(snapshot.Hero.MaxMana - _lastMaxMana) > 0.01))
            {
                _lastMana = snapshot.Hero.Mana;
                _lastMaxMana = snapshot.Hero.MaxMana;
                HeroMaxMana = snapshot.Hero.MaxMana;
                HeroMana = snapshot.Hero.Mana;
                HeroManaText = $"{snapshot.Hero.Mana:0}/{snapshot.Hero.MaxMana:0}（{snapshot.Hero.ManaPercent:0}%）";
            }

            if (snapshot.Player.Kills != _lastKills || snapshot.Player.Deaths != _lastDeaths || snapshot.Player.Assists != _lastAssists)
            {
                _lastKills = snapshot.Player.Kills;
                _lastDeaths = snapshot.Player.Deaths;
                _lastAssists = snapshot.Player.Assists;
                HeroKdaText = $"KDA {snapshot.Player.Kills}/{snapshot.Player.Deaths}/{snapshot.Player.Assists}";
            }

            if (snapshot.Player.Gold != _lastGold || snapshot.Player.LastHits != _lastLastHits)
            {
                _lastGold = snapshot.Player.Gold;
                _lastLastHits = snapshot.Player.LastHits;
                HeroStatsText = $"金钱 {snapshot.Player.Gold:N0} · GPM {snapshot.Player.Gpm} · XPM {snapshot.Player.Xpm} · 补刀 {snapshot.Player.LastHits}";
            }

            var mainItems = snapshot.Items
                .Where(item => item.SlotKind == ItemSlotKind.Main && !string.IsNullOrWhiteSpace(item.Name))
                .Take(6)
                .Select(item => item.Name)
                .ToList();

            if (!mainItems.SequenceEqual(_lastCoachItems, StringComparer.OrdinalIgnoreCase))
            {
                HeroItems.Clear();
                foreach (var item in snapshot.Items
                    .Where(item => item.SlotKind == ItemSlotKind.Main && !string.IsNullOrWhiteSpace(item.Name))
                    .Take(6))
                {
                    HeroItems.Add(new CoachItemSlot(
                        CoachRuleEngine.FriendlyItemName(item.Name),
                        OfficialItemSummary.ImageUrlFor(item.Name)));
                }
                _lastCoachItems = mainItems;
            }
        }
        else
        {
            if (IsHeroPanelVisible) IsHeroPanelVisible = false;
        }
    }

    public void AddAdvice(CoachAdvice advice)
    {
        Advices.Insert(0, advice);
        while (Advices.Count > 200) Advices.RemoveAt(Advices.Count - 1);
        AdviceCountText = $"{Advices.Count} 条提醒";
    }

    public void UpdateLiveHeroes(LiveMatchInfo info, Func<int, string> getHeroName, Func<int, string> getHeroIcon)
    {
        var picked = info.Heroes.Where(hero => hero.HeroId > 0).ToList();
        HeroesStatusText = picked.Count == 0
            ? $"比赛 {info.MatchId}：选人阶段"
            : $"已获取 {picked.Count} 名英雄（比赛 {info.MatchId}）";

        AllyHeroes.Clear();
        foreach (var hero in picked.Where(hero => hero.Team == info.MyTeam))
        {
            AllyHeroes.Add(new HeroChip(getHeroName(hero.HeroId), getHeroIcon(hero.HeroId)));
        }
        AllyHeroesCountText = AllyHeroes.Count > 0 ? $"{AllyHeroes.Count} 人" : "";

        EnemyHeroes.Clear();
        foreach (var hero in picked.Where(hero => hero.Team != info.MyTeam))
        {
            EnemyHeroes.Add(new HeroChip(getHeroName(hero.HeroId), getHeroIcon(hero.HeroId)));
        }
        EnemyHeroesCountText = EnemyHeroes.Count > 0 ? $"{EnemyHeroes.Count} 人" : "";
    }

    public void UpdateCounters(
        IEnumerable<CounterMatchupItem> countersMe,
        IEnumerable<CounterMatchupItem> countersTarget,
        IEnumerable<CounterPickRecommendation> pickRecommendations,
        string statusText)
    {
        CountersMe.Clear();
        foreach (var item in countersMe) CountersMe.Add(item);

        CountersTarget.Clear();
        foreach (var item in countersTarget) CountersTarget.Add(item);

        PickRecommendations.Clear();
        foreach (var item in pickRecommendations) PickRecommendations.Add(item);

        CounterStatusText = statusText;
    }
}
