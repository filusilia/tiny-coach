using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using AegisCoach.Core.Models;
using AegisCoach.Core.Services;
using AegisCoach.Native.Models;
using AegisCoach.Native.Services;
using AegisCoach.Native.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AegisCoach.Native;

public sealed partial class MainWindow : Window
{
    private readonly DotaDataService _dataService = new();
    private readonly List<ItemCardItem> _allItems = [];
    private readonly ReportRepository _reportRepository = new(() => MatchSessionTracker.AnalysisDirectory);
    private bool _itemsLoaded;
    private bool _itemsLoading;
    private CoachRuleEngine _coachRules = new();
    private readonly CloudCoachService _cloudCoach = new();
    private readonly NpuGenAiCoachService _npuCoach = new();
    private readonly NpuExecutionProviderService _npuRuntime = new();
    private readonly LiveMatchHeroService _liveMatchService = new();
    private readonly ApiMatchAnalyzer _apiMatchAnalyzer = new();
    private MatchSessionTracker _sessionTracker = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, DotaDataService.HeroItemPopularity> _popularityCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, List<DotaDataService.HeroMatchupStat>> _matchupCache = new();
    private Dictionary<string, int> _cooldownDraft = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedCooldownKey;
    private readonly SemaphoreSlim _recommendationGate = new(1, 1);
    private readonly SemaphoreSlim _counterGate = new(1, 1);
    private readonly object _recommendationSync = new();
    private CancellationTokenSource? _recommendationCts;
    private Task? _recommendationTask;
    private string? _lastRecommendationFingerprint;
    private DateTimeOffset _lastRecommendationAt = DateTimeOffset.MinValue;
    private readonly object _counterSync = new();
    private CancellationTokenSource? _counterCts;
    private Task? _counterTask;
    private string? _lastCounterFingerprint;
    private readonly object _aiExplanationSync = new();
    private CancellationTokenSource? _aiExplanationCts;
    private Task? _aiExplanationTask;
    private readonly CancellationTokenSource _appLifetimeCts = new();
    private readonly object _backgroundTaskSync = new();
    private readonly List<Task> _backgroundTasks = [];
    private GsiReceiver? _receiver;
    private GsiProcessingPipeline? _pipeline;
    private MatchSnapshot? _latestSnapshot;
    private string? _trackedMatchId;
    private CancellationTokenSource? _heroTrackingCts;
    private LiveMatchInfo? _liveMatch;
    private AppSettings _settings = new();
    private AppWindowTitleBar? _appWindowTitleBar;
    private bool _isClosing;
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


    private readonly Stopwatch _startupStopwatch = Stopwatch.StartNew();

    public MainWindow()
    {
        InitializeComponent();
        HeroesPage.UseDataService(_dataService);
        ItemCatalogPage.UseDataService(_dataService);
        SetupTitleBar();
        RootGrid.DataContext = this;
        RootGrid.Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        CoachPage.ViewModel.OpenSettingsRequested += async () => await OpenCoachSettingsAsync();
        PerformanceMetrics.Instance.RecordWindowConstructed(_startupStopwatch.ElapsedMilliseconds);
    }

    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            _appWindowTitleBar = AppWindow.TitleBar;
            _appWindowTitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
            _appWindowTitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindowTitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    private void TitleBarPaneToggleButton_Click(object sender, RoutedEventArgs e)
    {
        AppNavigation.IsPaneOpen = !AppNavigation.IsPaneOpen;
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        AppNavigation.IsPaneOpen = !AppNavigation.IsPaneOpen;
    }

    private async void MainWindow_Closed(object _, WindowEventArgs __)
    {
        if (_isClosing) return;
        _isClosing = true;
        _appLifetimeCts.Cancel();
        CancelHeroTracking();
        CancelRecommendation();
        CancelCounterAnalysis();
        CancelSmartExplanation();
        AnalysisPage.Cancel();
        _receiver?.Stop();
        var pipeline = _pipeline;
        _pipeline = null;
        if (pipeline is not null)
        {
            await pipeline.StopAsync();
        }

        HeroesPage.Cancel();
        ItemCatalogPage.Cancel();
        await StopBackgroundTasksAsync();
    }

    private async void MainWindow_Loaded(object _, RoutedEventArgs __)
    {
        PerformanceMetrics.Instance.RecordWindowLoaded(_startupStopwatch.ElapsedMilliseconds);
        HeroesPage.ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(HeroesViewModel.SearchText) && _lastNavigationItem?.Tag?.ToString() is "heroes" or null or "")
            {
                if (TitleBarSearchBox.Text != HeroesPage.ViewModel.SearchText)
                    TitleBarSearchBox.Text = HeroesPage.ViewModel.SearchText;
            }
        };
        ItemCatalogPage.ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ItemCatalogViewModel.SearchText) && _lastNavigationItem?.Tag?.ToString() == "items")
            {
                if (TitleBarSearchBox.Text != ItemCatalogPage.ViewModel.SearchText)
                    TitleBarSearchBox.Text = ItemCatalogPage.ViewModel.SearchText;
            }
        };
        UpdateTitleBarForPage("heroes");

        _settings = AppSettings.Load();
        PerformanceMetrics.Instance.RecordSettingsLoaded(_startupStopwatch.ElapsedMilliseconds);
        var isWarm = File.Exists(Path.Combine(_settings.CacheDirectory, "catalog.json"));
        PerformanceMetrics.Instance.SetScenario(isWarm ? PerformanceScenario.WarmStart : PerformanceScenario.ColdStart);
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
        RegisterBackgroundTask(RefreshNpuRuntimeAsync());

        AnalysisPage.ViewModel.ConfigureServices(
            _reportRepository,
            _dataService,
            () => SnapshotDirectory,
            () => _settings,
            () => Catalog,
            () => _allItems,
            _npuCoach,
            _cloudCoach,
            _apiMatchAnalyzer,
            RegisterBackgroundTask,
            AppendCoachLog,
            DispatcherQueue);

        AppendCoachLog("应用已按当前设置启动");
        if (_settings.AutoStartGsi) await ToggleCoachAsync(true);
        else UpdateCoachStatusUi();

        // 首屏顺序：英雄目录先可搜索、可点击、可滚动；其余数据作为可取消的后台增强。
        await TrackHeroesLoadingAsync();
        RegisterBackgroundTask(LoadAnalysisDataAsync());
        RegisterBackgroundTask(LoadItemsInBackgroundAsync());
        // 预热物品资料页（遵守首次启动门控：仅在设置保存成功后触发）。
        RegisterBackgroundTask(ItemCatalogPage.LoadAsync(false));
        RegisterBackgroundTask(RefreshAllHeroDetailsInBackgroundAsync());
    }

    private void RegisterBackgroundTask(Task task)
    {
        lock (_backgroundTaskSync)
        {
            _backgroundTasks.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (_backgroundTaskSync)
                {
                    _backgroundTasks.Remove(task);
                }

                if (completed.IsFaulted && completed.Exception is not null)
                {
                    WriteErrorLog(completed.Exception.GetBaseException());
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task StopBackgroundTasksAsync()
    {
        Task[] tasks;
        lock (_backgroundTaskSync)
        {
            tasks = _backgroundTasks.ToArray();
        }

        if (tasks.Length == 0) return;
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            WriteErrorLog(exception);
        }
    }

    private async Task TrackHeroesLoadingAsync()
    {
        try
        {
            await HeroesPage.LoadAsync(false);
            var elapsed = _startupStopwatch.ElapsedMilliseconds;
            PerformanceMetrics.Instance.RecordHeroesAvailable(elapsed);
            PerformanceMetrics.Instance.RecordFirstScreenInteractive(elapsed);
            PerformanceMetrics.Instance.LogToFile(title: "首屏就绪性能基线");
        }
        catch (Exception exception)
        {
            WriteErrorLog(exception);
        }
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

    private async Task LoadAnalysisDataAsync()
    {
        try
        {
            await AnalysisPage.ViewModel.InitializeAsync(AppToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            WriteErrorLog(exception);
        }
    }

    private async Task LoadItemsAsync(bool force)
    {
        // 教练页依赖 _allItems（推荐热度/胜率与名称反查）；物品资料页由
        // Views/ItemCatalogPage 独立加载与展示（含官网式分组与悬浮详情卡）。
        try
        {
            var items = await _dataService.LoadItemsAsync(force, AppToken);

            // 1. 基础物品列表就绪即刻开放可用，彻底杜绝等待慢速聚合统计网络查询（秒开基线）
            _allItems.Clear();
            _allItems.AddRange(items.Select(item => new ItemCardItem(item, null)));
            _itemsLoaded = true;
            PerformanceMetrics.Instance.RecordItemsAvailable(_startupStopwatch.ElapsedMilliseconds);
            PerformanceMetrics.Instance.LogToFile(title: "物品目录就绪性能基线");

            // 2. 后台异步数据增强：非阻塞方式拉取物品热度与胜率指标，完成后平滑增量更新
            RegisterBackgroundTask(Task.Run(async () =>
            {
                try
                {
                    var metrics = await _dataService.LoadItemMetricsAsync(force, AppToken);
                    if (metrics.Count > 0)
                    {
                        foreach (var card in _allItems)
                        {
                            if (metrics.TryGetValue(card.Item.Id, out var metric))
                            {
                                card.Metric = metric;
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    WriteErrorLog(exception);
                }
            }, AppToken));

            // 3. 后台预下载物品图片到本地缓存。
            var prefetchSw = Stopwatch.StartNew();
            var imageUrls = items.Select(item => item.ImageUrl).ToList();
            RegisterBackgroundTask(ImageCacheService.Instance.PrefetchAsync(imageUrls, AppToken).ContinueWith(_ =>
            {
                prefetchSw.Stop();
                PerformanceMetrics.Instance.RecordImagePrefetch(imageUrls.Count, prefetchSw.ElapsedMilliseconds);
            }, TaskScheduler.Default));
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
        UpdateTitleBarForPage(tag);

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

    private void UpdateTitleBarForPage(string? tag)
    {
        if (tag == "heroes" || string.IsNullOrEmpty(tag))
        {
            TitleBarSearchBox.PlaceholderText = "搜索英雄 / 拼音 (Ctrl+F)";
            TitleBarSearchBox.Text = HeroesPage.ViewModel.SearchText;
        }
        else if (tag == "items")
        {
            TitleBarSearchBox.PlaceholderText = "搜索物品 / 属性 (Ctrl+F)";
            TitleBarSearchBox.Text = ItemCatalogPage.ViewModel.SearchText;
        }
        else if (tag == "coach")
        {
            TitleBarSearchBox.PlaceholderText = "检索英雄或物品 (Ctrl+F)";
            TitleBarSearchBox.Text = string.Empty;
        }
        else if (tag == "analysis")
        {
            TitleBarSearchBox.PlaceholderText = "搜索比赛 ID 或英雄 (Ctrl+F)";
            TitleBarSearchBox.Text = string.Empty;
        }
    }

    private void TitleBarSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        var tag = (_lastNavigationItem ?? AppNavigation.SelectedItem as NavigationViewItem)?.Tag?.ToString();
        if (tag == "heroes" || string.IsNullOrEmpty(tag))
        {
            if (HeroesPage.ViewModel.SearchText != textBox.Text)
                HeroesPage.ViewModel.SearchText = textBox.Text;
        }
        else if (tag == "items")
        {
            if (ItemCatalogPage.ViewModel.SearchText != textBox.Text)
                ItemCatalogPage.ViewModel.SearchText = textBox.Text;
        }
    }

    private void TitleBarSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            SubmitTitleBarSearch();
            e.Handled = true;
        }
    }

    private void TitleBarSearchSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitTitleBarSearch();
    }

    private void SubmitTitleBarSearch()
    {
        var tag = (_lastNavigationItem ?? AppNavigation.SelectedItem as NavigationViewItem)?.Tag?.ToString();
        if (tag == "heroes" || string.IsNullOrEmpty(tag))
        {
            HeroesPage.ViewModel.ApplyFilterNow();
        }
        else if (tag == "items")
        {
            ItemCatalogPage.ViewModel.ApplyFilterNow();
        }
    }

    private async void TitleBarRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        var tag = (_lastNavigationItem ?? AppNavigation.SelectedItem as NavigationViewItem)?.Tag?.ToString();
        if (tag == "heroes" || string.IsNullOrEmpty(tag))
        {
            await HeroesPage.LoadAsync(true);
        }
        else if (tag == "items")
        {
            await ItemCatalogPage.LoadAsync(true);
        }
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isCtrlDown = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (isCtrlDown && e.Key == Windows.System.VirtualKey.F)
        {
            TitleBarSearchBox.Focus(FocusState.Programmatic);
            TitleBarSearchBox.SelectAll();
            e.Handled = true;
        }
        else if (isCtrlDown && e.Key == Windows.System.VirtualKey.B)
        {
            AppNavigation.IsPaneOpen = !AppNavigation.IsPaneOpen;
            e.Handled = true;
        }
    }

    private void RootGrid_SizeChanged(object _, SizeChangedEventArgs __)
    {
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (RootGrid is null) return;
        var compact = RootGrid.ActualWidth > 0 && RootGrid.ActualWidth < 1100;
        AnalysisPage.ApplyResponsiveLayout(compact);
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

    private async Task ToggleCoachAsync(bool start)
    {
        if (_isClosing) return;

        if (start)
        {
            ApplyCoachSettings();
            _sessionTracker = new MatchSessionTracker();
            _pipeline = new GsiProcessingPipeline(
                _coachRules,
                _sessionTracker,
                uiDispatcher: action => DispatcherQueue.TryEnqueue(new Microsoft.UI.Dispatching.DispatcherQueueHandler(action)),
                onAdvicePublished: HandleAdvicePublished,
                onMatchFinished: HandleFinishedMatch,
                onStatePublished: HandleStatePublished);
            _pipeline.Start();

            var authToken = EnsureGsiAuthToken();
            _receiver = new GsiReceiver(AppSettings.GsiPort, token: authToken, snapshotDirectory: SnapshotDirectory);
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
            var receiver = _receiver;
            _receiver = null;
            receiver?.Stop();

            var pipeline = _pipeline;
            _pipeline = null;
            if (pipeline is not null)
            {
                await pipeline.StopAsync();
            }

            _coachRunning = false;
            _latestSnapshot = null;
            CancelHeroTracking();
            CancelRecommendation();
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
        _pipeline?.UpdateRuleEngine(_coachRules);
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
        PerformanceMetrics.Instance.RecordSnapshotReceived();
        _pipeline?.Enqueue(snapshot);
    }

    private void HandleAdvicePublished(CoachAdvice item)
    {
        CoachPage.ViewModel.AddAdvice(item);
        var targetSnapshot = item.Snapshot ?? _latestSnapshot;
        if (targetSnapshot is not null)
        {
            QueueSmartExplanation(item, targetSnapshot);
        }
    }

    private void HandleStatePublished(MatchSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        HandleHeroTracking(snapshot);
        CoachPage.ViewModel.UpdateSnapshot(snapshot, LocalizedHeroNameById, LocalizedHeroIconById);
        QueueItemRecommendationRefresh(snapshot);
        UpdateCoachStatusUi();
    }

    private void HandleFinishedMatch(MatchSessionRecord record)
    {
        MatchSessionTracker.AppendHabit(record);
        var report = new MatchReport
        {
            Id = $"report-{DateTime.Now:yyyyMMdd-HHmmss}-{record.MatchId}",
            GeneratedAt = DateTimeOffset.UtcNow,
            Match = record,
            Backend = "规则摘要",
        };
        AnalysisPage.ViewModel.AddCompletedReport(report);
    }

    private bool TryReserveSmartExplanation()
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(_settings.SmartCoachMinIntervalSeconds, 1, 60));
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSmartExplanationAt < interval) return false;
        _lastSmartExplanationAt = now;
        return true;
    }

    private async Task ExplainAdviceAsync(CoachAdvice advice, MatchSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            string? explanation = null;
            switch (_settings.BackendKind)
            {
                case AiBackendKind.Npu:
                    explanation = await _npuCoach.ExplainAsync(
                        CoachAiPrompts.BuildPrompt(advice, snapshot), cancellationToken);
                    break;
                case AiBackendKind.Cloud:
                    explanation = await _cloudCoach.ExplainAsync(
                        advice, snapshot, _settings.CloudEndpoint, _settings.CloudModel, _settings.CloudApiKey,
                        cancellationToken);
                    break;
            }

            if (string.IsNullOrWhiteSpace(explanation)) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isClosing && !cancellationToken.IsCancellationRequested)
                    advice.Message += $" 智能解释：{explanation}";
            });
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
            RegisterBackgroundTask(TrackLiveMatchAsync(matchId, steamId, snapshot.Player.TeamName ?? string.Empty, _heroTrackingCts.Token));
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
        if (_isClosing) return;
        CoachPage.ViewModel.UpdateLiveHeroes(info, LocalizedHeroNameById, LocalizedHeroIconById);
        if (_latestSnapshot is not null) QueueItemRecommendationRefresh(_latestSnapshot);
        if (_latestSnapshot is not null) QueueCounterAnalysisRefresh(_latestSnapshot);
    }

    private void QueueCounterAnalysisRefresh(MatchSnapshot snapshot)
    {
        var liveMatch = _liveMatch;
        if (_isClosing || liveMatch is null) return;

        var fingerprint = BuildCounterFingerprint(snapshot, liveMatch);
        lock (_counterSync)
        {
            // 克制分析只由敌方阵容（或玩家英雄）变化触发，不随每个 GSI 快照重跑。
            if (string.Equals(fingerprint, _lastCounterFingerprint, StringComparison.Ordinal)) return;

            _lastCounterFingerprint = fingerprint;
            _counterCts?.Cancel();
            _counterCts = CancellationTokenSource.CreateLinkedTokenSource(AppToken);
            _counterTask = RefreshCounterAnalysisAsync(snapshot, liveMatch, fingerprint, _counterCts.Token);
            RegisterBackgroundTask(_counterTask);
        }
    }

    private string BuildCounterFingerprint(MatchSnapshot snapshot, LiveMatchInfo liveMatch)
    {
        var enemies = string.Join(',', liveMatch.Heroes
            .Where(hero => hero.HeroId > 0 && hero.Team != liveMatch.MyTeam)
            .Select(hero => hero.HeroId)
            .Distinct()
            .OrderBy(id => id));
        return $"{snapshot.MatchId}|{snapshot.Hero.Id}|{liveMatch.MyTeam}|{enemies}";
    }

    private bool IsCurrentCounterRequest(string fingerprint)
    {
        lock (_counterSync)
        {
            return !_isClosing && string.Equals(fingerprint, _lastCounterFingerprint, StringComparison.Ordinal);
        }
    }

    private async Task RefreshCounterAnalysisAsync(
        MatchSnapshot snapshot,
        LiveMatchInfo liveMatch,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var entered = false;
        var sw = Stopwatch.StartNew();
        try
        {
            await _counterGate.WaitAsync(cancellationToken);
            entered = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentCounterRequest(fingerprint)) return;

            if (liveMatch is null)
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

            var enemies = liveMatch.Heroes
                .Where(hero => hero.HeroId > 0 && hero.Team != liveMatch.MyTeam)
                .ToList();
            var enemyIds = enemies.Select(hero => hero.HeroId).Distinct().ToList();
            if (enemyIds.Count == 0)
            {
                CoachPage.ViewModel.CounterStatusText = "敌方英雄尚未确认…";
                return;
            }

            // 敌方克制我 / 我克制敌方：以我方英雄的公开对阵胜率为准。
            var myMatchups = await GetMatchupsAsync(myHeroId, cancellationToken);
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
            var taken = liveMatch.Heroes.Where(hero => hero.HeroId > 0).Select(hero => hero.HeroId).ToHashSet();
            var scores = new Dictionary<int, (int Count, double WinRateSum)>();
            foreach (var enemyId in enemyIds)
            {
                List<DotaDataService.HeroMatchupStat> enemyMatchups;
                try
                {
                    enemyMatchups = await GetMatchupsAsync(enemyId, cancellationToken);
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

            if (IsCurrentCounterRequest(fingerprint))
            {
                CoachPage.ViewModel.UpdateCounters(countersMe, countersTarget, pickRecommendations, statusText);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentCounterRequest(fingerprint))
            {
                // 保留上一次有效结果，只更新降级原因。
                CoachPage.ViewModel.CounterStatusText = "克制分析暂不可用，保留上次结果：" + exception.Message;
            }
        }
        finally
        {
            if (entered) _counterGate.Release();
            sw.Stop();
            PerformanceMetrics.Instance.RecordCounterAnalysis(sw.ElapsedMilliseconds);
        }
    }

    private string LocalizedHeroNameById(int heroId) =>
        Catalog?.Heroes.FirstOrDefault(hero => hero.Id == heroId)?.LocalizedName ?? $"英雄{heroId}";

    private string LocalizedHeroIconById(int heroId) =>
        Catalog?.Heroes.FirstOrDefault(hero => hero.Id == heroId)?.IconUrl ?? string.Empty;

    private void QueueItemRecommendationRefresh(MatchSnapshot snapshot)
    {
        if (_isClosing) return;

        var fingerprint = BuildRecommendationFingerprint(snapshot);
        var now = DateTimeOffset.UtcNow;
        lock (_recommendationSync)
        {
            var minInterval = TimeSpan.FromSeconds(Math.Clamp(_settings.SmartCoachMinIntervalSeconds, 3, 60));
            if (string.Equals(fingerprint, _lastRecommendationFingerprint, StringComparison.Ordinal)
                && now - _lastRecommendationAt < minInterval)
            {
                return;
            }

            _lastRecommendationFingerprint = fingerprint;
            _lastRecommendationAt = now;
            _recommendationCts?.Cancel();
            _recommendationCts = CancellationTokenSource.CreateLinkedTokenSource(AppToken);
            _recommendationTask = RefreshItemRecommendationAsync(snapshot, _recommendationCts.Token);
            RegisterBackgroundTask(_recommendationTask);
        }
    }

    private string BuildRecommendationFingerprint(MatchSnapshot snapshot)
    {
        var itemIds = string.Join(',', snapshot.Items
            .Where(item => item.SlotKind == ItemSlotKind.Main)
            .Select(item => item.Name)
            .OrderBy(item => item, StringComparer.Ordinal));
        var heroes = _liveMatch is null
            ? string.Empty
            : string.Join(',', _liveMatch.Heroes
                .Where(hero => hero.HeroId > 0)
                .Select(hero => $"{hero.Team}:{hero.HeroId}")
                .OrderBy(item => item, StringComparer.Ordinal));
        var goldBucket = Math.Max(0, snapshot.Player.Gold) / 500;
        return $"{snapshot.Hero.Id}|{itemIds}|{snapshot.Phase}|{heroes}|{goldBucket}";
    }

    private void CancelRecommendation()
    {
        lock (_recommendationSync)
        {
            _recommendationCts?.Cancel();
            _recommendationCts = null;
            _lastRecommendationFingerprint = null;
            _lastRecommendationAt = DateTimeOffset.MinValue;
        }
    }

    private void CancelCounterAnalysis()
    {
        lock (_counterSync)
        {
            _counterCts?.Cancel();
            _counterCts = null;
            _lastCounterFingerprint = null;
        }
    }

    private void QueueSmartExplanation(CoachAdvice advice, MatchSnapshot snapshot)
    {
        if (!_settings.SmartCoachEnabled || _isClosing || !TryReserveSmartExplanation()) return;

        lock (_aiExplanationSync)
        {
            _aiExplanationCts?.Cancel();
            _aiExplanationCts = CancellationTokenSource.CreateLinkedTokenSource(AppToken);
            _aiExplanationTask = ExplainAdviceAsync(advice, snapshot, _aiExplanationCts.Token);
            RegisterBackgroundTask(_aiExplanationTask);
        }
    }

    private void CancelSmartExplanation()
    {
        lock (_aiExplanationSync)
        {
            _aiExplanationCts?.Cancel();
            _aiExplanationCts = null;
        }
    }

    private async Task RefreshItemRecommendationAsync(MatchSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!await _recommendationGate.WaitAsync(0, cancellationToken)) return;
        var sw = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_liveMatch is null)
            {
                CoachPage.ViewModel.ItemRecommendText = "等待全场英雄数据（仅公开比赛）…";
                return;
            }

            var myHeroId = snapshot.Hero.Id;
            if (myHeroId <= 0)
            {
                CoachPage.ViewModel.ItemRecommendText = "等待英雄数据…";
                return;
            }

            var popularity = await GetPopularityAsync(myHeroId, cancellationToken);
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
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            CoachPage.ViewModel.ItemRecommendText = "推荐数据获取失败：" + exception.Message;
        }
        finally
        {
            _recommendationGate.Release();
            sw.Stop();
            PerformanceMetrics.Instance.RecordItemRecommendation(sw.ElapsedMilliseconds);
        }
    }

    private async Task<DotaDataService.HeroItemPopularity> GetPopularityAsync(int heroId, CancellationToken cancellationToken)
    {
        if (_popularityCache.TryGetValue(heroId, out var cached)) return cached;
        var value = await _dataService.GetHeroItemPopularityAsync(heroId, cancellationToken);
        _popularityCache[heroId] = value;
        return value;
    }

    private async Task<List<DotaDataService.HeroMatchupStat>> GetMatchupsAsync(int heroId, CancellationToken cancellationToken = default)
    {
        if (_matchupCache.TryGetValue(heroId, out var cached)) return cached;
        var value = await _dataService.GetHeroMatchupsAsync(heroId, cancellationToken);
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
        CancelCounterAnalysis();
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

    private async void CoachToggleButton_Click(object _, RoutedEventArgs __)
    {
        if (_applicationStarted) await ToggleCoachAsync(!_coachRunning);
    }

    private void CoachInstallButton_Click(object _, RoutedEventArgs __)
    {
        InstallCoachConfig();
    }

    private void InstallCoachConfig()
    {
        var manualPath = string.IsNullOrWhiteSpace(CoachPathBox.Text) ? null : CoachPathBox.Text.Trim();
        var authToken = EnsureGsiAuthToken();
        var result = GsiConfigInstaller.Install(manualPath, AppSettings.GsiPort, authToken);
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

    private static string EnsureGsiAuthToken()
    {
        const string TokenCredentialKey = "GsiAuthToken";
        var token = AppCredentialStore.Load(TokenCredentialKey);
        if (string.IsNullOrWhiteSpace(token) || token.Length < 16)
        {
            Span<byte> randomBytes = stackalloc byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
            token = Convert.ToHexString(randomBytes).ToLowerInvariant();
            AppCredentialStore.Save(TokenCredentialKey, token, out _);
        }
        return token;
    }

    private sealed record RuleCooldownChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }
}



