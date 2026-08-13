using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using AegisCoach.Core.Models;
using AegisCoach.Core.Services;
using AegisCoach.Native.Models;
using AegisCoach.Native.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AegisCoach.Native;

public sealed partial class MainWindow : Window
{
    private readonly DotaDataService _dataService = new();
    private readonly List<HeroCardItem> _allHeroes = [];
    private readonly List<ItemCardItem> _allItems = [];
    private DotaCatalog? _catalog;
    private RankOption _selectedRank;
    private SortOption _selectedSort;
    private int _detailsRequestId;
    private HeroCardItem? _selectedHero;
    private ItemCardItem? _selectedItem;
    private MatchReport? _selectedReport;
    private string _selectedSectionId = "all-heroes";
    private string _selectedItemCategory = "商店物品";
    private int _totalProActions;
    private bool _itemsLoaded;
    private bool _itemsLoading;
    private CoachRuleEngine _coachRules = new();
    private readonly CloudCoachService _cloudCoach = new();
    private readonly NpuGenAiCoachService _npuCoach = new();
    private readonly NpuExecutionProviderService _npuRuntime = new();
    private readonly LiveMatchHeroService _liveMatchService = new();
    private readonly ApiMatchAnalyzer _apiMatchAnalyzer = new();
    private readonly MatchSessionTracker _sessionTracker = new();
    private readonly Dictionary<int, DotaDataService.HeroItemPopularity> _popularityCache = new();
    private readonly Dictionary<int, List<DotaDataService.HeroMatchupStat>> _matchupCache = new();
    private readonly Dictionary<int, Microsoft.UI.Xaml.Media.Imaging.BitmapImage> _coachHeroImageCache = new();
    private Dictionary<string, int> _cooldownDraft = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedCooldownKey;
    private List<string> _lastCoachItems = [];
    private readonly SemaphoreSlim _recommendationGate = new(1, 1);
    private readonly SemaphoreSlim _counterGate = new(1, 1);
    private readonly CancellationTokenSource _appLifetimeCts = new();
    private GsiReceiver? _receiver;
    private MatchSnapshot? _previousSnapshot;
    private MatchSnapshot? _latestSnapshot;
    private string? _trackedMatchId;
    private CancellationTokenSource? _heroTrackingCts;
    private LiveMatchInfo? _liveMatch;
    private AppSettings _settings = new();
    private bool _coachRunning;
    private bool _applicationStarted;
    private bool _settingsSaved;
    private DateTimeOffset _lastSmartExplanationAt = DateTimeOffset.MinValue;
    private NavigationViewItem? _lastNavigationItem;
    private static string SnapshotDirectory => AegisCoachPaths.Snapshots;
    private CancellationToken AppToken => _appLifetimeCts.Token;
    private static IReadOnlyList<RuleCooldownChoice> RuleCooldownChoices { get; } =
    [
        new("game_state", "比赛阶段"),
        new("low_health", "低血"),
        new("low_mana", "低蓝"),
        new("ult_ready", "大招就绪"),
        new("died", "阵亡"),
        new("control_locked", "受控"),
        new("buyback_window", "买活窗口"),
        new("fight_ready", "集结状态"),
        new("smoke_purchased", "购买诡计之雾"),
        new("smoke_used", "使用诡计之雾"),
        new("smoked_active", "处于雾中"),
        new("night_fall", "昼夜变化"),
        new("building_attacked", "建筑受击"),
        new("building_destroyed", "建筑摧毁"),
        new("roshan_killed", "肉山击杀"),
        new("roshan_respawned", "肉山复活"),
        new("item_ready", "道具就绪"),
        new("ability_ready", "技能就绪"),
        new("map_awareness", "关键时间"),
    ];

    public ObservableCollection<HeroCardItem> VisibleHeroes { get; } = [];
    public ObservableCollection<HeroCardItem> VisibleAttributeHeroes { get; } = [];
    public ObservableCollection<HeroSection> HeroSections { get; } =
    [
        new("all-heroes", "全部", "全英雄"),
        new("str", "力量", "力量英雄"),
        new("agi", "敏捷", "敏捷英雄"),
        new("int", "智力", "智力英雄"),
        new("all", "全才", "全才英雄"),
    ];
    public ObservableCollection<RankPerformanceItem> SelectedRankMetrics { get; } = [];
    public ObservableCollection<AbilityDisplay> SelectedAbilities { get; } = [];
    public ObservableCollection<TalentLevelRow> TalentRows { get; } = [];
    public ObservableCollection<HeroStatChip> HeroStats { get; } = [];
    public ObservableCollection<BuildRecommendation> SelectedBuilds { get; } = [];
    public ObservableCollection<BuildRecommendationGroup> SelectedBuildGroups { get; } = [];
    public ObservableCollection<ItemCardItem> VisibleItems { get; } = [];
    public ObservableCollection<ItemCategoryGroup> VisibleItemGroups { get; } = [];
    public ObservableCollection<ItemShopSection> VisibleItemSections { get; } = [];
    public ObservableCollection<AbilityDetailStat> ItemDetailStats { get; } = [];
    public ObservableCollection<WikiTip> SelectedWikiTips { get; } = [];
    public ObservableCollection<HeroAttributeRow> HeroAttributes { get; } = [];
    public ObservableCollection<string> HeroRoleTags { get; } = [];
    public ObservableCollection<CounterMatchupItem> HeroCountersMe { get; } = [];
    public ObservableCollection<CounterMatchupItem> HeroCountersTarget { get; } = [];
    public ObservableCollection<CoachAdvice> CoachAdvices { get; } = [];
    public ObservableCollection<HeroChip> CoachAllyHeroes { get; } = [];
    public ObservableCollection<HeroChip> CoachEnemyHeroes { get; } = [];
    public ObservableCollection<CounterMatchupItem> CoachCountersMe { get; } = [];
    public ObservableCollection<CounterMatchupItem> CoachCountersTarget { get; } = [];
    public ObservableCollection<CounterPickRecommendation> CoachPickRecommendations { get; } = [];
    public ObservableCollection<CoachItemSlot> CoachHeroItems { get; } = [];
    public ObservableCollection<MatchReport> AnalysisReports { get; } = [];
    private PlayerHabits _habits = new();
    public List<RankOption> RankOptions { get; } =
    [
        new() { Id = "all", Label = "全部分段" },
        new() { Id = "1", Label = "先锋" },
        new() { Id = "2", Label = "卫士" },
        new() { Id = "3", Label = "中军" },
        new() { Id = "4", Label = "统帅" },
        new() { Id = "5", Label = "传奇" },
        new() { Id = "6", Label = "万古" },
        new() { Id = "7", Label = "超凡" },
        new() { Id = "8", Label = "冠绝" },
    ];
    public List<SortOption> SortOptions { get; } =
    [
        new() { Id = "winrate", Label = "胜率" },
        new() { Id = "samples", Label = "样本量" },
        new() { Id = "name", Label = "名称" },
    ];

    public static IReadOnlyList<string> ItemCategories => ItemCategory.Order;

    public MainWindow()
    {
        InitializeComponent();
        RootGrid.DataContext = this;
        ItemCategoryBox.ItemsSource = new[] { "商店物品", "中立物品" };
        _selectedRank = RankOptions[0];
        _selectedSort = SortOptions[0];
        RootGrid.Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        HeroListHost.SizeChanged += HeroListHost_SizeChanged;
    }

    private void MainWindow_Closed(object _, WindowEventArgs __)
    {
        _appLifetimeCts.Cancel();
        CancelHeroTracking();
        _receiver?.Stop();
    }

    private async void MainWindow_Loaded(object _, RoutedEventArgs __)
    {
        _settings = AppSettings.Load();
        _selectedRank = RankOptions.FirstOrDefault(rank => rank.Id == _settings.DefaultRankId) ?? RankOptions[0];
        RankBox.SelectedItem = _selectedRank;
        SortBox.SelectedIndex = 0;
        ItemCategoryBox.SelectedIndex = 0;
        AttributeTabs.SelectedIndex = 0;
        PopulateSettings();
        ApplyResponsiveLayout();
        if (!_settings.SetupCompleted)
        {
            var configured = await OpenCoachSettingsAsync(firstRun: true);
            if (!configured) return;
        }

        await StartApplicationAsync();
    }

    private async Task StartApplicationAsync()
    {
        if (_applicationStarted) return;
        _applicationStarted = true;
        _dataService.CacheDirectory = _settings.CacheDirectory;
        MatchSessionTracker.HabitsDirectory = AegisCoachPaths.Habits;
        MatchSessionTracker.AnalysisDirectory = _settings.AnalysisDirectory;
        _npuCoach.ModelPath = _settings.NpuModelPath;
        NpuStatusText.Text = NpuCapabilityDetector.Detect().Summary;
        UpdateNpuModelStatus();
        _ = RefreshNpuRuntimeAsync();
        AppendCoachLog("应用已按当前设置启动");
        if (_settings.AutoStartGsi) ToggleCoach(true);
        else UpdateCoachStatusUi();
        LoadAnalysisData();
        await LoadCatalogAsync(false);
        _ = LoadItemsInBackgroundAsync();
    }

    private void LoadAnalysisData()
    {
        _habits = MatchSessionTracker.LoadHabits();
        AnalysisReports.Clear();
        foreach (var report in MatchSessionTracker.LoadReports()) AnalysisReports.Add(report);
        UpdateHabitsSummary();
        AnalysisCountText.Text = $"{AnalysisReports.Count} 份报告";
    }

    private void UpdateHabitsSummary()
    {
        if (AnalysisSummaryText is null) return;
        AnalysisSummaryText.Text = _habits.TotalMatches == 0
            ? "暂无历史对局：通过 GSI 打完一局后，这里会自动记录你的游戏习惯并生成赛后分析。"
            : $"共 {_habits.TotalMatches} 场 · 胜率 {_habits.WinRateText} · 常用英雄 {_habits.MostPlayedHero} · 平均 KDA {_habits.AverageKda} · 常见装备 {_habits.MostBoughtItem} · 场均时长 {_habits.AverageDuration}";
    }

    private async Task LoadItemsInBackgroundAsync()
    {
        if (_itemsLoading || _itemsLoaded) return;
        _itemsLoading = true;
        try
        {
            await LoadItemsAsync(false);
        }
        catch
        {
            // 物品图标可直接由官方 CDN 地址生成，加载失败不影响实时教练页。
        }
        finally
        {
            _itemsLoading = false;
        }
    }

    private async Task LoadCatalogAsync(bool force)
    {
        ErrorBar.IsOpen = false;
        SourceStatusText.Text = "正在同步数据…";
        try
        {
            _catalog = await _dataService.LoadCatalogAsync(force, AppToken);
            _allHeroes.Clear();
            _totalProActions = _catalog.Heroes.Sum(hero => hero.StatInt("pro_pick") + hero.StatInt("pro_ban"));
            _allHeroes.AddRange(_catalog.Heroes.Select(hero => new HeroCardItem(hero, _selectedRank, _totalProActions)));
            HeroCountText.Text = $"{_allHeroes.Count} 位英雄";
            ApplyFilters();
            SourceStatusText.Text = $"官方中文数据 + OpenDota · 目标版本 v{DotaDataService.TargetPatch}";
        }
        catch (Exception exception)
        {
            WriteErrorLog(exception);
            SourceStatusText.Text = "数据加载失败";
            ErrorBar.Title = "无法加载英雄数据";
            ErrorBar.Message = exception.Message;
            ErrorBar.IsOpen = true;
        }
    }

    private async Task LoadItemsAsync(bool force)
    {
        ItemSourceStatusText.Text = "正在同步物品数据…";
        ItemErrorBar.IsOpen = false;
        try
        {
            var items = await _dataService.LoadItemsAsync(force, AppToken);
            Dictionary<int, ItemMetric> metrics;
            try
            {
                metrics = await _dataService.LoadItemMetricsAsync(force, AppToken);
            }
            catch (Exception exception)
            {
                WriteErrorLog(exception);
                metrics = [];
                ItemSourceStatusText.Text = "官方物品资料可用 · 最近30天统计暂不可用";
            }

            _allItems.Clear();
            _allItems.AddRange(items.Select(item =>
                new ItemCardItem(item, metrics.GetValueOrDefault(item.Id))));
            _itemsLoaded = true;
            ItemCountText.Text = $"{_allItems.Count} 件物品";
            ApplyItemFilters();
            if (ItemSourceStatusText.Text == "正在同步物品数据…")
            {
                ItemSourceStatusText.Text = $"{_dataService.ItemCatalogSource} · 目标版本 v{DotaDataService.TargetPatch} · OpenDota 最近30天统计";
            }
        }
        catch (Exception exception)
        {
            WriteErrorLog(exception);
            ItemSourceStatusText.Text = "物品数据加载失败";
            ItemErrorBar.Title = "无法加载物品数据";
            ItemErrorBar.Message = exception.Message;
            ItemErrorBar.IsOpen = true;
        }
    }

    private void ApplyFilters()
    {
        var keyword = SearchBox.Text.Trim();
        foreach (var hero in _allHeroes) hero.UpdateRank(_selectedRank);

        var query = _allHeroes.Where(item => string.IsNullOrWhiteSpace(keyword) ||
            item.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
            item.Hero.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        query = _selectedSort.Id switch
        {
            "samples" => query.OrderByDescending(item => HeroCardItem.MetricsFor(item.Hero, _selectedRank.Id).Picks),
            "name" => query.OrderBy(item => item.Name, StringComparer.CurrentCulture),
            _ => query.OrderByDescending(item => item.WinRate),
        };

        var filteredHeroes = query.ToList();
        VisibleHeroes.Clear();
        foreach (var item in filteredHeroes) VisibleHeroes.Add(item);
        foreach (var section in HeroSections)
        {
            section.Heroes.Clear();
            var sectionHeroes = section.Id == "all-heroes"
                ? filteredHeroes
                : filteredHeroes.Where(item => AttributeId(item.Hero) == section.Id);
            foreach (var item in sectionHeroes)
            {
                section.Heroes.Add(item);
            }
        }
        UpdateVisibleAttributeHeroes();

        RankCaptionText.Text = _selectedRank.Label;
        UpdateHeroCardLayout();
    }

    private async void HeroGrid_SelectionChanged(object sender, SelectionChangedEventArgs _)
    {
        if (sender is GridView grid && grid.SelectedItem is HeroCardItem item)
        {
            _selectedHero = item;
            await ShowHeroAsync(item);
            grid.SelectedItem = null;
        }
    }

    private async Task ShowHeroAsync(HeroCardItem item)
    {
        var requestId = ++_detailsRequestId;
        HeroDetailPanel.Visibility = Visibility.Visible;
        HeroEmptyState.Visibility = Visibility.Collapsed;
        HeroDetailHost.Visibility = Visibility.Visible;
        ApplyResponsiveLayout();
        HeroNameText.Text = item.Name;
        HeroEnglishNameText.Text = LocalizedText.ContainsChinese(item.Hero.EnglishName)
            ? item.Hero.Name.Replace("npc_dota_hero_", string.Empty).Replace('_', ' ').ToUpperInvariant()
            : item.Hero.EnglishName.ToUpperInvariant();
        HeroMetaText.Text = $"{item.AttributeLabel} · {item.AttackLabel}";
        HeroImage.Source = new BitmapImage(new Uri(item.Hero.ImageUrl));
        HeroAttrBadgeText.Text = item.AttributeLabel;
        HeroAttrBadge.Background = item.AttrBrush;
        HeroAttackTypeText.Text = item.AttackLabel;
        HeroHypeText.Text = "正在读取英雄资料";
        HeroBiographyText.Text = "官方中文背景故事正在同步。";
        HeroUpgradesSection.Visibility = Visibility.Collapsed;
        ScepterUpgradePanel.Visibility = Visibility.Collapsed;
        ShardUpgradePanel.Visibility = Visibility.Collapsed;
        UpdateHeroStats(item.Hero);
        UpdateHeroAttributes(item.Hero);
        HeroRoleTags.Clear();
        foreach (var role in item.Hero.Roles) HeroRoleTags.Add(role);

        SelectedRankMetrics.Clear();
        SelectedRankMetrics.Add(new RankPerformanceItem(
            _selectedRank.Label,
            HeroCardItem.MetricsFor(item.Hero, _selectedRank.Id)));

        HeroCountersMe.Clear();
        HeroCountersTarget.Clear();
        HeroCountersBorder.Visibility = Visibility.Visible;
        HeroCountersStatusText.Text = "正在读取对阵数据…";
        SelectedAbilities.Clear();
        TalentRows.Clear();
        SelectedBuilds.Clear();
        SelectedBuildGroups.Clear();
        SelectedWikiTips.Clear();
        foreach (var tip in WikiTipCatalog.ForHero(item.Hero.Name)) SelectedWikiTips.Add(tip);
        TalentBorder.Visibility = Visibility.Collapsed;
        BuildBorder.Visibility = Visibility.Collapsed;
        BuildStatusText.Text = "正在读取当前分段样本…";
        AbilityStatusText.Text = "正在同步官方中文…";
        LocalizedHeroData localized = new() { Id = item.Hero.Id };
        try
        {
            localized = await _dataService.LoadLocalizedHeroAsync(item.Hero.Id, false, AppToken);
        }
        catch (Exception exception)
        {
            AbilityStatusText.Text = "官方中文详情暂不可用，显示基础技能";
            ErrorBar.Title = "无法加载该英雄的官方中文技能";
            ErrorBar.Message = exception.Message;
            ErrorBar.IsOpen = true;
        }

        if (requestId != _detailsRequestId) return;
        HeroHypeText.Text = LocalizedText.ChineseOr(localized.Hype, $"{item.Name}英雄资料");
        HeroBiographyText.Text = LocalizedText.ChineseOr(localized.Biography, "官方中文背景故事暂不可用。");

        var officialByName = localized.Abilities
            .Where(ability => !string.IsNullOrWhiteSpace(ability.Name))
            .GroupBy(ability => ability.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var heroAbilityMap = _catalog?.HeroAbilities.GetValueOrDefault(item.Hero.Name)
            ?? _catalog?.HeroAbilities.GetValueOrDefault(item.Hero.Name.Replace("npc_dota_hero_", string.Empty));
        var abilityIds = (heroAbilityMap?.AbilityIds ?? Enumerable.Empty<string>())
            .Concat(localized.Abilities.Select(ability => ability.Name))
            .Where(id => !string.IsNullOrWhiteSpace(id) && !id.Equals("generic_hidden", StringComparison.OrdinalIgnoreCase))
            .Where(id => !id.StartsWith("special_bonus", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var abilityIndex = 0;
        foreach (var abilityId in abilityIds)
        {
            AbilityDefinition? baseAbility = null;
            _catalog?.Abilities.TryGetValue(abilityId, out baseAbility);
            officialByName.TryGetValue(abilityId, out var officialAbility);
            var display = new AbilityDisplay(abilityId, baseAbility, officialAbility)
            {
                SlotLabel = abilityIndex switch
                {
                    0 => "Q",
                    1 => "W",
                    2 => "E",
                    3 => "R",
                    4 => "R",
                    _ => string.Empty,
                },
            };
            SelectedAbilities.Add(display);
            abilityIndex++;
        }

        SelectedAbilityPanel.DataContext = SelectedAbilities.FirstOrDefault();
        UpdateHeroUpgrades();

        // 技能加载完成后按实际技能数据重新生成实战小技巧。
        SelectedWikiTips.Clear();
        foreach (var tip in WikiTipCatalog.ForHero(item.Hero, SelectedAbilities)) SelectedWikiTips.Add(tip);

        var resolvedTalents = new Dictionary<int, (string Left, string Right)>();
        for (var index = 0; index < localized.Talents.Count; index++)
        {
            var talent = localized.Talents[index];
            var level = 10 + index / 2 * 5;
            var isLeft = index % 2 == 0;
            AbilityDefinition? talentBase = null;
            _catalog?.Abilities.TryGetValue(talent.Name, out talentBase);
            var localizedTalent = AbilityDisplay.ResolveTalentParameters(
                CleanText(talent.LocalizedName), talent, talentBase);
            if (string.IsNullOrWhiteSpace(localizedTalent)) continue;
            if (!resolvedTalents.TryGetValue(level, out var pair)) pair = (string.Empty, string.Empty);
            if (isLeft) pair.Left = localizedTalent;
            else pair.Right = localizedTalent;
            resolvedTalents[level] = pair;
        }

        foreach (var (level, pair) in resolvedTalents.OrderBy(item => item.Key))
        {
            if (string.IsNullOrWhiteSpace(pair.Left) && string.IsNullOrWhiteSpace(pair.Right)) continue;
            TalentRows.Add(new TalentLevelRow(
                level,
                new TalentDisplay(level, true, pair.Left),
                new TalentDisplay(level, false, pair.Right)));
        }
        TalentBorder.Visibility = TalentRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        await LoadHeroBuildsAsync(item, requestId);
        await LoadHeroCountersAsync(item, requestId);
        AbilityStatusText.Text = localized.Abilities.Count > 0
            ? $"{SelectedAbilities.Count} 项技能"
        : $"{SelectedAbilities.Count} 项基础技能 · 中文详情暂不可用";
    }

    private void UpdateHeroAttributes(HeroStat hero)
    {
        HeroAttributes.Clear();
        var strBrush = BrushFromResource("HeroAttrStrBrush");
        var agiBrush = BrushFromResource("HeroAttrAgiBrush");
        var intBrush = BrushFromResource("HeroAttrIntBrush");
        AddHeroAttribute("力量", hero.StatDouble("base_str"), hero.StatDouble("str_gain"), strBrush);
        AddHeroAttribute("敏捷", hero.StatDouble("base_agi"), hero.StatDouble("agi_gain"), agiBrush);
        AddHeroAttribute("智力", hero.StatDouble("base_int"), hero.StatDouble("int_gain"), intBrush);
    }

    private void AbilityButton_Click(object sender, RoutedEventArgs _)
    {
        if (sender is Button { DataContext: AbilityDisplay ability })
        {
            SelectedAbilityPanel.DataContext = ability;
        }
    }

    private void UpdateHeroUpgrades()
    {
        var scepter = SelectedAbilities
            .Where(ability => !string.IsNullOrWhiteSpace(ability.Scepter))
            .Select(ability => $"{ability.Name}：{ability.Scepter}")
            .ToList();
        var shard = SelectedAbilities
            .Where(ability => !string.IsNullOrWhiteSpace(ability.Shard))
            .Select(ability => $"{ability.Name}：{ability.Shard}")
            .ToList();

        ScepterUpgradeText.Text = string.Join("\n\n", scepter);
        ShardUpgradeText.Text = string.Join("\n\n", shard);
        ScepterUpgradePanel.Visibility = scepter.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShardUpgradePanel.Visibility = shard.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeroUpgradesSection.Visibility = scepter.Count + shard.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RebuildBuildGroups()
    {
        SelectedBuildGroups.Clear();
        foreach (var phase in new[] { "出门", "前期", "核心", "中期", "后期" })
        {
            var items = SelectedBuilds.Where(item => item.Phase == phase).ToList();
            if (items.Count > 0)
            {
                SelectedBuildGroups.Add(new BuildRecommendationGroup($"{phase}装备", items));
            }
        }
    }

    private void AddHeroAttribute(string label, double value, double growth, Brush color)
    {
        HeroAttributes.Add(new HeroAttributeRow(
            label,
            value > 0 ? value.ToString("0.#") : "--",
            growth > 0 ? growth.ToString("0.#") : string.Empty,
            color));
    }

    private void AddHeroAttribute(string label, string value, string growth, Brush color)
    {
        HeroAttributes.Add(new HeroAttributeRow(label, value, growth, color));
    }

    private async Task LoadHeroCountersAsync(HeroCardItem hero, int requestId)
    {
        try
        {
            var matchups = await GetMatchupsAsync(hero.Hero.Id);
            if (requestId != _detailsRequestId) return;
            var samples = matchups
                .Where(item => item.HeroId != hero.Hero.Id && item.GamesPlayed >= 30)
                .ToList();
            var meList = samples.Where(item => item.WinRate >= 52).OrderByDescending(item => item.WinRate).Take(6).ToList();
            var targetList = samples.Where(item => item.WinRate <= 48).OrderBy(item => item.WinRate).Take(6).ToList();

            // OpenDota 当前样本量小，阈值过滤后可能一边为空；用胜率两端补足，保证两边都有内容。
            if (meList.Count < 3)
            {
                meList.AddRange(samples
                    .Where(item => !meList.Contains(item))
                    .OrderByDescending(item => item.WinRate)
                    .Take(6 - meList.Count));
            }
            if (targetList.Count < 3)
            {
                targetList.AddRange(samples
                    .Where(item => !targetList.Contains(item))
                    .OrderBy(item => item.WinRate)
                    .Take(6 - targetList.Count));
            }

            foreach (var stat in meList)
            {
                HeroCountersMe.Add(new CounterMatchupItem(
                    stat.HeroId,
                    LocalizedHeroNameById(stat.HeroId),
                    LocalizedHeroIconById(stat.HeroId),
                    stat.WinRate,
                    stat.GamesPlayed,
                    countersTarget: true));
            }

            foreach (var stat in targetList)
            {
                HeroCountersTarget.Add(new CounterMatchupItem(
                    stat.HeroId,
                    LocalizedHeroNameById(stat.HeroId),
                    LocalizedHeroIconById(stat.HeroId),
                    stat.WinRate,
                    stat.GamesPlayed,
                    countersTarget: false));
            }

            HeroCountersStatusText.Text = samples.Count > 0
                ? $"样本来自 OpenDota 对阵数据（样本 ≥30，共 {samples.Count} 组）"
                : "暂无足够对阵样本";
            HeroCountersBorder.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            WriteErrorLog(exception);
            HeroCountersStatusText.Text = "对阵数据暂不可用：" + exception.Message;
            HeroCountersBorder.Visibility = Visibility.Visible;
        }
    }

    private void UpdateHeroStats(HeroStat hero)
    {
        HeroStats.Clear();
        var gold = BrushFromResource("CoachGoldBrush");
        var red = BrushFromResource("CoachDangerBrush");
        var blue = BrushFromResource("CoachInfoBlueBrush");
        var green = BrushFromResource("CoachSuccessBrush");
        var neutral = BrushFromResource("CoachNeutralBrush");
        var minAttack = hero.StatInt("base_attack_min");
        var maxAttack = hero.StatInt("base_attack_max");
        var moveSpeed = hero.StatInt("move_speed");
        var attackRange = hero.StatInt("attack_range");
        var baseHealth = hero.StatInt("base_health");
        var baseMana = hero.StatInt("base_mana");
        HeroStats.Add(new HeroStatChip("攻击", minAttack > 0 && maxAttack > 0 ? $"{minAttack}-{maxAttack}" : "--", red));
        HeroStats.Add(new HeroStatChip("护甲", $"{hero.StatDouble("base_armor"):0.#}", neutral));
        HeroStats.Add(new HeroStatChip("移动速度", moveSpeed > 0 ? moveSpeed.ToString() : "--", gold));
        HeroStats.Add(new HeroStatChip("攻击距离", attackRange > 0 ? attackRange.ToString() : "--", blue));
        HeroStats.Add(new HeroStatChip("基础生命", baseHealth > 0 ? baseHealth.ToString("N0") : "--", green));
        HeroStats.Add(new HeroStatChip("基础魔法", baseMana > 0 ? baseMana.ToString("N0") : "--", blue));
    }

    private static SolidColorBrush BrushFromResource(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

    private async Task LoadHeroBuildsAsync(HeroCardItem hero, int requestId)
    {
        try
        {
            var metrics = await _dataService.LoadHeroBuildsAsync(hero.Hero.Id, _selectedRank.Id, false, AppToken);
            if (requestId != _detailsRequestId) return;
            var itemCatalog = await _dataService.LoadItemsAsync(false, AppToken);
            var byId = itemCatalog.ToDictionary(item => item.Id);
            var order = 0;
            foreach (var metric in metrics)
            {
                if (!byId.TryGetValue(metric.ItemId, out var item)) continue;
                order++;
                var phase = item.IsPregameSuggested ? "出门" : item.IsEarlyGameSuggested ? "前期" : item.IsLateGameSuggested ? "后期" : "核心";
                var rate = metric.HasReliableSample && metric.WinRate is double value
                    ? $"胜率 {value:0.0}%"
                    : "样本不足";
                SelectedBuilds.Add(new BuildRecommendation
                {
                    Order = order,
                    Phase = phase,
                    Name = LocalizedText.ChineseOr(item.LocalizedName, "中文名称待同步"),
                    StatsText = $"样本 {HeroCardItem.CompactNumber(metric.Picks)} · {rate}",
                    Image = new BitmapImage(new Uri(item.ImageUrl)),
                });
            }

            if (metrics.Count == 0)
            {
                // OpenDota Explorer 近 30 天样本极少（当前全站仅数百场），大多数英雄查不到出装；
                // 用 itemPopularity 的早/中/后期购买热度兜底，避免推荐为空。
                var popularity = await GetPopularityAsync(hero.Hero.Id);
                if (requestId != _detailsRequestId) return;
                var stages = new (string Phase, Dictionary<int, int> Items)[]
                {
                    ("出门", popularity.StartGameItems),
                    ("前期", popularity.EarlyGameItems),
                    ("中期", popularity.MidGameItems),
                    ("后期", popularity.LateGameItems),
                };
                foreach (var (phase, items) in stages)
                {
                    if (items.Count == 0) continue;
                    var total = items.Values.Sum();
                    foreach (var pair in items.OrderByDescending(item => item.Value).Take(3))
                    {
                        if (!byId.TryGetValue(pair.Key, out var item)) continue;
                        order++;
                        var percent = total > 0 ? pair.Value * 100d / total : 0;
                        SelectedBuilds.Add(new BuildRecommendation
                        {
                            Order = order,
                            Phase = phase,
                            Name = LocalizedText.ChineseOr(item.LocalizedName, "中文名称待同步"),
                            StatsText = $"热度 {percent:0.0}%",
                            Image = new BitmapImage(new Uri(item.ImageUrl)),
                        });
                    }
                }
                BuildStatusText.Text = SelectedBuilds.Count > 0
                    ? "近30天样本不足，已按购买热度给出推荐"
                    : "当前分段暂无足够样本";
            }
            else
            {
                BuildStatusText.Text = $"{_selectedRank.Label} · 最近30天 · 按出场率";
            }
            RebuildBuildGroups();
            BuildBorder.Visibility = SelectedBuildGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            WriteErrorLog(exception);
            SelectedBuildGroups.Clear();
            BuildStatusText.Text = "当前分段出装暂不可用";
            BuildBorder.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ShowItemAsync(ItemCardItem item)
    {
        var requestId = ++_detailsRequestId;
        ItemDetailPanel.Visibility = Visibility.Visible;
        ApplyResponsiveLayout();
        ItemNameText.Text = item.Name;
        ItemMetaText.Text = item.TierText;
        ItemCategoryText.Text = item.CategoryLabel;
        ItemImage.Source = item.Image;
        ItemCostText.Text = item.Metric.Picks > 0
            ? $"{item.Metric.WinRateText}  ·  购买 {item.Metric.PicksText}  ·  胜局 {item.Metric.WinsText}"
            : "暂无最近 30 天统计";
        ItemDescriptionText.Text = "正在同步官方中文资料…";
        ItemLoreText.Text = string.Empty;
        ItemDetailStats.Clear();
        ItemNotes.ItemsSource = null;
        ItemRecipeItems.ItemsSource = item.Item.RecipeComponents;
        ItemRecipeBorder.Visibility = item.Item.RecipeComponents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        try
        {
            var detail = await _dataService.LoadItemDetailsAsync(item.Item.Id, false, AppToken);
            if (requestId != _detailsRequestId) return;

            ItemCostText.Text = detail.Cost > 0
                ? $"价格 {detail.Cost:N0}"
                : "价格信息暂不可用";
            var description = CleanText(detail.Description ?? string.Empty);
            ItemDescriptionText.Text = LocalizedText.ContainsChinese(description)
                ? AbilityDisplay.ResolveParameters(description, detail.SpecialValues, null)
                : "中文说明暂不可用";
            ItemLoreText.Text = LocalizedText.ChineseOrNull(CleanText(detail.Lore ?? string.Empty)) ?? string.Empty;
            ItemNotes.ItemsSource = detail.Notes
                .Select(CleanText)
                .Where(LocalizedText.ContainsChinese)
                .Select(text => AbilityDisplay.ResolveParameters(text, detail.SpecialValues, null))
                .Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
            foreach (var stat in AbilityDisplay.BuildStats(detail.SpecialValues, null))
            {
                ItemDetailStats.Add(stat);
            }
        }
        catch (Exception exception)
        {
            ItemDescriptionText.Text = "中文详情加载失败";
            ItemErrorBar.Title = "无法加载该物品的官方中文资料";
            ItemErrorBar.Message = exception.Message;
            ItemErrorBar.IsOpen = true;
        }
    }

    private static string CleanText(string value)
    {
        return HtmlTagRegex().Replace(value.Replace("<br>", "\n"), string.Empty).Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    private static void WriteErrorLog(Exception exception)
    {
        try
        {
            var directory = AegisCoachPaths.Root;
            Directory.CreateDirectory(directory);
            AtomicFile.WriteAllText(Path.Combine(directory, "last-error.txt"), exception.ToString());
        }
        catch
        {
            // Diagnostics must never replace the user-facing error state.
        }
    }

    private void SearchBox_TextChanged(object _, TextChangedEventArgs __) => ApplyFilters();

    private void ItemSearchBox_TextChanged(object _, TextChangedEventArgs __) => ApplyItemFilters();

    private void ApplyItemFilters()
    {
        var keyword = ItemSearchBox.Text.Trim();
        var query = _allItems.Where(item => string.IsNullOrWhiteSpace(keyword) ||
            item.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
            item.EnglishName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        query = _selectedItemCategory == "中立物品"
            ? query.Where(item => item.ShopSection == "中立物品")
            : query.Where(item => item.ShopSection != "中立物品");
        VisibleItems.Clear();
        foreach (var item in query
            .OrderBy(item => Array.IndexOf(ItemCategory.Order.ToArray(), item.CategoryLabel))
            .ThenBy(item => item.Name, StringComparer.CurrentCulture))
        {
            VisibleItems.Add(item);
        }

        VisibleItemGroups.Clear();
        foreach (var category in ItemCategory.Order)
        {
            var categoryItems = VisibleItems.Where(item => item.CategoryLabel == category).ToList();
            if (categoryItems.Count > 0) VisibleItemGroups.Add(new ItemCategoryGroup(category, categoryItems));
        }

        VisibleItemSections.Clear();
        if (_selectedItemCategory == "中立物品")
        {
            if (VisibleItemGroups.Count > 0)
            {
                VisibleItemSections.Add(new ItemShopSection("中立物品", "按当前中立物品层级与名称浏览", VisibleItemGroups));
            }
        }
        else
        {
            var basic = VisibleItemGroups.Where(group => group.Name is "消耗品" or "属性" or "装备" or "其他" or "神秘商店").ToList();
            var recipes = VisibleItemGroups.Where(group => group.Name is "配件" or "辅助" or "法器" or "防具" or "兵刃" or "宝物").ToList();
            if (basic.Count > 0) VisibleItemSections.Add(new ItemShopSection("基础分类", "基础物品与神秘商店", basic));
            if (recipes.Count > 0) VisibleItemSections.Add(new ItemShopSection("合成分类", "配件、辅助、法器、防具、兵刃与宝物", recipes));
        }

        ItemResultCountText.Text = $"显示 {VisibleItems.Count} 件 · {VisibleItemGroups.Count} 个分类";
    }

    private void ItemCategory_SelectionChanged(object _, SelectionChangedEventArgs __)
    {
        if (ItemCategoryBox.SelectedItem is string category)
        {
            _selectedItemCategory = category;
            ApplyItemFilters();
        }
    }

    private async void AppNavigation_SelectionChanged(NavigationView _, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            await OpenCoachSettingsAsync();
            AppNavigation.SelectedItem = _lastNavigationItem ?? AppNavigation.MenuItems[0];
            return;
        }

        if (args.SelectedItem is NavigationViewItem selectedItem) _lastNavigationItem = selectedItem;
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
        var showItems = tag == "items";
        var showCoach = tag == "coach";
        var showAnalysis = tag == "analysis";
        HeroPage.Visibility = showItems || showCoach || showAnalysis ? Visibility.Collapsed : Visibility.Visible;
        ItemPage.Visibility = showItems ? Visibility.Visible : Visibility.Collapsed;
        CoachPage.Visibility = showCoach ? Visibility.Visible : Visibility.Collapsed;
        AnalysisPage.Visibility = showAnalysis ? Visibility.Visible : Visibility.Collapsed;
        if (showItems && !_itemsLoaded && !_itemsLoading)
        {
            await LoadItemsAsync(false);
        }
    }

    private void AnalysisList_SelectionChanged(object _, SelectionChangedEventArgs __)
    {
        if (AnalysisList.SelectedItem is MatchReport report)
        {
            _selectedReport = report;
            ShowAnalysisReport(report);
        }
    }

    private void ShowAnalysisReport(MatchReport report)
    {
        var record = report.Match;
        AnalysisDetailTitleText.Text = $"{record.HeroName} · {record.ResultText} · {record.DurationText}";
        AnalysisDetailMetaText.Text = $"{record.DateText}  KDA {record.KdaText}  补刀 {record.LastHits}  GPM {record.Gpm}  XPM {record.Xpm}  最高等级 {record.MaxLevel}";
        AnalysisDetailBackendText.Text = $"报告生成于 {report.GeneratedText} · 由 {report.Backend} 生成";
        AnalysisDetailReviewText.Text = string.IsNullOrWhiteSpace(report.AiReview)
            ? "（暂无复盘内容，AI 后端不可用时请稍后查看规则摘要）"
            : report.AiReview;
        AnalysisDetailItemsText.Text = record.FinalItems.Count > 0
            ? string.Join("、", record.FinalItems)
            : "无";
        AnalysisDetailMomentsText.Text = record.KeyMoments.Count > 0
            ? string.Join("\n", record.KeyMoments)
            : "本局没有采集到关键节点";
        AnalysisDetailEconomyText.Text = record.Economy.Count > 0
            ? string.Join("  ", record.Economy.Select(sample => $"{sample.TimeText} GPM{sample.Gpm}/XPM{sample.Xpm}"))
            : "暂无经济采样";
        AnalysisOperationsHost.Text = FormatOperationsForUi(report.Operations);
        AnalysisOperationsEmptyText.Visibility = report.Operations is null ? Visibility.Visible : Visibility.Collapsed;
        AnalysisOperationsHost.Visibility = report.Operations is null ? Visibility.Collapsed : Visibility.Visible;
        if (report.Api is { HeroId: > 0 })
        {
            AnalysisApiHost.Text = string.Join("\n", CoachAiPrompts.BuildApiContextText(report.Api));
            AnalysisApiEmptyText.Visibility = Visibility.Collapsed;
            AnalysisApiHost.Visibility = Visibility.Visible;
        }
        else
        {
            AnalysisApiEmptyText.Visibility = Visibility.Visible;
            AnalysisApiHost.Visibility = Visibility.Collapsed;
        }
        AnalysisEmptyState.Visibility = Visibility.Collapsed;
        AnalysisDetailHost.Visibility = Visibility.Visible;
        AnalysisDetailPanel.Visibility = Visibility.Visible;
        ApplyResponsiveLayout();
    }

    private static MatchOperationSummary EmptyOperationsFrom(MatchSessionRecord record, string heroName, ApiMatchSummary? api)
    {
        var operations = new MatchOperationSummary
        {
            MatchId = record.MatchId,
            HeroId = record.HeroId,
            HeroName = heroName,
            Won = record.Won,
            DurationSeconds = record.DurationSeconds,
            ScoreText = api?.ScoreText ?? string.Empty,
            Kills = record.Kills,
            Deaths = record.Deaths,
            Assists = record.Assists,
            LastHits = record.LastHits,
            Denies = record.Denies,
            Gpm = record.Gpm,
            Xpm = record.Xpm,
            MaxLevel = record.MaxLevel,
        };
        if (api is not null)
        {
            operations.Phases = BuildApiPhaseStats(api);
            operations.Economy = api.Economy.Select(point => new EconomySample
            {
                GameTime = point.Minute * 60d,
                Gpm = (int)point.Gold,
                Xpm = (int)point.Xp,
            }).ToList();
        }
        return operations;
    }

    private static List<PhaseStat> BuildApiPhaseStats(ApiMatchSummary api)
    {
        var buckets = new List<PhaseStat>();
        var durationMinutes = Math.Max(1, (int)Math.Ceiling(api.DurationSeconds / 60d));
        var labels = new[] { "对线期", "中期", "中后期", "后期" };
        for (var bucket = 0; bucket < 4; bucket++)
        {
            var start = bucket * 10;
            var end = Math.Min((bucket + 1) * 10, durationMinutes);
            if (start >= durationMinutes) break;
            var points = api.Economy.Where(point => point.Minute >= start && point.Minute < end).ToList();
            if (points.Count == 0) continue;
            var first = points[0];
            var last = points[^1];
            buckets.Add(new PhaseStat
            {
                StartSeconds = start * 60d,
                EndSeconds = end * 60d,
                Label = bucket < labels.Length ? labels[bucket] : $"第{bucket + 1}阶段",
                AvgGpm = points.Average(point => point.Gold),
                AvgXpm = points.Average(point => point.Xp),
                LastHitsDelta = Math.Max(0, last.LastHits - (bucket == 0 ? 0 : first.LastHits)),
                LastHitsPerMinute = Math.Max(0, last.LastHits - (bucket == 0 ? 0 : first.LastHits)) / Math.Max(1, end - start),
            });
        }
        return buckets;
    }

    private string ResolveApiHeroInternalName(int heroId) =>
        _catalog?.Heroes.FirstOrDefault(hero => hero.Id == heroId)?.Name ?? string.Empty;

    private static string ResolveApiItemName(string keyOrFullName) =>
        CoachRuleEngine.FriendlyItemName(keyOrFullName);

    private string ResolveApiItemById(int itemId)
    {
        var item = _allItems.FirstOrDefault(card => card.Item.Id == itemId);
        return item is not null ? item.Name : $"道具{itemId}";
    }

    private void EnrichApiNames(ApiMatchSummary api)
    {
        if (_catalog is null) return;
        foreach (var player in api.AllPlayers)
        {
            var hero = _catalog.Heroes.FirstOrDefault(item => item.Id == player.HeroId);
            if (hero is not null && !string.IsNullOrWhiteSpace(hero.LocalizedName))
            {
                player.HeroName = hero.LocalizedName;
            }
        }
    }

    private async void AnalysisFetchMatchButton_Click(object _, RoutedEventArgs __)
    {
        var text = AnalysisMatchIdBox.Text.Trim();
        if (!long.TryParse(text, out var matchId) || matchId <= 0)
        {
            AppendCoachLog("比赛 ID 无效");
            return;
        }

        AnalysisFetchMatchButton.IsEnabled = false;
        try
        {
            var api = await _apiMatchAnalyzer.FetchAsync(
                matchId,
                _habits.SteamId,
                0,
                ResolveApiHeroInternalName,
                ResolveApiItemName,
                ResolveApiItemById,
                AppToken);
            if (api is null || api.HeroId <= 0)
            {
                AppendCoachLog($"API 拉取失败：{api?.Error ?? "无返回"}");
                return;
            }

            EnrichApiNames(api);
            var record = new MatchSessionRecord
            {
                MatchId = matchId,
                HeroId = api.HeroId,
                HeroName = LocalizedHeroNameById(api.HeroId),
                SteamId = _habits.SteamId ?? string.Empty,
                StartedAt = DateTimeOffset.UtcNow,
                EndedAt = DateTimeOffset.UtcNow,
                DurationSeconds = api.DurationSeconds,
                Won = api.RadiantWin is null ? null : api.RadiantWin == api.IsRadiant,
                Kills = api.Kills,
                Deaths = api.Deaths,
                Assists = api.Assists,
                LastHits = api.LastHits,
                Denies = api.Denies,
                Gpm = api.Gpm,
                Xpm = api.Xpm,
                MaxLevel = api.Level,
                FinalItems = api.FinalItems,
            };
            var report = new MatchReport
            {
                Id = $"report-{DateTime.Now:yyyyMMdd-HHmmss}-{matchId}",
                GeneratedAt = DateTimeOffset.UtcNow,
                Match = record,
                Api = api,
                AiReview = BuildFallbackReview(record, null),
                Backend = "OpenDota",
            };
            AnalysisReports.Insert(0, report);
            if (AnalysisReports.Count > 200) AnalysisReports.RemoveAt(AnalysisReports.Count - 1);
            AnalysisCountText.Text = $"{AnalysisReports.Count} 份报告";
            AnalysisList.SelectedItem = report;
            ShowAnalysisReport(report);
            AppendCoachLog($"已从 OpenDota 拉取比赛 {matchId}，正在生成复盘…");
            _ = GenerateMatchReviewAsync(report);
        }
        finally
        {
            AnalysisFetchMatchButton.IsEnabled = true;
        }
    }

    private static string FormatOperationsForUi(MatchOperationSummary? ops)
    {
        if (ops is null) return string.Empty;
        var lines = new List<string>();
        foreach (var death in ops.DeathsDetailed)
        {
            var parts = new List<string>
            {
                $"{death.TimeText} 第{death.DeathNumber}次阵亡 · 阵亡前血量 {death.HealthPercentAtDeath:0}%（窗口内最低{death.MinHealthPercentInWindow:0}%）",
                $"等级{death.Level} · 金钱{death.Gold}",
            };
            if (death.UltReadyAtDeath) parts.Add("大招就绪未使用");
            if (death.ReadyActivesUnused.Count > 0) parts.Add($"{string.Join("、", death.ReadyActivesUnused)}就绪未使用");
            if (death.TpAvailable) parts.Add("TP可用");
            if (death.RecentCasts.Count > 0) parts.Add("20秒内操作：" + string.Join("、", death.RecentCasts));
            parts.Add(death.BuybackAvailable
                ? $"可买活（{death.BuybackCost}）{(death.BuybackUsed ? "已买活" : "未买活")}"
                : "不可买活");
            lines.Add(string.Join(" · ", parts));
        }

        var shown = 0;
        foreach (var op in ops.Operations.OrderBy(item => item.GameTime))
        {
            if (op.Kind == "death") continue;
            lines.Add(CoachAiPrompts.FormatOperation(op));
            shown++;
            if (shown >= 60) break;
        }
        return lines.Count > 0 ? string.Join("\n", lines) : "本局没有采集到操作事件";
    }

    private void AnalysisRegenerateButton_Click(object _, RoutedEventArgs __)
    {
        if (AnalysisList.SelectedItem is not MatchReport report) return;
        AnalysisRegenerateButton.IsEnabled = false;
        _ = RegenerateReviewAsync(report);
    }

    private async Task RegenerateReviewAsync(MatchReport report)
    {
        try
        {
            await GenerateMatchReviewAsync(report);
        }
        finally
        {
            DispatcherQueue.TryEnqueue(() => AnalysisRegenerateButton.IsEnabled = true);
        }
    }

    private async void ItemCardButton_Click(object sender, RoutedEventArgs _)
    {
        if (sender is Button { DataContext: ItemCardItem item })
        {
            _selectedItem = item;
            await ShowItemAsync(item);
        }
    }

    private void HeroCloseButton_Click(object _, RoutedEventArgs __)
    {
        _selectedHero = null;
        _detailsRequestId++;
        HeroDetailHost.Visibility = Visibility.Collapsed;
        HeroDetailPanel.Visibility = Visibility.Collapsed;
        ApplyResponsiveLayout();
    }

    private void ItemCloseButton_Click(object _, RoutedEventArgs __)
    {
        _selectedItem = null;
        _detailsRequestId++;
        ItemDetailPanel.Visibility = Visibility.Collapsed;
        ApplyResponsiveLayout();
    }

    private void AnalysisCloseButton_Click(object _, RoutedEventArgs __)
    {
        _selectedReport = null;
        AnalysisList.SelectedItem = null;
        AnalysisDetailHost.Visibility = Visibility.Collapsed;
        AnalysisDetailPanel.Visibility = Visibility.Collapsed;
        ApplyResponsiveLayout();
    }

    private void AttributeTabs_SelectionChanged(object _, SelectionChangedEventArgs __)
    {
        if (AttributeTabs.SelectedItem is HeroSection section)
        {
            _selectedSectionId = section.Id;
            UpdateVisibleAttributeHeroes();
        }
    }

    private async void RankBox_SelectionChanged(object _, SelectionChangedEventArgs __)
    {
        if (RankBox.SelectedItem is RankOption rank)
        {
            _selectedRank = rank;
            _settings.DefaultRankId = rank.Id;
            if (_applicationStarted && !_settings.Save()) WriteErrorLog(new IOException(_settings.LastSaveError));
            ApplyFilters();
            if (_selectedHero is not null) await ShowHeroAsync(_selectedHero);
        }
    }

    private void SortBox_SelectionChanged(object _, SelectionChangedEventArgs __)
    {
        if (SortBox.SelectedItem is SortOption sort)
        {
            _selectedSort = sort;
            ApplyFilters();
        }
    }

    private async void RefreshButton_Click(object _, RoutedEventArgs __)
    {
        _dataService.ClearCache();
        await LoadCatalogAsync(true);
        if (_selectedHero is not null) await ShowHeroAsync(_selectedHero);
    }

    private async void ItemRefreshButton_Click(object _, RoutedEventArgs __)
    {
        _dataService.ClearCache();
        _itemsLoaded = false;
        await LoadItemsAsync(true);
        if (_selectedItem is not null) await ShowItemAsync(_selectedItem);
    }

    private void HeroListHost_SizeChanged(object _, SizeChangedEventArgs __) => UpdateHeroCardLayout();

    private void UpdateVisibleAttributeHeroes()
    {
        var section = HeroSections.FirstOrDefault(item => item.Id == _selectedSectionId);
        VisibleAttributeHeroes.Clear();
        if (section is null) return;
        foreach (var hero in section.Heroes) VisibleAttributeHeroes.Add(hero);
        var scope = section.Id == "all-heroes" ? "英雄" : $"{section.Title}英雄";
        ResultCountText.Text = $"{VisibleAttributeHeroes.Count} 位{scope} · {_selectedRank.Label}";
    }

    private void UpdateHeroCardLayout()
    {
        var availableWidth = HeroListHost.ActualWidth;
        if (availableWidth <= 0) return;

        var columns = availableWidth switch
        {
            < 520 => 3,
            < 820 => 4,
            < 1120 => 5,
            _ => 6,
        };
        const double gap = 12;
        var cardWidth = Math.Clamp((availableWidth - (columns - 1) * gap) / columns, 112, 224);
        foreach (var hero in _allHeroes) hero.UpdateLayout(cardWidth);
    }

    private void RootGrid_SizeChanged(object _, SizeChangedEventArgs __) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        if (RootGrid is null || HeroPage is null) return;
        var compact = RootGrid.ActualWidth > 0 && RootGrid.ActualWidth < 1100;
        var pagePadding = compact ? new Thickness(12, 12, 12, 16) : new Thickness(24, 18, 24, 22);
        HeroPage.Padding = pagePadding;
        ItemPage.Padding = pagePadding;
        CoachPage.Padding = pagePadding;
        AnalysisPage.Padding = pagePadding;

        ConfigureFullPageDetail(
            _selectedHero is not null,
            HeroListColumn,
            HeroDetailColumn,
            HeroListHost,
            HeroDetailPanel);
        ConfigureFullPageDetail(
            _selectedItem is not null,
            ItemListColumn,
            ItemDetailColumn,
            ItemListHost,
            ItemDetailPanel);

        var hasAnalysisDetail = _selectedReport is not null;
        AnalysisDetailPanel.Visibility = hasAnalysisDetail ? Visibility.Visible : Visibility.Collapsed;
        if (!hasAnalysisDetail)
        {
            AnalysisList.Visibility = Visibility.Visible;
            AnalysisListColumn.Width = new GridLength(1, GridUnitType.Star);
            AnalysisListColumn.MinWidth = 0;
            AnalysisDetailColumn.Width = new GridLength(0);
            AnalysisDetailColumn.MinWidth = 0;
        }
        else if (compact)
        {
            AnalysisList.Visibility = Visibility.Collapsed;
            AnalysisListColumn.Width = new GridLength(0);
            AnalysisListColumn.MinWidth = 0;
            AnalysisDetailColumn.Width = new GridLength(1, GridUnitType.Star);
            AnalysisDetailColumn.MinWidth = 0;
        }
        else
        {
            AnalysisList.Visibility = Visibility.Visible;
            AnalysisListColumn.Width = new GridLength(330);
            AnalysisListColumn.MinWidth = 280;
            AnalysisDetailColumn.Width = new GridLength(1, GridUnitType.Star);
            AnalysisDetailColumn.MinWidth = 360;
        }

        Grid.SetRow(AnalysisImportPanel, compact ? 1 : 0);
        Grid.SetColumn(AnalysisImportPanel, 0);
        AnalysisImportPanel.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        ApplyCoachResponsiveLayout(compact);
        ApplyReferenceResponsiveLayout(compact);
        UpdateHeroCardLayout();
    }

    private void ApplyReferenceResponsiveLayout(bool compact)
    {
        HeroBannerPrimaryColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(2, GridUnitType.Star);
        HeroBannerBiographyColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        HeroBannerGrid.Height = compact ? 450 : 390;
        HeroBannerContentGrid.Margin = compact
            ? new Thickness(20, 24, 20, 24)
            : new Thickness(36, 28, 36, 28);
        Grid.SetRow(HeroBiographyPanel, compact ? 1 : 0);
        Grid.SetColumn(HeroBiographyPanel, compact ? 0 : 1);

        ScepterUpgradeColumn.Width = new GridLength(1, GridUnitType.Star);
        ShardUpgradeColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(ShardUpgradePanel, compact ? 1 : 0);
        Grid.SetColumn(ShardUpgradePanel, compact ? 0 : 1);

        ItemDetailImageColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(184);
        ItemDetailTextColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(ItemImageFrame, 0);
        Grid.SetColumn(ItemImageFrame, 0);
        var itemText = ItemNameText.Parent as FrameworkElement;
        if (itemText is not null)
        {
            Grid.SetRow(itemText, compact ? 1 : 0);
            Grid.SetColumn(itemText, compact ? 0 : 1);
        }
    }

    private static void ConfigureFullPageDetail(
        bool hasDetail,
        ColumnDefinition listColumn,
        ColumnDefinition detailColumn,
        UIElement listElement,
        UIElement detailElement)
    {
        listElement.Visibility = hasDetail ? Visibility.Collapsed : Visibility.Visible;
        detailElement.Visibility = hasDetail ? Visibility.Visible : Visibility.Collapsed;
        listColumn.Width = hasDetail ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        listColumn.MinWidth = 0;
        detailColumn.Width = hasDetail ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        detailColumn.MinWidth = 0;
    }

    private void ApplyCoachResponsiveLayout(bool compact)
    {
        if (compact)
        {
            CoachPrimaryGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            CoachPrimaryGrid.ColumnDefinitions[0].MinWidth = 0;
            CoachPrimaryGrid.ColumnDefinitions[1].Width = new GridLength(0);
            CoachPrimaryGrid.ColumnDefinitions[1].MinWidth = 0;
            Place(CoachHeroPanel, 0, 0);
            Place(CoachAdvicePanel, 1, 0);

            StackThreeColumns(CoachRosterGrid, CoachAlliesPanel, CoachEnemiesPanel, CoachRecommendationPanel);
            StackThreeColumns(CoachCounterGrid, CoachCounterRiskPanel, CoachCounterAdvantagePanel, CoachCounterPickPanel);

            Place(CoachStatusLeft, 0, 0);
            Place(CoachActionsPanel, 0, 2);
            Place(CoachOverviewPanel, 1, 0, 3);
        }
        else
        {
            CoachPrimaryGrid.ColumnDefinitions[0].Width = new GridLength(0.95, GridUnitType.Star);
            CoachPrimaryGrid.ColumnDefinitions[0].MinWidth = 320;
            CoachPrimaryGrid.ColumnDefinitions[1].Width = new GridLength(1.05, GridUnitType.Star);
            CoachPrimaryGrid.ColumnDefinitions[1].MinWidth = 360;
            Place(CoachHeroPanel, 0, 0);
            Place(CoachAdvicePanel, 0, 1);

            RestoreThreeColumns(CoachRosterGrid, CoachAlliesPanel, CoachEnemiesPanel, CoachRecommendationPanel, 180, 180, 260);
            RestoreThreeColumns(CoachCounterGrid, CoachCounterRiskPanel, CoachCounterAdvantagePanel, CoachCounterPickPanel, 200, 200, 200);

            Place(CoachStatusLeft, 0, 0);
            Place(CoachOverviewPanel, 0, 1);
            Place(CoachActionsPanel, 0, 2);
        }
    }

    private static void StackThreeColumns(Grid grid, FrameworkElement first, FrameworkElement second, FrameworkElement third)
    {
        grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[0].MinWidth = 0;
        for (var index = 1; index < 3; index++)
        {
            grid.ColumnDefinitions[index].Width = new GridLength(0);
            grid.ColumnDefinitions[index].MinWidth = 0;
        }
        Place(first, 0, 0);
        Place(second, 1, 0);
        Place(third, 2, 0);
    }

    private static void RestoreThreeColumns(
        Grid grid,
        FrameworkElement first,
        FrameworkElement second,
        FrameworkElement third,
        double firstMin,
        double secondMin,
        double thirdMin)
    {
        var weights = new[] { 1d, 1d, 1.3d };
        var minimums = new[] { firstMin, secondMin, thirdMin };
        for (var index = 0; index < 3; index++)
        {
            grid.ColumnDefinitions[index].Width = new GridLength(weights[index], GridUnitType.Star);
            grid.ColumnDefinitions[index].MinWidth = minimums[index];
        }
        Place(first, 0, 0);
        Place(second, 0, 1);
        Place(third, 0, 2);
    }

    private static void Place(FrameworkElement element, int row, int column, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
    }

    private static string AttributeId(HeroStat hero) => hero.PrimaryAttribute switch
    {
        "str" => "str",
        "agi" => "agi",
        "int" => "int",
        _ => "all",
    };

    private void PopulateSettings()
    {
        _settings.RuleOptions ??= new CoachRuleOptions();
        _settings.RuleOptions.Normalize();
        _cooldownDraft = new Dictionary<string, int>(
            _settings.RuleOptions.CooldownSeconds,
            StringComparer.OrdinalIgnoreCase);
        _selectedCooldownKey = null;
        CoachCooldownRuleBox.ItemsSource = RuleCooldownChoices;
        CoachCooldownRuleBox.SelectedIndex = 0;
        CoachLowHealthBox.Text = _settings.RuleOptions.LowHealthPercent.ToString("0.#");
        CoachLowManaBox.Text = _settings.RuleOptions.LowManaPercent.ToString("0.#");
        CoachFightHealthBox.Text = _settings.RuleOptions.FightReadyHealthPercent.ToString("0.#");
        CoachFightManaBox.Text = _settings.RuleOptions.FightReadyManaPercent.ToString("0.#");
        CoachLowHealthEnabledBox.IsChecked = _settings.RuleOptions.EnableLowHealth;
        CoachLowManaEnabledBox.IsChecked = _settings.RuleOptions.EnableLowMana;
        CoachUltimateEnabledBox.IsChecked = _settings.RuleOptions.EnableUltimateReady;
        CoachDeathEnabledBox.IsChecked = _settings.RuleOptions.EnableDeath;
        CoachControlEnabledBox.IsChecked = _settings.RuleOptions.EnableControlLocked;
        CoachBuybackEnabledBox.IsChecked = _settings.RuleOptions.EnableBuybackWindow;
        CoachFightReadyEnabledBox.IsChecked = _settings.RuleOptions.EnableFightReady;
        CoachSmokeEnabledBox.IsChecked = _settings.RuleOptions.EnableSmoke;
        CoachSmokedEnabledBox.IsChecked = _settings.RuleOptions.EnableSmokedState;
        CoachNightEnabledBox.IsChecked = _settings.RuleOptions.EnableNightFall;
        CoachBuildingEnabledBox.IsChecked = _settings.RuleOptions.EnableBuildings;
        CoachRoshanEnabledBox.IsChecked = _settings.RuleOptions.EnableRoshan;
        CoachItemReadyEnabledBox.IsChecked = _settings.RuleOptions.EnableItemReady;
        CoachAbilityReadyEnabledBox.IsChecked = _settings.RuleOptions.EnableAbilityReady;
        CoachMapAwarenessEnabledBox.IsChecked = _settings.RuleOptions.EnableMapAwareness;
        _coachRules = new CoachRuleEngine(options: _settings.RuleOptions);
        CoachPathBox.Text = _settings.DotaCfgManualPath ?? string.Empty;
        CoachAutoStartBox.IsOn = _settings.AutoStartGsi;
        SmartCoachEnabledBox.IsChecked = _settings.SmartCoachEnabled;
        AiBackendBox.ItemsSource = new List<string>
        {
            "NPU 本地推理（低功耗，推荐）",
            "云端 API（零本地性能开销）",
            "规则原文（零消耗）",
        };
        AiBackendBox.SelectedIndex = _settings.BackendKind switch
        {
            AiBackendKind.Cloud => 1,
            AiBackendKind.Rules => 2,
            _ => 0,
        };
        CloudEndpointBox.Text = _settings.CloudEndpoint;
        CloudModelBox.Text = _settings.CloudModel;
        CloudApiKeyBox.Password = _settings.CloudApiKey;
        CloudSection.Visibility = _settings.BackendKind == AiBackendKind.Cloud ? Visibility.Visible : Visibility.Collapsed;
        SmartCoachIntervalBox.Text = _settings.SmartCoachMinIntervalSeconds.ToString();
        NpuStatusText.Text = _applicationStarted ? NpuCapabilityDetector.Detect().Summary : "保存后检查本机能力";
        NpuModelPathBox.Text = _settings.NpuModelPath;
        DataDirectoryText.Text = AegisCoachPaths.Root;
        _npuCoach.ModelPath = _settings.NpuModelPath;
        StratzApiKeyBox.Password = _settings.StratzApiKey;
        LiveHeroTrackingEnabledBox.IsChecked = _settings.LiveHeroTrackingEnabled;
        UpdateNpuModelStatus();
        if (!_settings.LiveHeroTrackingEnabled) CoachHeroesStatusText.Text = "未启用";
        CoachInfoBar.IsOpen = false;
        UpdateCoachStatusUi();
    }

    private void ToggleCoach(bool start)
    {
        if (start)
        {
            ApplyCoachSettings();
            _receiver = new GsiReceiver(AppSettings.GsiPort, snapshotDirectory: SnapshotDirectory);
            _receiver.SnapshotReceived += OnSnapshotReceived;
            _receiver.ConnectionChanged += OnConnectionChanged;
            _receiver.Log += OnCoachLog;
            _receiver.Start();
            _coachRunning = _receiver.IsRunning;
            AppendCoachLog($"原始快照记录目录：{SnapshotDirectory}");
            if (!_coachRunning && _receiver.LastError is not null)
            {
                ShowCoachInfo(InfoBarSeverity.Error, "GSI 启动失败", _receiver.LastError);
            }
        }
        else
        {
            _receiver?.Stop();
            _receiver = null;
            _coachRunning = false;
            _previousSnapshot = null;
            _latestSnapshot = null;
            CancelHeroTracking();
            AppendCoachLog("GSI 监听已停止");
        }

        UpdateCoachStatusUi();
    }

    private bool ApplyCoachSettings(bool completeSetup = false)
    {
        _settings.DotaCfgManualPath = string.IsNullOrWhiteSpace(CoachPathBox.Text) ? null : CoachPathBox.Text.Trim();
        _settings.AutoStartGsi = CoachAutoStartBox.IsOn;
        _settings.SmartCoachEnabled = SmartCoachEnabledBox.IsChecked == true;
        _settings.AiBackend = AiBackendBox.SelectedIndex switch
        {
            1 => "cloud",
            2 => "rules",
            _ => "npu",
        };
        _settings.CloudEndpoint = string.IsNullOrWhiteSpace(CloudEndpointBox.Text)
            ? "https://api.openai.com/v1" : CloudEndpointBox.Text.Trim();
        _settings.CloudModel = string.IsNullOrWhiteSpace(CloudModelBox.Text)
            ? "gpt-4o-mini" : CloudModelBox.Text.Trim();
        _settings.CloudApiKey = CloudApiKeyBox.Password.Trim();
        _settings.SmartCoachMinIntervalSeconds = int.TryParse(SmartCoachIntervalBox.Text.Trim(), out var interval)
            ? Math.Clamp(interval, 1, 60) : 3;
        _settings.NpuModelPath = string.IsNullOrWhiteSpace(NpuModelPathBox.Text)
            ? _settings.NpuModelPath : NpuModelPathBox.Text.Trim();
        _npuCoach.ModelPath = _settings.NpuModelPath;
        _dataService.CacheDirectory = _settings.CacheDirectory;
        MatchSessionTracker.HabitsDirectory = AegisCoachPaths.Habits;
        MatchSessionTracker.AnalysisDirectory = _settings.AnalysisDirectory;
        _settings.LiveHeroTrackingEnabled = LiveHeroTrackingEnabledBox.IsChecked == true;
        _settings.StratzApiKey = StratzApiKeyBox.Password.Trim();
        if (!_settings.LiveHeroTrackingEnabled) CancelHeroTracking();
        var rules = _settings.RuleOptions ??= new CoachRuleOptions();
        rules.LowHealthPercent = ParseRulePercent(CoachLowHealthBox.Text, rules.LowHealthPercent);
        rules.LowManaPercent = ParseRulePercent(CoachLowManaBox.Text, rules.LowManaPercent);
        rules.FightReadyHealthPercent = ParseRulePercent(CoachFightHealthBox.Text, rules.FightReadyHealthPercent);
        rules.FightReadyManaPercent = ParseRulePercent(CoachFightManaBox.Text, rules.FightReadyManaPercent);
        rules.EnableLowHealth = CoachLowHealthEnabledBox.IsChecked == true;
        rules.EnableLowMana = CoachLowManaEnabledBox.IsChecked == true;
        rules.EnableUltimateReady = CoachUltimateEnabledBox.IsChecked == true;
        rules.EnableDeath = CoachDeathEnabledBox.IsChecked == true;
        rules.EnableControlLocked = CoachControlEnabledBox.IsChecked == true;
        rules.EnableBuybackWindow = CoachBuybackEnabledBox.IsChecked == true;
        rules.EnableFightReady = CoachFightReadyEnabledBox.IsChecked == true;
        rules.EnableSmoke = CoachSmokeEnabledBox.IsChecked == true;
        rules.EnableSmokedState = CoachSmokedEnabledBox.IsChecked == true;
        rules.EnableNightFall = CoachNightEnabledBox.IsChecked == true;
        rules.EnableBuildings = CoachBuildingEnabledBox.IsChecked == true;
        rules.EnableRoshan = CoachRoshanEnabledBox.IsChecked == true;
        rules.EnableItemReady = CoachItemReadyEnabledBox.IsChecked == true;
        rules.EnableAbilityReady = CoachAbilityReadyEnabledBox.IsChecked == true;
        rules.EnableMapAwareness = CoachMapAwarenessEnabledBox.IsChecked == true;
        SaveCurrentCooldownDraft();
        rules.CooldownSeconds = new Dictionary<string, int>(_cooldownDraft, StringComparer.OrdinalIgnoreCase);
        rules.Normalize();
        _coachRules = new CoachRuleEngine(options: rules);
        if (completeSetup) _settings.SetupCompleted = true;
        if (!_settings.Save())
        {
            if (completeSetup) _settings.SetupCompleted = false;
            ShowCoachInfo(
                InfoBarSeverity.Error,
                "设置无法保存",
                _settings.LastSaveError ?? $"请确认可以写入 {AegisCoachPaths.Root}");
            return false;
        }
        SmartCoachIntervalBox.Text = _settings.SmartCoachMinIntervalSeconds.ToString();
        UpdateNpuModelStatus();
        if (!_settings.LiveHeroTrackingEnabled) CoachHeroesStatusText.Text = "未启用";
        CoachLowHealthBox.Text = rules.LowHealthPercent.ToString("0.#");
        CoachLowManaBox.Text = rules.LowManaPercent.ToString("0.#");
        CoachFightHealthBox.Text = rules.FightReadyHealthPercent.ToString("0.#");
        CoachFightManaBox.Text = rules.FightReadyManaPercent.ToString("0.#");
        return true;
    }

    private static double ParseRulePercent(string value, double fallback) =>
        double.TryParse(value.Trim(), out var parsed) ? Math.Clamp(parsed, 1, 100) : fallback;

    private void CoachCooldownRuleBox_SelectionChanged(object _, SelectionChangedEventArgs __)
    {
        SaveCurrentCooldownDraft();
        if (CoachCooldownRuleBox.SelectedItem is not RuleCooldownChoice choice) return;
        _selectedCooldownKey = choice.Key;
        CoachCooldownSecondsBox.Value = _cooldownDraft.GetValueOrDefault(choice.Key, 30);
    }

    private void SaveCurrentCooldownDraft()
    {
        if (string.IsNullOrWhiteSpace(_selectedCooldownKey)) return;
        var value = double.IsNaN(CoachCooldownSecondsBox.Value)
            ? _cooldownDraft.GetValueOrDefault(_selectedCooldownKey, 30)
            : (int)Math.Round(CoachCooldownSecondsBox.Value);
        _cooldownDraft[_selectedCooldownKey] = Math.Clamp(value, 1, 3600);
    }

    private void UpdateNpuModelStatus()
    {
        if (NpuModelStatusText is null) return;
        if (_settings.BackendKind != AiBackendKind.Npu)
        {
            NpuModelStatusText.Text = _settings.BackendKind == AiBackendKind.Cloud
                ? "云端后端启用，本机不执行推理"
                : "规则原文后端，不调用模型";
            return;
        }

        NpuModelStatusText.Text = _npuCoach.IsConfigured
            ? "模型目录就绪，首次解释时加载（约 30 秒）"
            : "未找到模型目录，请检查路径或先下载 NPU 模型";
    }

    private void OnSnapshotReceived(object? _, MatchSnapshot snapshot)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var finishedMatch = _sessionTracker.OnSnapshot(snapshot);
            var advice = _coachRules.Evaluate(_previousSnapshot, snapshot);
            _previousSnapshot = snapshot;
            _latestSnapshot = snapshot;
            HandleHeroTracking(snapshot);
            if (finishedMatch is not null) HandleFinishedMatch(finishedMatch);
            foreach (var item in advice)
            {
                CoachAdvices.Insert(0, item);
                if (_settings.SmartCoachEnabled && TryReserveSmartExplanation()) _ = ExplainAdviceAsync(item, snapshot);
            }

            while (CoachAdvices.Count > 200) CoachAdvices.RemoveAt(CoachAdvices.Count - 1);
            CoachAdviceCountText.Text = $"{CoachAdvices.Count} 条提醒";
            UpdateCoachSnapshotUi(snapshot);
            UpdateCoachStatusUi();
        });
    }

    private void HandleFinishedMatch(MatchSessionRecord record)
    {
        _habits = MatchSessionTracker.AppendHabit(record);
        UpdateHabitsSummary();

        var report = new MatchReport
        {
            Id = $"report-{DateTime.Now:yyyyMMdd-HHmmss}-{record.MatchId}",
            GeneratedAt = DateTimeOffset.UtcNow,
            Match = record,
            AiReview = BuildFallbackReview(record, null),
            Backend = "规则摘要",
        };
        AnalysisReports.Insert(0, report);
        if (AnalysisReports.Count > 200) AnalysisReports.RemoveAt(AnalysisReports.Count - 1);
        AnalysisCountText.Text = $"{AnalysisReports.Count} 份报告";
        AppendCoachLog($"赛后分析：本局（{record.HeroName}，{record.ResultText}）已记录，正在生成复盘…");
        if (_settings.SmartCoachEnabled) _ = GenerateMatchReviewAsync(report);
        else MatchSessionTracker.SaveReport(report);
    }

    private async Task GenerateMatchReviewAsync(MatchReport report)
    {
        var record = report.Match;
        var heroName = string.IsNullOrWhiteSpace(record.HeroName)
            ? LocalizedHeroNameById(record.HeroId)
            : record.HeroName;
        var operations = report.Operations ?? await Task.Run(() =>
            new MatchOperationExtractor().Extract(
                SnapshotDirectory, record.MatchId, record.StartedAt, record.EndedAt));
        if (operations is not null)
        {
            operations.HeroName = heroName;
            operations.Won = record.Won;
            report.Operations = operations;
        }

        ApiMatchSummary? api = report.Api;
        if (api is null && record.MatchId > 0)
        {
            var steamId = string.IsNullOrWhiteSpace(record.SteamId) ? _habits.SteamId : record.SteamId;
            using var fetchTimeout = CancellationTokenSource.CreateLinkedTokenSource(AppToken);
            fetchTimeout.CancelAfter(TimeSpan.FromSeconds(12));
            api = await _apiMatchAnalyzer.FetchAsync(
                record.MatchId,
                steamId,
                record.HeroId,
                ResolveApiHeroInternalName,
                ResolveApiItemName,
                ResolveApiItemById,
                fetchTimeout.Token);
            if (api is { HeroId: > 0 })
            {
                EnrichApiNames(api);
                report.Api = api;
            }
        }

        var prompt = operations is not null || api is { HeroId: > 0 }
            ? CoachAiPrompts.BuildMatchReviewPrompt(
                heroName,
                record.Won == true,
                record.DurationSeconds,
                operations ?? EmptyOperationsFrom(record, heroName, api),
                record.FinalItems,
                api)
            : BuildStatsOnlyPrompt(heroName, record);
        string? review = null;
        try
        {
            switch (_settings.BackendKind)
            {
                case AiBackendKind.Npu:
                    review = await _npuCoach.ExplainAsync(prompt, AppToken, maxTokens: 420);
                    if (!string.IsNullOrWhiteSpace(review)) report.Backend = "NPU 本地推理";
                    break;
                case AiBackendKind.Cloud:
                    review = await _cloudCoach.GenerateAsync(
                        prompt,
                        "你是Dota 2赛后复盘教练，只依据给定操作记录分析，不编造数据；区分事实与推断；建议必须带游戏时间。",
                        _settings.CloudEndpoint,
                        _settings.CloudModel,
                        _settings.CloudApiKey,
                        maxTokens: 700,
                        AppToken);
                    if (!string.IsNullOrWhiteSpace(review)) report.Backend = "云端 API";
                    break;
            }
        }
        catch
        {
            // 后端不可用时保留规则摘要。
        }

        if (!string.IsNullOrWhiteSpace(review)) report.AiReview = review;
        MatchSessionTracker.SaveReport(report);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (AnalysisList.SelectedItem is MatchReport selected && selected == report)
            {
                ShowAnalysisReport(report);
            }
        });
    }

    private static string BuildStatsOnlyPrompt(string heroName, MatchSessionRecord record) =>
        "你是Dota 2赛后复盘教练。请基于下面的统计写一份中文复盘报告，控制在250字以内：\n" +
        "1) 总体评价；2) 指出1-2个最值得改进的点并给出可执行建议；3) 提到做得好的地方。只使用下面的事实，不要编造数据。\n\n" +
        $"英雄：{heroName}，结果：{(record.Won == true ? "胜利" : record.Won == false ? "失败" : "未知")}，" +
        $"时长 {record.DurationText}\nKDA {record.KdaText}，补刀 {record.LastHits}，GPM {record.Gpm}，XPM {record.Xpm}，" +
        $"最高等级 {record.MaxLevel}，最终装备：{(record.FinalItems.Count > 0 ? string.Join("、", record.FinalItems) : "无")}";

    private static string BuildFallbackReview(MatchSessionRecord record, MatchOperationSummary? ops)
    {
        var lines = new List<string>
        {
            record.Won switch
            {
                true => "总体评价：本局获胜，团队执行整体到位。",
                false => "总体评价：本局失利，可以从中复盘关键转折。",
                null => "总体评价：本局结束，结果未知。",
            },
            $"关键数据：KDA {record.KdaText}，GPM {record.Gpm}，XPM {record.Xpm}，补刀 {record.LastHits}，反补 {record.Denies}，最高等级 {record.MaxLevel}，时长 {record.DurationText}。",
        };
        if (ops is not null)
        {
            var laning = ops.Phases.FirstOrDefault(phase => phase.Label == "对线期");
            if (laning is not null)
            {
                lines.Add($"对线期（{laning.TimeText}）补刀 {laning.LastHitsDelta}（{laning.LastHitsPerMinute:0.#}/分），" +
                    $"死亡 {laning.DeathsDelta} 次，击杀 {laning.KillsDelta}。对线期死亡过多会直接拖慢经济与等级；" +
                    (laning.LastHitsPerMinute < 5
                        ? "补刀效率偏低，建议在训练模式练正反补节奏，并用补刀练习地图。"
                        : "补刀节奏正常，继续保持并注意边补边看小地图。"));
            }

            foreach (var death in ops.DeathsDetailed)
            {
                var causes = new List<string>();
                if (death.HealthPercentAtDeath <= 30 && death.LastHitsInWindow > 2)
                    causes.Add($"阵亡前血量已低至 {death.HealthPercentAtDeath:0}% 仍在补刀");
                if (death.UltReadyAtDeath)
                    causes.Add("大招就绪未使用");
                if (death.ReadyActivesUnused.Count > 0)
                    causes.Add($"{string.Join("、", death.ReadyActivesUnused)}就绪未使用");
                if (death.TpAvailable)
                    causes.Add("TP 可用未撤退");
                if (causes.Count > 0)
                {
                    lines.Add($"{death.TimeText} 第{death.DeathNumber}次阵亡：{string.Join("；", causes)}。建议：低血量或资源不足时立即后撤/回城，不要贪刀。");
                }
                else
                {
                    lines.Add($"{death.TimeText} 第{death.DeathNumber}次阵亡（阵亡前血量 {death.HealthPercentAtDeath:0}%）。可回看该时间点核对敌方动向。");
                }
            }

            var buybacksMissed = ops.DeathsDetailed.Count(death => death.BuybackAvailable && !death.BuybackUsed);
            if (buybacksMissed > 0)
                lines.Add($"本局有 {buybacksMissed} 次可买活未使用。买活只在高地防守、肉山团或即将结束比赛时使用，平时留着买活更好。");
        }
        if (record.Deaths >= 8)
        {
            lines.Add("改进点：阵亡偏多。优先保证视野与抱团，避免单人带线过河；每次阵亡后记录敌方位置与技能状态。");
        }

        if (record.Won != true && record.Gpm < 420)
        {
            lines.Add("改进点：经济偏低。提升补刀与参战效率，规划更早的推塔与野区资源，而不是被动等待。");
        }

        if (record.FinalItems.Count > 0)
        {
            lines.Add($"最终装备：{string.Join("、", record.FinalItems)}。对照版本强势出装，确认关键道具购买时机是否合理。");
        }

        if (record.KeyMoments.Count > 0)
        {
            lines.Add("关键节点：" + string.Join("；", record.KeyMoments.Take(8)) + "。");
        }

        lines.Add("建议：结合回放回看 1-2 个关键团战，核对每次开团前的视野、技能冷却与敌方位置。");
        return string.Join("\n", lines);
    }

    private bool TryReserveSmartExplanation()
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(_settings.SmartCoachMinIntervalSeconds, 1, 60));
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSmartExplanationAt < interval) return false;
        _lastSmartExplanationAt = now;
        return true;
    }

    private async Task ExplainAdviceAsync(CoachAdvice advice, MatchSnapshot snapshot)
    {
        try
        {
            string? explanation = null;
            switch (_settings.BackendKind)
            {
                case AiBackendKind.Npu:
                    explanation = await _npuCoach.ExplainAsync(
                        CoachAiPrompts.BuildPrompt(advice, snapshot), AppToken);
                    break;
                case AiBackendKind.Cloud:
                    explanation = await _cloudCoach.ExplainAsync(
                        advice, snapshot, _settings.CloudEndpoint, _settings.CloudModel, _settings.CloudApiKey,
                        AppToken);
                    break;
            }

            if (string.IsNullOrWhiteSpace(explanation)) return;
            DispatcherQueue.TryEnqueue(() => advice.Message += $" 智能解释：{explanation}");
        }
        catch
        {
            // The deterministic rule message remains complete when the AI backend is unavailable.
        }
    }

    private async Task RefreshNpuRuntimeAsync()
    {
        try
        {
            var report = await _npuRuntime.RegisterAsync(AppToken);
            DispatcherQueue.TryEnqueue(() => NpuStatusText.Text = report.Summary);
        }
        catch (Exception exception)
        {
            DispatcherQueue.TryEnqueue(() => NpuStatusText.Text =
                "NPU provider 检查失败；GPU provider 已禁用，AI 解释仅允许使用 CPU。 " + exception.Message);
        }
    }

    private async void SmartCoachCloudProbeButton_Click(object _, RoutedEventArgs __)
    {
        SmartCoachStatusText.Text = "正在测试云端连接…";
        try
        {
            var endpoint = string.IsNullOrWhiteSpace(CloudEndpointBox.Text)
                ? "https://api.openai.com/v1"
                : CloudEndpointBox.Text.Trim();
            var available = await _cloudCoach.ProbeAsync(
                endpoint, CloudApiKeyBox.Password.Trim(), AppToken);
            SmartCoachStatusText.Text = available
                ? "云端连接可用"
                : "云端连接失败（地址或密钥无效，或网络不可达）";
        }
        catch (Exception exception)
        {
            SmartCoachStatusText.Text = "测试失败：" + exception.Message;
        }
    }

    private void AiBackendBox_SelectionChanged(object _, SelectionChangedEventArgs __)
    {
        if (CloudSection is null || AiBackendBox is null) return;
        CloudSection.Visibility = AiBackendBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>天赋树节点点击：每级左右二选一。</summary>
    private void TalentNode_Click(object sender, RoutedEventArgs _)
    {
        if (sender is not ToggleButton toggle || toggle.DataContext is not TalentLevelRow row) return;
        if (toggle.IsChecked != true) return;
        var isLeft = string.Equals(toggle.Tag as string, "Left", StringComparison.Ordinal);
        (isLeft ? row.Right : row.Left).IsSelected = false;
    }

    private void OnConnectionChanged(object? _, bool connected)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            CoachStatusDot.Fill = connected
                ? new SolidColorBrush(Microsoft.UI.Colors.MediumSeaGreen)
                : ThemeBrush("TertiaryTextBrush");
            CoachStatusText.Text = connected ? "已连接" : "未连接";
        });
    }

    private void OnCoachLog(object? _, string message)
    {
        DispatcherQueue.TryEnqueue(() => AppendCoachLog(message));
    }

    private void HandleHeroTracking(MatchSnapshot snapshot)
    {
        if (!_settings.LiveHeroTrackingEnabled)
        {
            CancelHeroTracking();
            return;
        }

        var matchId = snapshot.MatchId > 0 ? snapshot.MatchId.ToString() : null;
        if (matchId is null)
        {
            CancelHeroTracking();
            return;
        }

        if (_trackedMatchId == matchId) return;

        CancelHeroTracking();
        _trackedMatchId = matchId;
        if (long.TryParse(snapshot.Player.SteamId, out var steamId) && steamId > 0)
        {
            _heroTrackingCts = CancellationTokenSource.CreateLinkedTokenSource(AppToken);
            _ = TrackLiveMatchAsync(matchId, steamId, snapshot.Player.TeamName ?? string.Empty, _heroTrackingCts.Token);
            CoachHeroesStatusText.Text = "正在检索公开对局（人机/私人局不可用）…";
        }
    }

    private async Task TrackLiveMatchAsync(string matchId, long steamId, string myTeamName, CancellationToken cancellationToken)
    {
        var emptyAttempts = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                LiveMatchInfo? info = null;
                if (!string.IsNullOrWhiteSpace(_settings.StratzApiKey))
                {
                    // Stratz 对任意公开对局实时收录，对局中（含选人阶段）即可返回全场英雄。
                    try
                    {
                        info = await _liveMatchService.FindMatchByStratzAsync(matchId, _settings.StratzApiKey, myTeamName, cancellationToken);
                    }
                    catch
                    {
                    }
                }

                // 未配置 Stratz 或查询失败时回退：比赛 ID 直查 OpenDota（收录后即可拿到全场英雄，
                // 热门对局对局中可查，普通对局通常在赛后收录），实时列表只覆盖热门对局，作为补充。
                if (!string.IsNullOrWhiteSpace(matchId) && !string.IsNullOrWhiteSpace(myTeamName))
                {
                    try
                    {
                        info = await _liveMatchService.FindMatchByIdAsync(matchId, myTeamName, cancellationToken);
                    }
                    catch
                    {
                    }
                }

                if (info is null)
                {
                    try
                    {
                        info = await _liveMatchService.FindLiveMatchAsync(steamId, cancellationToken);
                    }
                    catch
                    {
                    }
                }

                if (info is not null)
                {
                    emptyAttempts = 0;
                    _liveMatch = info;
                    DispatcherQueue.TryEnqueue(() => UpdateLiveHeroUi(info));
                }
                else
                {
                    emptyAttempts++;
                    var status = emptyAttempts <= 2
                        ? "正在检索公开对局（人机/私人局不可用）…"
                        : string.IsNullOrWhiteSpace(_settings.StratzApiKey)
                            ? "本局尚未被 OpenDota 收录：热门对局可实时获取，普通对局通常在结束后收录，正在自动重试…"
                            : "Stratz 暂未返回本局数据，正在自动重试…";
                    DispatcherQueue.TryEnqueue(() => CoachHeroesStatusText.Text = status);
                }

                await Task.Delay(TimeSpan.FromSeconds(40), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            DispatcherQueue.TryEnqueue(() => CoachHeroesStatusText.Text = "全场英雄检索失败（网络或接口不可用）");
        }
    }

    private void UpdateLiveHeroUi(LiveMatchInfo info)
    {
        var picked = info.Heroes.Where(hero => hero.HeroId > 0).ToList();
        CoachHeroesStatusText.Text = picked.Count == 0
            ? $"比赛 {info.MatchId}：选人阶段"
            : $"已获取 {picked.Count} 名英雄（比赛 {info.MatchId}）";

        CoachAllyHeroes.Clear();
        foreach (var hero in picked.Where(hero => hero.Team == info.MyTeam))
        {
            CoachAllyHeroes.Add(new HeroChip(LocalizedHeroNameById(hero.HeroId), LocalizedHeroIconById(hero.HeroId)));
        }
        CoachAllyHeroesText.Text = CoachAllyHeroes.Count > 0 ? $"{CoachAllyHeroes.Count} 人" : "";

        CoachEnemyHeroes.Clear();
        foreach (var hero in picked.Where(hero => hero.Team != info.MyTeam))
        {
            CoachEnemyHeroes.Add(new HeroChip(LocalizedHeroNameById(hero.HeroId), LocalizedHeroIconById(hero.HeroId)));
        }
        CoachEnemyHeroesText.Text = CoachEnemyHeroes.Count > 0 ? $"{CoachEnemyHeroes.Count} 人" : "";

        if (_latestSnapshot is not null) _ = RefreshItemRecommendationAsync(_latestSnapshot);
        if (_latestSnapshot is not null) _ = RefreshCounterAnalysisAsync(_latestSnapshot);
    }

    private async Task RefreshCounterAnalysisAsync(MatchSnapshot snapshot)
    {
        if (!await _counterGate.WaitAsync(0)) return;
        try
        {
            if (_liveMatch is null)
            {
                CoachCounterStatusText.Text = "等待全场英雄数据（仅公开比赛）…";
                return;
            }

            var myHeroId = snapshot.Hero.Id;
            if (myHeroId <= 0)
            {
                CoachCounterStatusText.Text = "等待英雄数据…";
                return;
            }

            var enemies = _liveMatch.Heroes
                .Where(hero => hero.HeroId > 0 && hero.Team != _liveMatch.MyTeam)
                .ToList();
            var enemyIds = enemies.Select(hero => hero.HeroId).Distinct().ToList();
            if (enemyIds.Count == 0)
            {
                CoachCounterStatusText.Text = "敌方英雄尚未确认…";
                return;
            }

            // 敌方克制我 / 我克制敌方：以我方英雄的公开对阵胜率为准。
            var myMatchups = await GetMatchupsAsync(myHeroId);
            CoachCountersMe.Clear();
            CoachCountersTarget.Clear();
            var enemyStats = new List<DotaDataService.HeroMatchupStat>();
            foreach (var enemy in enemies)
            {
                var stat = myMatchups.FirstOrDefault(item => item.HeroId == enemy.HeroId);
                if (stat is null || stat.GamesPlayed < 30) continue;
                enemyStats.Add(stat);
                if (stat.WinRate <= 48)
                {
                    CoachCountersMe.Add(new CounterMatchupItem(
                        enemy.HeroId,
                        LocalizedHeroNameById(enemy.HeroId),
                        LocalizedHeroIconById(enemy.HeroId),
                        stat.WinRate,
                        stat.GamesPlayed,
                        countersTarget: false));
                }
                else if (stat.WinRate >= 52)
                {
                    CoachCountersTarget.Add(new CounterMatchupItem(
                        enemy.HeroId,
                        LocalizedHeroNameById(enemy.HeroId),
                        LocalizedHeroIconById(enemy.HeroId),
                        stat.WinRate,
                        stat.GamesPlayed,
                        countersTarget: true));
                }
            }

            // OpenDota 样本量小时用两端补足，保证克制与被克制都有展示。
            if (CoachCountersMe.Count == 0)
            {
                foreach (var stat in enemyStats.OrderBy(item => item.WinRate).Take(3))
                {
                    CoachCountersMe.Add(new CounterMatchupItem(
                        stat.HeroId,
                        LocalizedHeroNameById(stat.HeroId),
                        LocalizedHeroIconById(stat.HeroId),
                        stat.WinRate,
                        stat.GamesPlayed,
                        countersTarget: false));
                }
            }
            if (CoachCountersTarget.Count == 0)
            {
                foreach (var stat in enemyStats.OrderByDescending(item => item.WinRate).Take(3))
                {
                    CoachCountersTarget.Add(new CounterMatchupItem(
                        stat.HeroId,
                        LocalizedHeroNameById(stat.HeroId),
                        LocalizedHeroIconById(stat.HeroId),
                        stat.WinRate,
                        stat.GamesPlayed,
                        countersTarget: true));
                }
            }

            // 选人推荐：统计候选英雄对敌方阵容的克制面。用敌方英雄自身的对阵数据反向推导：
            // 敌方英雄对候选者的胜率越低，说明该候选者越克制它。
            CoachPickRecommendations.Clear();
            var taken = _liveMatch.Heroes.Where(hero => hero.HeroId > 0).Select(hero => hero.HeroId).ToHashSet();
            var scores = new Dictionary<int, (int Count, double WinRateSum)>();
            foreach (var enemyId in enemyIds)
            {
                List<DotaDataService.HeroMatchupStat> enemyMatchups;
                try
                {
                    enemyMatchups = await GetMatchupsAsync(enemyId);
                }
                catch
                {
                    continue;
                }

                foreach (var stat in enemyMatchups)
                {
                    if (taken.Contains(stat.HeroId) || stat.HeroId == myHeroId) continue;
                    if (stat.GamesPlayed < 30) continue;
                    // 敌方对该候选者的胜率 ≤48%：候选者克制敌方。
                    if (stat.WinRate > 48) continue;
                    if (!scores.TryGetValue(stat.HeroId, out var score))
                    {
                        score = (0, 0);
                    }
                    scores[stat.HeroId] = (score.Count + 1, score.WinRateSum + stat.WinRate);
                }
            }

            foreach (var pair in scores
                .Where(pair => pair.Value.Count >= 1)
                .OrderByDescending(pair => pair.Value.Count)
                .ThenBy(pair => pair.Value.WinRateSum / pair.Value.Count)
                .Take(6))
            {
                CoachPickRecommendations.Add(new CounterPickRecommendation(
                    pair.Key,
                    LocalizedHeroNameById(pair.Key),
                    LocalizedHeroIconById(pair.Key),
                    pair.Value.Count,
                    pair.Value.WinRateSum / pair.Value.Count));
            }

            CoachCounterStatusText.Text = CoachCountersMe.Count + CoachCountersTarget.Count + CoachPickRecommendations.Count > 0
                ? "基于 OpenDota 对阵胜率（样本 ≥30）"
                : "暂无足够对阵样本";
        }
        catch (Exception exception)
        {
            CoachCounterStatusText.Text = "克制分析暂不可用：" + exception.Message;
        }
        finally
        {
            _counterGate.Release();
        }
    }

    private string LocalizedHeroNameById(int heroId) =>
        _catalog?.Heroes.FirstOrDefault(hero => hero.Id == heroId)?.LocalizedName ?? $"英雄{heroId}";

    private string LocalizedHeroIconById(int heroId) =>
        _catalog?.Heroes.FirstOrDefault(hero => hero.Id == heroId)?.IconUrl ?? string.Empty;

    private async Task RefreshItemRecommendationAsync(MatchSnapshot snapshot)
    {
        if (!await _recommendationGate.WaitAsync(0)) return;
        if (_liveMatch is null)
        {
            CoachItemRecommendText.Text = "等待全场英雄数据（仅公开比赛）…";
            _recommendationGate.Release();
            return;
        }

        try
        {
            var myHeroId = snapshot.Hero.Id;
            if (myHeroId <= 0)
            {
                CoachItemRecommendText.Text = "等待英雄数据…";
                return;
            }

            var popularity = await GetPopularityAsync(myHeroId);
            var stage = snapshot.GameTime switch
            {
                < 600 => popularity.EarlyGameItems,
                < 1800 => popularity.MidGameItems,
                _ => popularity.LateGameItems,
            };
            if (stage.Count == 0) stage = popularity.MidGameItems.Count > 0 ? popularity.MidGameItems : popularity.LateGameItems;

            var total = stage.Values.Sum();
            var lines = new List<string>();
            var rank = 0;
            foreach (var pair in stage.OrderByDescending(pair => pair.Value))
            {
                var card = _allItems.FirstOrDefault(item => item.Item.Id == pair.Key);
                if (card is null) continue;
                var percent = total > 0 ? pair.Value * 100d / total : 0;
                var winText = card.Metric.WinRate is double winRate ? $" · 胜率 {winRate:0.0}%" : "";
                lines.Add($"{++rank}. {card.Name} —— 热度 {percent:0.0}%{winText}");
                if (rank >= 3) break;
            }

            var notes = BuildCounterNotes(myHeroId);
            if (notes.Count > 0) lines.Add("局势：" + string.Join("；", notes.Take(2)));

            var hints = BuildThreatHints();
            if (hints.Count > 0) lines.Add("提示：" + string.Join("；", hints.Take(2)));

            CoachItemRecommendText.Text = lines.Count > 0
                ? string.Join("\n", lines)
                : "暂无推荐数据（数据源未返回该英雄物品热度）";
        }
        catch (Exception exception)
        {
            CoachItemRecommendText.Text = "推荐数据获取失败：" + exception.Message;
        }
        finally
        {
            _recommendationGate.Release();
        }
    }

    private async Task<DotaDataService.HeroItemPopularity> GetPopularityAsync(int heroId)
    {
        if (_popularityCache.TryGetValue(heroId, out var cached)) return cached;
        var value = await _dataService.GetHeroItemPopularityAsync(heroId, AppToken);
        _popularityCache[heroId] = value;
        return value;
    }

    private async Task<List<DotaDataService.HeroMatchupStat>> GetMatchupsAsync(int heroId)
    {
        if (_matchupCache.TryGetValue(heroId, out var cached)) return cached;
        var value = await _dataService.GetHeroMatchupsAsync(heroId, AppToken);
        _matchupCache[heroId] = value;
        return value;
    }

    private List<string> BuildCounterNotes(int myHeroId)
    {
        var notes = new List<string>();
        var enemies = _liveMatch?.Heroes.Where(hero => hero.HeroId > 0 && hero.Team != _liveMatch.MyTeam).ToList();
        if (enemies is null || enemies.Count == 0) return notes;

        var matchups = _matchupCache.GetValueOrDefault(myHeroId);
        if (matchups is null) return notes;
        foreach (var enemy in enemies)
        {
            var stat = matchups.FirstOrDefault(item => item.HeroId == enemy.HeroId);
            if (stat is null || stat.GamesPlayed < 100) continue;
            if (stat.WinRate < 45)
            {
                notes.Add($"敌方 {LocalizedHeroNameById(enemy.HeroId)} 对你有 {stat.WinRate:0}% 胜率优势（样本 {stat.GamesPlayed:N0}）");
            }
        }

        return notes.OrderByDescending(note => note.Length).ToList();
    }

    private List<string> BuildThreatHints()
    {
        if (_liveMatch is null || _latestSnapshot is null) return [];
        var profiles = new Dictionary<int, HeroProfile>();
        foreach (var hero in _catalog?.Heroes ?? [])
        {
            profiles[hero.Id] = new HeroProfile(hero.Name, hero.LocalizedName, hero.PrimaryAttribute);
        }

        var refs = _liveMatch.Heroes
            .Where(hero => hero.HeroId > 0)
            .Select(hero => new MatchHeroRef(hero.HeroId, hero.Team != _liveMatch.MyTeam))
            .ToList();
        if (refs.Count == 0) return [];

        return ItemRecommendationEngine.Recommend(
                new ItemRecommendationContext(refs, profiles, _latestSnapshot.GameTime, _latestSnapshot.Player.Gold))
            .Select(suggestion => $"{suggestion.ItemName}（{suggestion.Reason}）")
            .ToList();
    }

    private void CancelHeroTracking()
    {
        _heroTrackingCts?.Cancel();
        _heroTrackingCts?.Dispose();
        _heroTrackingCts = null;
        _trackedMatchId = null;
        _liveMatch = null;
    }

    private void UpdateCoachStatusUi()
    {
        if (_coachRunning)
        {
            var connected = _receiver?.IsConnected == true;
            CoachStatusDot.Fill = connected
                ? new SolidColorBrush(Microsoft.UI.Colors.MediumSeaGreen)
                : ThemeBrush("TertiaryTextBrush");
            CoachStatusText.Text = _receiver?.LastError is null
                ? (connected ? "已连接" : "未连接")
                : "GSI 启动失败";
        }
        else
        {
            CoachStatusText.Text = "已停止";
            CoachStatusDot.Fill = ThemeBrush("TertiaryTextBrush");
        }

        CoachToggleButton.Content = _coachRunning ? "停止监听" : "启动监听";
    }

    private async Task<bool> OpenCoachSettingsAsync(bool firstRun = false)
    {
        PopulateSettings();
        CoachSettingsDialog.Title = firstRun ? "首次设置" : "设置";
        CoachSettingsDialog.PrimaryButtonText = firstRun ? "保存并开始" : "保存设置";
        CoachSettingsDialog.CloseButtonText = firstRun ? string.Empty : "取消";
        SettingsIntroTitle.Text = firstRun ? "完成基础设置后开始运行" : "调整应用设置";
        SettingsIntroText.Text = firstRun
            ? "保存前不会加载资料、连接 Dota 2 或启动智能服务。"
            : "常用选项立即显示，专业参数统一收在高级设置中。";
        CoachToggleButton.IsEnabled = _applicationStarted;
        _settingsSaved = false;
        CoachSettingsDialog.XamlRoot = RootGrid.XamlRoot;
        var result = await CoachSettingsDialog.ShowAsync();
        if (result != ContentDialogResult.Primary || !_settingsSaved) return false;
        if (firstRun) InstallCoachConfig();
        return true;
    }

    private void CoachSettingsDialog_PrimaryButtonClick(ContentDialog _, ContentDialogButtonClickEventArgs args)
    {
        _settingsSaved = ApplyCoachSettings(completeSetup: true);
        args.Cancel = !_settingsSaved;
    }

    private static SolidColorBrush ThemeBrush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

    private void AppendCoachLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        CoachLogText.Text = string.IsNullOrWhiteSpace(CoachLogText.Text) || CoachLogText.Text == "尚未启动"
            ? line
            : $"{line}\n{CoachLogText.Text}";
        var lines = CoachLogText.Text.Split('\n');
        if (lines.Length > 80) CoachLogText.Text = string.Join('\n', lines.Take(80));
    }

    private void ShowCoachInfo(InfoBarSeverity severity, string title, string message)
    {
        CoachInfoBar.Severity = severity;
        CoachInfoBar.Title = title;
        CoachInfoBar.Message = message;
        CoachInfoBar.IsOpen = true;
        AppendCoachLog($"{title}：{message}");
    }

    private void CoachToggleButton_Click(object _, RoutedEventArgs __)
    {
        if (_applicationStarted) ToggleCoach(!_coachRunning);
    }

    private void CoachInstallButton_Click(object _, RoutedEventArgs __)
    {
        InstallCoachConfig();
    }

    private void InstallCoachConfig()
    {
        var manualPath = string.IsNullOrWhiteSpace(CoachPathBox.Text) ? null : CoachPathBox.Text.Trim();
        var result = GsiConfigInstaller.Install(manualPath, AppSettings.GsiPort, string.Empty);
        ShowCoachInfo(result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
            result.Success ? "GSI 配置已安装" : "GSI 配置未安装",
            result.Success ? $"{result.Message} 请完全退出并重新启动 Dota 2 后生效。" : result.Message);
        if (!result.Success)
        {
            var export = AegisCoachPaths.ExportedGsiConfig;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(export)!);
                AtomicFile.WriteAllText(export, result.CfgContent);
                CoachInfoBar.Message += $" 已同时导出到 {export}";
            }
            catch (Exception exception)
            {
                CoachInfoBar.Message += $" 导出失败：{exception.Message}";
            }
        }
    }

    private async void CoachBrowseButton_Click(object _, RoutedEventArgs __)
    {
        var folder = await PickFolderAsync();
        if (folder is not null)
        {
            CoachPathBox.Text = folder.Path;
            AppendCoachLog($"已设置 Dota cfg 目录：{folder.Path}");
        }
    }

    private async void CoachBrowseModelButton_Click(object _, RoutedEventArgs __)
    {
        var folder = await PickFolderAsync();
        if (folder is not null) NpuModelPathBox.Text = folder.Path;
    }

    private async Task<Windows.Storage.StorageFolder?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSingleFolderAsync();
    }

    private void CoachOpenCfgButton_Click(object _, RoutedEventArgs __)
    {
        var manualPath = string.IsNullOrWhiteSpace(CoachPathBox.Text) ? null : CoachPathBox.Text.Trim();
        var directory = GsiConfigInstaller.LocateDotaCfgDirectory(manualPath);
        if (directory is null)
        {
            ShowCoachInfo(InfoBarSeverity.Warning, "未找到 Dota 2 cfg 目录",
                "请用“选择目录”指定 ...\\dota 2 beta\\game\\dota\\cfg，或参考 Steam 接入手册。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
            AppendCoachLog($"已打开 Dota 2 cfg 目录：{directory}");
            var cfgPath = Path.Combine(directory, GsiConfigInstaller.CfgFileName);
            ShowCoachInfo(InfoBarSeverity.Informational, "cfg 目录已打开",
                File.Exists(cfgPath) ? $"已找到 {GsiConfigInstaller.CfgFileName}" : "当前目录尚未安装 AegisCoach cfg");
        }
        catch (Exception exception)
        {
            ShowCoachInfo(InfoBarSeverity.Error, "无法打开 cfg 目录", exception.Message);
        }
    }

    private void CoachOpenDataButton_Click(object _, RoutedEventArgs __)
    {
        try
        {
            Directory.CreateDirectory(AegisCoachPaths.Root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AegisCoachPaths.Root}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowCoachInfo(InfoBarSeverity.Error, "无法打开数据目录", exception.Message);
        }
    }

    private void CoachClearButton_Click(object _, RoutedEventArgs __)
    {
        CoachAdvices.Clear();
        CoachAdviceCountText.Text = "0 条提醒";
    }

    private void UpdateCoachSnapshotUi(MatchSnapshot snapshot)
    {
        var matchParts = new List<string> { snapshot.PhaseText, snapshot.GameTimeText };
        if (snapshot.InMatch) matchParts.Add($"比分 {snapshot.ScoreText}");
        matchParts.Add(snapshot.Daytime ? "白天" : "夜晚");
        if (!string.IsNullOrWhiteSpace(snapshot.BuildingSummary)) matchParts.Add("建筑 " + snapshot.BuildingSummary);
        CoachOverviewText.Text = string.Join(" · ", matchParts);

        if (snapshot.Hero.Id > 0)
        {
            CoachHeroPanel.Visibility = Visibility.Visible;
            CoachHeroNameText.Text = LocalizedHeroNameById(snapshot.Hero.Id);
            CoachHeroLevelText.Text = $"Lv{snapshot.Hero.Level}";
            var iconUrl = LocalizedHeroIconById(snapshot.Hero.Id);
            if (!string.IsNullOrWhiteSpace(iconUrl) && !_coachHeroImageCache.TryGetValue(snapshot.Hero.Id, out _))
            {
                try
                {
                    _coachHeroImageCache[snapshot.Hero.Id] = new BitmapImage(new Uri(iconUrl));
                }
                catch
                {
                    /* 图标失败不影响状态卡 */
                }
            }
            if (_coachHeroImageCache.TryGetValue(snapshot.Hero.Id, out var heroImage))
            {
                CoachHeroImage.Source = heroImage;
            }

            if (snapshot.Hero.MaxHealth > 0)
            {
                CoachHeroHpBar.Maximum = snapshot.Hero.MaxHealth;
                CoachHeroHpBar.Value = snapshot.Hero.Health;
                CoachHeroHpText.Text = $"{snapshot.Hero.Health}/{snapshot.Hero.MaxHealth}（{snapshot.Hero.HealthPercent:0}%）";
            }

            if (snapshot.Hero.MaxMana > 0)
            {
                CoachHeroManaBar.Maximum = snapshot.Hero.MaxMana;
                CoachHeroManaBar.Value = snapshot.Hero.Mana;
                CoachHeroManaText.Text = $"{snapshot.Hero.Mana}/{snapshot.Hero.MaxMana}（{snapshot.Hero.ManaPercent:0}%）";
            }

            CoachHeroKdaText.Text = $"KDA {snapshot.Player.Kills}/{snapshot.Player.Deaths}/{snapshot.Player.Assists}";
            CoachHeroStatsText.Text =
                $"金钱 {snapshot.Player.Gold:N0} · GPM {snapshot.Player.Gpm} · XPM {snapshot.Player.Xpm} · 补刀 {snapshot.Player.LastHits}";
            var mainItems = snapshot.Items
                .Where(item => item.SlotKind == ItemSlotKind.Main && !string.IsNullOrWhiteSpace(item.Name))
                .Take(6)
                .Select(item => item.Name)
                .ToList();
            if (!mainItems.SequenceEqual(_lastCoachItems, StringComparer.OrdinalIgnoreCase))
            {
                CoachHeroItems.Clear();
                foreach (var item in snapshot.Items
                    .Where(item => item.SlotKind == ItemSlotKind.Main && !string.IsNullOrWhiteSpace(item.Name))
                    .Take(6))
                {
                    CoachHeroItems.Add(new CoachItemSlot(
                        CoachRuleEngine.FriendlyItemName(item.Name),
                        OfficialItemSummary.ImageUrlFor(item.Name)));
                }
                _lastCoachItems = mainItems;
            }
        }
        else
        {
            CoachHeroPanel.Visibility = Visibility.Collapsed;
        }

        _ = RefreshItemRecommendationAsync(snapshot);
    }

    private async void CoachSettingsButton_Click(object _, RoutedEventArgs __) => await OpenCoachSettingsAsync();

    private sealed record RuleCooldownChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }
}



