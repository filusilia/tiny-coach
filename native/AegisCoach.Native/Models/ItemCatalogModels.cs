using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegisCoach.Native.Models;

/// <summary>物品技能类型。</summary>
public enum AbilityType
{
    Active,
    Passive,
    Toggle,
    Use,
}

/// <summary>物品主动/被动技能条目（悬浮详情卡“技能详情区”数据）。</summary>
public sealed class ItemAbility
{
    public AbilityType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Cooldown { get; set; }
    public string? ManaCost { get; set; }

    public string TypeLabel => Type switch
    {
        AbilityType.Active => "主动",
        AbilityType.Passive => "被动",
        AbilityType.Toggle => "切换",
        _ => "使用",
    };

    /// <summary>主动类技能用蓝色高亮，被动类用主题强调色。</summary>
    public bool IsActive => Type is AbilityType.Active or AbilityType.Toggle or AbilityType.Use;
    public bool HasCooldownOrMana =>
        !string.IsNullOrWhiteSpace(Cooldown) || !string.IsNullOrWhiteSpace(ManaCost);
}

/// <summary>物品属性加成条目（如 +10 全属性；官网 attrib 行则直接以原文展示 "+攻击力：9"）。</summary>
public sealed class ItemStat
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    /// <summary>Value 为空时直接展示官网原文（Label 已含 "＋攻击力：9" 完整文本），否则以绿字加号格式展示。</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(Value)
        ? Label
        : $"+{Value.TrimStart('+', '-', '−')} {Label}".TrimEnd();
}

/// <summary>合成小件（悬浮详情卡“合成路线”缩略图）。</summary>
public sealed class ItemComponentCard
{
    public string Name { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
}

/// <summary>
/// 物品库条目（Dota 2 官网物品页信息结构）：名称、子分类、价格/阶级、
/// 属性加成、主动/被动技能、合成路线与背景故事。
/// 悬浮卡在打开时先展示目录级信息，随后异步补充官方详情（属性/技能/传说）。
/// </summary>
public sealed partial class CatalogItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;

    /// <summary>内部数字 ID，用于请求官方详情。</summary>
    public int OriginalId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Category { get; set; } = string.Empty;      // 商店物品 / 中立物品
    public string SubCategory { get; set; } = string.Empty;   // 消耗品 / T1 / 附魔 等
    public int? Tier { get; set; }
    public int? Price { get; set; }
    public string IconUrl { get; set; } = string.Empty;

    /// <summary>搜索关键词（中文名/英文名/内部 ID/子类英文标签/拼音）。</summary>
    public string SearchTokens { get; set; } = string.Empty;

    /// <summary>合成所需小件内部 ID（与官网结构一致）。</summary>
    public List<string> Components { get; set; } = [];

    public ObservableCollection<ItemStat> Stats { get; } = [];
    public ObservableCollection<ItemAbility> Abilities { get; } = [];
    public ObservableCollection<ItemComponentCard> ComponentCards { get; } = [];
    public string? Lore { get; set; }

    /// <summary>合成卷轴价格（总价 - 各小件价格之和，无法计算时为 null）。</summary>
    public int? RecipeScrollCost { get; set; }

    [ObservableProperty]
    public partial bool DetailsLoaded { get; set; }

    public string PriceText => Price is > 0 ? $"$ {Price.Value:N0}" : string.Empty;
    public bool HasPrice => Price is > 0;
    public string TierText => Tier is > 0 ? $"中立物品 T{Tier}" : string.Empty;
    public string ScrollCostText => RecipeScrollCost is > 0 ? $"卷轴 $ {RecipeScrollCost.Value:N0}" : string.Empty;
    public bool HasScrollCost => RecipeScrollCost is > 0;

    public bool HasStats => Stats.Count > 0;
    public bool HasAbilities => Abilities.Count > 0;
    public bool HasComponents => ComponentCards.Count > 0;
    public bool HasLore => !string.IsNullOrWhiteSpace(Lore);

    /// <summary>详情异步加载完成后通知悬浮卡刷新显示状态。</summary>
    public void NotifyDisplayStateChanged()
    {
        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(HasAbilities));
        OnPropertyChanged(nameof(HasComponents));
        OnPropertyChanged(nameof(HasLore));
        OnPropertyChanged(nameof(ScrollCostText));
        OnPropertyChanged(nameof(HasScrollCost));
    }
}

/// <summary>
/// 分组单位（大分类 -&gt; 子分类 -&gt; 物品图标网格）。
/// 实现 IList&lt;CatalogItem&gt; 并暴露 Key，供 CollectionViewSource(IsSourceGrouped) 分组。
/// </summary>
public sealed class ItemGroup : List<CatalogItem>
{
    public ItemGroup(string key, string sectionLabel, IEnumerable<CatalogItem> items)
        : base(items)
    {
        Key = key;
        SectionLabel = sectionLabel;
    }

    public string Key { get; }
    public string SectionLabel { get; }
}
