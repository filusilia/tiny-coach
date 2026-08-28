using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using AegisCoach.Native.Data;
using AegisCoach.Native.Models;
using AegisCoach.Native.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AegisCoach.Native.ViewModels;

/// <summary>
/// 英雄资料库与详情 ViewModel：官方目录加载、四属性分栏、分段胜率与出场排序、
/// 防抖搜索、英雄卡片网格自适应、英雄详情（背景故事、三维成长、基础数值、
/// 技能解析与槽位、天赋树左右选、神杖魔晶升级、推荐出装、克制对阵、实战技巧）。
/// </summary>
public sealed partial class HeroesViewModel : ObservableObject
{
    private readonly DotaDataService _dataService = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly List<HeroCardItem> _allHeroes = [];
    private readonly Dictionary<int, DotaDataService.HeroItemPopularity> _popularityCache = new();
    private readonly Dictionary<int, List<DotaDataService.HeroMatchupStat>> _matchupCache = new();
    private DotaCatalog? _catalog;
    private int _totalProActions;
    private int _loadRequestId;
    private int _detailsRequestId;
    private bool _loadedOnce;
    private DispatcherQueueTimer? _searchDebounce;

    public ObservableCollection<HeroSection> HeroSections { get; } =
    [
        new("all-heroes", "全部", "全英雄", "ms-appx:///Assets/Icons/rank_icon_0.png"),
        new("str", "力量", "Strength", "ms-appx:///Assets/Icons/hero_strength.png"),
        new("agi", "敏捷", "Agility", "ms-appx:///Assets/Icons/hero_agility.png"),
        new("int", "智力", "Intelligence", "ms-appx:///Assets/Icons/hero_intelligence.png"),
        new("all", "全才", "Universal", "ms-appx:///Assets/Icons/hero_universal.png"),
    ];

    public ObservableCollection<HeroCardItem> VisibleAttributeHeroes { get; } = [];
    public ObservableCollection<RankOption> RankOptions { get; } = [];
    public ObservableCollection<SortOption> SortOptions { get; } = [];
    public ObservableCollection<string> HeroRoleTags { get; } = [];
    public ObservableCollection<AbilityDisplay> SelectedAbilities { get; } = [];
    public ObservableCollection<TalentLevelRow> TalentRows { get; } = [];
    public ObservableCollection<BuildRecommendationGroup> SelectedBuildGroups { get; } = [];
    public ObservableCollection<CounterMatchupItem> HeroCountersMe { get; } = [];
    public ObservableCollection<CounterMatchupItem> HeroCountersTarget { get; } = [];
    public ObservableCollection<WikiTip> SelectedWikiTips { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "目标版本 v7.41e";

    [ObservableProperty]
    public partial string HeroCountText { get; set; } = "正在同步数据…";

    [ObservableProperty]
    public partial string RankCaptionText { get; set; } = "全部分段";

    [ObservableProperty]
    public partial string ResultCountText { get; set; } = "正在同步数据…";

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRankIcon))]
    public partial RankOption? SelectedRank { get; set; }

    public string SelectedRankIcon => SelectedRank?.IconUrl ?? "ms-appx:///Assets/Icons/rank_icon_0.png";

    [ObservableProperty]
    public partial SortOption? SelectedSort { get; set; }

    [ObservableProperty]
    public partial string SelectedSectionId { get; set; } = "all-heroes";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial HeroCardItem? SelectedHero { get; set; }

    public bool IsDetailOpen => SelectedHero is not null;
    public bool IsEmpty => !IsLoading && !HasError && !IsDetailOpen && VisibleAttributeHeroes.Count == 0;

    // ================= 详情视图绑定的展示属性 =================
    [ObservableProperty]
    public partial string DetailHeroName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailEnglishName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailHypeText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBiography))]
    public partial string DetailBiographyText { get; set; } = string.Empty;

    public bool HasBiography => !string.IsNullOrWhiteSpace(DetailBiographyText);

    [ObservableProperty]
    public partial string DetailMetaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailAttrBadgeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Brush? DetailAttrBadgeBrush { get; set; }

    [ObservableProperty]
    public partial string DetailPrimaryAttributeIcon { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailMelee))]
    [NotifyPropertyChangedFor(nameof(IsDetailRanged))]
    public partial string DetailAttackTypeText { get; set; } = string.Empty;

    public bool IsDetailMelee => DetailAttackTypeText == "近战";
    public bool IsDetailRanged => DetailAttackTypeText == "远程";

    [ObservableProperty]
    public partial string DetailAttackTypeIcon { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ImageSource? DetailImage { get; set; }

    [ObservableProperty]
    public partial string DetailRankSummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailStrValue { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailStrGrowth { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailAgiValue { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailAgiGrowth { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailIntValue { get; set; } = "--";

    [ObservableProperty]
    public partial string DetailIntGrowth { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HeroStatAttack { get; set; } = "--";

    [ObservableProperty]
    public partial string HeroStatArmor { get; set; } = "--";

    [ObservableProperty]
    public partial string HeroStatMoveSpeed { get; set; } = "--";

    [ObservableProperty]
    public partial string HeroStatAttackRange { get; set; } = "--";

    [ObservableProperty]
    public partial string HeroStatBaseHealth { get; set; } = "--";

    [ObservableProperty]
    public partial string HeroStatHealthRegen { get; set; } = "+0.0";

    [ObservableProperty]
    public partial string HeroStatBaseMana { get; set; } = "--";

    [ObservableProperty]
    public partial string HeroStatManaRegen { get; set; } = "+0.0";

    [ObservableProperty]
    public partial string HeroStatAttackRate { get; set; } = "1.7s";

    [ObservableProperty]
    public partial string HeroStatProjectileSpeed { get; set; } = "--";

    [ObservableProperty]
    public partial string HeroStatMagicResist { get; set; } = "25%";

    [ObservableProperty]
    public partial string HeroStatTurnRate { get; set; } = "0.6";

    [ObservableProperty]
    public partial string HeroStatVision { get; set; } = "1800 / 800";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryStrBorderBrush))]
    public partial bool IsPrimaryStr { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryAgiBorderBrush))]
    public partial bool IsPrimaryAgi { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryIntBorderBrush))]
    public partial bool IsPrimaryInt { get; set; }

    [ObservableProperty]
    public partial bool IsPrimaryAll { get; set; }

    public Brush PrimaryStrBorderBrush => IsPrimaryStr
        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["HeroAttrStrBrush"]
        : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["GameBorderBrush"];

    public Brush PrimaryAgiBorderBrush => IsPrimaryAgi
        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["HeroAttrAgiBrush"]
        : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["GameBorderBrush"];

    public Brush PrimaryIntBorderBrush => IsPrimaryInt
        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["HeroAttrIntBrush"]
        : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["GameBorderBrush"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BioMaxLines))]
    [NotifyPropertyChangedFor(nameof(BioToggleText))]
    public partial bool IsBioExpanded { get; set; }

    public int BioMaxLines => IsBioExpanded ? 0 : 3;

    public string BioToggleText => IsBioExpanded ? "收起背景故事" : "展开完整背景故事";

    [RelayCommand]
    private void ToggleBio() => IsBioExpanded = !IsBioExpanded;

    [ObservableProperty]
    public partial AbilityDisplay? CurrentAbility { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TalentTileBackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(TalentTileBorderBrush))]
    [NotifyPropertyChangedFor(nameof(TalentTileBorderThickness))]
    [NotifyPropertyChangedFor(nameof(TalentTileOpacity))]
    public partial bool IsTalentSelected { get; set; }

    public Brush TalentTileBackgroundBrush => IsTalentSelected
        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["GameSurfaceRaisedBrush"]
        : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["GameSurfaceSubtleBrush"];

    public Brush TalentTileBorderBrush => IsTalentSelected
        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["CoachGoldBrush"]
        : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["GameBorderBrush"];

    public Thickness TalentTileBorderThickness => IsTalentSelected ? new Thickness(2) : new Thickness(1);
    public double TalentTileOpacity => IsTalentSelected ? 1.0 : 0.82;

    [ObservableProperty]
    public partial string AbilityStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTalents))]
    public partial bool HasTalentsValue { get; set; }

    public bool HasTalents => TalentRows.Count > 0;

    [ObservableProperty]
    public partial string ScepterUpgradeText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpgrades))]
    public partial bool HasScepterUpgrade { get; set; }

    [ObservableProperty]
    public partial string ShardUpgradeText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpgrades))]
    public partial bool HasShardUpgrade { get; set; }

    public bool HasUpgrades => HasScepterUpgrade || HasShardUpgrade;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBuilds))]
    public partial bool HasBuildsValue { get; set; }

    public bool HasBuilds => SelectedBuildGroups.Count > 0;

    [ObservableProperty]
    public partial string BuildStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HeroCountersStatusText { get; set; } = string.Empty;

    public DotaCatalog? Catalog => _catalog;

    public HeroesViewModel()
    {
        PopulateRankAndSortOptions();
    }

    private void PopulateRankAndSortOptions()
    {
        RankOptions.Clear();
        foreach (var option in new RankOption[]
        {
            new() { Id = "all", Label = "全部分段" },
            new() { Id = "1", Label = "先锋" },
            new() { Id = "2", Label = "卫士" },
            new() { Id = "3", Label = "中军" },
            new() { Id = "4", Label = "统帅" },
            new() { Id = "5", Label = "传奇" },
            new() { Id = "6", Label = "万古" },
            new() { Id = "7", Label = "超凡" },
            new() { Id = "8", Label = "冠绝" },
        })
        {
            RankOptions.Add(option);
        }

        SelectedRank = RankOptions[0];

        SortOptions.Clear();
        foreach (var option in new SortOption[]
        {
            new() { Id = "winrate", Label = "胜率" },
            new() { Id = "samples", Label = "样本量" },
            new() { Id = "name", Label = "名称" },
        })
        {
            SortOptions.Add(option);
        }

        SelectedSort = SortOptions[0];
    }

    /// <summary>分段切换回调，用于持久化用户设置。</summary>
    public Action<string>? OnRankChanged { get; set; }

    /// <summary>使用自定义缓存目录。</summary>
    public void UseCacheDirectory(string path) => _dataService.CacheDirectory = path;

    /// <summary>设置默认分段。</summary>
    public void SetDefaultRank(string? rankId)
    {
        if (string.IsNullOrWhiteSpace(rankId)) return;
        var target = RankOptions.FirstOrDefault(r => string.Equals(r.Id, rankId, StringComparison.OrdinalIgnoreCase));
        if (target is not null && target != SelectedRank)
        {
            SelectedRank = target;
        }
    }

    partial void OnSearchTextChanged(string value) => ScheduleDebouncedFilter();

    partial void OnSelectedRankChanged(RankOption? value)
    {
        if (value is null) return;
        OnRankChanged?.Invoke(value.Id);
        ApplyFilters();
        if (SelectedHero is not null)
        {
            _ = ShowHeroDetailsAsync(SelectedHero);
        }
    }

    partial void OnSelectedSortChanged(SortOption? value)
    {
        if (value is null) return;
        ApplyFilters();
    }

    partial void OnSelectedSectionIdChanged(string value) => UpdateVisibleAttributeHeroes();

    [RelayCommand]
    public async Task LoadAsync(bool force)
    {
        if (!force && (_loadedOnce || IsLoading)) return;
        var requestId = ++_loadRequestId;
        IsLoading = true;
        HasError = false;
        StatusText = "正在同步数据…";
        try
        {
            _catalog = await _dataService.LoadCatalogAsync(force, _lifetimeCts.Token);
            if (requestId != _loadRequestId) return;
            _allHeroes.Clear();
            _totalProActions = _catalog.Heroes.Sum(hero => hero.StatInt("pro_pick") + hero.StatInt("pro_ban"));
            var currentRank = SelectedRank ?? RankOptions[0];
            _allHeroes.AddRange(_catalog.Heroes.Select(hero => new HeroCardItem(hero, currentRank, _totalProActions)));
            HeroCountText = $"{_allHeroes.Count} 位英雄";
            _loadedOnce = true;
            ApplyFilters();

            // 后台预下载英雄图片与技能图标到本地缓存，保证秒开与离线可用
            var prefetchUrls = _catalog.Heroes.SelectMany(hero => new[] { hero.ImageUrl, hero.IconUrl })
                .Concat(_catalog.Abilities.Values.Select(a => a.ImageUrl).Where(u => !string.IsNullOrWhiteSpace(u)));
            _ = ImageCacheService.Instance.PrefetchAsync(prefetchUrls, _lifetimeCts.Token);
            StatusText = $"{_dataService.CatalogSourceNote} · 目标版本 v{DotaDataService.TargetPatch}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (requestId != _loadRequestId) return;
            HasError = true;
            ErrorMessage = exception.Message;
            StatusText = "数据加载失败";
            HeroCountText = "数据不可用";
            ResultCountText = "数据不可用";
        }
        finally
        {
            if (requestId == _loadRequestId)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _dataService.ClearCache();
        await LoadAsync(true);
        if (SelectedHero is not null)
        {
            await ShowHeroDetailsAsync(SelectedHero);
        }
    }

    [RelayCommand]
    public async Task SelectHeroAsync(HeroCardItem? hero)
    {
        if (hero is null) return;
        SelectedHero = hero;
        await ShowHeroDetailsAsync(hero);
    }

    [RelayCommand]
    public void CloseDetail()
    {
        SelectedHero = null;
        CurrentAbility = null;
        IsTalentSelected = false;
        _detailsRequestId++;
        SelectedAbilities.Clear();
        TalentRows.Clear();
        SelectedBuildGroups.Clear();
        HeroCountersMe.Clear();
        HeroCountersTarget.Clear();
        SelectedWikiTips.Clear();
        OnPropertyChanged(nameof(IsDetailOpen));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    public void SelectTalents()
    {
        IsTalentSelected = true;
        foreach (var item in SelectedAbilities)
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand]
    public void SelectAbility(AbilityDisplay? ability)
    {
        if (ability is null) return;
        IsTalentSelected = false;
        foreach (var item in SelectedAbilities)
        {
            item.IsSelected = ReferenceEquals(item, ability);
        }
        CurrentAbility = ability;
    }

    [RelayCommand]
    public void SelectSection(string? sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId)) return;
        SelectedSectionId = sectionId;
    }

    public void ApplyFilterNow()
    {
        _searchDebounce?.Stop();
        ApplyFilters();
    }

    public void Cancel()
    {
        _lifetimeCts.Cancel();
        _searchDebounce?.Stop();
    }

    public void UpdateHeroCardLayout(double availableWidth)
    {
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

    private void ApplyFilters()
    {
        var keyword = SearchText.Trim();
        var currentRank = SelectedRank ?? RankOptions[0];
        foreach (var hero in _allHeroes) hero.UpdateRank(currentRank);

        var query = _allHeroes.Where(item => string.IsNullOrWhiteSpace(keyword) ||
            item.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
            item.Hero.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        query = (SelectedSort?.Id ?? "winrate") switch
        {
            "samples" => query.OrderByDescending(item => HeroCardItem.MetricsFor(item.Hero, currentRank.Id).Picks),
            "name" => query.OrderBy(item => item.Name, StringComparer.CurrentCulture),
            _ => query.OrderByDescending(item => item.WinRate),
        };

        var filteredHeroes = query.ToList();
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
        RankCaptionText = currentRank.Label;
    }

    private void UpdateVisibleAttributeHeroes()
    {
        var section = HeroSections.FirstOrDefault(item => item.Id == SelectedSectionId);
        VisibleAttributeHeroes.Clear();
        if (section is null) return;
        foreach (var hero in section.Heroes) VisibleAttributeHeroes.Add(hero);
        var currentRank = SelectedRank ?? RankOptions[0];
        var scope = section.Id == "all-heroes" ? "英雄" : $"{section.Title}英雄";
        ResultCountText = $"{VisibleAttributeHeroes.Count} 位{scope} · {currentRank.Label}";
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task ShowHeroDetailsAsync(HeroCardItem item)
    {
        var requestId = ++_detailsRequestId;
        CurrentAbility = null;
        DetailHeroName = item.Name;
        DetailEnglishName = LocalizedText.ContainsChinese(item.Hero.EnglishName)
            ? item.Hero.Name.Replace("npc_dota_hero_", string.Empty).Replace('_', ' ').ToUpperInvariant()
            : item.Hero.EnglishName.ToUpperInvariant();
        DetailMetaText = $"{item.AttributeLabel} · {item.AttackLabel}";
        DetailImage = new BitmapImage(ImageCacheService.Instance.Resolve(item.Hero.ImageUrl));
        DetailAttrBadgeText = item.AttributeLabel;
        DetailAttrBadgeBrush = item.AttrBrush;
        DetailPrimaryAttributeIcon = item.Hero.PrimaryAttributeIcon;
        DetailAttackTypeText = item.AttackLabel;
        DetailAttackTypeIcon = item.Hero.AttackTypeIcon;
        DetailHypeText = "正在读取英雄资料";
        DetailBiographyText = "官方中文背景故事正在同步。";
        IsBioExpanded = false;

        HasScepterUpgrade = false;
        HasShardUpgrade = false;
        ScepterUpgradeText = string.Empty;
        ShardUpgradeText = string.Empty;

        UpdateHeroStats(item.Hero);
        UpdateHeroAttributes(item.Hero);

        HeroRoleTags.Clear();
        foreach (var role in item.Hero.Roles) HeroRoleTags.Add(role);

        var currentRank = SelectedRank ?? RankOptions[0];
        var metrics = HeroCardItem.MetricsFor(item.Hero, currentRank.Id);
        DetailRankSummaryText = metrics.Picks == 0
            ? $"{currentRank.Label} · 暂无样本"
            : $"{currentRank.Label} · 胜率 {(metrics.WinRate.HasValue ? $"{metrics.WinRate.Value:0.0}%" : "--")} · {HeroCardItem.CompactNumber(metrics.Picks)} 场";

        HeroCountersMe.Clear();
        HeroCountersTarget.Clear();
        HeroCountersStatusText = "正在读取对阵数据…";

        SelectedAbilities.Clear();
        TalentRows.Clear();
        SelectedBuildGroups.Clear();
        SelectedWikiTips.Clear();
        foreach (var tip in WikiTipCatalog.ForHero(item.Hero.Name)) SelectedWikiTips.Add(tip);

        BuildStatusText = "正在读取当前分段样本…";
        AbilityStatusText = "正在同步官方中文…";

        LocalizedHeroData localized = new() { Id = item.Hero.Id };
        try
        {
            localized = await _dataService.LoadLocalizedHeroAsync(item.Hero.Id, false, _lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            AbilityStatusText = "官方中文详情暂不可用，显示基础技能";
            HasError = true;
            ErrorMessage = $"无法加载该英雄的官方中文技能：{exception.Message}";
        }

        if (requestId != _detailsRequestId) return;
        DetailHypeText = LocalizedText.ChineseOr(CleanText(localized.Hype), $"{item.Name}英雄资料");
        DetailBiographyText = LocalizedText.ChineseOr(CleanText(localized.Biography), "官方中文背景故事暂不可用。");

        var officialByName = localized.Abilities
            .Where(ability => !string.IsNullOrWhiteSpace(ability.Name))
            .GroupBy(ability => ability.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var heroAbilityMap = _catalog?.HeroAbilities.GetValueOrDefault(item.Hero.Name)
            ?? _catalog?.HeroAbilities.GetValueOrDefault(item.Hero.Name.Replace("npc_dota_hero_", string.Empty));

        var innateIds = (heroAbilityMap?.InnateIds ?? Enumerable.Empty<string>())
            .Concat(localized.Abilities.Where(a => a.IsInnate || a.Behavior?.Contains("INNATE", StringComparison.OrdinalIgnoreCase) == true).Select(a => a.Name))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var abilityIds = (heroAbilityMap?.InnateIds ?? Enumerable.Empty<string>())
            .Concat(heroAbilityMap?.AbilityIds ?? Enumerable.Empty<string>())
            .Concat(localized.Abilities.Select(ability => ability.Name))
            .Concat(SummonedUnitAbilityIds(item.Hero.Name))
            .Where(id => !string.IsNullOrWhiteSpace(id) && !id.Equals("generic_hidden", StringComparison.OrdinalIgnoreCase))
            .Where(id => !id.StartsWith("special_bonus", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var parsedDisplays = new List<AbilityDisplay>();
        foreach (var abilityId in abilityIds)
        {
            AbilityDefinition? baseAbility = null;
            _catalog?.Abilities.TryGetValue(abilityId, out baseAbility);
            officialByName.TryGetValue(abilityId, out var officialAbility);
            var display = new AbilityDisplay(abilityId, baseAbility, officialAbility);

            var isInnate = display.IsInnate || innateIds.Contains(abilityId);
            display.IsInnate = isInnate;
            parsedDisplays.Add(display);
        }

        // 1. 先天技能排在最左侧，普通技能按顺序分配 Q/W/E/D/F/R（保证大招始终为 R）
        var innateDisplays = parsedDisplays.Where(d => d.IsInnate).ToList();
        var normalDisplays = parsedDisplays.Where(d => !d.IsInnate).ToList();

        foreach (var display in innateDisplays)
        {
            display.SlotLabel = "先天";
            SelectedAbilities.Add(display);
        }

        var normalCount = normalDisplays.Count;
        for (var i = 0; i < normalDisplays.Count; i++)
        {
            var display = normalDisplays[i];
            if (display.IsGrantedByShard)
            {
                display.SlotLabel = "魔晶";
            }
            else if (display.IsGrantedByScepter)
            {
                display.SlotLabel = "神杖";
            }
            else
            {
                display.SlotLabel = normalCount switch
                {
                    1 => "Q",
                    2 => i == 0 ? "Q" : "R",
                    3 => i switch { 0 => "Q", 1 => "W", _ => "R" },
                    4 => i switch { 0 => "Q", 1 => "W", 2 => "E", _ => "R" },
                    5 => i switch { 0 => "Q", 1 => "W", 2 => "E", 3 => "D", _ => "R" },
                    6 => i switch { 0 => "Q", 1 => "W", 2 => "E", 3 => "D", 4 => "F", _ => "R" },
                    _ => i == normalCount - 1 ? "R" : i switch { 0 => "Q", 1 => "W", 2 => "E", 3 => "D", 4 => "F", _ => string.Empty },
                };
            }

            SelectedAbilities.Add(display);
        }

        CurrentAbility = SelectedAbilities.FirstOrDefault();
        if (CurrentAbility is not null)
        {
            CurrentAbility.IsSelected = true;
        }
        UpdateHeroUpgrades();

        // 重新生成实战小技巧
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
            var talentText = CleanText(talent.LocalizedName);
            if (string.IsNullOrWhiteSpace(talentText) && talentBase?.DisplayName is { Length: > 0 } displayName)
            {
                talentText = CleanText(displayName);
            }
            var localizedTalent = AbilityDisplay.ResolveTalentParameters(talentText, talent, talentBase);
            if (string.IsNullOrWhiteSpace(localizedTalent)) continue;
            if (!resolvedTalents.TryGetValue(level, out var pair)) pair = (string.Empty, string.Empty);
            if (isLeft) pair.Left = localizedTalent;
            else pair.Right = localizedTalent;
            resolvedTalents[level] = pair;
        }

        // 天赋树倒序展示（25 -> 20 -> 15 -> 10），符合游戏从下往上点击习惯
        foreach (var (level, pair) in resolvedTalents.OrderByDescending(entry => entry.Key))
        {
            if (string.IsNullOrWhiteSpace(pair.Left) && string.IsNullOrWhiteSpace(pair.Right)) continue;
            TalentRows.Add(new TalentLevelRow(
                level,
                new TalentDisplay(level, true, pair.Left),
                new TalentDisplay(level, false, pair.Right)));
        }

        OnPropertyChanged(nameof(HasTalents));
        await LoadHeroBuildsAsync(item, requestId);
        await LoadHeroCountersAsync(item, requestId);

        AbilityStatusText = localized.Abilities.Count > 0
            ? $"{SelectedAbilities.Count} 项技能"
            : $"{SelectedAbilities.Count} 项基础技能 · 中文详情暂不可用";
    }

    private void UpdateHeroAttributes(HeroStat hero)
    {
        var baseStr = hero.StatDouble("base_str");
        var strGain = hero.StatDouble("str_gain");
        DetailStrValue = baseStr > 0 ? baseStr.ToString("0.#") : "--";
        DetailStrGrowth = strGain > 0 ? $"+{strGain:0.#}" : string.Empty;

        var baseAgi = hero.StatDouble("base_agi");
        var agiGain = hero.StatDouble("agi_gain");
        DetailAgiValue = baseAgi > 0 ? baseAgi.ToString("0.#") : "--";
        DetailAgiGrowth = agiGain > 0 ? $"+{agiGain:0.#}" : string.Empty;

        var baseInt = hero.StatDouble("base_int");
        var intGain = hero.StatDouble("int_gain");
        DetailIntValue = baseInt > 0 ? baseInt.ToString("0.#") : "--";
        DetailIntGrowth = intGain > 0 ? $"+{intGain:0.#}" : string.Empty;

        var primary = hero.PrimaryAttribute?.ToLowerInvariant().Trim() ?? string.Empty;
        IsPrimaryStr = primary is "str" or "strength" or "0";
        IsPrimaryAgi = primary is "agi" or "agility" or "1";
        IsPrimaryInt = primary is "int" or "intelligence" or "2";
        IsPrimaryAll = primary is "all" or "universal" or "3";
    }

    private void UpdateHeroStats(HeroStat hero)
    {
        var baseStr = hero.StatDouble("base_str");
        var baseAgi = hero.StatDouble("base_agi");
        var baseInt = hero.StatDouble("base_int");
        var primary = hero.PrimaryAttribute?.ToLowerInvariant().Trim() ?? string.Empty;

        // 1. 攻击力计算（含主属性加成）：
        // 力量英雄：+base_str；敏捷英雄：+base_agi；智力英雄：+base_int；全才英雄：+floor((base_str+base_agi+base_int)*0.7)
        var attrBonusDamage = primary switch
        {
            "str" or "strength" or "0" => (int)Math.Floor(baseStr),
            "agi" or "agility" or "1" => (int)Math.Floor(baseAgi),
            "int" or "intelligence" or "2" => (int)Math.Floor(baseInt),
            "all" or "universal" or "3" => (int)Math.Floor((baseStr + baseAgi + baseInt) * 0.7),
            _ => (int)Math.Floor(baseStr),
        };

        var minAttack = hero.StatInt("base_attack_min") + attrBonusDamage;
        var maxAttack = hero.StatInt("base_attack_max") + attrBonusDamage;
        HeroStatAttack = minAttack > 0 && maxAttack > 0 ? $"{minAttack}-{maxAttack}" : "--";

        // 2. 总护甲计算（基础护甲 + 敏捷加成：每 1 点敏捷提供 1/6 护甲）
        var baseArmor = hero.StatDouble("base_armor");
        var totalArmor = baseArmor + (baseAgi / 6.0);
        HeroStatArmor = $"{totalArmor:0.#}";

        // 3. 移动速度
        var moveSpeed = hero.StatInt("move_speed");
        HeroStatMoveSpeed = moveSpeed > 0 ? moveSpeed.ToString() : "--";

        // 4. 攻击距离
        var attackRange = hero.StatInt("attack_range");
        HeroStatAttackRange = attackRange > 0 ? attackRange.ToString() : "--";

        // 5. 初始总生命值（基础生命 120 + 力量 × 22）
        var rawBaseHp = hero.StatInt("base_health");
        var baseHp = rawBaseHp > 0 ? rawBaseHp : 120;
        var totalHp = baseHp + (int)Math.Round(baseStr * 22);
        HeroStatBaseHealth = totalHp.ToString("N0");

        // 6. 初始生命恢复（基础恢复 + 力量 × 0.1）
        var rawBaseHpRegen = hero.StatDoubleOr("base_health_regen", 0.25);
        var totalHpRegen = rawBaseHpRegen + (baseStr * 0.1);
        HeroStatHealthRegen = $"+{totalHpRegen:0.#}";

        // 7. 初始总魔法值（基础魔法 75 + 智力 × 12）
        var rawBaseMana = hero.StatInt("base_mana");
        var baseMana = rawBaseMana > 0 ? rawBaseMana : 75;
        var totalMana = baseMana + (int)Math.Round(baseInt * 12);
        HeroStatBaseMana = totalMana.ToString("N0");

        // 8. 初始魔法恢复（基础恢复 + 智力 × 0.05）
        var rawBaseManaRegen = hero.StatDoubleOr("base_mana_regen", 0.0);
        var totalManaRegen = rawBaseManaRegen + (baseInt * 0.05);
        HeroStatManaRegen = $"+{totalManaRegen:0.#}";

        // 9. 基础攻击间隔（BAT）
        var attackRate = hero.StatDoubleOr("attack_rate", 1.7);
        HeroStatAttackRate = $"{attackRate:0.##}s";

        // 10. 基础魔法抗性
        var magicResist = hero.StatDoubleOr("base_mr", 25);
        HeroStatMagicResist = $"{magicResist:0.#}%";

        // 11. 弹道速度
        var projectileSpeed = hero.StatInt("projectile_speed");
        HeroStatProjectileSpeed = hero.AttackType == "Melee" ? "即时" : (projectileSpeed > 0 ? projectileSpeed.ToString() : "即时");

        // 12. 转身速率
        var turnRate = hero.StatDoubleOr("turn_rate", 0.6);
        HeroStatTurnRate = $"{turnRate:0.#}";

        // 13. 视野
        var dayVision = hero.StatInt("sight_range_day");
        var nightVision = hero.StatInt("sight_range_night");
        var day = dayVision > 0 ? dayVision : 1800;
        var night = nightVision > 0 ? nightVision : 800;
        HeroStatVision = $"{day} / {night}";
    }

    private void UpdateHeroUpgrades()
    {
        var scepter = SelectedAbilities
            .Where(ability => !string.IsNullOrWhiteSpace(ability.Scepter))
            .Select(ability => $"{ability.Name}：{ability.Scepter}")
            .Distinct()
            .ToList();
        var shard = SelectedAbilities
            .Where(ability => !string.IsNullOrWhiteSpace(ability.Shard))
            .Select(ability => $"{ability.Name}：{ability.Shard}")
            .Distinct()
            .ToList();

        ScepterUpgradeText = scepter.Count > 0 ? string.Join("\n\n", scepter) : "暂无阿哈利姆神杖专属升级";
        ShardUpgradeText = shard.Count > 0 ? string.Join("\n\n", shard) : "暂无阿哈利姆魔晶专属升级";
        HasScepterUpgrade = scepter.Count > 0;
        HasShardUpgrade = shard.Count > 0;
        OnPropertyChanged(nameof(HasUpgrades));
    }

    private async Task LoadHeroBuildsAsync(HeroCardItem hero, int requestId)
    {
        var currentRank = SelectedRank ?? RankOptions[0];
        try
        {
            var metrics = await _dataService.LoadHeroBuildsAsync(hero.Hero.Id, currentRank.Id, false, _lifetimeCts.Token);
            if (requestId != _detailsRequestId) return;
            var itemCatalog = await _dataService.LoadItemsAsync(false, _lifetimeCts.Token);
            var byId = itemCatalog.ToDictionary(item => item.Id);
            var builds = new List<BuildRecommendation>();
            var order = 0;
            foreach (var metric in metrics)
            {
                if (!byId.TryGetValue(metric.ItemId, out var item)) continue;
                order++;
                var phase = item.IsPregameSuggested ? "出门" : item.IsEarlyGameSuggested ? "前期" : item.IsLateGameSuggested ? "后期" : "核心";
                var rate = metric.HasReliableSample && metric.WinRate is double value
                    ? $"胜率 {value:0.0}%"
                    : "样本不足";
                builds.Add(new BuildRecommendation
                {
                    Order = order,
                    Phase = phase,
                    Name = item.DisplayName,
                    StatsText = $"样本 {HeroCardItem.CompactNumber(metric.Picks)} · {rate}",
                    Image = new BitmapImage(ImageCacheService.Instance.Resolve(item.ImageUrl)),
                });
            }

            if (metrics.Count == 0)
            {
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
                        var share = total > 0 ? (double)pair.Value / total * 100.0 : 0;
                        builds.Add(new BuildRecommendation
                        {
                            Order = order,
                            Phase = phase,
                            Name = item.DisplayName,
                            StatsText = $"选用 {HeroCardItem.CompactNumber(pair.Value)} · {share:0.0}%",
                            Image = new BitmapImage(ImageCacheService.Instance.Resolve(item.ImageUrl)),
                        });
                    }
                }
                BuildStatusText = builds.Count > 0
                    ? "近30天样本不足，已按购买热度给出推荐"
                    : "当前分段暂无足够样本";
            }
            else
            {
                BuildStatusText = $"{currentRank.Label} · 最近30天 · 按出场率";
            }

            SelectedBuildGroups.Clear();
            foreach (var phase in new[] { "出门", "前期", "核心", "中期", "后期" })
            {
                var items = builds.Where(item => item.Phase == phase).ToList();
                if (items.Count > 0)
                {
                    SelectedBuildGroups.Add(new BuildRecommendationGroup($"{phase}装备", items));
                }
            }
            OnPropertyChanged(nameof(HasBuilds));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            SelectedBuildGroups.Clear();
            BuildStatusText = "当前分段出装暂不可用";
            OnPropertyChanged(nameof(HasBuilds));
        }
    }

    private async Task<DotaDataService.HeroItemPopularity> GetPopularityAsync(int heroId)
    {
        if (_popularityCache.TryGetValue(heroId, out var cached)) return cached;
        var popularity = await _dataService.GetHeroItemPopularityAsync(heroId, _lifetimeCts.Token);
        _popularityCache[heroId] = popularity;
        return popularity;
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

            HeroCountersStatusText = samples.Count > 0
                ? $"样本来自 OpenDota 对阵数据（样本 ≥30，共 {samples.Count} 组）"
                : "暂无足够对阵样本";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            HeroCountersStatusText = "对阵数据暂不可用：" + exception.Message;
        }
    }

    private async Task<List<DotaDataService.HeroMatchupStat>> GetMatchupsAsync(int heroId)
    {
        if (_matchupCache.TryGetValue(heroId, out var cached)) return cached;
        var matchups = await _dataService.GetHeroMatchupsAsync(heroId, _lifetimeCts.Token);
        _matchupCache[heroId] = matchups;
        return matchups;
    }

    private string LocalizedHeroNameById(int heroId)
    {
        var hero = _catalog?.Heroes.FirstOrDefault(h => h.Id == heroId);
        var name = hero?.LocalizedName;
        return DotaHeroNames.GetChineseName(heroId, !string.IsNullOrWhiteSpace(name) ? name : $"英雄 #{heroId}");
    }

    private string LocalizedHeroIconById(int heroId) =>
        _catalog?.Heroes.FirstOrDefault(hero => hero.Id == heroId)?.IconUrl ?? string.Empty;

    private static string AttributeId(HeroStat hero) => hero.PrimaryAttribute switch
    {
        "str" => "str",
        "agi" => "agi",
        "int" => "int",
        _ => "all",
    };

    private static IReadOnlyList<string> SummonedUnitAbilityIds(string heroName) => heroName switch
    {
        "npc_dota_hero_lone_druid" =>
        [
            "lone_druid_spirit_bear_return",
            "lone_druid_spirit_bear_demolish",
            "lone_druid_spirit_bear_fetch",
            "lone_druid_spirit_bear_entangle",
        ],
        _ => [],
    };

    private void ScheduleDebouncedFilter()
    {
        _searchDebounce ??= CreateDebouncer();
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private DispatcherQueueTimer CreateDebouncer()
    {
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ApplyFilters();
        };
        return timer;
    }

    private static string CleanText(string? value) => AbilityDisplay.Clean(value) ?? string.Empty;
}
