using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AegisCoach.Native.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AegisCoach.Native.Models;

[JsonConverter(typeof(HeroStatConverter))]
public sealed class HeroStat
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("localized_name")] public string LocalizedName { get; set; } = string.Empty;
    [JsonPropertyName("english_name")] public string EnglishName { get; set; } = string.Empty;
    [JsonPropertyName("primary_attr")] public string PrimaryAttribute { get; set; } = string.Empty;
    [JsonPropertyName("attack_type")] public string AttackType { get; set; } = string.Empty;
    [JsonPropertyName("img")] public string ImagePath { get; set; } = string.Empty;
    [JsonPropertyName("icon")] public string IconPath { get; set; } = string.Empty;

    /// <summary>
    /// 英雄统计扩展字段（胜率、分段样本、属性成长等），值已在反序列化时物化为
    /// long/double/string/bool/List/Dictionary 等安全类型，可跨文档生命周期缓存。
    /// </summary>
    public Dictionary<string, object?> Stats { get; set; } = [];

    [JsonIgnore]
    public string ImageUrl => AssetUrl(ImagePath);

    [JsonIgnore]
    public string IconUrl => AssetUrl(string.IsNullOrWhiteSpace(IconPath) ? ImagePath : IconPath);

    /// <summary>OpenDota heroStats 的 roles 字段：Carry / Nuker / Support 等角色标签。</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Roles
    {
        get
        {
            if (!Stats.TryGetValue("roles", out var value) || value is not List<object?> roles) return [];
            return roles
                .OfType<string>()
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(RoleLabel)
                .ToList();
        }
    }

    public int StatInt(string key) => ValueToInt(Stats.GetValueOrDefault(key));

    public double StatDouble(string key) => ValueToDouble(Stats.GetValueOrDefault(key));

    public double StatDoubleOr(string key, double fallback = 0) =>
        Stats.TryGetValue(key, out var value) && value is not null ? ValueToDouble(value) : fallback;

    internal static int ValueToInt(object? value) => value switch
    {
        long number => (int)number,
        int number => number,
        double number => (int)number,
        float number => (int)number,
        string text when int.TryParse(text, out var number) => number,
        _ => 0,
    };

    internal static double ValueToDouble(object? value) => value switch
    {
        long number => number,
        int number => number,
        double number => number,
        float number => number,
        string text when double.TryParse(text, out var number) => number,
        _ => 0,
    };

    private static string RoleLabel(string role) => role.ToLowerInvariant() switch
    {
        "carry" => "核心",
        "support" => "辅助",
        "nuker" => "爆发",
        "initiator" => "先手",
        "disabler" => "控制",
        "durable" => "肉盾",
        "escape" => "逃生",
        "pusher" => "推进",
        "jungler" => "打野",
        _ => role,
    };

    public static string AssetUrl(string path) =>
        path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"https://cdn.cloudflare.steamstatic.com{path}";
}

/// <summary>英雄详情页基础属性行（Liquipedia 风格数值表）。</summary>
public sealed class HeroAttributeRow
{
    public HeroAttributeRow(string label, string value, string growth, Brush color)
    {
        Label = label;
        Value = value;
        Growth = growth;
        Color = color;
    }

    public string Label { get; }
    public string Value { get; }
    public string Growth { get; }
    public Brush Color { get; }
    public string GrowthText => string.IsNullOrWhiteSpace(Growth) ? string.Empty : $"+{Growth}/级";
}

/// <summary>英雄克制关系条目：方向表示“我克制对方”或“被对方克制”。</summary>
public sealed class CounterMatchupItem
{
    public CounterMatchupItem(
        int heroId,
        string heroName,
        string iconUrl,
        double winRate,
        int gamesPlayed,
        bool countersTarget)
    {
        HeroId = heroId;
        HeroName = heroName;
        IconUrl = iconUrl;
        WinRate = winRate;
        GamesPlayed = gamesPlayed;
        CountersTarget = countersTarget;
    }

    public int HeroId { get; }
    public string HeroName { get; }
    public string IconUrl { get; }
    public double WinRate { get; }
    public int GamesPlayed { get; }
    public bool CountersTarget { get; }
    public string WinRateText => $"{WinRate:0.0}%";
    public string SampleText => $"样本 {HeroCardItem.CompactNumber(GamesPlayed)}";
    public string DirectionText => CountersTarget ? "克制对方" : "被对方克制";
}

/// <summary>实时教练页的选人推荐：某英雄对当前敌方阵容的克制评分。</summary>
public sealed class CounterPickRecommendation
{
    public CounterPickRecommendation(
        int heroId,
        string heroName,
        string iconUrl,
        int advantageCount,
        double averageWinRate)
    {
        HeroId = heroId;
        HeroName = heroName;
        IconUrl = iconUrl;
        AdvantageCount = advantageCount;
        AverageWinRate = averageWinRate;
    }

    public int HeroId { get; }
    public string HeroName { get; }
    public string IconUrl { get; }
    public int AdvantageCount { get; }
    public double AverageWinRate { get; }
    public string SummaryText => $"克制 {AdvantageCount} 名敌方 · 平均胜率 {AverageWinRate:0}%";
}

/// <summary>实时教练页“我的英雄”装备槽。</summary>
public sealed class CoachItemSlot
{
    // GSI 每秒推送快照，图标按物品名缓存 BitmapImage，避免每帧重建导致闪图/空白。
    private static readonly Dictionary<string, BitmapImage> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public CoachItemSlot(string name, string imageUrl)
    {
        Name = name;
        Image = IconCache.GetValueOrDefault(imageUrl) ?? CreateImage(imageUrl);
    }

    public string Name { get; }
    public BitmapImage Image { get; }

    private static BitmapImage CreateImage(string url)
    {
        try
        {
            var image = new BitmapImage(new Uri(url));
            IconCache[url] = image;
            return image;
        }
        catch
        {
            return new BitmapImage();
        }
    }
}

/// <summary>实时教练页的全场英雄芯片数据（头像 + 中文名）。</summary>
public sealed class HeroChip
{
    public HeroChip(string name, string iconUrl)
    {
        Name = name;
        IconUrl = iconUrl;
    }

    public string Name { get; set; }
    public string IconUrl { get; set; }
}

public sealed class HeroAbilityMap
{
    private List<object?> _abilities = [];

    [JsonPropertyName("abilities")]
    public List<object?> Abilities
    {
        get => _abilities;
        set => _abilities = value ?? [];
    }

    [JsonIgnore]
    public IEnumerable<string> AbilityIds => Abilities.SelectMany(ReadAbilityIds);

    private static IEnumerable<string> ReadAbilityIds(object? value)
    {
        if (value is string id)
        {
            if (!string.IsNullOrWhiteSpace(id)) yield return id;
            yield break;
        }

        if (value is List<object?> list)
        {
            foreach (var child in list)
            {
                foreach (var abilityId in ReadAbilityIds(child)) yield return abilityId;
            }
        }
    }
}

public sealed class AbilityDefinition
{
    [JsonPropertyName("dname")] public string? DisplayName { get; set; }
    [JsonPropertyName("desc")] public string? Description { get; set; }
    [JsonPropertyName("lore")] public string? Lore { get; set; }
    [JsonPropertyName("img")] public string? ImagePath { get; set; }
    [JsonPropertyName("behavior"), JsonConverter(typeof(FlexibleStringConverter))] public string? Behavior { get; set; }
    [JsonPropertyName("damage_type"), JsonConverter(typeof(FlexibleStringConverter))] public string? DamageType { get; set; }
    private List<AbilityAttribute> _attributes = [];
    [JsonPropertyName("attrib")] public List<AbilityAttribute> Attributes { get => _attributes; set => _attributes = value ?? []; }
    [JsonPropertyName("cd"), JsonConverter(typeof(FlexibleStringConverter))] public string? Cooldown { get; set; }
    [JsonPropertyName("mc"), JsonConverter(typeof(FlexibleStringConverter))] public string? ManaCost { get; set; }
    [JsonIgnore] public string ImageUrl => HeroStat.AssetUrl(ImagePath ?? string.Empty);
}

public sealed class AbilityAttribute
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("value"), JsonConverter(typeof(FlexibleStringConverter))] public string? ValueText { get; set; }
}

public sealed class OfficialHeroSummary
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("name_loc")] public string LocalizedName { get; set; } = string.Empty;
}

public sealed class OfficialItemSummary
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("name_loc")] public string LocalizedName { get; set; } = string.Empty;
    [JsonPropertyName("name_english_loc")] public string EnglishName { get; set; } = string.Empty;
    [JsonPropertyName("neutral_item_tier")] public int NeutralTier { get; set; }
    [JsonPropertyName("is_pregame_suggested")] public bool IsPregameSuggested { get; set; }
    [JsonPropertyName("is_earlygame_suggested")] public bool IsEarlyGameSuggested { get; set; }
    [JsonPropertyName("is_lategame_suggested")] public bool IsLateGameSuggested { get; set; }
    [JsonPropertyName("item_category")] public string CategoryCode { get; set; } = string.Empty;
    [JsonPropertyName("item_type")] public string TypeCode { get; set; } = string.Empty;
    [JsonPropertyName("item_shop_tags")] public string ShopTags { get; set; } = string.Empty;

    [JsonIgnore]
    public List<ItemRecipeComponent> RecipeComponents { get; } = [];

    [JsonIgnore]
    public bool IsRecipe => Name.StartsWith("item_recipe_", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string ImageUrl
    {
        get => ImageUrlFor(Name);
    }

    public static string ImageUrlFor(string name)
    {
        if (name.StartsWith("item_recipe_", StringComparison.OrdinalIgnoreCase))
        {
            return "https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/recipe.png";
        }

        var shortName = name.StartsWith("item_", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;
        return $"https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/{shortName}.png";
    }
}

public sealed class ItemRecipeComponent
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string LocalizedName { get; init; } = string.Empty;
    public string EnglishName { get; init; } = string.Empty;
    public int Count { get; init; } = 1;
    public string CountText => $"x{Count}";
    public string DisplayName => LocalizedText.ChineseOr(LocalizedName, "中文名称待同步");
    public string ImageUrl => OfficialItemSummary.ImageUrlFor(Name);
}

public sealed class OfficialItemData
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("name_loc")] public string LocalizedName { get; set; } = string.Empty;
    [JsonPropertyName("desc_loc")] public string? Description { get; set; }
    [JsonPropertyName("lore_loc")] public string? Lore { get; set; }
    private List<string> _notes = [];
    private List<AbilitySpecialValue> _specialValues = [];
    [JsonPropertyName("notes_loc")] public List<string> Notes { get => _notes; set => _notes = value ?? []; }
    [JsonPropertyName("item_cost")] public int Cost { get; set; }
    [JsonPropertyName("cooldowns"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> Cooldowns { get; set; } = [];
    [JsonPropertyName("mana_costs"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> ManaCosts { get; set; } = [];
    [JsonPropertyName("special_values")] public List<AbilitySpecialValue> SpecialValues { get => _specialValues; set => _specialValues = value ?? []; }
}

public sealed class ItemMetric
{
    public const int MinimumReliableSample = 30;

    public int Picks { get; init; }
    public int Wins { get; init; }
    public double? WinRate => Picks > 0 ? Wins * 100d / Picks : null;
    public bool HasReliableSample => Picks >= MinimumReliableSample;
    public string PicksText => HeroCardItem.CompactNumber(Picks);
    public string WinsText => HeroCardItem.CompactNumber(Wins);
    public string WinRateText => HasReliableSample && WinRate is double value ? $"{value:0.0}%" : "样本不足";
}

public sealed class ItemCardItem
{
    public ItemCardItem(OfficialItemSummary item, ItemMetric? metric)
    {
        Item = item;
        Metric = metric ?? new ItemMetric();
        Image = new BitmapImage(new Uri(item.ImageUrl));
    }

    public OfficialItemSummary Item { get; }
    public ItemMetric Metric { get; }
    public BitmapImage Image { get; }
    public string Name => LocalizedText.ChineseOr(Item.LocalizedName, "中文名称待同步");
    public string EnglishName => Item.EnglishName;
    public string PicksText => Metric.Picks > 0 ? $"购买 {Metric.PicksText}" : "暂无统计";
    public string WinRateText => Metric.Picks == 0
        ? "胜率 --"
        : Metric.HasReliableSample ? $"胜率 {Metric.WinRateText}" : "样本不足";
    public string TierText => CurrentItemCatalog.IsNeutralItem(Item.Name)
        ? (Item.NeutralTier > 0 ? $"中立物品 T{Item.NeutralTier}" : "中立物品 · 附魔")
        : CurrentItemCatalog.IsSpecialItem(Item.Name) ? "特殊物品" : "商店物品";
    public string CategoryLabel => ItemCategory.Classify(Item);
    public string ShopSection => CurrentItemCatalog.IsNeutralItem(Item.Name) ? "中立物品" :
        CategoryLabel is "消耗品" or "属性" or "装备" or "其他" or "神秘商店" ? "基础分类" : "合成分类";
}

public static class ItemCategory
{
    public static readonly IReadOnlyList<string> Order =
        ["消耗品", "属性", "装备", "其他", "神秘商店", "配件", "辅助", "法器", "防具", "兵刃", "宝物", "中立物品"];

    public static string Classify(OfficialItemSummary item)
    {
        if (CurrentItemCatalog.IsNeutralItem(item.Name)) return "中立物品";
        if (CurrentItemCatalog.IsSpecialItem(item.Name)) return "其他";
        var source = string.Join(" ", item.CategoryCode, item.TypeCode, item.ShopTags).ToLowerInvariant();
        if (source.Contains("consum") || source.Contains("消耗")) return "消耗品";
        if (source.Contains("attribute") || source.Contains("属性")) return "属性";
        if (source.Contains("support") || source.Contains("辅助")) return "辅助";
        if (source.Contains("magic") || source.Contains("arcane") || source.Contains("魔法")) return "法器";
        if (source.Contains("armor") || source.Contains("defense") || source.Contains("护甲")) return "防具";
        if (source.Contains("weapon") || source.Contains("武器")) return "兵刃";
        if (source.Contains("artifact") || source.Contains("神器")) return "宝物";

        var name = item.Name.ToLowerInvariant();
        if (name.Contains("tango") || name.Contains("healing_salv") || name.Contains("clarity") ||
            name.Contains("ward_observer") || name.Contains("ward_sentry") || name.Contains("dust") ||
            name.Contains("smoke_of_deceit") || name.Contains("faerie_fire") || name.Contains("bottle"))
            return "消耗品";
        if (name.Contains("branch") || name.Contains("gauntlet") || name.Contains("slippers") ||
            name.Contains("mantle") || name.Contains("circlet") || name.Contains("belt_of_strength") ||
            name.Contains("band_of_elvenskin") || name.Contains("null_talisman") ||
            name.Contains("wraith_band") || name.Contains("bracer"))
            return "属性";
        if (name.Contains("armor") || name.Contains("chainmail") || name.Contains("plate_mail") ||
            name.Contains("ring_of_protection") || name.Contains("cloak") || name.Contains("hood_of_defiance"))
            return "防具";
        if (name.Contains("blade") || name.Contains("sword") || name.Contains("axe") ||
            name.Contains("javelin") || name.Contains("desolator") || name.Contains("rapier"))
            return "兵刃";
        if (name.Contains("staff") || name.Contains("scepter") || name.Contains("octarine") ||
            name.Contains("refresher") || name.Contains("shiva") || name.Contains("aghs"))
            return "宝物";
        if (item.RecipeComponents.Count > 0) return "配件";
        return "装备";
    }
}

public sealed class ItemCategoryGroup
{
    public ItemCategoryGroup(string name, IEnumerable<ItemCardItem> items)
    {
        Name = name;
        Items = new ObservableCollection<ItemCardItem>(items);
    }

    public string Name { get; }
    public ObservableCollection<ItemCardItem> Items { get; }
    public string CountText => $"{Items.Count} 件";
}

public static class LocalizedText
{
    public static bool ContainsChinese(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Any(character => character is >= '\u3400' and <= '\u9fff');

    public static string ChineseOr(string? value, string fallback) => ContainsChinese(value) ? value!.Trim() : fallback;

    public static string? ChineseOrNull(string? value) => ContainsChinese(value) ? value!.Trim() : null;
}

public sealed class OfficialAbility
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("name_loc")] public string LocalizedName { get; set; } = string.Empty;
    [JsonPropertyName("desc_loc")] public string? Description { get; set; }
    [JsonPropertyName("lore_loc")] public string? Lore { get; set; }
    private List<string> _notes = [];
    private List<AbilitySpecialValue> _specialValues = [];
    [JsonPropertyName("notes_loc")] public List<string> Notes { get => _notes; set => _notes = value ?? []; }
    [JsonPropertyName("shard_loc")] public string? Shard { get; set; }
    [JsonPropertyName("scepter_loc")] public string? Scepter { get; set; }
    [JsonPropertyName("cooldowns"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> Cooldowns { get; set; } = [];
    [JsonPropertyName("mana_costs"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> ManaCosts { get; set; } = [];
    [JsonPropertyName("special_values")] public List<AbilitySpecialValue> SpecialValues { get => _specialValues; set => _specialValues = value ?? []; }
}

public sealed class AbilitySpecialValue
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    private List<double> _values = [];
    private List<double> _alternateValues = [];
    [JsonPropertyName("values_float"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> Values { get => _values; set => _values = value ?? []; }
    [JsonPropertyName("values"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> AlternateValues { get => _alternateValues; set => _alternateValues = value ?? []; }
    [JsonPropertyName("is_percentage")] public bool IsPercentage { get; set; }

    [JsonIgnore]
    public IReadOnlyList<double> EffectiveValues => Values.Count > 0 ? Values : AlternateValues;
}

public sealed class LocalizedHeroData
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("name_loc")] public string LocalizedName { get; set; } = string.Empty;
    [JsonPropertyName("bio_loc")] public string Biography { get; set; } = string.Empty;
    [JsonPropertyName("hype_loc")] public string Hype { get; set; } = string.Empty;
    private List<OfficialAbility> _abilities = [];
    private List<OfficialAbility> _talents = [];
    [JsonPropertyName("abilities")] public List<OfficialAbility> Abilities { get => _abilities; set => _abilities = value ?? []; }
    [JsonPropertyName("talents")] public List<OfficialAbility> Talents { get => _talents; set => _talents = value ?? []; }
}

public sealed partial class TalentDisplay
{
    public TalentDisplay(int level, bool isLeft, string text)
    {
        Level = level;
        IsLeft = isLeft;
        LevelText = $"{level}级";
        SideText = isLeft ? "左侧" : "右侧";
        Text = text;
        var numberMatch = TalentNumberRegex().Match(text);
        if (numberMatch.Success)
        {
            NumberPrefix = numberMatch.Groups[1].Value;
            Label = text.Substring(numberMatch.Length).TrimStart(' ', '\u00A0').Trim();
        }
        else
        {
            NumberPrefix = string.Empty;
            Label = text;
        }
    }

    public int Level { get; }
    public bool IsLeft { get; }
    public string LevelText { get; }
    public string SideText { get; }
    public string Text { get; }
    /// <summary>天赋文本开头的数值部分（如 +300 / +2%），用于金色高亮。</summary>
    public string NumberPrefix { get; }
    /// <summary>去掉数值前缀后的天赋描述。</summary>
    public string Label { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [GeneratedRegex(@"^[+\-−]?\d+(?:\.\d+)?(?:%|秒)?")]
    private static partial Regex TalentNumberRegex();
}

public sealed class HeroBuildMetric
{
    public int ItemId { get; init; }
    public int Picks { get; init; }
    public int Wins { get; init; }
    public double? WinRate => Picks > 0 ? Wins * 100d / Picks : null;
    public bool HasReliableSample => Picks >= ItemMetric.MinimumReliableSample;
}

public sealed class BuildRecommendation
{
    public int Order { get; set; }
    public string Phase { get; set; } = "核心";
    public string Name { get; set; } = string.Empty;
    public string StatsText { get; set; } = string.Empty;
    public BitmapImage Image { get; set; } = new();
}

public sealed class WikiTip
{
    public WikiTip(string title, string text)
    {
        Title = title;
        Text = text;
    }

    public string Title { get; }
    public string Text { get; }
}

public static class WikiTipCatalog
{
    private static readonly Dictionary<string, IReadOnlyList<WikiTip>> Tips = new(StringComparer.OrdinalIgnoreCase)
    {
        ["npc_dota_hero_antimage"] =
        [new("对线", "用法力损毁压低敌方蓝量，再用闪烁保持安全距离；不要为了追击把自己闪进没有视野的区域。"),
         new("团战", "优先处理依赖魔法的核心或后排，法术反制留给关键控制，而不是开战前随手使用。")],
        ["npc_dota_hero_axe"] =
        [new("对线", "反击螺旋的触发依赖受击次数；吸引多个近战单位攻击通常比单纯追着目标普攻更有效。"),
         new("团战", "先用战斗饥渴限制退路，再根据敌方站位决定跳吼，避免跳入无法跟进的位置。")],
        ["npc_dota_hero_crystal_maiden"] =
        [new("资源", "奥术光环的价值取决于队伍总魔法池；技能释放前先确认自己不会因站位暴露被打断。"),
         new("团战", "冰封禁制和冰晶爆轰要分开覆盖不同目标，水晶新星主要用于制造减速和安全撤退窗口。")],
        ["npc_dota_hero_pudge"] =
        [new("视野", "钩子命中率首先由视野和敌方补刀路径决定；先控制逃生路线，再寻找直线角度。"),
         new("团战", "肢解不是无条件先手技能，优先留给能打断队友或改变团战结果的目标。")],
        ["npc_dota_hero_zeus"] =
        [new("施法", "雷击可以提供高处视野；先用它确认隐身或树林中的目标，再决定是否交出全局技能。"),
         new("团战", "保持距离并连续施法比追求单次爆发更重要，避免为了补最后一下伤害进入敌方先手范围。")],
        ["npc_dota_hero_invoker"] =
        [new("节奏", "元素祈唤的冷却取决于掌握的技能数；切技能前先想好接下来 5 秒内需要哪一组组合。"),
         new("团战", "超震声波和急速冷却分别是先手与单抓的核心，根据敌方 BKB 冷却决定先交控制还是先交爆发。")],
        ["npc_dota_hero_sniper"] =
        [new("站位", "射程是你的生命线；输出时优先站在树后或高坡，保持与先手英雄至少一个屏幕的距离。"),
         new("团战", "暗杀用于收割残血或打断持续施法，不要开场就交；爆头被动尽量保持对同一目标的持续输出。")],
        ["npc_dota_hero_shadow_fiend"] =
        [new("对线", "影压的施法前摇固定，压人前先预判对方走位方向；近身三连压需要对方进入三炮交叠区域。"),
         new("团战", "魂之挽歌的伤害随灵魂数量提升，入场前先确保灵魂充足，再配合跳刀或吹风打出满额大招。")],
        ["npc_dota_hero_phantom_assassin"] =
        [new("切入", "幻影突袭可以点地面位移；团战先在边缘等待魅影无形冷却，再切入击杀关键后排。"),
         new("发育", "恩赐解脱的暴击率在中期成型前不稳定，优先保证刷钱效率与关键装备节奏。")],
        ["npc_dota_hero_juggernaut"] =
        [new("对线", "剑刃风暴期间可以普攻（输出较低但能触发被动）；对拼时用它规避控制与爆发。"),
         new("团战", "无敌斩的目标选择会优先最近单位；先手前清理小兵或绕后，避免斩到酱油浪费输出。")],
        ["npc_dota_hero_earthshaker"] =
        [new("先手", "沟壑的宽度与施法点决定封路效果；跳大前先用沟壑分割战场，防止敌方反打集结。"),
         new("反手", "余震在跳大命中多人时提供连续控制；留在前排英雄附近，等敌方切入后再反手开大。")],
        ["npc_dota_hero_lina"] =
        [new("对线", "光击阵的施法延迟较长，接在眩晕或减速之后命中率更高；炽魂层数决定后续爆发。"),
         new("团战", "神灭斩留给敌方核心或依赖输出的英雄；不要为了击杀酱油提前交出大招。")],
        ["npc_dota_hero_windranger"] =
        [new("对线", "束缚击需要两个目标或树木；利用小兵与树林制造双捆角度，先手成功率更高。"),
         new("团战", "集中火力期间攻速上限极高，输出前先确认目标身上没有驱散与格挡类效果。")],
        ["npc_dota_hero_storm_spirit"] =
        [new("节奏", "球状闪电的耗蓝随距离增长；先用电光残影耗血，再决定是否远距离滚入击杀。"),
         new("团战", "被沉默时无法使用大招逃生，进场前留意敌方沉默与控制技能的冷却。")],
        ["npc_dota_hero_lion"] =
        [new("游走", "妖术的施法距离优秀，配合穿刺构成稳定二连；游走时优先抓没有逃生技能的英雄。"),
         new("团战", "死亡一指收割残血或秒杀关键目标；大招冷却较长，非必要不用于清兵。")],
        ["npc_dota_hero_disruptor"] =
        [new("团战", "静态风暴的沉默与伤害随持续时间提升；配合队友的控制链能让敌方核心无法反制。"),
         new("反手", "恶念瞥视能把即将逃走的敌人拉回，也可以把切入的敌人送回原位置，注意施法前摇。")],
        ["npc_dota_hero_techies"] =
        [new("布防", "感应地雷的伤害叠加但需要时间埋设；在高频经过的路线提前布置，比临时放置有效得多。"),
         new("防守", "遥控炸弹用于防守高地与肉山坑；留一组炸弹在撤退路线上，防止敌方越塔追击。")],
        ["npc_dota_hero_ogre_magi"] =
        [new("对线", "火焰爆轰的多重施法概率随机；即使触发概率不高，也要保持技能命中率保证基础收益。"),
         new("团战", "嗜血术的攻速与移速加成对队友同样重要；团战前给主力输出与开团英雄补上增益。")],
        ["npc_dota_hero_monkey_king"] =
        [new("对线", "如意棒法的额外攻击在小兵与英雄之间弹跳；利用树林保持多层被动，换血优势更大。"),
         new("团战", "猴子猴孙在狭窄地形收益最高；先手前确认敌方没有大范围清召唤物的技能。")],
        ["npc_dota_hero_vengefulspirit"] =
        [new("辅助", "恐怖波动的减甲是团队增益；开团前先释放，配合物理核心能显著提升集火效率。"),
         new("团战", "复仇光环死亡后仍生效，必要时可以主动换位先手；魔法箭的晕眩接在队友控制后更稳。")],
        ["npc_dota_hero_lich"] =
        [new("对线", "霜冻新星的减速与伤害适合消耗；邪恶祭品提供护甲与回蓝，前期对拼前先吃掉小兵。"),
         new("团战", "连环霜冻在目标密集时弹跳收益最大；先手命中减速目标，再让它扩散到更多单位。")],
    };

    public static IReadOnlyList<WikiTip> ForHero(string heroName)
    {
        if (Tips.TryGetValue(heroName, out var heroTips)) return heroTips;
        return GenerateFromData(heroName);
    }

    /// <summary>按英雄实际技能数据生成实战提示，保证每个英雄都有专属内容。</summary>
    public static IReadOnlyList<WikiTip> ForHero(HeroStat hero, IReadOnlyList<AbilityDisplay> abilities)
    {
        if (hero is null || abilities is null || abilities.Count == 0) return ForHero(hero?.Name ?? string.Empty);
        if (Tips.TryGetValue(hero.Name, out var heroTips)) return heroTips;
        return GenerateFromData(hero.Name, hero, abilities);
    }

    private static IReadOnlyList<WikiTip> GenerateFromData(string heroName, HeroStat? hero = null, IReadOnlyList<AbilityDisplay>? abilities = null)
    {
        var tips = new List<WikiTip>();
        var skills = (abilities ?? []).Where(ability =>
            !string.IsNullOrWhiteSpace(ability.SlotLabel) &&
            ability.SlotLabel != "R" &&
            !ability.TagsText.Contains("被动", StringComparison.OrdinalIgnoreCase)).ToList();
        var ultimate = (abilities ?? []).FirstOrDefault(ability => ability.SlotLabel == "R");
        var basic = skills.FirstOrDefault();

        if (basic is not null)
        {
            var parts = new List<string> { $"对线：{basic.Name} 是你的主要消耗手段" };
            if (!string.IsNullOrWhiteSpace(basic.ManaCostText))
                parts.Add($"单次 {basic.ManaCostText.Replace("魔法", "耗蓝")}，注意蓝量管理");
            if (!string.IsNullOrWhiteSpace(basic.CooldownText))
                parts.Add($"{basic.CooldownText.Replace("冷却", "冷却")}，miss 后有一段真空期");
            tips.Add(new WikiTip("对线", string.Join("；", parts) + "。"));
        }

        if (ultimate is not null && !string.IsNullOrWhiteSpace(ultimate.Name))
        {
            var cd = string.IsNullOrWhiteSpace(ultimate.CooldownText) ? string.Empty : $"，{ultimate.CooldownText}";
            tips.Add(new WikiTip("大招", $"{ultimate.Name} 是改变战局的关键技能{cd}；留到击杀窗口或关键团战再交，不要为收残血提前浪费。"));
        }

        if (skills.Count >= 2)
        {
            var combo = string.Join(" → ", skills.Take(2).Select(skill => skill.Name));
            tips.Add(new WikiTip("连招", $"常用连招顺序：{combo}。先控制或减速再跟伤害，命中率与收益更高。"));
        }

        if (hero is not null)
        {
            var roleTip = hero.Roles.FirstOrDefault()?.ToLowerInvariant() switch
            {
                "核心" => "定位：核心英雄，前期以发育和保证补刀为主，参团前先评估装备成型度与团队节奏。",
                "辅助" => "定位：辅助英雄，前期多承担视野、保护与游走职责，团战优先保证关键技能释放。",
                "爆发" => "定位：爆发英雄，擅长一波带走脆皮；找好切入时机，避免先手被控后无法输出。",
                "先手" => "定位：先手英雄，开团质量决定团战胜负；先确认队友能否跟上，再交出控制。",
                "控制" => "定位：控制英雄，留好关键控制应对敌方切入，比随意交掉更有价值。",
                "肉盾" => "定位：前排英雄，承担吸收伤害与分割战场的职责；推进和守塔时保持站位靠前。",
                "逃生" => "定位：机动英雄，利用位移技能拉扯空间；被打时先保命再考虑反打。",
                "推进" => "定位：推进英雄，利用召唤物或技能加速推塔；推塔时注意敌方绕后抓单。",
                "打野" => "定位：打野英雄，规划刷野路线与参团时机，避免前期脱离团队节奏。",
                _ => string.Empty,
            };
            if (!string.IsNullOrWhiteSpace(roleTip)) tips.Add(new WikiTip("定位", roleTip));

            var mobility = hero.AttackType == "Melee" ? "近战英雄" : "远程英雄";
            var attr = hero.PrimaryAttribute switch
            {
                "str" => "力量",
                "agi" => "敏捷",
                "int" => "智力",
                _ => "全才",
            };
            var attrTip = attr switch
            {
                "力量" => "血量成长高，适合承担前排与开团",
                "敏捷" => "攻速与护甲收益高，中期输出成型快",
                "智力" => "技能强度依赖装备与等级，注意站位保护",
                _ => "属性灵活，出装路线多样",
            };
            var meleeTip = hero.AttackType == "Melee" ? "近战换血成本" : "保持安全输出距离";
            tips.Add(new WikiTip("身板", $"{mobility}·{attr}英雄：{attrTip}；对线期注意{meleeTip}。"));
        }

        if (tips.Count == 0)
        {
            tips.Add(new WikiTip("阅读方式", "这些是根据技能机制整理的实战提示，不是当前版本胜率结论；出装和连招仍应结合阵容、地图视野与比赛时间判断。"));
        }

        return tips;
    }
}

public sealed class DotaCatalog
{
    private List<HeroStat> _heroes = [];
    public List<HeroStat> Heroes { get => _heroes; set => _heroes = value ?? []; }

    [JsonIgnore]
    public Dictionary<string, HeroAbilityMap> HeroAbilities { get; set; } = [];

    [JsonIgnore]
    public Dictionary<string, AbilityDefinition> Abilities { get; set; } = [];
}

public sealed class RankOption
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public override string ToString() => Label;
}

public sealed class SortOption
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public override string ToString() => Label;
}

public sealed class RankMetric
{
    public int Picks { get; init; }
    public int Wins { get; init; }
    public double? WinRate => Picks > 0 ? Wins * 100d / Picks : null;
}

public sealed class HeroCardItem : INotifyPropertyChanged
{
    private double _cardWidth = 180;
    private double _imageHeight = 104;
    private bool _isCompact;
    private double _winRate;
    private string _winRateText = "--";
    private string _sampleText = "0";

    public HeroCardItem(HeroStat hero, RankOption rank, int totalProActions = 0)
    {
        Hero = hero;
        TotalProActions = totalProActions;
        LargeImage = new BitmapImage(new Uri(hero.ImageUrl));
        SmallImage = new BitmapImage(new Uri(hero.IconUrl));
        UpdateRank(rank);
    }

    public HeroStat Hero { get; }
    public int TotalProActions { get; }
    public string Name => LocalizedText.ChineseOr(Hero.LocalizedName, "中文英雄名称待同步");
    public string AttributeLabel => Hero.PrimaryAttribute switch
    {
        "str" => "力量",
        "agi" => "敏捷",
        "int" => "智力",
        _ => "全才",
    };
    public string AttributeShort => Hero.PrimaryAttribute switch
    {
        "str" => "力量",
        "agi" => "敏捷",
        "int" => "智力",
        _ => "全才",
    };
    public string AttackLabel => Hero.AttackType == "Melee" ? "近战" : "远程";
    public Brush AttrBrush => Hero.PrimaryAttribute switch
    {
        "str" => (Brush)Microsoft.UI.Xaml.Application.Current.Resources["HeroAttrStrBrush"],
        "agi" => (Brush)Microsoft.UI.Xaml.Application.Current.Resources["HeroAttrAgiBrush"],
        "int" => (Brush)Microsoft.UI.Xaml.Application.Current.Resources["HeroAttrIntBrush"],
        _ => (Brush)Microsoft.UI.Xaml.Application.Current.Resources["HeroAttrAllBrush"],
    };
    public BitmapImage LargeImage { get; }
    public BitmapImage SmallImage { get; }
    public BitmapImage DisplayImage => IsCompact ? SmallImage : LargeImage;
    public double CardWidth => _cardWidth;
    public double ImageHeight => _imageHeight;
    public bool IsCompact => _isCompact;
    public double WinRate => _winRate;
    public string WinRateText => _winRateText;
    public string SampleText => _sampleText;
    public string BPText
    {
        get
        {
            var actions = Hero.StatInt("pro_pick") + Hero.StatInt("pro_ban");
            return TotalProActions > 0 ? $"{actions * 100d / TotalProActions:0.0}%" : "--";
        }
    }

    public void UpdateRank(RankOption rank)
    {
        var metric = MetricsFor(Hero, rank.Id);
        var reliable = metric.Picks >= ItemMetric.MinimumReliableSample;
        _winRate = reliable ? metric.WinRate ?? 0 : 0;
        _winRateText = reliable && metric.WinRate is double rate ? $"{rate:0.0}%" : "--";
        _sampleText = metric.Picks > 0 && !reliable
            ? $"{CompactNumber(metric.Picks)} · 样本不足"
            : CompactNumber(metric.Picks);
        OnPropertyChanged(nameof(WinRate));
        OnPropertyChanged(nameof(WinRateText));
        OnPropertyChanged(nameof(SampleText));
    }

    public void UpdateLayout(double cardWidth)
    {
        var compact = cardWidth < 148;
        var imageHeight = compact ? 76 : Math.Clamp(cardWidth * 0.56, 92, 150);
        if (Math.Abs(_cardWidth - cardWidth) > 0.1)
        {
            _cardWidth = cardWidth;
            OnPropertyChanged(nameof(CardWidth));
        }

        if (Math.Abs(_imageHeight - imageHeight) > 0.1)
        {
            _imageHeight = imageHeight;
            OnPropertyChanged(nameof(ImageHeight));
        }

        if (_isCompact != compact)
        {
            _isCompact = compact;
            OnPropertyChanged(nameof(IsCompact));
            OnPropertyChanged(nameof(DisplayImage));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public static RankMetric MetricsFor(HeroStat hero, string rankId)
    {
        var ids = rankId == "all" ? Enumerable.Range(1, 8).Select(i => i.ToString()) : [rankId];
        var picks = ids.Sum(id => hero.StatInt($"{id}_pick"));
        var wins = ids.Sum(id => hero.StatInt($"{id}_win"));
        return new RankMetric { Picks = picks, Wins = wins };
    }

    public static string CompactNumber(int value) => value switch
    {
        >= 1_000_000 => $"{value / 1_000_000d:0.#}百万",
        >= 10_000 => $"{value / 10_000d:0.#}万",
        >= 1_000 => $"{value / 1_000d:0.#}千",
        _ => value.ToString("N0"),
    };
}

public sealed class HeroSection
{
    public HeroSection(string id, string title, string subtitle)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
    }

    public string Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public ObservableCollection<HeroCardItem> Heroes { get; } = [];
    public Brush AccentBrush => Id switch
    {
        "all-heroes" => (Brush)Microsoft.UI.Xaml.Application.Current.Resources["CoachGoldBrush"],
        "str" => (Brush)Microsoft.UI.Xaml.Application.Current.Resources["HeroAttrStrBrush"],
        "agi" => (Brush)Microsoft.UI.Xaml.Application.Current.Resources["HeroAttrAgiBrush"],
        "int" => (Brush)Microsoft.UI.Xaml.Application.Current.Resources["HeroAttrIntBrush"],
        _ => (Brush)Microsoft.UI.Xaml.Application.Current.Resources["HeroAttrAllBrush"],
    };
}

/// <summary>英雄详情顶部的属性数值芯片（攻击/护甲/移动/射程/生命/魔法）。</summary>
public sealed class HeroStatChip
{
    public HeroStatChip(string label, string value, Brush color)
    {
        Label = label;
        Value = value;
        Color = color;
    }

    public string Label { get; }
    public string Value { get; }
    public Brush Color { get; }
}

/// <summary>Dota 2 风格天赋树的一行：等级 + 左右两个可选分支。</summary>
public sealed class TalentLevelRow
{
    public TalentLevelRow(int level, TalentDisplay left, TalentDisplay right)
    {
        Level = level;
        LevelText = $"{level}级";
        Left = left;
        Right = right;
    }

    public int Level { get; }
    public string LevelText { get; }
    public TalentDisplay Left { get; }
    public TalentDisplay Right { get; }
}

public sealed class RankPerformanceItem
{
    public RankPerformanceItem(string label, RankMetric metric)
    {
        Label = label;
        var reliable = metric.Picks >= ItemMetric.MinimumReliableSample;
        WinRate = reliable ? metric.WinRate ?? 0 : 0;
        WinRateText = reliable && metric.WinRate is double rate ? $"{rate:0.0}%" : "--";
        SampleText = reliable
            ? $"样本 {HeroCardItem.CompactNumber(metric.Picks)}"
            : $"样本 {HeroCardItem.CompactNumber(metric.Picks)} · 样本不足";
    }

    public string Label { get; }
    public double WinRate { get; }
    public string WinRateText { get; }
    public string SampleText { get; }
}

public sealed partial class AbilityDisplay
{
    private static readonly Dictionary<string, string> TagMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Unit Target"] = "单位目标", ["Point Target"] = "点目标", ["No Target"] = "无目标",
        ["Passive"] = "被动", ["Toggle"] = "切换", ["Channelled"] = "持续施法",
        ["Instant Cast"] = "即时施法", ["Hidden"] = "隐藏", ["AOE"] = "范围",
        ["Physical"] = "物理", ["Magical"] = "魔法", ["Pure"] = "纯粹",
    };

    public AbilityDisplay(string id, AbilityDefinition? baseData, OfficialAbility? official)
    {
        Id = id;
        Name = LocalizedText.ChineseOr(official?.LocalizedName, "中文技能名称待同步");
        var description = LocalizedText.ChineseOrNull(Clean(official?.Description));
        Description = description is null
            ? "中文说明暂不可用"
            : ResolveParameters(description, official?.SpecialValues, baseData?.Attributes);
        Lore = LocalizedText.ChineseOrNull(Clean(official?.Lore));
        Notes = official?.Notes.Select(Clean).Where(LocalizedText.ContainsChinese).OfType<string>().ToList() ?? [];
        var shard = LocalizedText.ChineseOrNull(Clean(official?.Shard));
        var scepter = LocalizedText.ChineseOrNull(Clean(official?.Scepter));
        Shard = shard is null ? string.Empty : ResolveParameters(shard, official?.SpecialValues, baseData?.Attributes);
        Scepter = scepter is null ? string.Empty : ResolveParameters(scepter, official?.SpecialValues, baseData?.Attributes);
        Cooldown = Series(official?.Cooldowns) ?? baseData?.Cooldown;
        ManaCost = Series(official?.ManaCosts) ?? baseData?.ManaCost;
        Image = CreateImage(baseData?.ImageUrl);
        TagsText = LocalizeTags(baseData?.Behavior, baseData?.DamageType);
        StatsDetails = BuildStats(official?.SpecialValues, baseData?.Attributes);
    }

    public string Id { get; }
    public string SlotLabel { get; set; } = string.Empty;
    public string Name { get; }
    public string Description { get; }
    public string? Lore { get; }
    public List<string> Notes { get; }
    public string? Shard { get; }
    public string? Scepter { get; }
    public string? Cooldown { get; }
    public string? ManaCost { get; }
    public string CooldownText => string.IsNullOrWhiteSpace(Cooldown) ? string.Empty : $"冷却 {Cooldown}";
    public string ManaCostText => string.IsNullOrWhiteSpace(ManaCost) ? string.Empty : $"魔法 {ManaCost}";
    public string ScepterText => string.IsNullOrWhiteSpace(Scepter) ? string.Empty : $"神杖：{Scepter}";
    public string ShardText => string.IsNullOrWhiteSpace(Shard) ? string.Empty : $"魔晶：{Shard}";
    public BitmapImage Image { get; }
    public string TagsText { get; }
    public IReadOnlyList<AbilityDetailStat> StatsDetails { get; }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return System.Text.RegularExpressions.Regex.Replace(value.Replace("<br>", "\n"), "<[^>]+>", string.Empty).Trim();
    }

    private static BitmapImage CreateImage(string? url) =>
        string.IsNullOrWhiteSpace(url) ? new BitmapImage() : new BitmapImage(new Uri(url));

    public static string ResolveParameters(
        string text,
        IEnumerable<AbilitySpecialValue>? officialValues,
        IEnumerable<AbilityAttribute>? openDotaValues)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (officialValues is not null)
        {
            foreach (var value in officialValues)
            {
                if (value.EffectiveValues.Count == 0) continue;
                var suffix = value.IsPercentage ? "%" : string.Empty;
                AddValue(values, value.Name, string.Join(" / ", value.EffectiveValues.Select(number => $"{number:0.#}{suffix}")));
            }
        }

        if (openDotaValues is not null)
        {
            foreach (var value in openDotaValues)
            {
                if (!string.IsNullOrWhiteSpace(value.Key) && !string.IsNullOrWhiteSpace(value.ValueText))
                {
                    AddValue(values, value.Key, value.ValueText!);
                }
            }
        }

        return ParameterRegex().Replace(text, match =>
        {
            var key = match.Groups.Cast<Group>().Skip(1).FirstOrDefault(group => group.Success)?.Value;
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            return values.TryGetValue(NormalizeKey(key), out var replacement) ? replacement : string.Empty;
        }).Replace("%%", "%").Replace("  ", " ");
    }

    public static string ResolveTalentParameters(
        string text,
        OfficialAbility talent,
        AbilityDefinition? baseData)
    {
        var resolved = ResolveParameters(text, talent.SpecialValues, baseData?.Attributes);
        var placeholders = ParameterRegex().Matches(text)
            .Select(match => match.Groups.Values
                .Skip(1)
                .Select(group => group.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        var values = talent.SpecialValues?.SelectMany(value => value.EffectiveValues.Select(number => number.ToString("0.#")))
            .Concat(baseData?.Attributes?.Select(value => value.ValueText).OfType<string>() ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];

        // Some talent definitions expose one value under a key that does not
        // match the localized placeholder. Re-resolve from the original text
        // so the number is not lost by the general description sanitizer.
        if (placeholders.Count > 0 && values.Count == 1 && !Regex.IsMatch(resolved, @"[+\-−]?\d"))
        {
            resolved = ParameterRegex().Replace(text, _ => values[0]);
        }

        // Generic talents encode the value in the definition id, for example
        // special_bonus_hp_300. This fallback is limited to a generic "value"
        // placeholder so named gameplay variables are never guessed.
        if (placeholders.Count > 0 &&
            placeholders.All(key => key.Equals("value", StringComparison.OrdinalIgnoreCase)) &&
            !Regex.IsMatch(resolved, @"[+\-−]?\d"))
        {
            var match = Regex.Match(talent.Name, @"(?:^|_)(\d+(?:\.\d+)?)$");
            if (match.Success) resolved = ParameterRegex().Replace(text, _ => match.Groups[1].Value);
        }

        resolved = ParameterRegex().Replace(resolved, string.Empty).Replace("%%", "%").Replace("  ", " ").Trim();

        // 数值来源缺失时，去掉残留的孤立符号/单位（如 "+% 吸血灵魂吸血" -> "吸血灵魂吸血"）。
        if (!Regex.IsMatch(resolved, @"^[+\-−]?\d"))
        {
            resolved = Regex.Replace(resolved, @"^[+\-−]?%?(秒)?\s+", string.Empty);
        }

        return resolved;
    }

    [GeneratedRegex(@"\{s:([^}]+)\}|%([^%]+)%|\{\{([^}]+)\}\}|\{([^{}]+)\}")]
    private static partial Regex ParameterRegex();

    private static void AddValue(Dictionary<string, string> values, string? key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;
        var normalized = NormalizeKey(key);
        values.TryAdd(NormalizeKey(key), value);
        values.TryAdd(normalized.Replace('_', ' '), value);
    }

    private static string NormalizeKey(string key) => key.Trim().Trim('%', '{', '}')
        .Replace("s:", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Trim().ToLowerInvariant();

    private static string? Series(IEnumerable<double>? values)
    {
        var useful = values?.Where(value => value > 0).ToList();
        return useful is { Count: > 0 } ? string.Join(" / ", useful.Select(value => value % 1 == 0 ? value.ToString("0") : value.ToString("0.#"))) : null;
    }

    private static string LocalizeTags(string? behavior, string? damageType)
    {
        var values = new[] { behavior, damageType }.Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(" / ", StringSplitOptions.RemoveEmptyEntries))
            .Select(value => TagMap.TryGetValue(value.Trim(), out var localized) ? localized : null)
            .OfType<string>();
        return string.Join(" / ", values);
    }

    public static IReadOnlyList<AbilityDetailStat> BuildStats(
        IEnumerable<AbilitySpecialValue>? officialValues,
        IEnumerable<AbilityAttribute>? openDotaValues)
    {
        var stats = new List<AbilityDetailStat>();
        if (officialValues is not null)
        {
            foreach (var value in officialValues)
            {
                if (value.EffectiveValues.Count == 0 || value.Name.StartsWith("Ability", StringComparison.OrdinalIgnoreCase)) continue;
                var label = HumanizeStatName(value.Name);
                var suffix = value.IsPercentage ? "%" : string.Empty;
                stats.Add(new AbilityDetailStat(label,
                    string.Join(" / ", value.EffectiveValues.Select(number => $"{number:0.#}{suffix}"))));
            }
        }

        if (stats.Count == 0 && openDotaValues is not null)
        {
            stats.AddRange(openDotaValues
                .Where(value => !string.IsNullOrWhiteSpace(value.Key) && !string.IsNullOrWhiteSpace(value.ValueText))
                .Select(value => new AbilityDetailStat(HumanizeStatName(value.Key), value.ValueText!)));
        }

        return stats.Take(12).ToList();
    }

    private static string HumanizeStatName(string name)
    {
        var known = name.ToLowerInvariant() switch
        {
            "damage" => "伤害",
            "duration" => "持续时间",
            "radius" => "作用范围",
            "range" => "施法距离",
            "speed" => "速度",
            "slow" => "减速",
            "stun_duration" => "眩晕时间",
            "damage_per_second" => "每秒伤害",
            "bonus_damage" => "额外伤害",
            "attack_speed" => "攻击速度",
            "movement_speed" => "移动速度",
            _ => string.Empty,
        };
        if (!string.IsNullOrEmpty(known)) return known;

        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("heal")) return "治疗效果";
        if (normalized.Contains("mana")) return "魔法效果";
        if (normalized.Contains("armor")) return "护甲";
        if (normalized.Contains("strength")) return "力量";
        if (normalized.Contains("agility")) return "敏捷";
        if (normalized.Contains("intellect")) return "智力";
        if (normalized.Contains("health")) return "生命";
        if (normalized.Contains("attack")) return "攻击效果";
        if (normalized.Contains("cast")) return "施法效果";
        if (normalized.Contains("duration")) return "持续时间";
        if (normalized.Contains("cooldown")) return "冷却时间";
        return "数值参数";
    }
}

public sealed class FlexibleDoubleListConverter : JsonConverter<List<double>>
{
    public override List<double> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new List<double>();
        if (reader.TokenType == JsonTokenType.Null) return result;
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            AddNumber(result, reader);
            return result;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            AddNumber(result, reader);
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, List<double> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var number in value) writer.WriteNumberValue(number);
        writer.WriteEndArray();
    }

    private static void AddNumber(List<double> values, Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var number)) values.Add(number);
        else if (reader.TokenType == JsonTokenType.String && double.TryParse(reader.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number)) values.Add(number);
    }
}

/// <summary>
/// JsonElement → 安全原始值。System.Text.Json 的 JsonElement 在底层 JsonDocument
/// 释放后失效（Clone 也不可靠），缓存持久化前必须物化。
/// </summary>
internal static class JsonSafe
{
    public static object? ToPrimitive(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Array => element.EnumerateArray().Select(ToPrimitive).ToList(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => ToPrimitive(property.Value),
            StringComparer.Ordinal),
        _ => null,
    };
}

/// <summary>把 JSON 中的字符串/数字/数组统一读成字符串（用于技能行为、伤害类型、冷却等展示字段）。</summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var integer)
                    ? integer.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            case JsonTokenType.StartArray:
            {
                using var document = JsonDocument.ParseValue(ref reader);
                var parts = new List<string>();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var text = item.ValueKind switch
                    {
                        JsonValueKind.String => item.GetString(),
                        JsonValueKind.Number => item.TryGetInt64(out var number)
                            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            : item.GetDouble().ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                        _ => null,
                    };
                    if (!string.IsNullOrWhiteSpace(text)) parts.Add(text!);
                }

                return parts.Count > 0 ? string.Join(" / ", parts) : null;
            }
            default:
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>
/// HeroStat 的 JSON 转换器：把 heroStats 的全部扩展字段物化为原始值（long/double/string/bool/List），
/// 已知字段写入强类型属性，其余保留在 <see cref="HeroStat.Stats"/>，保证缓存读写安全。
/// </summary>
public sealed class HeroStatConverter : JsonConverter<HeroStat>
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "name", "localized_name", "primary_attr", "attack_type", "img", "icon",
    };

    public override HeroStat? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var all = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            all[property.Name] = JsonSafe.ToPrimitive(property.Value);
        }

        var stat = new HeroStat
        {
            Id = (int)HeroStat.ValueToDouble(all.GetValueOrDefault("id")),
            Name = all.GetValueOrDefault("name") as string ?? string.Empty,
            LocalizedName = all.GetValueOrDefault("localized_name") as string ?? string.Empty,
            PrimaryAttribute = all.GetValueOrDefault("primary_attr") as string ?? string.Empty,
            AttackType = all.GetValueOrDefault("attack_type") as string ?? string.Empty,
            ImagePath = all.GetValueOrDefault("img") as string ?? string.Empty,
            IconPath = all.GetValueOrDefault("icon") as string ?? string.Empty,
        };
        foreach (var key in KnownKeys) all.Remove(key);
        stat.Stats = all;
        return stat;
    }

    public override void Write(Utf8JsonWriter writer, HeroStat value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("id", value.Id);
        writer.WriteString("name", value.Name);
        writer.WriteString("localized_name", value.LocalizedName);
        writer.WriteString("primary_attr", value.PrimaryAttribute);
        writer.WriteString("attack_type", value.AttackType);
        writer.WriteString("img", value.ImagePath);
        writer.WriteString("icon", value.IconPath);
        if (value.Stats is not null)
        {
            foreach (var (key, item) in value.Stats)
            {
                writer.WritePropertyName(key);
                JsonSerializer.Serialize(writer, item, options);
            }
        }

        writer.WriteEndObject();
    }
}
