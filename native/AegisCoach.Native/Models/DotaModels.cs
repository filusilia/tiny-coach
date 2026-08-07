using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AegisCoach.Native.Models;

public sealed class HeroStat
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("localized_name")] public string LocalizedName { get; set; } = string.Empty;
    [JsonPropertyName("primary_attr")] public string PrimaryAttribute { get; set; } = string.Empty;
    [JsonPropertyName("attack_type")] public string AttackType { get; set; } = string.Empty;
    [JsonPropertyName("img")] public string ImagePath { get; set; } = string.Empty;
    [JsonPropertyName("icon")] public string IconPath { get; set; } = string.Empty;
    private Dictionary<string, JsonElement>? _stats;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Stats
    {
        get => _stats;
        set => _stats = value?.ToDictionary(item => item.Key, item => item.Value.Clone());
    }

    [JsonIgnore]
    public string ImageUrl => AssetUrl(ImagePath);

    [JsonIgnore]
    public string IconUrl => AssetUrl(string.IsNullOrWhiteSpace(IconPath) ? ImagePath : IconPath);

    public int StatInt(string key)
    {
        if (Stats is null || !Stats.TryGetValue(key, out var value)) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
    }

    public static string AssetUrl(string path) =>
        path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"https://cdn.cloudflare.steamstatic.com{path}";
}

public sealed class HeroAbilityMap
{
    private List<JsonElement> _abilities = [];

    [JsonPropertyName("abilities")]
    public List<JsonElement> Abilities
    {
        get => _abilities;
        set => _abilities = value.Select(item => item.Clone()).ToList();
    }

    [JsonIgnore]
    public IEnumerable<string> AbilityIds => Abilities.SelectMany(ReadAbilityIds);

    private static IEnumerable<string> ReadAbilityIds(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var id = value.GetString();
            if (!string.IsNullOrWhiteSpace(id)) yield return id;
            yield break;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray())
            {
                foreach (var id in ReadAbilityIds(child)) yield return id;
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
    [JsonPropertyName("behavior")] public JsonElement Behavior { get; set; }
    [JsonPropertyName("damage_type")] public JsonElement DamageType { get; set; }
    private List<AbilityAttribute> _attributes = [];
    [JsonPropertyName("attrib")] public List<AbilityAttribute> Attributes { get => _attributes; set => _attributes = value ?? []; }
    private JsonElement? _cooldown;
    private JsonElement? _manaCost;

    [JsonPropertyName("cd")] public JsonElement? Cooldown { get => _cooldown; set => _cooldown = value?.Clone(); }
    [JsonPropertyName("mc")] public JsonElement? ManaCost { get => _manaCost; set => _manaCost = value?.Clone(); }
    [JsonIgnore] public string ImageUrl => HeroStat.AssetUrl(ImagePath ?? string.Empty);
}

public sealed class AbilityAttribute
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("value")] public JsonElement Value { get; set; }

    [JsonIgnore]
    public string? ValueText => Value.ValueKind switch
    {
        JsonValueKind.String => Value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => Value.ToString(),
        JsonValueKind.Array => string.Join(" / ", Value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => Value.ToString(),
    };
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
    public string DisplayName => string.IsNullOrWhiteSpace(LocalizedName) ? EnglishName : LocalizedName;
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
    public int Picks { get; init; }
    public int Wins { get; init; }
    public double? WinRate => Picks > 0 ? Wins * 100d / Picks : null;
    public string PicksText => HeroCardItem.CompactNumber(Picks);
    public string WinsText => HeroCardItem.CompactNumber(Wins);
    public string WinRateText => WinRate is double value ? $"{value:0.0}%" : "--";
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
    public string Name => string.IsNullOrWhiteSpace(Item.LocalizedName) ? Item.EnglishName : Item.LocalizedName;
    public string EnglishName => Item.EnglishName;
    public string PicksText => Metric.Picks > 0 ? $"购买 {Metric.PicksText}" : "暂无统计";
    public string WinRateText => Metric.WinRate is double ? $"胜率 {Metric.WinRateText}" : "胜率 --";
    public string TierText => Item.NeutralTier > 0 ? $"中立物品 T{Item.NeutralTier}" : "普通物品";
    public string CategoryLabel => ItemCategory.Classify(Item);
}

public static class ItemCategory
{
    public static readonly IReadOnlyList<string> Order =
        ["全部", "消耗品", "属性", "装备", "辅助", "魔法", "护甲", "武器", "神器", "中立物品", "其他"];

    public static string Classify(OfficialItemSummary item)
    {
        if (item.NeutralTier > 0) return "中立物品";
        var source = string.Join(" ", item.CategoryCode, item.TypeCode, item.ShopTags).ToLowerInvariant();
        if (source.Contains("consum") || source.Contains("消耗")) return "消耗品";
        if (source.Contains("attribute") || source.Contains("属性")) return "属性";
        if (source.Contains("support") || source.Contains("辅助")) return "辅助";
        if (source.Contains("magic") || source.Contains("arcane") || source.Contains("魔法")) return "魔法";
        if (source.Contains("armor") || source.Contains("defense") || source.Contains("护甲")) return "护甲";
        if (source.Contains("weapon") || source.Contains("武器")) return "武器";
        if (source.Contains("artifact") || source.Contains("神器")) return "神器";

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
            return "护甲";
        if (name.Contains("blade") || name.Contains("sword") || name.Contains("axe") ||
            name.Contains("javelin") || name.Contains("desolator") || name.Contains("rapier"))
            return "武器";
        if (name.Contains("staff") || name.Contains("scepter") || name.Contains("octarine") ||
            name.Contains("refresher") || name.Contains("shiva") || name.Contains("aghs"))
            return "神器";
        return "装备";
    }
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
    private List<OfficialAbility> _abilities = [];
    private List<OfficialAbility> _talents = [];
    [JsonPropertyName("abilities")] public List<OfficialAbility> Abilities { get => _abilities; set => _abilities = value ?? []; }
    [JsonPropertyName("talents")] public List<OfficialAbility> Talents { get => _talents; set => _talents = value ?? []; }
}

public sealed class TalentDisplay
{
    public TalentDisplay(int level, string side, string text)
    {
        LevelText = $"{level}级";
        SideText = side;
        Text = text;
    }

    public string LevelText { get; }
    public string SideText { get; }
    public string Text { get; }
}

public sealed class HeroBuildMetric
{
    public int ItemId { get; init; }
    public int Picks { get; init; }
    public int Wins { get; init; }
    public double? WinRate => Picks > 0 ? Wins * 100d / Picks : null;
}

public sealed class BuildRecommendation
{
    public int Order { get; set; }
    public string Phase { get; set; } = "核心";
    public string Name { get; set; } = string.Empty;
    public string EnglishName { get; set; } = string.Empty;
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
    };

    public static IReadOnlyList<WikiTip> ForHero(string heroName)
    {
        if (Tips.TryGetValue(heroName, out var heroTips)) return heroTips;
        return
        [new("阅读方式", "这些是根据技能机制整理的实战提示，不是当前版本胜率结论；出装和连招仍应结合阵容、地图视野与比赛时间判断。"),
         new("信息边界", "技能数值来自官方资料源，技巧属于编辑性补充，版本更新后应重新核对。")];
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
    public string Name => Hero.LocalizedName;
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
        _winRate = metric.WinRate ?? 0;
        _winRateText = metric.WinRate is double rate ? $"{rate:0.0}%" : "--";
        _sampleText = CompactNumber(metric.Picks);
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
}

public sealed class RankPerformanceItem
{
    public RankPerformanceItem(string label, RankMetric metric)
    {
        Label = label;
        WinRate = metric.WinRate ?? 0;
        WinRateText = metric.WinRate is double rate ? $"{rate:0.0}%" : "--";
        SampleText = $"样本 {HeroCardItem.CompactNumber(metric.Picks)}";
    }

    public string Label { get; }
    public double WinRate { get; }
    public string WinRateText { get; }
    public string SampleText { get; }
}

public sealed partial class AbilityDisplay
{
    private static readonly Dictionary<string, string> TagMap = new()
    {
        ["Unit Target"] = "单位目标", ["Point Target"] = "点目标", ["No Target"] = "无目标",
        ["Passive"] = "被动", ["Toggle"] = "切换", ["Channelled"] = "持续施法",
        ["Instant Cast"] = "即时施法", ["Hidden"] = "隐藏", ["AOE"] = "范围",
        ["Physical"] = "物理", ["Magical"] = "魔法", ["Pure"] = "纯粹",
    };

    public AbilityDisplay(string id, AbilityDefinition? baseData, OfficialAbility? official)
    {
        Id = id;
        Name = FirstText(official?.LocalizedName, baseData?.DisplayName, id.Replace('_', ' '));
        Description = ResolveParameters(Clean(official?.Description) ?? baseData?.Description ?? "暂无说明", official?.SpecialValues, baseData?.Attributes);
        Lore = Clean(official?.Lore) ?? baseData?.Lore;
        Notes = official?.Notes.Select(Clean).OfType<string>().ToList() ?? [];
        Shard = ResolveParameters(Clean(official?.Shard) ?? string.Empty, official?.SpecialValues, baseData?.Attributes);
        Scepter = ResolveParameters(Clean(official?.Scepter) ?? string.Empty, official?.SpecialValues, baseData?.Attributes);
        Cooldown = Series(official?.Cooldowns) ?? JsonSeries(baseData?.Cooldown);
        ManaCost = Series(official?.ManaCosts) ?? JsonSeries(baseData?.ManaCost);
        Image = CreateImage(baseData?.ImageUrl);
        TagsText = LocalizeTags(JsonText(baseData?.Behavior), JsonText(baseData?.DamageType));
    }

    public string Id { get; }
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

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return System.Text.RegularExpressions.Regex.Replace(value.Replace("<br>", "\n"), "<[^>]+>", string.Empty).Trim();
    }

    private static string FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "未知技能";

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
        var values = talent.SpecialValues?.SelectMany(value => value.EffectiveValues.Select(number => number.ToString("0.#")))
            .Concat(baseData?.Attributes?.Select(value => value.ValueText).OfType<string>() ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];

        // Most special_bonus definitions expose exactly one attrib value. It is
        // safe to use it only for a talent, where the localized text names one
        // gameplay variable and otherwise leaves a placeholder behind.
        if (values.Count == 1)
        {
            resolved = ParameterRegex().Replace(resolved, _ => values[0]);
        }

        // A few generic bonuses encode the value in the ability id, for example
        // special_bonus_hp_300. This is the final fallback when the API omits attrib.
        if (resolved.Contains('{') || resolved.Contains('%'))
        {
            var match = Regex.Match(talent.Name, @"(?:^|_)(\d+(?:\.\d+)?)$");
            if (match.Success) resolved = ParameterRegex().Replace(resolved, _ => match.Groups[1].Value);
        }

        return ParameterRegex().Replace(resolved, string.Empty).Replace("%%", "%").Replace("  ", " ").Trim();
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

    private static string? JsonSeries(JsonElement? element)
    {
        if (element is not JsonElement value) return null;
        if (value.ValueKind == JsonValueKind.Array)
        {
            var values = value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Number).Select(item => item.GetDouble());
            return Series(values);
        }
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? JsonText(JsonElement? element)
    {
        if (element is not JsonElement value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind != JsonValueKind.Array) return null;

        var values = value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
            .OfType<string>()
            .Where(item => !string.IsNullOrWhiteSpace(item));
        return string.Join(" / ", values);
    }

    private static string LocalizeTags(string? behavior, string? damageType)
    {
        var values = new[] { behavior, damageType }.Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(" / ", StringSplitOptions.RemoveEmptyEntries))
            .Select(value => TagMap.TryGetValue(value, out var localized) ? localized : value);
        return string.Join(" / ", values);
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
