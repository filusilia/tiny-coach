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
    private readonly List<ItemCardItem> _allItems = [];
    private MatchReport? _selectedReport;
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
    private Dictionary<string, int> _cooldownDraft = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedCooldownKey;
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
    private DotaCatalog? Catalog => HeroesPage.ViewModel.Catalog;
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

    public ObservableCollection<MatchReport> AnalysisReports { get; } = [];
    private PlayerHabits _habits = new();

    public MainWindow()
    {
        InitializeComponent();
        RootGrid.DataContext = this;
        RootGrid.Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        CoachPage.ViewModel.OpenSettingsRequested += async () => await OpenCoachSettingsAsync();
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
        HeroesPage.ViewModel.UseCacheDirectory(_settings.CacheDirectory);
        ItemCatalogPage.ViewModel.UseCacheDirectory(_settings.CacheDirectory);
        HeroesPage.ViewModel.SetDefaultRank(_settings.DefaultRankId);
        HeroesPage.ViewModel.OnRankChanged = rankId =>
        {
            _settings.DefaultRankId = rankId;
            if (_applicationStarted && !_settings.Save())
            {
                WriteErrorLog(new IOException(_settings.LastSaveError));
            }
        };
        MatchSessionTracker.HabitsDirectory = AegisCoachPaths.Habits;
        MatchSessionTracker.AnalysisDirectory = _settings.AnalysisDirectory;
        _npuCoach.ModelPath = _settings.NpuModelPath;
        try { Directory.CreateDirectory(DotaDataService.OfficialDataDirectory); } catch { /* 官网数据文件目录可选 */ }
        NpuStatusText.Text = NpuCapabilityDetector.Detect().Summary;
        UpdateNpuModelStatus();
        _ = RefreshNpuRuntimeAsync();
        AppendCoachLog("应用已按当前设置启动");
        if (_settings.AutoStartGsi) ToggleCoach(true);
        else UpdateCoachStatusUi();
        LoadAnalysisData();
        _ = HeroesPage.LoadAsync(false);
        _ = LoadItemsInBackgroundAsync();
        // 预热物品资料页（遵守首次启动门控：仅在设置保存成功后触发）。
        _ = ItemCatalogPage.LoadAsync(false);
        _ = RefreshAllHeroDetailsInBackgroundAsync();
    }

    /// <summary>后台批量刷新全部英雄的中文详情（官网接口优先），并清理不再属于当前版本的旧详情缓存。</summary>
    private async Task RefreshAllHeroDetailsInBackgroundAsync()
    {
        await HeroesPage.LoadAsync(false);
        var heroIds = Catalog?.Heroes.Select(hero => hero.Id).ToList() ?? [];
        if (heroIds.Count == 0) return;

        try
        {
            var current = heroIds.ToHashSet();
            var purged = _dataService.PurgeStaleHeroDetailCaches(current);
            if (purged > 0) AppendCoachLog($"已清理 {purged} 个旧英雄资料缓存");
            var updated = await _dataService.RefreshAllHeroDetailsAsync(heroIds, AppToken);
            AppendCoachLog($"英雄资料同步完成：{updated}/{heroIds.Count} 位英雄详情已更新");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppendCoachLog("英雄资料后台同步失败：" + exception.Message);
        }
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

    private async Task LoadItemsAsync(bool force)
    {
        // 教练页依赖 _allItems（推荐热度/胜率与名称反查）；物品资料页由
        // Views/ItemCatalogPage 独立加载与展示（含官网式分组与悬浮详情卡）。
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
            }

            _allItems.Clear();
            _allItems.AddRange(items.Select(item => new ItemCardItem(item, metrics.GetValueOrDefault(item.Id))));
            _itemsLoaded = true;
            // 后台预下载物品图片到本地缓存。
            _ = ImageCacheService.Instance.PrefetchAsync(items.Select(item => item.ImageUrl), AppToken);
        }
        catch (Exception exception)
        {
            WriteErrorLog(exception);
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
        var showHeroes = tag == "heroes" || string.IsNullOrEmpty(tag);
        var showItems = tag == "items";
        var showCoach = tag == "coach";
        var showAnalysis = tag == "analysis";
        HeroesPage.Visibility = showHeroes ? Visibility.Visible : Visibility.Collapsed;
        ItemCatalogPage.Visibility = showItems ? Visibility.Visible : Visibility.Collapsed;
        CoachPage.Visibility = showCoach ? Visibility.Visible : Visibility.Collapsed;
        AnalysisPage.Visibility = showAnalysis ? Visibility.Visible : Visibility.Collapsed;
        if (showHeroes)
        {
            await HeroesPage.LoadAsync(false);
        }
        else if (showItems)
        {
            // 物品资料页自带加载保护；这里同时为教练页预热 _allItems（推荐反查用）。
            await ItemCatalogPage.LoadAsync(false);
            if (!_itemsLoaded && !_itemsLoading) await LoadItemsAsync(false);
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
        AnalysisDetailRoot.Visibility = Visibility.Visible;
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
        Catalog?.Heroes.FirstOrDefault(hero => hero.Id == heroId)?.Name ?? string.Empty;

    private static string ResolveApiItemName(string keyOrFullName) =>
        CoachRuleEngine.FriendlyItemName(keyOrFullName);

    private string ResolveApiItemById(int itemId)
    {
        var item = _allItems.FirstOrDefault(card => card.Item.Id == itemId);
        return item is not null ? item.Name : $"道具{itemId}";
    }

    private void EnrichApiNames(ApiMatchSummary api)
    {
        if (Catalog is null) return;
        foreach (var player in api.AllPlayers)
        {
            var hero = Catalog.Heroes.FirstOrDefault(item => item.Id == player.HeroId);
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

    private void AnalysisCloseButton_Click(object _, RoutedEventArgs __)
    {
        _selectedReport = null;
        AnalysisList.SelectedItem = null;
        AnalysisDetailRoot.Visibility = Visibility.Collapsed;
        AnalysisDetailPanel.Visibility = Visibility.Collapsed;
        ApplyResponsiveLayout();
    }

    private void RootGrid_SizeChanged(object _, SizeChangedEventArgs __) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        if (RootGrid is null) return;
        var compact = RootGrid.ActualWidth > 0 && RootGrid.ActualWidth < 1100;
        var pagePadding = compact ? new Thickness(12, 12, 12, 16) : new Thickness(24, 18, 24, 22);
        AnalysisPage.Padding = pagePadding;

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

        CoachPage.ApplyResponsiveLayout(compact);
    }

    /// <summary>详情打开时折叠页头与搜索区，让详情铺满页面；返回列表时恢复。</summary>
    private static void SetPageChrome(UIElement header, UIElement divider, UIElement search, bool detailOpen)
    {
        var visibility = detailOpen ? Visibility.Collapsed : Visibility.Visible;
        header.Visibility = visibility;
        divider.Visibility = visibility;
        search.Visibility = visibility;
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
        HeroesPage.ViewModel.SetDefaultRank(_settings.DefaultRankId);
        AiBackendBox.ItemsSource = new List<string>
        {
            "规则引擎（毫秒级响应，零性能消耗，推荐）",
            "AI 智能解释（兼容 OpenAI / DeepSeek / Ollama / 本地大模型）",
        };
        AiBackendBox.SelectedIndex = _settings.BackendKind == AiBackendKind.Rules ? 0 : 1;
        CloudEndpointBox.Text = _settings.CloudEndpoint;
        CloudModelBox.Text = _settings.CloudModel;
        CloudApiKeyBox.Password = _settings.CloudApiKey;
        CloudSection.Visibility = _settings.BackendKind != AiBackendKind.Rules ? Visibility.Visible : Visibility.Collapsed;
        SmartCoachIntervalBox.Text = _settings.SmartCoachMinIntervalSeconds.ToString();
        NpuStatusText.Text = _applicationStarted ? NpuCapabilityDetector.Detect().Summary : "保存后检查本机能力";
        NpuModelPathBox.Text = _settings.NpuModelPath;
        DataDirectoryText.Text = AegisCoachPaths.Root;
        _npuCoach.ModelPath = _settings.NpuModelPath;
        StratzApiKeyBox.Password = _settings.StratzApiKey;
        LiveHeroTrackingEnabledBox.IsChecked = _settings.LiveHeroTrackingEnabled;
        UpdateNpuModelStatus();
        if (!_settings.LiveHeroTrackingEnabled) CoachPage.ViewModel.HeroesStatusText = "未启用";
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
        _settings.AiBackend = AiBackendBox.SelectedIndex == 0 ? "rules" : "cloud";
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
        HeroesPage.ViewModel.UseCacheDirectory(_settings.CacheDirectory);
        ItemCatalogPage.ViewModel.UseCacheDirectory(_settings.CacheDirectory);
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
        if (!_settings.LiveHeroTrackingEnabled) CoachPage.ViewModel.HeroesStatusText = "未启用";
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
                CoachPage.ViewModel.AddAdvice(item);
                if (_settings.SmartCoachEnabled && TryReserveSmartExplanation()) _ = ExplainAdviceAsync(item, snapshot);
            }

            CoachPage.ViewModel.UpdateSnapshot(snapshot, LocalizedHeroNameById, LocalizedHeroIconById);
            _ = RefreshItemRecommendationAsync(snapshot);
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

    private void OnConnectionChanged(object? _, bool connected)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            CoachPage.ViewModel.UpdateStatus(_coachRunning, connected, _receiver?.LastError);
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
            CoachPage.ViewModel.HeroesStatusText = "未启用";
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
            CoachPage.ViewModel.HeroesStatusText = "正在检索公开对局（人机/私人局不可用）…";
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
                    DispatcherQueue.TryEnqueue(() => CoachPage.ViewModel.HeroesStatusText = status);
                }

                await Task.Delay(TimeSpan.FromSeconds(40), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            DispatcherQueue.TryEnqueue(() => CoachPage.ViewModel.HeroesStatusText = "全场英雄检索失败（网络或接口不可用）");
        }
    }

    private void UpdateLiveHeroUi(LiveMatchInfo info)
    {
        CoachPage.ViewModel.UpdateLiveHeroes(info, LocalizedHeroNameById, LocalizedHeroIconById);
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
                CoachPage.ViewModel.CounterStatusText = "等待全场英雄数据（仅公开比赛）…";
                return;
            }

            var myHeroId = snapshot.Hero.Id;
            if (myHeroId <= 0)
            {
                CoachPage.ViewModel.CounterStatusText = "等待英雄数据…";
                return;
            }

            var enemies = _liveMatch.Heroes
                .Where(hero => hero.HeroId > 0 && hero.Team != _liveMatch.MyTeam)
                .ToList();
            var enemyIds = enemies.Select(hero => hero.HeroId).Distinct().ToList();
            if (enemyIds.Count == 0)
            {
                CoachPage.ViewModel.CounterStatusText = "敌方英雄尚未确认…";
                return;
            }

            // 敌方克制我 / 我克制敌方：以我方英雄的公开对阵胜率为准。
            var myMatchups = await GetMatchupsAsync(myHeroId);
            var countersMe = new List<CounterMatchupItem>();
            var countersTarget = new List<CounterMatchupItem>();
            var enemyStats = new List<DotaDataService.HeroMatchupStat>();
            foreach (var enemy in enemies)
            {
                var stat = myMatchups.FirstOrDefault(item => item.HeroId == enemy.HeroId);
                if (stat is null || stat.GamesPlayed < 30) continue;
                enemyStats.Add(stat);
                if (stat.WinRate <= 48)
                {
                    countersMe.Add(new CounterMatchupItem(
                        enemy.HeroId,
                        LocalizedHeroNameById(enemy.HeroId),
                        LocalizedHeroIconById(enemy.HeroId),
                        stat.WinRate,
                        stat.GamesPlayed,
                        countersTarget: false));
                }
                else if (stat.WinRate >= 52)
                {
                    countersTarget.Add(new CounterMatchupItem(
                        enemy.HeroId,
                        LocalizedHeroNameById(enemy.HeroId),
                        LocalizedHeroIconById(enemy.HeroId),
                        stat.WinRate,
                        stat.GamesPlayed,
                        countersTarget: true));
                }
            }

            // OpenDota 样本量小时用两端补足，保证克制与被克制都有展示。
            if (countersMe.Count == 0)
            {
                foreach (var stat in enemyStats.OrderBy(item => item.WinRate).Take(3))
                {
                    countersMe.Add(new CounterMatchupItem(
                        stat.HeroId,
                        LocalizedHeroNameById(stat.HeroId),
                        LocalizedHeroIconById(stat.HeroId),
                        stat.WinRate,
                        stat.GamesPlayed,
                        countersTarget: false));
                }
            }
            if (countersTarget.Count == 0)
            {
                foreach (var stat in enemyStats.OrderByDescending(item => item.WinRate).Take(3))
                {
                    countersTarget.Add(new CounterMatchupItem(
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
            var pickRecommendations = new List<CounterPickRecommendation>();
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
                pickRecommendations.Add(new CounterPickRecommendation(
                    pair.Key,
                    LocalizedHeroNameById(pair.Key),
                    LocalizedHeroIconById(pair.Key),
                    pair.Value.Count,
                    pair.Value.WinRateSum / pair.Value.Count));
            }

            var statusText = countersMe.Count + countersTarget.Count + pickRecommendations.Count > 0
                ? "基于 OpenDota 对阵胜率（样本 ≥30）"
                : "暂无足够对阵样本";

            CoachPage.ViewModel.UpdateCounters(countersMe, countersTarget, pickRecommendations, statusText);
        }
        catch (Exception exception)
        {
            CoachPage.ViewModel.CounterStatusText = "克制分析暂不可用：" + exception.Message;
        }
        finally
        {
            _counterGate.Release();
        }
    }

    private string LocalizedHeroNameById(int heroId) =>
        Catalog?.Heroes.FirstOrDefault(hero => hero.Id == heroId)?.LocalizedName ?? $"英雄{heroId}";

    private string LocalizedHeroIconById(int heroId) =>
        Catalog?.Heroes.FirstOrDefault(hero => hero.Id == heroId)?.IconUrl ?? string.Empty;

    private async Task RefreshItemRecommendationAsync(MatchSnapshot snapshot)
    {
        if (!await _recommendationGate.WaitAsync(0)) return;
        if (_liveMatch is null)
        {
            CoachPage.ViewModel.ItemRecommendText = "等待全场英雄数据（仅公开比赛）…";
            _recommendationGate.Release();
            return;
        }

        try
        {
            var myHeroId = snapshot.Hero.Id;
            if (myHeroId <= 0)
            {
                CoachPage.ViewModel.ItemRecommendText = "等待英雄数据…";
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

            CoachPage.ViewModel.ItemRecommendText = lines.Count > 0
                ? string.Join("\n", lines)
                : "暂无推荐数据（数据源未返回该英雄物品热度）";
        }
        catch (Exception exception)
        {
            CoachPage.ViewModel.ItemRecommendText = "推荐数据获取失败：" + exception.Message;
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
        foreach (var hero in Catalog?.Heroes ?? [])
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
        CoachPage.ViewModel.UpdateStatus(_coachRunning, _receiver?.IsConnected == true, _receiver?.LastError);
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

    private sealed record RuleCooldownChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }
}



