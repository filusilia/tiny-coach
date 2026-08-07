using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using AegisCoach.Core.Models;
using AegisCoach.Core.Services;
using AegisCoach.Native.Models;
using AegisCoach.Native.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;

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
    private string _selectedSectionId = "str";
    private string _selectedItemCategory = "全部";
    private int _totalProActions;
    private bool _itemsLoaded;
    private readonly CoachRuleEngine _coachRules = new();
    private readonly LocalCoachService _localCoach = new();
    private GsiReceiver? _receiver;
    private MatchSnapshot? _previousSnapshot;
    private AppSettings _settings = new();
    private bool _coachRunning;
    private static string SnapshotDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AegisCoach",
        "gsi");

    public ObservableCollection<HeroCardItem> VisibleHeroes { get; } = [];
    public ObservableCollection<HeroCardItem> VisibleAttributeHeroes { get; } = [];
    public ObservableCollection<HeroSection> HeroSections { get; } =
    [
        new("str", "力量", "STRENGTH"),
        new("agi", "敏捷", "AGILITY"),
        new("int", "智力", "INTELLIGENCE"),
        new("all", "全才", "UNIVERSAL"),
    ];
    public ObservableCollection<RankPerformanceItem> SelectedRankMetrics { get; } = [];
    public ObservableCollection<AbilityDisplay> SelectedAbilities { get; } = [];
    public ObservableCollection<TalentDisplay> SelectedTalents { get; } = [];
    public ObservableCollection<BuildRecommendation> SelectedBuilds { get; } = [];
    public ObservableCollection<ItemCardItem> VisibleItems { get; } = [];
    public ObservableCollection<WikiTip> SelectedWikiTips { get; } = [];
    public ObservableCollection<CoachAdvice> CoachAdvices { get; } = [];
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
        ItemCategoryBox.ItemsSource = ItemCategories;
        _selectedRank = RankOptions[0];
        _selectedSort = SortOptions[0];
        RootGrid.Loaded += MainWindow_Loaded;
        HeroListHost.SizeChanged += HeroListHost_SizeChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RankBox.SelectedIndex = 0;
        SortBox.SelectedIndex = 0;
        ItemCategoryBox.SelectedIndex = 0;
        AttributeTabs.SelectedIndex = 0;
        SetupCoachPage();
        await LoadCatalogAsync(false);
    }

    private async Task LoadCatalogAsync(bool force)
    {
        ErrorBar.IsOpen = false;
        SourceStatusText.Text = "正在同步数据…";
        try
        {
            _catalog = await _dataService.LoadCatalogAsync(force, CancellationToken.None);
            _allHeroes.Clear();
            _totalProActions = _catalog.Heroes.Sum(hero => hero.StatInt("pro_pick") + hero.StatInt("pro_ban"));
            _allHeroes.AddRange(_catalog.Heroes.Select(hero => new HeroCardItem(hero, _selectedRank, _totalProActions)));
            HeroCountText.Text = $"{_allHeroes.Count} 位英雄";
            ApplyFilters();
            var firstHero = VisibleAttributeHeroes.FirstOrDefault()
                ?? HeroSections.SelectMany(section => section.Heroes).FirstOrDefault();
            if (_selectedHero is null && firstHero is not null)
            {
                _selectedHero = firstHero;
                await ShowHeroAsync(firstHero);
            }
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
            var items = await _dataService.LoadItemsAsync(force, CancellationToken.None);
            Dictionary<int, ItemMetric> metrics;
            try
            {
                metrics = await _dataService.LoadItemMetricsAsync(force, CancellationToken.None);
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
            var firstItem = VisibleItems.FirstOrDefault();
            if (_selectedItem is null && firstItem is not null)
            {
                _selectedItem = firstItem;
                await ShowItemAsync(firstItem);
            }

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
            foreach (var item in filteredHeroes.Where(item => AttributeId(item.Hero) == section.Id))
            {
                section.Heroes.Add(item);
            }
        }
        UpdateVisibleAttributeHeroes();

        RankCaptionText.Text = _selectedRank.Label;
        UpdateHeroCardLayout();
    }

    private async void HeroGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
        HeroNameText.Text = item.Name;
        HeroMetaText.Text = $"{item.AttributeLabel} · {item.AttackLabel}";
        HeroImage.Source = new BitmapImage(new Uri(item.Hero.ImageUrl));

        SelectedRankMetrics.Clear();
        foreach (var rank in RankOptions.Skip(1))
        {
            SelectedRankMetrics.Add(new RankPerformanceItem(rank.Label, HeroCardItem.MetricsFor(item.Hero, rank.Id)));
        }

        SelectedAbilities.Clear();
        SelectedTalents.Clear();
        SelectedBuilds.Clear();
        SelectedWikiTips.Clear();
        foreach (var tip in WikiTipCatalog.ForHero(item.Hero.Name)) SelectedWikiTips.Add(tip);
        TalentBorder.Visibility = Visibility.Collapsed;
        BuildBorder.Visibility = Visibility.Collapsed;
        BuildStatusText.Text = "正在读取当前分段样本…";
        AbilityStatusText.Text = "正在同步官方中文…";
        LocalizedHeroData localized = new() { Id = item.Hero.Id };
        try
        {
            localized = await _dataService.LoadLocalizedHeroAsync(item.Hero.Id, false, CancellationToken.None);
        }
        catch (Exception exception)
        {
            AbilityStatusText.Text = "官方中文详情暂不可用，显示基础技能";
            ErrorBar.Title = "无法加载该英雄的官方中文技能";
            ErrorBar.Message = exception.Message;
            ErrorBar.IsOpen = true;
        }

        if (requestId != _detailsRequestId) return;

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
        foreach (var abilityId in abilityIds)
        {
            AbilityDefinition? baseAbility = null;
            _catalog?.Abilities.TryGetValue(abilityId, out baseAbility);
            officialByName.TryGetValue(abilityId, out var officialAbility);
            SelectedAbilities.Add(new AbilityDisplay(abilityId, baseAbility, officialAbility));
        }

        foreach (var talent in localized.Talents)
        {
            AbilityDefinition? talentBase = null;
            _catalog?.Abilities.TryGetValue(talent.Name, out talentBase);
            var localizedTalent = AbilityDisplay.ResolveTalentParameters(
                CleanText(talent.LocalizedName), talent, talentBase);
            if (!string.IsNullOrWhiteSpace(localizedTalent))
            {
                var index = SelectedTalents.Count;
                SelectedTalents.Add(new TalentDisplay(10 + index / 2 * 5, index % 2 == 0 ? "左侧" : "右侧", localizedTalent));
            }
        }
        TalentBorder.Visibility = SelectedTalents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        await LoadHeroBuildsAsync(item, requestId);
        AbilityStatusText.Text = localized.Abilities.Count > 0
            ? $"{SelectedAbilities.Count} 项技能"
        : $"{SelectedAbilities.Count} 项基础技能 · 中文详情暂不可用";
    }

    private async Task LoadHeroBuildsAsync(HeroCardItem hero, int requestId)
    {
        try
        {
            var metrics = await _dataService.LoadHeroBuildsAsync(hero.Hero.Id, _selectedRank.Id, false, CancellationToken.None);
            if (requestId != _detailsRequestId) return;
            var itemCatalog = await _dataService.LoadItemsAsync(false, CancellationToken.None);
            var byId = itemCatalog.ToDictionary(item => item.Id);
            var order = 0;
            foreach (var metric in metrics)
            {
                if (!byId.TryGetValue(metric.ItemId, out var item)) continue;
                order++;
                var phase = item.IsPregameSuggested ? "出门" : item.IsEarlyGameSuggested ? "前期" : item.IsLateGameSuggested ? "后期" : "核心";
                var rate = metric.WinRate is double value ? $"胜率 {value:0.0}%" : "胜率 --";
                SelectedBuilds.Add(new BuildRecommendation
                {
                    Order = order,
                    Phase = phase,
                    Name = string.IsNullOrWhiteSpace(item.LocalizedName) ? item.EnglishName : item.LocalizedName,
                    EnglishName = item.EnglishName,
                    StatsText = $"样本 {HeroCardItem.CompactNumber(metric.Picks)} · {rate}",
                    Image = new BitmapImage(new Uri(item.ImageUrl)),
                });
            }
            BuildStatusText.Text = metrics.Count > 0
                ? $"{_selectedRank.Label} · 最近30天 · 按出场率"
                : "当前分段暂无足够样本";
            BuildBorder.Visibility = SelectedBuilds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            WriteErrorLog(exception);
            BuildStatusText.Text = "当前分段出装暂不可用";
            BuildBorder.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ShowItemAsync(ItemCardItem item)
    {
        var requestId = ++_detailsRequestId;
        ItemNameText.Text = item.Name;
        ItemMetaText.Text = $"{item.TierText} · {item.EnglishName}";
        ItemImage.Source = item.Image;
        ItemCostText.Text = item.Metric.Picks > 0
            ? $"{item.Metric.WinRateText}  ·  购买 {item.Metric.PicksText}  ·  胜局 {item.Metric.WinsText}"
            : "暂无最近 30 天统计";
        ItemDescriptionText.Text = "正在同步官方中文资料…";
        ItemLoreText.Text = string.Empty;
        ItemNotes.ItemsSource = null;
        ItemRecipeItems.ItemsSource = item.Item.RecipeComponents;
        ItemRecipeBorder.Visibility = item.Item.RecipeComponents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        try
        {
            var detail = await _dataService.LoadItemDetailsAsync(item.Item.Id, false, CancellationToken.None);
            if (requestId != _detailsRequestId) return;

            var metricText = item.Metric.Picks > 0
                ? $"{item.Metric.WinRateText}  ·  购买 {item.Metric.PicksText}  ·  胜局 {item.Metric.WinsText}"
                : "暂无最近 30 天统计";
            ItemCostText.Text = detail.Cost > 0
                ? $"价格 {detail.Cost:N0}  ·  {metricText}"
                : metricText;
            ItemDescriptionText.Text = AbilityDisplay.ResolveParameters(
                CleanText(detail.Description ?? "暂无说明"), detail.SpecialValues, null);
            ItemLoreText.Text = CleanText(detail.Lore ?? string.Empty);
            ItemNotes.ItemsSource = detail.Notes
                .Select(CleanText)
                .Select(text => AbilityDisplay.ResolveParameters(text, detail.SpecialValues, null))
                .Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
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
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AegisCoach");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "last-error.txt"), exception.ToString());
        }
        catch
        {
            // Diagnostics must never replace the user-facing error state.
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

    private void ItemSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyItemFilters();

    private void ApplyItemFilters()
    {
        var keyword = ItemSearchBox.Text.Trim();
        var query = _allItems.Where(item => string.IsNullOrWhiteSpace(keyword) ||
            item.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
            item.EnglishName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (_selectedItemCategory != "全部")
        {
            query = query.Where(item => item.CategoryLabel == _selectedItemCategory);
        }
        VisibleItems.Clear();
        foreach (var item in query
            .OrderBy(item => Array.IndexOf(ItemCategory.Order.ToArray(), item.CategoryLabel))
            .ThenBy(item => item.Name, StringComparer.CurrentCulture))
        {
            VisibleItems.Add(item);
        }

        ItemResultCountText.Text = $"显示 {VisibleItems.Count} 件";
    }

    private void ItemCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemCategoryBox.SelectedItem is string category)
        {
            _selectedItemCategory = category;
            ApplyItemFilters();
        }
    }

    private async void AppNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
        var showItems = tag == "items";
        var showCoach = tag == "coach";
        HeroPage.Visibility = showItems || showCoach ? Visibility.Collapsed : Visibility.Visible;
        ItemPage.Visibility = showItems ? Visibility.Visible : Visibility.Collapsed;
        CoachPage.Visibility = showCoach ? Visibility.Visible : Visibility.Collapsed;
        if (showItems && !_itemsLoaded)
        {
            await LoadItemsAsync(false);
        }
    }

    private async void ItemGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is GridView grid && grid.SelectedItem is ItemCardItem item)
        {
            _selectedItem = item;
            await ShowItemAsync(item);
            grid.SelectedItem = null;
        }
    }

    private void AttributeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AttributeTabs.SelectedItem is HeroSection section)
        {
            _selectedSectionId = section.Id;
            UpdateVisibleAttributeHeroes();
        }
    }

    private async void RankBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RankBox.SelectedItem is RankOption rank)
        {
            _selectedRank = rank;
            ApplyFilters();
            if (_selectedHero is not null) await ShowHeroAsync(_selectedHero);
        }
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortBox.SelectedItem is SortOption sort)
        {
            _selectedSort = sort;
            ApplyFilters();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _dataService.ClearCache();
        await LoadCatalogAsync(true);
        if (_selectedHero is not null) await ShowHeroAsync(_selectedHero);
    }

    private async void ItemRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _dataService.ClearCache();
        _itemsLoaded = false;
        await LoadItemsAsync(true);
        if (_selectedItem is not null) await ShowItemAsync(_selectedItem);
    }

    private void HeroListHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateHeroCardLayout();

    private void UpdateVisibleAttributeHeroes()
    {
        var section = HeroSections.FirstOrDefault(item => item.Id == _selectedSectionId);
        VisibleAttributeHeroes.Clear();
        if (section is null) return;
        foreach (var hero in section.Heroes) VisibleAttributeHeroes.Add(hero);
        ResultCountText.Text = $"{VisibleAttributeHeroes.Count} 位 {section.Title}英雄 · {_selectedRank.Label}";
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

    private static string AttributeId(HeroStat hero) => hero.PrimaryAttribute switch
    {
        "str" => "str",
        "agi" => "agi",
        "int" => "int",
        _ => "all",
    };

    private void SetupCoachPage()
    {
        _settings = AppSettings.Load();
        CoachPortBox.Text = _settings.GsiPort.ToString();
        CoachTokenBox.Text = _settings.GsiToken;
        CoachPathBox.Text = _settings.DotaCfgManualPath ?? string.Empty;
        CoachAutoStartBox.IsChecked = _settings.AutoStartGsi;
        SmartCoachEnabledBox.IsChecked = _settings.SmartCoachEnabled;
        LocalModelEndpointBox.Text = _settings.LocalModelEndpoint;
        LocalModelNameBox.Text = _settings.LocalModelName;
        CoachInfoBar.IsOpen = false;
        AppendCoachLog("实时教练页面已初始化");
        if (_settings.AutoStartGsi)
        {
            AppendCoachLog("按设置自动启动 GSI 监听…");
            ToggleCoach(true);
        }
        else
        {
            UpdateCoachStatusUi();
        }
    }

    private void ToggleCoach(bool start)
    {
        if (start)
        {
            ApplyCoachSettings();
            _receiver = new GsiReceiver(_settings.GsiPort, _settings.GsiToken, snapshotDirectory: SnapshotDirectory);
            _receiver.SnapshotReceived += OnSnapshotReceived;
            _receiver.ConnectionChanged += OnConnectionChanged;
            _receiver.Log += OnCoachLog;
            _receiver.Start();
            _coachRunning = true;
            AppendCoachLog($"原始快照记录目录：{SnapshotDirectory}");
            if (_receiver.LastError is not null)
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
            AppendCoachLog("GSI 监听已停止");
        }

        UpdateCoachStatusUi();
    }

    private void ApplyCoachSettings()
    {
        _settings.GsiPort = int.TryParse(CoachPortBox.Text.Trim(), out var port) && port is > 0 and < 65536 ? port : 4444;
        _settings.GsiToken = CoachTokenBox.Text.Trim();
        _settings.DotaCfgManualPath = string.IsNullOrWhiteSpace(CoachPathBox.Text) ? null : CoachPathBox.Text.Trim();
        _settings.AutoStartGsi = CoachAutoStartBox.IsChecked == true;
        _settings.SmartCoachEnabled = SmartCoachEnabledBox.IsChecked == true;
        _settings.LocalModelEndpoint = string.IsNullOrWhiteSpace(LocalModelEndpointBox.Text)
            ? "http://127.0.0.1:11434" : LocalModelEndpointBox.Text.Trim();
        _settings.LocalModelName = string.IsNullOrWhiteSpace(LocalModelNameBox.Text)
            ? "qwen2.5:1.5b" : LocalModelNameBox.Text.Trim();
        _settings.Save();
        CoachPortBox.Text = _settings.GsiPort.ToString();
    }

    private void OnSnapshotReceived(object? sender, MatchSnapshot snapshot)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var advice = _coachRules.Evaluate(_previousSnapshot, snapshot);
            _previousSnapshot = snapshot;
            foreach (var item in advice)
            {
                CoachAdvices.Insert(0, item);
                if (_settings.SmartCoachEnabled) _ = ExplainAdviceAsync(item, snapshot);
            }

            while (CoachAdvices.Count > 200) CoachAdvices.RemoveAt(CoachAdvices.Count - 1);
            CoachAdviceCountText.Text = $"{CoachAdvices.Count} 条";
            UpdateCoachSnapshotUi(snapshot);
            UpdateCoachStatusUi();
        });
    }

    private async Task ExplainAdviceAsync(CoachAdvice advice, MatchSnapshot snapshot)
    {
        try
        {
            var explanation = await _localCoach.ExplainAsync(
                advice, snapshot, _settings.LocalModelEndpoint, _settings.LocalModelName, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(explanation)) return;
            DispatcherQueue.TryEnqueue(() => advice.Message += $" 智能解释：{explanation}");
        }
        catch
        {
            // The deterministic rule message remains complete when Ollama is unavailable.
        }
    }

    private async void SmartCoachProbeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCoachSettings();
        SmartCoachStatusText.Text = "正在检测…";
        try
        {
            var result = await _localCoach.ProbeAsync(_settings.LocalModelEndpoint, CancellationToken.None);
            SmartCoachStatusText.Text = result is null ? "未发现本地模型服务" : "本地模型服务可用";
        }
        catch (Exception exception)
        {
            SmartCoachStatusText.Text = "检测失败：" + exception.Message;
        }
    }

    private async void SmartCoachPullButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCoachSettings();
        SmartCoachStatusText.Text = "正在请求下载…";
        try
        {
            var progress = new Progress<string>(status => SmartCoachStatusText.Text = status);
            var result = await _localCoach.PullAsync(
                _settings.LocalModelEndpoint, _settings.LocalModelName, progress, CancellationToken.None);
            SmartCoachStatusText.Text = result.Message;
        }
        catch (Exception exception)
        {
            SmartCoachStatusText.Text = "下载失败：" + exception.Message;
        }
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            CoachConnectionText.Text = connected ? "已连接" : "未连接";
            CoachConnectionText.Foreground = connected
                ? ThemeBrush("AccentBrush")
                : ThemeBrush("TertiaryTextBrush");
        });
    }

    private void OnCoachLog(object? sender, string message)
    {
        DispatcherQueue.TryEnqueue(() => AppendCoachLog(message));
    }

    private void UpdateCoachSnapshotUi(MatchSnapshot snapshot)
    {
        CoachMatchIdText.Text = snapshot.MatchId > 0 ? snapshot.MatchId.ToString() : "--";
        CoachPhaseText.Text = snapshot.PhaseText;
        CoachGameTimeText.Text = snapshot.GameTimeText;
        CoachScoreText.Text = snapshot.InMatch ? snapshot.ScoreText : "--";
        CoachHeroNameText.Text = LocalizedHeroName(snapshot.Hero.Name) ?? "--";
        CoachHeroLevelText.Text = snapshot.Hero.Level > 0 ? $"{snapshot.Hero.Level} 级" : "--";
        CoachHpBar.Value = Math.Clamp(snapshot.Hero.HealthPercent, 0, 100);
        CoachHpText.Text = snapshot.Hero.MaxHealth > 0
            ? $"{snapshot.Hero.HealthPercent:0}%  {snapshot.Hero.Health} / {snapshot.Hero.MaxHealth}"
            : "--";
        CoachManaBar.Value = Math.Clamp(snapshot.Hero.ManaPercent, 0, 100);
        CoachManaText.Text = snapshot.Hero.MaxMana > 0
            ? $"{snapshot.Hero.ManaPercent:0}%  {snapshot.Hero.Mana} / {snapshot.Hero.MaxMana}"
            : "--";
        CoachHeroStatusText.Text = !snapshot.Hero.Alive
            ? $"已阵亡 · {snapshot.Hero.RespawnSeconds} 秒后复活"
            : snapshot.Hero.RespawnSeconds > 0 ? $"存活 · 复活剩余 {snapshot.Hero.RespawnSeconds} 秒" : "存活";
        CoachHeroStatsText.Text = snapshot.Player.Deaths > 0 || snapshot.Player.Kills > 0 || snapshot.Player.Assists > 0
            ? $"KDA {snapshot.Player.Kills}/{snapshot.Player.Deaths}/{snapshot.Player.Assists} · 补刀 {snapshot.Player.LastHits}/{snapshot.Player.Denies}"
            : "--";
        CoachHeroGoldText.Text = snapshot.Player.Gold > 0 || snapshot.Player.Gpm > 0
            ? $"金钱 {snapshot.Player.Gold:N0} · GPM {snapshot.Player.Gpm} · XPM {snapshot.Player.Xpm}"
            : "--";
    }

    private string? LocalizedHeroName(string? internalName)
    {
        if (string.IsNullOrWhiteSpace(internalName)) return null;
        var hero = _catalog?.Heroes.FirstOrDefault(item => item.Name == internalName);
        if (hero is not null && !string.IsNullOrWhiteSpace(hero.LocalizedName)) return hero.LocalizedName;
        return internalName.Replace("npc_dota_hero_", string.Empty).Replace('_', ' ').Trim();
    }

    private void UpdateCoachStatusUi()
    {
        if (_coachRunning)
        {
            CoachStatusText.Text = _receiver?.LastError is null ? "GSI 监听中" : "GSI 启动失败";
            CoachEndpointText.Text = _receiver is null ? "--" : $"http://127.0.0.1:{_receiver.BoundPort}/";
            CoachConnectionText.Text = _receiver?.IsConnected == true ? "已连接" : "未连接";
            CoachConnectionText.Foreground = _receiver?.IsConnected == true
                ? ThemeBrush("AccentBrush")
                : ThemeBrush("TertiaryTextBrush");
            CoachSnapshotCountText.Text = _receiver?.SnapshotCount.ToString() ?? "0";
            CoachLastSnapshotText.Text = _receiver?.LastSnapshotAt?.ToLocalTime().ToString("HH:mm:ss") ?? "--";
        }
        else
        {
            CoachStatusText.Text = "已停止";
            CoachEndpointText.Text = "--";
            CoachConnectionText.Text = "未连接";
            CoachConnectionText.Foreground = ThemeBrush("TertiaryTextBrush");
        }

        CoachToggleButton.Content = _coachRunning ? "停止监听" : "启动监听";
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

    private void CoachToggleButton_Click(object sender, RoutedEventArgs e) => ToggleCoach(!_coachRunning);

    private void CoachInstallButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCoachSettings();
        var result = GsiConfigInstaller.Install(_settings.DotaCfgManualPath, _settings.GsiPort, _settings.GsiToken);
        ShowCoachInfo(result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
            result.Success ? "GSI 配置已安装" : "GSI 配置未安装",
            result.Success ? $"{result.Message} 请完全退出并重新启动 Dota 2 后生效。" : result.Message);
        if (!result.Success)
        {
            var export = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AegisCoach",
                GsiConfigInstaller.CfgFileName);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(export)!);
                File.WriteAllText(export, result.CfgContent);
                CoachInfoBar.Message += $" 已同时导出到 {export}";
            }
            catch (Exception exception)
            {
                CoachInfoBar.Message += $" 导出失败：{exception.Message}";
            }
        }
    }

    private void CoachExportButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCoachSettings();
        var export = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AegisCoach",
            GsiConfigInstaller.CfgFileName);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(export)!);
            File.WriteAllText(export, GsiConfigInstaller.GenerateCfg(_settings.GsiPort, _settings.GsiToken));
            ShowCoachInfo(InfoBarSeverity.Success, "配置已导出", export);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{export}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowCoachInfo(InfoBarSeverity.Error, "导出失败", exception.Message);
        }
    }

    private void CoachCopyButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCoachSettings();
        var package = new DataPackage();
        package.SetText(GsiConfigInstaller.GenerateCfg(_settings.GsiPort, _settings.GsiToken));
        Clipboard.SetContent(package);
        ShowCoachInfo(InfoBarSeverity.Success, "配置已复制", "已复制到剪贴板，可粘贴为 gamestate_integration_aegiscoach.cfg");
    }

    private async void CoachBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            CoachPathBox.Text = folder.Path;
            _settings.DotaCfgManualPath = folder.Path;
            _settings.Save();
            AppendCoachLog($"已设置 Dota cfg 目录：{folder.Path}");
        }
    }

    private void CoachOpenCfgButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCoachSettings();
        var directory = GsiConfigInstaller.LocateDotaCfgDirectory(_settings.DotaCfgManualPath);
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

    private void CoachClearButton_Click(object sender, RoutedEventArgs e)
    {
        CoachAdvices.Clear();
        CoachAdviceCountText.Text = "0 条";
    }

    private void CoachOpenSnapshotsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SnapshotDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SnapshotDirectory}\"") { UseShellExecute = true });
            AppendCoachLog($"已打开快照记录目录：{SnapshotDirectory}");
        }
        catch (Exception exception)
        {
            ShowCoachInfo(InfoBarSeverity.Error, "无法打开快照目录", exception.Message);
        }
    }
}



