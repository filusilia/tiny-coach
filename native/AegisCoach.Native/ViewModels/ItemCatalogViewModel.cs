using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using AegisCoach.Native.Data;
using AegisCoach.Native.Models;
using AegisCoach.Native.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace AegisCoach.Native.ViewModels;

/// <summary>子类筛选 Chip（多选；全部未选时表示显示全部）。</summary>
public sealed partial class SubCategoryFilter : ObservableObject
{
    public SubCategoryFilter(string name) => Name = name;

    public string Name { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// 物品资料库 ViewModel：加载官方目录、商店/中立切换、子类多选筛选、
/// 名称/英文/拼音防抖搜索、分组集合（CollectionViewSource）与悬浮卡详情懒加载。
/// </summary>
public sealed partial class ItemCatalogViewModel : ObservableObject
{
    public const string ShopMode = "商店物品";
    public const string NeutralMode = "中立物品";

    private readonly DotaDataService _dataService = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly List<OfficialItemSummary> _sourceItems = [];
    private readonly List<CatalogItem> _allItems = [];
    private readonly Dictionary<int, Task<OfficialItemData>> _detailTasks = new();
    private DispatcherQueueTimer? _searchDebounce;
    private int _loadRequestId;
    private bool _loadedOnce;

    public ObservableCollection<SubCategoryFilter> SubCategoryFilters { get; } = [];
    public ObservableCollection<ItemGroup> Groups { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "目标版本 v7.41e";

    [ObservableProperty]
    public partial string CountText { get; set; } = "正在同步数据…";

    [ObservableProperty]
    public partial int ModeIndex { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public string Mode => ModeIndex == 1 ? NeutralMode : ShopMode;
    public bool IsEmpty => !IsLoading && !HasError && Groups.Count == 0;

    /// <summary>使用设置页自定义的缓存目录（默认即文档目录，可在设置页修改）。</summary>
    public void UseCacheDirectory(string path) => _dataService.CacheDirectory = path;

    partial void OnModeIndexChanged(int value)
    {
        RebuildFilterOptions();
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value) => ScheduleDebouncedFilter();

    [RelayCommand]
    private async Task LoadAsync(bool force)
    {
        if (!force && (_loadedOnce || IsLoading)) return;
        var requestId = ++_loadRequestId;
        IsLoading = true;
        HasError = false;
        StatusText = "正在同步物品数据…";
        try
        {
            var items = await _dataService.LoadItemsAsync(force, _lifetimeCts.Token);
            if (requestId != _loadRequestId) return;
            _sourceItems.Clear();
            _sourceItems.AddRange(items);
            _allItems.Clear();
            _allItems.AddRange(items.Select(ToCatalogItem));
            _detailTasks.Clear();
            _loadedOnce = true;
            StatusText = $"{_dataService.ItemCatalogSource} · 目标版本 v{DotaDataService.TargetPatch}";
            _ = ImageCacheService.Instance.PrefetchAsync(items.Select(item => item.ImageUrl), _lifetimeCts.Token);
            ApplyFilters();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (requestId != _loadRequestId) return;
            HasError = true;
            ErrorMessage = exception.Message;
            StatusText = "物品数据加载失败";
            CountText = "数据不可用";
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
    private Task RefreshAsync() => LoadAsync(true);

    /// <summary>悬浮卡打开时异步补齐官方详情；失败时保留目录级信息。</summary>
    public async Task EnsureDetailsLoadedAsync(CatalogItem item)
    {
        if (item.DetailsLoaded) return;
        try
        {
            var detail = await GetDetailAsync(item.OriginalId);
            if (item.DetailsLoaded) return;
            ApplyDetails(item, detail);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // 详情失败不阻断悬浮卡展示目录级信息。
        }
    }

    /// <summary>搜索回车/清空时立即应用筛选（不等待防抖）。</summary>
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

    private async Task<OfficialItemData> GetDetailAsync(int itemId)
    {
        if (_detailTasks.TryGetValue(itemId, out var cached)) return await cached;
        var task = _dataService.LoadItemDetailsAsync(itemId, false, _lifetimeCts.Token);
        _detailTasks[itemId] = task;
        try
        {
            return await task;
        }
        catch
        {
            // 失败后移除，允许下次悬浮重试。
            _detailTasks.Remove(itemId);
            throw;
        }
    }

    /// <summary>
    /// 应用官网详情：属性区优先使用官网 attrib 行（"+攻击力：9" 最终文本），
    /// 技能区按 desc 的 &lt;h1&gt;主动/被动/使用…&lt;/h1&gt; 分段，冷却/魔耗用官网 cd/mc，
    /// 价格与背景故事同步补齐。
    /// </summary>
    private void ApplyDetails(CatalogItem item, OfficialItemData detail)
    {
        item.DetailsLoaded = true;
        var specials = detail.SpecialValues;

        // 合成路线：官网 requirements 优先（离线、权威），否则保留目录级组件。
        if (detail.Requirements.Count > 0)
        {
            ApplyOfficialRecipe(item, detail.Requirements);
        }

        if (detail.AttribLines.Count > 0)
        {
            foreach (var line in detail.AttribLines)
            {
                item.Stats.Add(new ItemStat { Label = line });
            }
        }
        else
        {
            foreach (var stat in AbilityDisplay.BuildStats(specials, null))
            {
                item.Stats.Add(new ItemStat { Label = stat.Label, Value = stat.Value });
            }
        }

        var cooldown = Series(detail.Cooldowns);
        var manaCost = Series(detail.ManaCosts);

        var abilities = ParseOfficialDescription(detail.Description);
        if (abilities.Count == 0)
        {
            var description = CleanText(detail.Description ?? string.Empty);
            if (LocalizedText.ContainsChinese(description))
            {
                description = AbilityDisplay.ResolveParameters(description, specials, null);
            }
            else
            {
                description = string.Empty;
            }

            var isActive = detail.Cooldowns.Any(value => value > 0) || detail.ManaCosts.Any(value => value > 0);
            abilities.Add(new ItemAbility
            {
                Type = isActive ? AbilityType.Active : AbilityType.Passive,
                Name = item.Name,
                Description = description,
            });
        }

        foreach (var ability in abilities)
        {
            ability.Cooldown ??= cooldown;
            ability.ManaCost ??= manaCost;
            item.Abilities.Add(ability);
        }

        var lore = CleanText(detail.Lore ?? string.Empty);
        item.Lore = LocalizedText.ChineseOrNull(lore);

        // 官网详情提供的价格优先于目录数据（OpenDota 降级目录价格可能缺失）。
        if (detail.Cost > 0 && item.Price != detail.Cost)
        {
            item.Price = detail.Cost;
        }

        item.NotifyDisplayStateChanged();
    }

    /// <summary>
    /// 用官网合成配方（requirements）重建合成路线：小件按出现次数计数并生成缩略图卡，
    /// 卷轴（recipe_ 前缀）不计入卡片、由总价与组件价差计算卷轴价格。
    /// </summary>
    private void ApplyOfficialRecipe(CatalogItem item, List<string> requirements)
    {
        if (requirements.Count == 0) return;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in requirements)
        {
            var name = raw.TrimEnd('*').Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!name.StartsWith("item_", StringComparison.OrdinalIgnoreCase)) name = "item_" + name;
            if (name.StartsWith("item_recipe_", StringComparison.OrdinalIgnoreCase)) continue; // 卷轴单独算价
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        if (counts.Count == 0) return;
        item.Components.Clear();
        item.ComponentCards.Clear();
        var recipeCost = 0;
        foreach (var (componentName, count) in counts)
        {
            item.Components.Add(componentName);
            var source = _sourceItems.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, componentName, StringComparison.OrdinalIgnoreCase));
            if (source is null) continue;
            item.ComponentCards.Add(new ItemComponentCard
            {
                Name = source.Name,
                ImageUrl = ImageCacheService.Instance.ResolveText(source.ImageUrl),
                CountText = count > 1 ? $"x{count}" : string.Empty,
            });
            recipeCost += source.Cost * count;
        }

        // 卷轴价格 = 总价（官网详情已更新）- 各小件价格之和。
        if (item.Price is int price && recipeCost > 0 && price > recipeCost)
        {
            item.RecipeScrollCost = price - recipeCost;
        }
    }

    /// <summary>按官网 desc 的 &lt;h1&gt;技能头&lt;/h1&gt; 分段，每段生成一个技能条目（类型/名称/描述）。</summary>
    private static List<ItemAbility> ParseOfficialDescription(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];
        var normalized = html
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase);
        var matches = AbilityHeaderRegex().Matches(normalized).ToList();
        if (matches.Count == 0) return [];

        var abilities = new List<ItemAbility>(matches.Count);
        for (var index = 0; index < matches.Count; index++)
        {
            var header = CleanText(matches[index].Groups[1].Value);
            var start = matches[index].Index + matches[index].Length;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : normalized.Length;
            var body = CleanText(normalized.Substring(start, end - start));
            var (type, name) = ParseAbilityHeader(header);
            abilities.Add(new ItemAbility
            {
                Type = type,
                Name = string.IsNullOrWhiteSpace(name) ? header : name,
                Description = body,
            });
        }

        return abilities;
    }

    /// <summary>解析官网技能头（"主动：闪烁" / "被动：穿刺" / "切换：转变" / "升级：回城卷轴"）。</summary>
    private static (AbilityType Type, string Name) ParseAbilityHeader(string header)
    {
        var separator = header.IndexOfAny(['：', ':', ' ']);
        string typePart;
        string name;
        if (separator >= 0)
        {
            typePart = header[..separator].Trim();
            name = header[(separator + 1)..].Trim();
        }
        else
        {
            typePart = header;
            name = string.Empty;
        }

        var type = typePart switch
        {
            "主动" => AbilityType.Active,
            "被动" => AbilityType.Passive,
            "使用" => AbilityType.Use,
            "切换" or "开关" => AbilityType.Toggle,
            "升级" => AbilityType.Active,
            _ when typePart.Contains("被动", StringComparison.OrdinalIgnoreCase) => AbilityType.Passive,
            _ => AbilityType.Active,
        };
        return (type, name);
    }

    private CatalogItem ToCatalogItem(OfficialItemSummary summary)
    {
        var isNeutral = CurrentItemCatalog.IsNeutralItem(summary.Name);
        var tier = isNeutral ? summary.NeutralTier : 0;
        var subCategory = isNeutral
            ? tier > 0 ? $"T{tier}" : "附魔"
            : ItemCategory.Classify(summary);
        var tokens = string.Join(" ", new[]
        {
            summary.Name,
            summary.LocalizedName,
            summary.EnglishName,
            summary.CategoryCode,
            summary.TypeCode,
            summary.ShopTags,
            subCategory,
            PinyinFor(subCategory),
        }).ToLowerInvariant();

        var item = new CatalogItem
        {
            Id = summary.Name,
            OriginalId = summary.Id,
            Name = LocalizedText.ChineseOr(summary.LocalizedName, "中文名称待同步"),
            Subtitle = summary.EnglishName,
            Category = isNeutral ? NeutralMode : ShopMode,
            SubCategory = subCategory,
            Tier = isNeutral ? tier : null,
            Price = summary.Cost > 0 ? summary.Cost : null,
            IconUrl = ImageCacheService.Instance.ResolveText(summary.ImageUrl),
            SearchTokens = tokens,
        };

        var recipeCost = 0;
        foreach (var component in summary.RecipeComponents)
        {
            item.Components.Add(component.Name);
            item.ComponentCards.Add(new ItemComponentCard
            {
                Name = component.DisplayName,
                ImageUrl = component.ImageUrl,
                CountText = component.Count > 1 ? $"x{component.Count}" : string.Empty,
            });
            var source = _sourceItems.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, component.Name, StringComparison.OrdinalIgnoreCase));
            if (source is not null) recipeCost += source.Cost * component.Count;
        }

        // 合成卷轴价格 = 总价 - 各小件价格之和（小件缺失时不计）。
        if (item.Price is int price && recipeCost > 0 && price > recipeCost)
        {
            item.RecipeScrollCost = price - recipeCost;
        }

        return item;
    }

    private void RebuildFilterOptions()
    {
        foreach (var filter in SubCategoryFilters) filter.PropertyChanged -= OnFilterChanged;
        SubCategoryFilters.Clear();
        var names = Mode == NeutralMode
            ? (IEnumerable<string>)["T1", "T2", "T3", "T4", "T5", "附魔"]
            : ItemCategory.Order.Where(name => name != "中立物品");
        foreach (var name in names)
        {
            var filter = new SubCategoryFilter(name);
            filter.PropertyChanged += OnFilterChanged;
            SubCategoryFilters.Add(filter);
        }
    }

    private void OnFilterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SubCategoryFilter.IsSelected)) ApplyFilters();
    }

    private void ApplyFilters()
    {
        var keyword = SearchText.Trim();
        var selected = SubCategoryFilters
            .Where(filter => filter.IsSelected)
            .Select(filter => filter.Name)
            .ToHashSet(StringComparer.Ordinal);

        IEnumerable<CatalogItem> query = _allItems;
        if (selected.Count > 0) query = query.Where(item => selected.Contains(item.SubCategory));
        if (!string.IsNullOrWhiteSpace(keyword)) query = query.Where(item => MatchesKeyword(item, keyword));
        var matched = query.ToList();

        Groups.Clear();
        foreach (var group in BuildGroups(matched)) Groups.Add(group);
        CountText = $"{matched.Count} 件物品 · {Groups.Count} 个分类";
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>大分类（基础/升级/中立）-&gt; 子分类 -&gt; 物品图标网格的分组顺序。</summary>
    private List<ItemGroup> BuildGroups(List<CatalogItem> matched)
    {
        var groups = new List<ItemGroup>();
        if (Mode == NeutralMode)
        {
            foreach (var key in new[] { "T1", "T2", "T3", "T4", "T5", "附魔" })
            {
                var items = matched.Where(item => item.SubCategory == key)
                    .OrderBy(item => item.Name, StringComparer.CurrentCulture).ToList();
                if (items.Count > 0) groups.Add(new ItemGroup(key, NeutralMode, items));
            }
        }
        else
        {
            var order = ItemCategory.Order.Where(name => name != "中立物品").ToList();
            for (var index = 0; index < order.Count; index++)
            {
                var key = order[index];
                var items = matched.Where(item => item.SubCategory == key)
                    .OrderBy(item => item.Name, StringComparer.CurrentCulture).ToList();
                if (items.Count == 0) continue;
                var section = index < 5 ? "基础物品" : "升级物品";
                groups.Add(new ItemGroup(key, section, items));
            }
        }

        return groups;
    }

    private static bool MatchesKeyword(CatalogItem item, string keyword)
    {
        if (item.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)) return true;
        if (item.Subtitle?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) return true;
        return item.SearchTokens.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Dictionary<string, string> PinyinMap = new(StringComparer.Ordinal)
    {
        ["消耗品"] = "xiaofeipin",
        ["属性"] = "shuxing",
        ["装备"] = "zhuangbei",
        ["其他"] = "qita",
        ["神秘商店"] = "shenmishangdian",
        ["配件"] = "peijian",
        ["辅助"] = "fuzhu",
        ["法器"] = "faqi",
        ["防具"] = "fangju",
        ["兵刃"] = "bingren",
        ["宝物"] = "baowu",
        ["中立物品"] = "zhongliwupin",
        ["附魔"] = "fumo",
        ["T1"] = "yiji",
        ["T2"] = "erji",
        ["T3"] = "sanji",
        ["T4"] = "siji",
        ["T5"] = "wuji",
    };

    private static string PinyinFor(string subCategory) =>
        PinyinMap.GetValueOrDefault(subCategory) ?? string.Empty;

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

    private static string? Series(IEnumerable<double>? values)
    {
        var useful = values?.Where(value => value > 0).ToList();
        return useful is { Count: > 0 }
            ? string.Join(" / ", useful.Select(value => value % 1 == 0 ? value.ToString("0") : value.ToString("0.#")))
            : null;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AbilityHeaderRegex();

    private static string CleanText(string value) =>
        HtmlTagRegex().Replace(value.Replace("<br>", "\n"), string.Empty).Trim();
}
