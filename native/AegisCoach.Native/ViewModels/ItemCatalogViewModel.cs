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
        if (item.DetailsLoaded)
        {
            PopulateBuildsInto(item);
            return;
        }
        try
        {
            var detail = await GetDetailAsync(item.OriginalId);
            if (item.DetailsLoaded)
            {
                PopulateBuildsInto(item);
                return;
            }
            ApplyDetails(item, detail);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // 详情失败不阻断悬浮卡展示目录级信息。
            PopulateBuildsInto(item);
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
    /// 应用官网详情：属性区优先使用官网 attrib 行（"+ 10 力量" 最终文本），
    /// 技能区按 desc 的 &lt;h1&gt;主动/被动/使用…&lt;/h1&gt; 分段，冷却/魔耗用官网 cd/mc，
    /// 价格、注意事项与背景故事同步补齐。
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

        item.Stats.Clear();
        foreach (var stat in ExtractItemStats(detail))
        {
            item.Stats.Add(stat);
        }

        var cooldown = Series(detail.Cooldowns);
        var manaCost = Series(detail.ManaCosts);

        item.Abilities.Clear();
        var abilities = ParseOfficialDescription(detail.Description);
        if (abilities.Count == 0)
        {
            var description = AbilityDisplay.Clean(detail.Description ?? string.Empty) ?? string.Empty;
            if (LocalizedText.ContainsChinese(description))
            {
                description = AbilityDisplay.ResolveParameters(description, specials, null);
            }
            else
            {
                description = string.Empty;
            }

            var isActive = detail.Cooldowns.Any(value => value > 0) || detail.ManaCosts.Any(value => value > 0);
            if (!string.IsNullOrWhiteSpace(description) || isActive)
            {
                abilities.Add(new ItemAbility
                {
                    Type = isActive ? AbilityType.Active : AbilityType.Passive,
                    Name = string.Empty,
                    Description = description,
                });
            }
        }

        foreach (var ability in abilities)
        {
            if (!string.IsNullOrWhiteSpace(ability.Description))
            {
                ability.Description = AbilityDisplay.ResolveParameters(ability.Description, specials, null);
            }
            ability.Cooldown ??= cooldown;
            ability.ManaCost ??= manaCost;
            item.Abilities.Add(ability);
        }

        // 注意事项（Notes）
        item.Notes.Clear();
        if (detail.Notes.Count > 0)
        {
            foreach (var rawNote in detail.Notes)
            {
                var cleaned = AbilityDisplay.Clean(rawNote);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    var resolved = AbilityDisplay.ResolveParameters(cleaned, specials, null);
                    item.Notes.Add(resolved);
                }
            }
        }

        var lore = AbilityDisplay.Clean(detail.Lore ?? string.Empty);
        item.Lore = LocalizedText.ChineseOrNull(lore);

        // 官网详情提供的价格优先于目录数据（OpenDota 降级目录价格可能缺失）。
        if (detail.Cost > 0 && item.Price != detail.Cost)
        {
            item.Price = detail.Cost;
        }

        PopulateBuildsInto(item);
        item.NotifyDisplayStateChanged();
    }

    /// <summary>计算当前物品可以用于合成哪些高级装备（进阶合成）。</summary>
    private void PopulateBuildsInto(CatalogItem item)
    {
        if (item.BuildsIntoCards.Count > 0) return;
        var cleanId = item.Id.StartsWith("item_", StringComparison.OrdinalIgnoreCase) ? item.Id[5..] : item.Id;
        var targets = _allItems
            .Where(candidate => candidate.Components.Any(c =>
            {
                var targetClean = c.StartsWith("item_", StringComparison.OrdinalIgnoreCase) ? c[5..] : c;
                return string.Equals(c, item.Id, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(targetClean, cleanId, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals("item_" + targetClean, item.Id, StringComparison.OrdinalIgnoreCase);
            }))
            .OrderBy(candidate => candidate.Price ?? int.MaxValue)
            .ToList();

        foreach (var target in targets)
        {
            item.BuildsIntoCards.Add(new ItemComponentCard
            {
                Id = target.Id,
                Name = target.Name,
                ImageUrl = target.IconUrl,
                Cost = target.Price ?? 0,
            });
        }
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
            var cleanName = componentName.StartsWith("item_", StringComparison.OrdinalIgnoreCase) ? componentName[5..] : componentName;
            var fallback = _allItems.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, componentName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Id.Replace("item_", string.Empty), cleanName, StringComparison.OrdinalIgnoreCase));

            var displayName = source?.LocalizedName ?? fallback?.Name ?? componentName;
            var imageUrl = source is not null ? ImageCacheService.Instance.ResolveText(source.ImageUrl) : (fallback?.IconUrl ?? string.Empty);
            var cost = source?.Cost ?? fallback?.Price ?? 0;

            item.ComponentCards.Add(new ItemComponentCard
            {
                Id = componentName,
                Name = LocalizedText.ChineseOr(displayName, componentName),
                ImageUrl = imageUrl,
                CountText = count > 1 ? $"x{count}" : string.Empty,
                Cost = cost,
            });
            recipeCost += cost * count;
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
            typePart = header.Trim();
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

        // 如果 name 等同于 typePart（如 "主动" / "被动" / "使用" / "切换"），则将其清空，避免在 UI 上重复显示 "[ 主动 ] 主动"
        if (string.Equals(name, typePart, StringComparison.OrdinalIgnoreCase) ||
            name is "主动" or "被动" or "使用" or "切换")
        {
            name = string.Empty;
        }

        return (type, name);
    }

    /// <summary>
    /// 从官网详情数据中提取真正的装备属性加成（绿字加号属性），
    /// 严格过滤掉技能内部参数（如持续时间、作用范围、伤害参数、小兵伤害等）。
    /// </summary>
    private static List<ItemStat> ExtractItemStats(OfficialItemData detail)
    {
        var stats = new List<ItemStat>();

        // 1. 官网详情已有的 attrib 文本行（例如 "+10 力量"、"+25 攻击力"）权威优先
        if (detail.AttribLines.Count > 0)
        {
            foreach (var line in detail.AttribLines)
            {
                var cleaned = AbilityDisplay.Clean(line);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    stats.Add(new ItemStat { Label = cleaned });
                }
            }
            if (stats.Count > 0) return stats;
        }

        // 2. 从 special_values 中精准提取常驻被动属性加成
        var specials = detail.SpecialValues;
        if (specials is null || specials.Count == 0) return stats;

        foreach (var value in specials)
        {
            if (value.EffectiveValues.Count == 0) continue;
            var rawName = value.Name?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawName)) continue;

            // 排除明确属于技能效果/机制的内部参数
            if (IsNonStatSpecialValue(rawName)) continue;

            // 识别标准装备属性加成
            var (isStat, label, isPct) = ClassifyItemStat(rawName, value.HeadingLoc);
            if (!isStat || string.IsNullOrWhiteSpace(label)) continue;

            var isPercentage = isPct || value.IsPercentage;
            var suffix = isPercentage ? "%" : string.Empty;
            var formattedValue = string.Join(" / ", value.EffectiveValues.Select(v => $"{v:0.##}{suffix}"));

            stats.Add(new ItemStat
            {
                Label = label,
                Value = formattedValue,
            });
        }

        return stats;
    }

    private static bool IsNonStatSpecialValue(string name)
    {
        if (name.StartsWith("ability", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("radius") || name.Contains("duration") || name.Contains("cooldown") || name.Contains("mana_cost")) return true;
        if (name.Contains("chance") || name.Contains("cleave") || name.Contains("burn") || name.Contains("stun")) return true;
        if (name.Contains("quell") || name.Contains("replenish") || name.Contains("charges") || name.Contains("fade")) return true;
        if (name.Contains("range_tooltip") || name.Contains("active_") || name.Contains("aura_") || name.Contains("windwalk")) return true;
        if (name.Contains("multiplier") || name.Contains("interval") || name.Contains("distance") || name.Contains("count") || name.Contains("targets")) return true;
        if (name.Contains("visibility") || name.Contains("backstab") || name.Contains("crit_") || name.Contains("feedback") || name.Contains("bash")) return true;
        if (name.Contains("damage_") || name.Contains("_damage") && !name.Equals("bonus_damage") && !name.Equals("attack_damage")) return true;
        if (name.Contains("slow_") || name.Contains("_slow") && !name.Equals("bonus_slow_resistance") && !name.Equals("slow_resistance")) return true;
        if (name.Equals("damage", StringComparison.OrdinalIgnoreCase) || name.Equals("dps", StringComparison.OrdinalIgnoreCase) || name.Equals("heal", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static (bool IsStat, string Label, bool IsPercentage) ClassifyItemStat(string name, string? headingLoc)
    {
        // 优先从 heading_loc 提取（如 +$str -> 力量）
        if (!string.IsNullOrWhiteSpace(headingLoc))
        {
            var cleanedHeading = AbilityDisplay.CleanStatHeading(headingLoc);
            if (!string.IsNullOrWhiteSpace(cleanedHeading) && !cleanedHeading.Equals("属性", StringComparison.OrdinalIgnoreCase))
            {
                return (true, cleanedHeading, headingLoc.Contains('%'));
            }
        }

        var normalized = name;
        if (normalized.StartsWith("bonus_")) normalized = normalized[6..];

        return normalized switch
        {
            "strength" or "str" => (true, "力量", false),
            "agility" or "agi" => (true, "敏捷", false),
            "intellect" or "intelligence" or "int" => (true, "智力", false),
            "all_stats" or "stats" => (true, "全属性", false),
            "health" or "hp" => (true, "生命值", false),
            "mana" or "mp" => (true, "魔法值", false),
            "health_regen" or "hp_regen" => (true, "生命恢复", false),
            "mana_regen" or "mp_regen" => (true, "魔法恢复", false),
            "mana_regen_percentage" => (true, "魔法恢复增强", true),
            "armor" or "physical_armor" => (true, "护甲", false),
            "magic_resistance" or "magic_resist" or "magical_armor" => (true, "魔法抗性", true),
            "status_resistance" or "status_resist" => (true, "状态抗性", true),
            "damage" or "attack_damage" when name.StartsWith("bonus_") || name.Equals("attack_damage") => (true, "攻击力", false),
            "attack_speed" => (true, "攻击速度", false),
            "movement" or "movement_speed" or "move_speed" or "movespeed" => (true, "移动速度", false),
            "attack_range" => (true, "攻击距离", false),
            "cast_range" => (true, "施法距离", false),
            "spell_amp" or "spell_amplification" => (true, "技能增强", true),
            "evasion" => (true, "闪避", true),
            "lifesteal" => (true, "攻击吸血", true),
            "spell_lifesteal" => (true, "技能吸血", true),
            "cooldown_reduction" => (true, "冷却缩减", true),
            "vision" or "night_vision" or "day_vision" => (true, "视野", false),
            "slow_resistance" => (true, "减速抗性", true),
            _ => (false, string.Empty, false)
        };
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
            if (component.Name.StartsWith("item_recipe_", StringComparison.OrdinalIgnoreCase)) continue; // 卷轴单独算价
            item.Components.Add(component.Name);
            var source = _sourceItems.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, component.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Name.Replace("item_", string.Empty), component.Name.Replace("item_", string.Empty), StringComparison.OrdinalIgnoreCase));
            var cost = source?.Cost ?? 0;
            var imageUrl = source is not null ? ImageCacheService.Instance.ResolveText(source.ImageUrl) : ImageCacheService.Instance.ResolveText(OfficialItemSummary.ImageUrlFor(component.Name));
            item.ComponentCards.Add(new ItemComponentCard
            {
                Id = component.Name,
                Name = source is not null ? LocalizedText.ChineseOr(source.LocalizedName, component.DisplayName) : component.DisplayName,
                ImageUrl = imageUrl,
                CountText = component.Count > 1 ? $"x{component.Count}" : string.Empty,
                Cost = cost,
            });
            recipeCost += cost * component.Count;
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

    [GeneratedRegex("<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AbilityHeaderRegex();

    private static string CleanText(string value) =>
        AbilityDisplay.Clean(value) ?? string.Empty;
}
