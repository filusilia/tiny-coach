using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AegisCoach.Native.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AegisCoach.Native.Models;

using AegisCoach.Native.Services;

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

    /// <summary>官网英雄列表数据提供的本地化大图（优先于 Steam CDN 图）。</summary>
    [JsonIgnore]
    public string? ImageUrlOverride { get; set; }

    [JsonIgnore]
    public string ImageUrl => ImageUrlOverride ?? AssetUrl(ImagePath);

    [JsonIgnore]
    public string IconUrl => ImageUrlOverride ?? AssetUrl(string.IsNullOrWhiteSpace(IconPath) ? ImagePath : IconPath);

    [JsonIgnore]
    public string PrimaryAttributeIcon => PrimaryAttribute.ToLowerInvariant().Trim() switch
    {
        "str" or "strength" or "0" => "ms-appx:///Assets/Icons/hero_strength.png",
        "agi" or "agility" or "1" => "ms-appx:///Assets/Icons/hero_agility.png",
        "int" or "intelligence" or "2" => "ms-appx:///Assets/Icons/hero_intelligence.png",
        _ => "ms-appx:///Assets/Icons/hero_universal.png",
    };

    [JsonIgnore]
    public string AttackTypeIcon => AttackType.Equals("Melee", StringComparison.OrdinalIgnoreCase) || AttackType.Equals("近战", StringComparison.OrdinalIgnoreCase)
        ? "ms-appx:///Assets/Icons/melee.svg"
        : "ms-appx:///Assets/Icons/ranged.svg";

    [JsonIgnore]
    public string AttackShortLabel => AttackType.Equals("Melee", StringComparison.OrdinalIgnoreCase) ? "近战" : "远程";

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
        IconUrl = ImageCacheService.Instance.ResolveText(iconUrl);
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
        IconUrl = ImageCacheService.Instance.ResolveText(iconUrl);
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
            var image = new BitmapImage(ImageCacheService.Instance.Resolve(url));
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
        IconUrl = ImageCacheService.Instance.ResolveText(iconUrl);
    }

    public string Name { get; set; }
    public string IconUrl { get; set; }
}

public sealed class HeroAbilityMap
{
    private List<object?> _abilities = [];
    private List<object?> _innates = [];

    [JsonPropertyName("abilities")]
    public List<object?> Abilities
    {
        get => _abilities;
        set => _abilities = value ?? [];
    }

    [JsonPropertyName("innates")]
    public List<object?> Innates
    {
        get => _innates;
        set => _innates = value ?? [];
    }

    [JsonIgnore]
    public IEnumerable<string> AbilityIds => Abilities.SelectMany(ReadAbilityIds);

    [JsonIgnore]
    public IEnumerable<string> InnateIds => Innates.SelectMany(ReadAbilityIds);

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
    [JsonPropertyName("is_innate")] public bool IsInnate { get; set; }
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
    [JsonPropertyName("name_loc")] public string NameLoc { get; set; } = string.Empty;
    [JsonPropertyName("localized_name")] public string LocalizedNameProp { get; set; } = string.Empty;
    [JsonPropertyName("name_english_loc")] public string EnglishName { get; set; } = string.Empty;

    public string LocalizedName
    {
        get => !string.IsNullOrWhiteSpace(NameLoc) ? NameLoc : (!string.IsNullOrWhiteSpace(LocalizedNameProp) ? LocalizedNameProp : DotaHeroNames.GetChineseName(Id, Name));
        set => NameLoc = value;
    }
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
    [JsonPropertyName("item_cost")] public int Cost { get; set; }

    /// <summary>官网物品列表提供的图片 URL（优先于 Steam CDN 图）。</summary>
    [JsonIgnore]
    public string? ImageUrlOverride { get; set; }

    [JsonIgnore]
    public List<ItemRecipeComponent> RecipeComponents { get; } = [];

    [JsonIgnore]
    public bool IsRecipe => Name.StartsWith("item_recipe_", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string ImageUrl => ImageUrlOverride ?? ImageUrlFor(Name);

    [JsonIgnore]
    public string DisplayName => DotaItemNameTable.ResolveChineseName(Name, LocalizedName, EnglishName, Id);

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

/// <summary>Dota 2 官方标准物品中文名称映射字典（防止外部 API 仅返回英文标识符或空中文名）。</summary>
public static class DotaItemNameTable
{
    private static readonly Dictionary<string, string> NameMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, string> IdMap = new();

    static DotaItemNameTable()
    {
        // 1. 从内置 Assets/Data/items.json 自动提取 500+ 官方全量中文字典
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Assets", "Data", "items.json"),
                Path.Combine(baseDir, "Assets", "items.json"),
                @"E:\openSource\demo\native\AegisCoach.Native\Assets\Data\items.json",
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    using var stream = File.OpenRead(path);
                    using var doc = JsonDocument.Parse(stream);
                    if (doc.RootElement.TryGetProperty("result", out var res) &&
                        res.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("itemabilities", out var itemabilities))
                    {
                        foreach (var item in itemabilities.EnumerateArray())
                        {
                            var id = item.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                            var name = item.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? string.Empty : string.Empty;
                            var nameLoc = item.TryGetProperty("name_loc", out var nlProp) ? nlProp.GetString() ?? string.Empty : string.Empty;
                            if (LocalizedText.ContainsChinese(nameLoc))
                            {
                                var trimmed = nameLoc.Trim();
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    NameMap[name] = trimmed;
                                    if (name.StartsWith("item_")) NameMap[name[5..]] = trimmed;
                                }
                                if (id > 0) IdMap[id] = trimmed;
                            }
                        }
                    }
                    break;
                }
            }
        }
        catch { }

        // 2. 补充核心及新版本物品字典确保绝对完整
        var fallbackEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["item_blink"] = "闪烁匕首", ["blink"] = "闪烁匕首",
            ["item_overwhelming_blink"] = "强袭闪烁", ["overwhelming_blink"] = "强袭闪烁",
            ["item_swift_blink"] = "迅捷闪烁", ["swift_blink"] = "迅捷闪烁",
            ["item_arcane_blink"] = "奥术闪烁", ["arcane_blink"] = "奥术闪烁",
            ["item_black_king_bar"] = "黑皇杖", ["black_king_bar"] = "黑皇杖",
            ["item_power_treads"] = "动力鞋", ["power_treads"] = "动力鞋",
            ["item_phase_boots"] = "相位鞋", ["phase_boots"] = "相位鞋",
            ["item_arcane_boots"] = "奥术鞋", ["arcane_boots"] = "奥术鞋",
            ["item_tranquil_boots"] = "静谧之鞋", ["tranquil_boots"] = "静谧之鞋",
            ["item_boots_of_bearing"] = "宽容之靴", ["boots_of_bearing"] = "宽容之靴",
            ["item_travel_boots"] = "远行鞋", ["travel_boots"] = "远行鞋",
            ["item_travel_boots_2"] = "远行鞋 2级", ["travel_boots_2"] = "远行鞋 2级",
            ["item_boots"] = "速度之靴", ["boots"] = "速度之靴",
            ["item_manta"] = "幻影斧", ["manta"] = "幻影斧",
            ["item_sange_and_yasha"] = "散夜对剑", ["sange_and_yasha"] = "散夜对剑",
            ["item_kaya_and_sange"] = "慧夜对剑", ["kaya_and_sange"] = "慧夜对剑",
            ["item_yasha_and_kaya"] = "夜慧对剑", ["yasha_and_kaya"] = "夜慧对剑",
            ["item_sange"] = "散华", ["sange"] = "散华",
            ["item_yasha"] = "夜叉", ["yasha"] = "夜叉",
            ["item_kaya"] = "慧光", ["kaya"] = "慧光",
            ["item_bfury"] = "狂战斧", ["bfury"] = "狂战斧",
            ["item_desolator"] = "暗灭", ["desolator"] = "暗灭",
            ["item_butterfly"] = "蝴蝶", ["butterfly"] = "蝴蝶",
            ["item_satanic"] = "撒旦之邪力", ["satanic"] = "撒旦之邪力",
            ["item_skadi"] = "斯嘉蒂之眼", ["skadi"] = "斯嘉蒂之眼",
            ["item_abyssal_blade"] = "深渊之刃", ["abyssal_blade"] = "深渊之刃",
            ["item_basher"] = "碎颅锤", ["basher"] = "碎颅锤",
            ["item_monkey_king_bar"] = "金箍棒", ["monkey_king_bar"] = "金箍棒",
            ["item_rapier"] = "圣剑", ["rapier"] = "圣剑",
            ["item_bloodthorn"] = "血棘", ["bloodthorn"] = "血棘",
            ["item_orchid"] = "紫怨", ["orchid"] = "紫怨",
            ["item_nullifier"] = "否决挂穗", ["nullifier"] = "否决挂穗",
            ["item_radiance"] = "辉耀", ["radiance"] = "辉耀",
            ["item_shivas_guard"] = "希瓦的守护", ["shivas_guard"] = "希瓦的守护",
            ["item_assault"] = "强袭胸甲", ["assault"] = "强袭胸甲",
            ["item_heart"] = "恐鳌之心", ["heart"] = "恐鳌之心",
            ["item_octarine_core"] = "玲珑心", ["octarine_core"] = "玲珑心",
            ["item_refresher"] = "刷新球", ["refresher"] = "刷新球",
            ["item_sphere"] = "林肯法球", ["sphere"] = "林肯法球",
            ["item_lotus_orb"] = "清莲宝珠", ["lotus_orb"] = "清莲宝珠",
            ["item_blade_mail"] = "刃甲", ["blade_mail"] = "刃甲",
            ["item_crimson_guard"] = "赤红甲", ["crimson_guard"] = "赤红甲",
            ["item_pipe"] = "洞察烟斗", ["pipe"] = "洞察烟斗",
            ["item_vanguard"] = "先锋盾", ["vanguard"] = "先锋盾",
            ["item_aeon_disk"] = "永恒之盘", ["aeon_disk"] = "永恒之盘",
            ["item_glimmer_cape"] = "微光披风", ["glimmer_cape"] = "微光披风",
            ["item_force_staff"] = "原力法杖", ["force_staff"] = "原力法杖",
            ["item_hurricane_pike"] = "飓风长戟", ["hurricane_pike"] = "飓风长戟",
            ["item_dragon_lance"] = "魔龙枪", ["dragon_lance"] = "魔龙枪",
            ["item_diffusal_blade"] = "净魂之刃", ["diffusal_blade"] = "净魂之刃",
            ["item_disperser"] = "散失之刃", ["disperser"] = "散失之刃",
            ["item_harpoon"] = "鱼叉", ["harpoon"] = "鱼叉",
            ["item_echo_sabre"] = "回音战刃", ["echo_sabre"] = "回音战刃",
            ["item_mage_slayer"] = "法师克星", ["mage_slayer"] = "法师克星",
            ["item_phylactery"] = "灵匣", ["phylactery"] = "灵匣",
            ["item_khanda"] = "圣洁毁灭", ["khanda"] = "圣洁毁灭",
            ["item_angels_demise"] = "绝刃", ["angels_demise"] = "绝刃",
            ["item_parasma"] = "绝刃", ["parasma"] = "绝刃",
            ["item_devastator"] = "绝刃", ["devastator"] = "绝刃",
            ["item_witch_blade"] = "巫师之刃", ["witch_blade"] = "巫师之刃",
            ["item_hand_of_midas"] = "迈达斯之手", ["hand_of_midas"] = "迈达斯之手",
            ["item_maelstrom"] = "漩涡", ["maelstrom"] = "漩涡",
            ["item_mjollnir"] = "雷神之锤", ["mjollnir"] = "雷神之锤",
            ["item_gungir"] = "缚灵索", ["gungir"] = "缚灵索",
            ["item_gungnir"] = "缚灵索", ["gungnir"] = "缚灵索",
            ["item_rod_of_atos"] = "阿托斯之棍", ["rod_of_atos"] = "阿托斯之棍",
            ["item_spirit_vessel"] = "魂之灵瓮", ["spirit_vessel"] = "魂之灵瓮",
            ["item_urn_of_shadows"] = "影之灵龛", ["urn_of_shadows"] = "影之灵龛",
            ["item_medallion_of_courage"] = "勇气勋章", ["medallion_of_courage"] = "勇气勋章",
            ["item_solar_crest"] = "炎阳纹章", ["solar_crest"] = "炎阳纹章",
            ["item_pavise"] = "坚韧法球", ["pavise"] = "坚韧法球",
            ["item_holy_locket"] = "圣洁吊坠", ["holy_locket"] = "圣洁吊坠",
            ["item_headdress"] = "恢复头巾", ["headdress"] = "恢复头巾",
            ["item_buckler"] = "玄冥盾牌", ["buckler"] = "玄冥盾牌",
            ["item_ring_of_basilius"] = "王者之戒", ["ring_of_basilius"] = "王者之戒",
            ["item_vladmir"] = "弗拉迪米尔的祭品", ["vladmir"] = "弗拉迪米尔的祭品",
            ["item_mekansm"] = "梅肯斯姆", ["mekansm"] = "梅肯斯姆",
            ["item_guardian_greaves"] = "卫士胫甲", ["guardian_greaves"] = "卫士胫甲",
            ["item_veil_of_discord"] = "纷争面纱", ["veil_of_discord"] = "纷争面纱",
            ["item_ancient_janggo"] = "韧鼓", ["ancient_janggo"] = "韧鼓",
            ["item_dagon"] = "达衮之神力", ["dagon"] = "达衮之神力",
            ["item_ethereal_blade"] = "虚灵之刃", ["ethereal_blade"] = "虚灵之刃",
            ["item_cyclone"] = "尤尔的神圣权杖", ["cyclone"] = "尤尔的神圣权杖",
            ["item_wind_waker"] = "风之杖", ["wind_waker"] = "风之杖",
            ["item_sheepstick"] = "邪恶镰刀", ["sheepstick"] = "邪恶镰刀",
            ["item_ultimate_scepter"] = "阿哈利姆神杖", ["ultimate_scepter"] = "阿哈利姆神杖",
            ["item_ultimate_scepter_2"] = "阿哈利姆神杖（合成）",
            ["item_aghanims_shard"] = "阿哈利姆魔晶", ["aghanims_shard"] = "阿哈利姆魔晶",
            ["item_moon_shard"] = "银月之晶", ["moon_shard"] = "银月之晶",
            ["item_silver_edge"] = "白银之锋", ["silver_edge"] = "白银之锋",
            ["item_invis_sword"] = "影刃", ["invis_sword"] = "影刃",
            ["item_armlet"] = "莫尔迪基安的臂章", ["armlet"] = "莫尔迪基安的臂章",
            ["item_meteor_hammer"] = "陨星锤", ["meteor_hammer"] = "陨星锤",
            ["item_orb_of_corrosion"] = "腐蚀之球", ["orb_of_corrosion"] = "腐蚀之球",
            ["item_falcon_blade"] = "猎鹰之刃", ["falcon_blade"] = "猎鹰之刃",
            ["item_soul_ring"] = "灵魂之戒", ["soul_ring"] = "灵魂之戒",
            ["item_mask_of_madness"] = "疯狂面具", ["mask_of_madness"] = "疯狂面具",
            ["item_helm_of_the_dominator"] = "支配头盔", ["helm_of_the_dominator"] = "支配头盔",
            ["item_helm_of_the_overlord"] = "霸者头盔", ["helm_of_the_overlord"] = "霸者头盔",
            ["item_bloodstone"] = "血精石", ["bloodstone"] = "血精石",
            ["item_aether_lens"] = "以太透镜", ["aether_lens"] = "以太透镜",
            ["item_heavens_halberd"] = "天堂之戟", ["heavens_halberd"] = "天堂之戟",
            ["item_tango"] = "树之祭祀", ["tango"] = "树之祭祀",
            ["item_flask"] = "治疗药膏", ["flask"] = "治疗药膏",
            ["item_healing_salve"] = "治疗药膏", ["healing_salve"] = "治疗药膏",
            ["item_clarity"] = "净化药水", ["clarity"] = "净化药水",
            ["item_enchanted_mango"] = "魔法芒果", ["enchanted_mango"] = "魔法芒果",
            ["item_faerie_fire"] = "仙灵之火", ["faerie_fire"] = "仙灵之火",
            ["item_bottle"] = "魔瓶", ["bottle"] = "魔瓶",
            ["item_blood_grenade"] = "血腥手雷", ["blood_grenade"] = "血腥手雷",
            ["item_dust"] = "显影之尘", ["dust"] = "显影之尘",
            ["item_ward_observer"] = "侦查守卫", ["ward_observer"] = "侦查守卫",
            ["item_ward_sentry"] = "岗哨守卫", ["ward_sentry"] = "岗哨守卫",
            ["item_ward_dispenser"] = "假眼真眼", ["ward_dispenser"] = "假眼真眼",
            ["item_smoke_of_deceit"] = "诡计之雾", ["smoke_of_deceit"] = "诡计之雾",
            ["item_tpscroll"] = "回城卷轴", ["tpscroll"] = "回城卷轴",
            ["item_gem"] = "真视宝石", ["gem"] = "真视宝石",
            ["item_magic_stick"] = "魔棒", ["magic_stick"] = "魔棒",
            ["item_magic_wand"] = "魔杖", ["magic_wand"] = "魔杖",
            ["item_branches"] = "铁树枝干", ["branches"] = "铁树枝干",
            ["item_gauntlets"] = "力量手套", ["gauntlets"] = "力量手套",
            ["item_slippers"] = "敏捷便鞋", ["slippers"] = "敏捷便鞋",
            ["item_mantle"] = "智力斗篷", ["mantle"] = "智力斗篷",
            ["item_circlet"] = "圆环", ["circlet"] = "圆环",
            ["item_belt_of_strength"] = "力量腰带", ["belt_of_strength"] = "力量腰带",
            ["item_boots_of_elves"] = "精灵布靴", ["boots_of_elves"] = "精灵布靴",
            ["item_robe"] = "法师长袍", ["robe"] = "法师长袍",
            ["item_crown"] = "王冠", ["crown"] = "王冠",
            ["item_diadem"] = "王冠", ["diadem"] = "王冠",
            ["item_ogre_axe"] = "食人魔之斧", ["ogre_axe"] = "食人魔之斧",
            ["item_blade_of_alacrity"] = "欢欣之刃", ["blade_of_alacrity"] = "欢欣之刃",
            ["item_staff_of_wizardry"] = "魔导士法杖", ["staff_of_wizardry"] = "魔导士法杖",
            ["item_ultimate_orb"] = "极限球", ["ultimate_orb"] = "极限球",
            ["item_fluffy_hat"] = "蓬蓬帽", ["fluffy_hat"] = "蓬蓬帽",
            ["item_wind_lace"] = "风灵之纹", ["wind_lace"] = "风灵之纹",
            ["item_ring_of_protection"] = "守护指环", ["ring_of_protection"] = "守护指环",
            ["item_quelling_blade"] = "压制之刃", ["quelling_blade"] = "压制之刃",
            ["item_blight_stone"] = "枯萎之石", ["blight_stone"] = "枯萎之石",
            ["item_orb_of_venom"] = "淬毒之珠", ["orb_of_venom"] = "淬毒之珠",
            ["item_orb_of_frost"] = "霜冻之珠", ["orb_of_frost"] = "霜冻之珠",
            ["item_blades_of_attack"] = "攻击之爪", ["blades_of_attack"] = "攻击之爪",
            ["item_gloves"] = "加速手套", ["gloves"] = "加速手套",
            ["item_chainmail"] = "锁子甲", ["chainmail"] = "锁子甲",
            ["item_quarterstaff"] = "短棍", ["quarterstaff"] = "短棍",
            ["item_helm_of_iron_will"] = "铁意头盔", ["helm_of_iron_will"] = "铁意头盔",
            ["item_broadsword"] = "阔剑", ["broadsword"] = "阔剑",
            ["item_claymore"] = "大剑", ["claymore"] = "大剑",
            ["item_javelin"] = "标枪", ["javelin"] = "标枪",
            ["item_mithril_hammer"] = "秘银锤", ["mithril_hammer"] = "秘银锤",
            ["item_blitz_knuckles"] = "闪电指套", ["blitz_knuckles"] = "闪电指套",
            ["item_platemail"] = "板甲", ["platemail"] = "板甲",
            ["item_talisman_of_evasion"] = "闪避护符", ["talisman_of_evasion"] = "闪避护符",
            ["item_hyperstone"] = "振奋宝石", ["hyperstone"] = "振奋宝石",
            ["item_demon_edge"] = "恶魔刀锋", ["demon_edge"] = "恶魔刀锋",
            ["item_mystic_staff"] = "神秘法杖", ["mystic_staff"] = "神秘法杖",
            ["item_reaver"] = "掠夺者之斧", ["reaver"] = "掠夺者之斧",
            ["item_eagle"] = "鹰歌弓", ["eagle"] = "鹰歌弓",
            ["item_relic"] = "圣者遗物", ["relic"] = "圣者遗物",
            ["item_ring_of_regen"] = "回复戒指", ["ring_of_regen"] = "回复戒指",
            ["item_sobi_mask"] = "贤者面具", ["sobi_mask"] = "贤者面具",
            ["item_ring_of_tarrasque"] = "恐鳌之戒", ["ring_of_tarrasque"] = "恐鳌之戒",
            ["item_ring_of_health"] = "治疗指环", ["ring_of_health"] = "治疗指环",
            ["item_void_stone"] = "虚无宝石", ["void_stone"] = "虚无宝石",
            ["item_energy_booster"] = "能量之球", ["energy_booster"] = "能量之球",
            ["item_vitality_booster"] = "活力之球", ["vitality_booster"] = "活力之球",
            ["item_point_booster"] = "精气之球", ["point_booster"] = "精气之球",
            ["item_pers"] = "坚韧球", ["pers"] = "坚韧球",
            ["item_soul_booster"] = "灵魂之球", ["soul_booster"] = "灵魂之球",
            ["item_voodoo_mask"] = "巫毒面具", ["voodoo_mask"] = "巫毒面具",
            ["item_shadow_amulet"] = "暗影护符", ["shadow_amulet"] = "暗影护符",
            ["item_ghost"] = "幽魂权杖", ["ghost"] = "幽魂权杖",
            ["item_cloak"] = "抗魔斗篷", ["cloak"] = "抗魔斗篷",
            ["item_bracer"] = "护腕", ["bracer"] = "护腕",
            ["item_wraith_band"] = "怨灵系带", ["wraith_band"] = "怨灵系带",
            ["item_null_talisman"] = "空灵挂件", ["null_talisman"] = "空灵挂件",
            ["item_greater_crit"] = "代达罗斯之殇", ["greater_crit"] = "代达罗斯之殇",
            ["item_lesser_crit"] = "水晶剑", ["lesser_crit"] = "水晶剑",
            ["item_courier"] = "动物信使", ["courier"] = "动物信使",
            ["item_flying_courier"] = "飞行信使", ["flying_courier"] = "飞行信使",
            ["item_famango"] = "莲花", ["famango"] = "莲花",
            ["item_great_famango"] = "大莲花", ["great_famango"] = "大莲花",
            ["item_greater_famango"] = "极品莲花", ["greater_famango"] = "极品莲花",
            ["item_chasm_stone"] = "裂隙之石", ["chasm_stone"] = "裂隙之石",
            ["item_consecrated_wraps"] = "圣洁裹布", ["consecrated_wraps"] = "圣洁裹布",
            ["item_crellas_crozier"] = "克雷拉的长杖", ["crellas_crozier"] = "克雷拉的长杖",
            ["item_essence_distiller"] = "精华蒸馏器", ["essence_distiller"] = "精华蒸馏器",
        };

        foreach (var (k, v) in fallbackEntries)
        {
            NameMap.TryAdd(k, v);
        }
    }

    public static string ResolveChineseName(string? codeName, string? localizedName, string? englishName = null, int itemId = 0)
    {
        if (LocalizedText.ContainsChinese(localizedName)) return localizedName!.Trim();
        if (itemId > 0 && IdMap.TryGetValue(itemId, out var idMatched) && LocalizedText.ContainsChinese(idMatched)) return idMatched;

        if (!string.IsNullOrWhiteSpace(codeName))
        {
            var cleanKey = codeName.Trim();
            if (NameMap.TryGetValue(cleanKey, out var matched)) return matched;
            if (cleanKey.StartsWith("item_") && NameMap.TryGetValue(cleanKey[5..], out matched)) return matched;

            // 自动解析卷轴（item_recipe_xxx -> xxx卷轴）
            if (cleanKey.StartsWith("item_recipe_", StringComparison.OrdinalIgnoreCase))
            {
                var baseKey = cleanKey[12..];
                var baseName = ResolveChineseName(baseKey, null, null);
                if (!string.IsNullOrWhiteSpace(baseName) && baseName != "未知装备" && !baseName.Equals(baseKey, StringComparison.OrdinalIgnoreCase))
                {
                    return baseName + "卷轴";
                }
                return "合成卷轴";
            }
            if (cleanKey.StartsWith("recipe_", StringComparison.OrdinalIgnoreCase))
            {
                var baseKey = cleanKey[7..];
                var baseName = ResolveChineseName(baseKey, null, null);
                if (!string.IsNullOrWhiteSpace(baseName) && baseName != "未知装备" && !baseName.Equals(baseKey, StringComparison.OrdinalIgnoreCase))
                {
                    return baseName + "卷轴";
                }
                return "合成卷轴";
            }
        }

        if (LocalizedText.ContainsChinese(englishName)) return englishName!.Trim();
        if (!string.IsNullOrWhiteSpace(englishName))
        {
            var engKey = englishName.Trim().ToLowerInvariant().Replace(' ', '_');
            if (NameMap.TryGetValue(engKey, out var matchedEng)) return matchedEng;
            if (NameMap.TryGetValue("item_" + engKey, out matchedEng)) return matchedEng;
        }

        if (!string.IsNullOrWhiteSpace(codeName))
        {
            var name = codeName.StartsWith("item_") ? codeName[5..] : codeName;
            return name.Replace('_', ' ');
        }

        return "未知装备";
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
    public string DisplayName => DotaItemNameTable.ResolveChineseName(Name, LocalizedName, EnglishName, Id);
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

    /// <summary>官网（dota2.com.cn）itemdata 的属性行（已清理 HTML，如 "+攻击力：9"）。</summary>
    [JsonIgnore]
    public List<string> AttribLines { get; set; } = [];

    /// <summary>官网合成配方（requirements 数组，元素为内部名，可能带 "*" 或为 recipe_ 卷轴）。</summary>
    [JsonIgnore]
    public List<string> Requirements { get; set; } = [];
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
        Image = new BitmapImage(ImageCacheService.Instance.Resolve(item.ImageUrl));
    }

    public OfficialItemSummary Item { get; }
    public ItemMetric Metric { get; }
    public BitmapImage Image { get; }
    public string Name => LocalizedText.ChineseOr(Item.LocalizedName, "中文名称待同步");
    public string EnglishName => Item.EnglishName;
    public string CostText => Item.Cost > 0 ? "$" + Item.Cost.ToString("N0") : string.Empty;
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
    [JsonPropertyName("is_innate"), JsonConverter(typeof(FlexibleBoolConverter))] public bool IsInnate { get; set; }
    [JsonPropertyName("ability_is_innate"), JsonConverter(typeof(FlexibleBoolConverter))] public bool AbilityIsInnate { get; set; }
    [JsonPropertyName("innate_loc")] public string? InnateLoc { get; set; }
    [JsonPropertyName("ability_is_granted_by_scepter"), JsonConverter(typeof(FlexibleBoolConverter))] public bool IsGrantedByScepter { get; set; }
    [JsonPropertyName("ability_is_granted_by_shard"), JsonConverter(typeof(FlexibleBoolConverter))] public bool IsGrantedByShard { get; set; }
    [JsonPropertyName("ability_has_scepter"), JsonConverter(typeof(FlexibleBoolConverter))] public bool HasScepterUpgrade { get; set; }
    [JsonPropertyName("ability_has_shard"), JsonConverter(typeof(FlexibleBoolConverter))] public bool HasShardUpgrade { get; set; }
    [JsonPropertyName("behavior"), JsonConverter(typeof(FlexibleStringConverter))] public string? Behavior { get; set; }
    [JsonPropertyName("damage_type"), JsonConverter(typeof(FlexibleStringConverter))] public string? DamageType { get; set; }
    [JsonPropertyName("cooldowns"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> Cooldowns { get; set; } = [];
    [JsonPropertyName("mana_costs"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> ManaCosts { get; set; } = [];
    [JsonPropertyName("special_values")] public List<AbilitySpecialValue> SpecialValues { get => _specialValues; set => _specialValues = value ?? []; }
}

public sealed class AbilitySpecialValue
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("heading_loc")] public string? HeadingLoc { get; set; }
    private List<double> _values = [];
    private List<double> _alternateValues = [];
    private List<TalentBonus> _bonuses = [];
    /// <summary>该参数在天赋升级时提供的数值（name 为天赋 ID，value 为加成量）。</summary>
    [JsonPropertyName("bonuses")] public List<TalentBonus> Bonuses { get => _bonuses; set => _bonuses = value ?? []; }
    [JsonPropertyName("values_float"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> Values { get => _values; set => _values = value ?? []; }
    [JsonPropertyName("values"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> AlternateValues { get => _alternateValues; set => _alternateValues = value ?? []; }
    private List<double> _scepterValues = [];
    private List<double> _shardValues = [];
    /// <summary>阿哈利姆神杖/魔晶购买后的专属数值（解析神杖/魔晶文本时优先使用）。</summary>
    [JsonPropertyName("values_scepter"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> ScepterValues { get => _scepterValues; set => _scepterValues = value ?? []; }
    [JsonPropertyName("values_shard"), JsonConverter(typeof(FlexibleDoubleListConverter))] public List<double> ShardValues { get => _shardValues; set => _shardValues = value ?? []; }
    [JsonPropertyName("is_percentage")] public bool IsPercentage { get; set; }

    [JsonIgnore]
    public IReadOnlyList<double> EffectiveValues => Values.Count > 0 ? Values : AlternateValues;
}

/// <summary>技能参数上的天赋加成：name 为天赋 ID，value 为该天赋提供的加成量。</summary>
public sealed class TalentBonus
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("value")] public double Value { get; set; }
    [JsonPropertyName("operation")] public int Operation { get; set; }
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
            // 正则没有捕获组，必须用 Value（整个匹配）而不是 Groups[1]（返回空导致数值丢失）。
            NumberPrefix = numberMatch.Value;
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
    public string IconUrl => Id switch
    {
        "1" => "ms-appx:///Assets/Icons/rank_icon_1.png",
        "2" => "ms-appx:///Assets/Icons/rank_icon_2.png",
        "3" => "ms-appx:///Assets/Icons/rank_icon_3.png",
        "4" => "ms-appx:///Assets/Icons/rank_icon_4.png",
        "5" => "ms-appx:///Assets/Icons/rank_icon_5.png",
        "6" => "ms-appx:///Assets/Icons/rank_icon_6.png",
        "7" => "ms-appx:///Assets/Icons/rank_icon_7.png",
        "8" => "ms-appx:///Assets/Icons/rank_icon_8.png",
        _ => "ms-appx:///Assets/Icons/rank_icon_0.png",
    };
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
        LargeImage = new BitmapImage(ImageCacheService.Instance.Resolve(hero.ImageUrl));
        SmallImage = new BitmapImage(ImageCacheService.Instance.Resolve(hero.IconUrl));
        UpdateRank(rank);
    }

    public HeroStat Hero { get; }
    public int TotalProActions { get; }
    public string Name => DotaHeroNames.GetChineseName(Hero.Id, LocalizedText.ChineseOr(Hero.LocalizedName, Hero.EnglishName));
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
    public string AttributeShortLabel => Hero.PrimaryAttribute switch
    {
        "str" => "力",
        "agi" => "敏",
        "int" => "智",
        _ => "全",
    };
    public string AttackLabel => Hero.AttackType == "Melee" ? "近战" : "远程";
    public string PrimaryAttributeIcon => Hero.PrimaryAttributeIcon;
    public string AttackTypeIcon => Hero.AttackTypeIcon;
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
    public HeroSection(string id, string title, string subtitle, string iconUrl = "")
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        IconUrl = !string.IsNullOrWhiteSpace(iconUrl) ? iconUrl : id switch
        {
            "str" => "ms-appx:///Assets/Icons/hero_strength.png",
            "agi" => "ms-appx:///Assets/Icons/hero_agility.png",
            "int" => "ms-appx:///Assets/Icons/hero_intelligence.png",
            "all" => "ms-appx:///Assets/Icons/hero_universal.png",
            _ => "ms-appx:///Assets/Icons/rank_icon_0.png",
        };
    }

    public string Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string IconUrl { get; }
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

public sealed partial class AbilityDisplay : INotifyPropertyChanged
{
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionBorderBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionBorderThickness)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionOpacity)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionBackgroundBrush)));
        }
    }

    public Brush SelectionBorderBrush => IsSelected
        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["CoachGoldBrush"]
        : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["GameBorderBrush"];

    public Thickness SelectionBorderThickness => IsSelected
        ? new Thickness(2)
        : new Thickness(1);

    public double SelectionOpacity => IsSelected ? 1.0 : 0.82;

    public Brush SelectionBackgroundBrush => IsSelected
        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["GameSurfaceRaisedBrush"]
        : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["GameSurfaceSubtleBrush"];

    public event PropertyChangedEventHandler? PropertyChanged;
    private static readonly Dictionary<string, string> TagMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // 施法行为
        ["Unit Target"] = "单位目标", ["Point Target"] = "点目标", ["No Target"] = "无目标",
        ["Passive"] = "被动技能", ["Toggle"] = "切换技能", ["Channelled"] = "持续施法",
        ["Instant Cast"] = "即时施法", ["Hidden"] = "隐藏技能", ["AOE"] = "范围技能",
        ["Auto Cast"] = "自动施法", ["Attack Target"] = "攻击目标",
        ["DOTA_ABILITY_BEHAVIOR_UNIT_TARGET"] = "单位目标",
        ["DOTA_ABILITY_BEHAVIOR_POINT"] = "点目标",
        ["DOTA_ABILITY_BEHAVIOR_NO_TARGET"] = "无目标",
        ["DOTA_ABILITY_BEHAVIOR_PASSIVE"] = "被动技能",
        ["DOTA_ABILITY_BEHAVIOR_TOGGLE"] = "切换技能",
        ["DOTA_ABILITY_BEHAVIOR_CHANNELLED"] = "持续施法",
        ["DOTA_ABILITY_BEHAVIOR_AOE"] = "范围技能",
        ["DOTA_ABILITY_BEHAVIOR_IMMEDIATE"] = "即时施法",
        ["DOTA_ABILITY_BEHAVIOR_AUTOCAST"] = "自动施法",
        ["DOTA_ABILITY_BEHAVIOR_ATTACK"] = "攻击目标",
        ["DOTA_ABILITY_BEHAVIOR_HIDDEN"] = "隐藏技能",
        // 伤害类型
        ["Physical"] = "物理伤害", ["Magical"] = "魔法伤害", ["Pure"] = "纯粹伤害",
        ["DAMAGE_TYPE_PHYSICAL"] = "物理伤害",
        ["DAMAGE_TYPE_MAGICAL"] = "魔法伤害",
        ["DAMAGE_TYPE_PURE"] = "纯粹伤害",
        ["DAMAGE_TYPE_NONE"] = "无伤害",
        // 驱散与魔免
        ["SPELL_IMMUNITY_ENEMIES_YES"] = "无视技能免疫",
        ["SPELL_IMMUNITY_ENEMIES_NO"] = "不无视技能免疫",
        ["SPELL_DISPELLABLE_YES"] = "可驱散",
        ["SPELL_DISPELLABLE_NO"] = "不可驱散",
        ["SPELL_DISPELLABLE_YES_STRONG"] = "强驱散可解",
    };

    private static string LocalizeTags(string? behavior, string? damageType)
    {
        var rawList = new List<string>();
        if (!string.IsNullOrWhiteSpace(behavior))
        {
            rawList.AddRange(behavior.Split(new[] { " / ", " | ", "|", "/", "," }, StringSplitOptions.RemoveEmptyEntries));
        }
        if (!string.IsNullOrWhiteSpace(damageType))
        {
            rawList.AddRange(damageType.Split(new[] { " / ", " | ", "|", "/", "," }, StringSplitOptions.RemoveEmptyEntries));
        }

        var results = new List<string>();
        foreach (var raw in rawList)
        {
            var trimmed = raw.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (TagMap.TryGetValue(trimmed, out var localized))
            {
                if (!results.Contains(localized)) results.Add(localized);
            }
            else
            {
                // 去除 DOTA_ABILITY_BEHAVIOR_ 等前缀并尝试翻译
                var cleanKey = trimmed.Replace("DOTA_ABILITY_BEHAVIOR_", string.Empty)
                                      .Replace("DAMAGE_TYPE_", string.Empty)
                                      .Replace('_', ' ');
                if (TagMap.TryGetValue(cleanKey, out var fallbackLocalized))
                {
                    if (!results.Contains(fallbackLocalized)) results.Add(fallbackLocalized);
                }
                else if (LocalizedText.ContainsChinese(trimmed))
                {
                    if (!results.Contains(trimmed)) results.Add(trimmed);
                }
            }
        }

        return string.Join(" · ", results);
    }

    public AbilityDisplay(string id, AbilityDefinition? baseData, OfficialAbility? official)
    {
        Id = id;
        IsInnate = official?.IsInnate == true || official?.AbilityIsInnate == true || baseData?.IsInnate == true
            || official?.Behavior?.Contains("INNATE", StringComparison.OrdinalIgnoreCase) == true
            || baseData?.Behavior?.Contains("INNATE", StringComparison.OrdinalIgnoreCase) == true;
        Name = LocalizedText.ChineseOr(official?.LocalizedName,
            (baseData?.DisplayName is { Length: > 0 } displayName ? Clean(displayName) : null) ?? "中文技能名称待同步");
        var description = LocalizedText.ChineseOrNull(Clean(official?.Description))
            ?? Clean(baseData?.Description);
        Description = description is null
            ? "中文说明暂不可用"
            : ResolveParameters(description, official?.SpecialValues, baseData?.Attributes);
        Lore = LocalizedText.ChineseOrNull(Clean(official?.Lore));
        Notes = official?.Notes.Select(Clean).Where(text => !string.IsNullOrWhiteSpace(text)).OfType<string>().ToList() ?? [];
        var shard = LocalizedText.ChineseOrNull(Clean(official?.Shard));
        var scepter = LocalizedText.ChineseOrNull(Clean(official?.Scepter));
        IsGrantedByScepter = official?.IsGrantedByScepter == true || baseData?.Description?.Contains("神杖", StringComparison.OrdinalIgnoreCase) == true;
        IsGrantedByShard = official?.IsGrantedByShard == true || baseData?.Description?.Contains("魔晶", StringComparison.OrdinalIgnoreCase) == true;
        HasScepterUpgrade = official?.HasScepterUpgrade == true || !string.IsNullOrWhiteSpace(scepter) || IsGrantedByScepter;
        HasShardUpgrade = official?.HasShardUpgrade == true || !string.IsNullOrWhiteSpace(shard) || IsGrantedByShard;

        // 神杖/魔晶文本使用 values_scepter / values_shard 专属数值（存在时优先）。
        var resolvedShard = shard is null ? string.Empty
            : ResolveParameters(shard, official?.SpecialValues, baseData?.Attributes,
                value => value.ShardValues.Count > 0 ? value.ShardValues : value.EffectiveValues);
        if (string.IsNullOrWhiteSpace(resolvedShard) && IsGrantedByShard)
        {
            resolvedShard = $"授予全新技能【{Name}】：{Description}";
        }
        Shard = resolvedShard;

        var resolvedScepter = scepter is null ? string.Empty
            : ResolveParameters(scepter, official?.SpecialValues, baseData?.Attributes,
                value => value.ScepterValues.Count > 0 ? value.ScepterValues : value.EffectiveValues);
        if (string.IsNullOrWhiteSpace(resolvedScepter) && IsGrantedByScepter)
        {
            resolvedScepter = $"授予全新技能【{Name}】：{Description}";
        }
        Scepter = resolvedScepter;

        Cooldown = Series(official?.Cooldowns) ?? baseData?.Cooldown;
        ManaCost = Series(official?.ManaCosts) ?? baseData?.ManaCost;
        Image = CreateImage(baseData?.ImageUrl);
        var tags = LocalizeTags(baseData?.Behavior ?? official?.Behavior, baseData?.DamageType ?? official?.DamageType);
        TagsText = IsInnate ? (string.IsNullOrWhiteSpace(tags) ? "[先天技能]" : $"[先天技能] · {tags}") : tags;
        StatsDetails = BuildStats(official?.SpecialValues, baseData?.Attributes);

        // 解析官网标准机制属性
        var behaviorRaw = (baseData?.Behavior ?? official?.Behavior ?? string.Empty).ToUpperInvariant();
        var damageRaw = (baseData?.DamageType ?? official?.DamageType ?? string.Empty).ToUpperInvariant();

        TargetTypeText = behaviorRaw.Contains("PASSIVE") ? "被动技能" :
                         behaviorRaw.Contains("NO_TARGET") || behaviorRaw.Contains("NO TARGET") ? "无目标" :
                         behaviorRaw.Contains("POINT") ? "点目标" :
                         behaviorRaw.Contains("UNIT_TARGET") || behaviorRaw.Contains("UNIT TARGET") ? "单位目标" :
                         behaviorRaw.Contains("CHANNELLED") ? "持续施法" :
                         behaviorRaw.Contains("TOGGLE") ? "切换技能" :
                         behaviorRaw.Contains("AUTOCAST") ? "自动施法" : "主动技能";

        TargetTeamText = behaviorRaw.Contains("PASSIVE") ? "自身" :
                         behaviorRaw.Contains("TREE") ? "树木" :
                         behaviorRaw.Contains("ALLIED") || behaviorRaw.Contains("FRIENDLY") ? "友方单位" :
                         behaviorRaw.Contains("ALL") ? "所有单位" : "敌方单位";

        DamageTypeText = damageRaw.Contains("MAGICAL") ? "魔法" :
                         damageRaw.Contains("PHYSICAL") ? "物理" :
                         damageRaw.Contains("PURE") ? "纯粹" :
                         behaviorRaw.Contains("PASSIVE") ? "无" : "无伤害";

        SpellImmunityText = behaviorRaw.Contains("SPELL_IMMUNITY_ENEMIES_YES") || behaviorRaw.Contains("PIERCES_IMMUNITY_YES") ? "是" : "否";
        DispellableText = behaviorRaw.Contains("SPELL_DISPELLABLE_NO") ? "否" :
                          behaviorRaw.Contains("SPELL_DISPELLABLE_YES_STRONG") ? "仅强驱散" :
                          behaviorRaw.Contains("SPELL_DISPELLABLE_YES") ? "是" : "否";

        AbilityVariantType = IsInnate ? "先天技能" :
                             IsGrantedByShard ? "魔晶提供新技能" :
                             IsGrantedByScepter ? "阿哈利姆神杖赋予新技能" : "普通技能";

        SubIconUrl = IsInnate ? "ms-appx:///Assets/Icons/innate_icon.png" :
                     IsGrantedByShard ? "ms-appx:///Assets/Icons/aghs_shard.png" :
                     IsGrantedByScepter ? "ms-appx:///Assets/Icons/aghs_scepter.png" :
                     HasShardUpgrade ? "ms-appx:///Assets/Icons/aghs_shard.png" :
                     HasScepterUpgrade ? "ms-appx:///Assets/Icons/aghs_scepter.png" : string.Empty;
    }

    public string Id { get; }
    public bool IsInnate { get; set; }
    public string SlotLabel { get; set; } = string.Empty;
    public string Name { get; set; }
    public string Description { get; set; }
    public string? Lore { get; set; }
    public List<string> Notes { get; set; }
    public string? Shard { get; set; }
    public string? Scepter { get; set; }
    public bool IsGrantedByScepter { get; set; }
    public bool IsGrantedByShard { get; set; }
    public bool HasScepterUpgrade { get; set; }
    public bool HasShardUpgrade { get; set; }
    public bool HasScepterUpgradeIcon => HasScepterUpgrade && !IsGrantedByScepter;
    public bool HasShardUpgradeIcon => HasShardUpgrade && !IsGrantedByShard;
    public string? Cooldown { get; set; }
    public string? ManaCost { get; set; }
    public string CooldownText => string.IsNullOrWhiteSpace(Cooldown) ? string.Empty : $"冷却 {Cooldown}";
    public string ManaCostText => string.IsNullOrWhiteSpace(ManaCost) ? string.Empty : $"魔法 {ManaCost}";
    public string ScepterText => string.IsNullOrWhiteSpace(Scepter) ? string.Empty : $"神杖：{Scepter}";
    public string ShardText => string.IsNullOrWhiteSpace(Shard) ? string.Empty : $"魔晶：{Shard}";
    public BitmapImage Image { get; set; }
    public string TagsText { get; set; }
    public IReadOnlyList<AbilityDetailStat> StatsDetails { get; set; }

    // 官网详细机制属性
    public string TargetTypeText { get; set; } = "单位目标";
    public string TargetTeamText { get; set; } = "敌方单位";
    public string DamageTypeText { get; set; } = "魔法";
    public string SpellImmunityText { get; set; } = "否";
    public string DispellableText { get; set; } = "是";
    public string AbilityVariantType { get; set; } = "普通技能";
    public string SubIconUrl { get; set; } = string.Empty;
    public bool HasSubIcon => !string.IsNullOrWhiteSpace(SubIconUrl);
    public bool HasScepterUpgradeText => !string.IsNullOrWhiteSpace(Scepter);
    public bool HasShardUpgradeText => !string.IsNullOrWhiteSpace(Shard);

    /// <summary>创建该技能的神杖升级变体技能项（供 AbilitySelector 展示与点击）。</summary>
    public AbilityDisplay CreateScepterVariant()
    {
        var variant = new AbilityDisplay(Id, null, null)
        {
            Name = Name,
            SlotLabel = "神杖",
            Description = !string.IsNullOrWhiteSpace(Scepter) ? Scepter : Description,
            Image = Image,
            Cooldown = Cooldown,
            ManaCost = ManaCost,
            Lore = Lore,
            Notes = Notes,
            StatsDetails = StatsDetails,
            TargetTypeText = TargetTypeText,
            TargetTeamText = TargetTeamText,
            DamageTypeText = DamageTypeText,
            SpellImmunityText = SpellImmunityText,
            DispellableText = DispellableText,
            AbilityVariantType = "阿哈利姆神杖升级",
            SubIconUrl = "ms-appx:///Assets/Icons/aghs_scepter.png",
            TagsText = "[阿哈利姆神杖升级] · " + TagsText.Replace("[先天技能] · ", string.Empty),
        };
        return variant;
    }

    /// <summary>创建该技能的魔晶升级变体技能项（供 AbilitySelector 展示与点击）。</summary>
    public AbilityDisplay CreateShardVariant()
    {
        var variant = new AbilityDisplay(Id, null, null)
        {
            Name = Name,
            SlotLabel = "魔晶",
            Description = !string.IsNullOrWhiteSpace(Shard) ? Shard : Description,
            Image = Image,
            Cooldown = Cooldown,
            ManaCost = ManaCost,
            Lore = Lore,
            Notes = Notes,
            StatsDetails = StatsDetails,
            TargetTypeText = TargetTypeText,
            TargetTeamText = TargetTeamText,
            DamageTypeText = DamageTypeText,
            SpellImmunityText = SpellImmunityText,
            DispellableText = DispellableText,
            AbilityVariantType = "阿哈利姆魔晶升级",
            SubIconUrl = "ms-appx:///Assets/Icons/aghs_shard.png",
            TagsText = "[阿哈利姆魔晶升级] · " + TagsText.Replace("[先天技能] · ", string.Empty),
        };
        return variant;
    }

    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // 1. HTML 实体解码（&nbsp; -> 空格, &lt; -> < 等）
        var text = System.Net.WebUtility.HtmlDecode(value);

        // 2. 块级和换行标签统一替换为换行
        text = Regex.Replace(text, @"(?i)<br\s*/?>|</p>|</div>|</li>|</h1>|</h2>|</h3>", "\n");
        text = Regex.Replace(text, @"(?i)<li\s*>", "\n• ");

        // 3. 移除剩余 HTML 标签（如 <font color="...">, <b>, <span> 等）
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);

        // 4. 规范化连续换行和多余空白
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\r\n|\r", "\n");
        text = Regex.Replace(text, @"\n\s*\n\s*\n+", "\n\n");

        return text.Trim();
    }

    private static BitmapImage CreateImage(string? url) =>
        string.IsNullOrWhiteSpace(url) ? new BitmapImage() : new BitmapImage(ImageCacheService.Instance.Resolve(url));

    public static string ResolveParameters(
        string text,
        IEnumerable<AbilitySpecialValue>? officialValues,
        IEnumerable<AbilityAttribute>? openDotaValues,
        Func<AbilitySpecialValue, IReadOnlyList<double>>? valueSelector = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (officialValues is not null)
        {
            foreach (var value in officialValues)
            {
                var effective = valueSelector is not null ? valueSelector(value) : value.EffectiveValues;
                if (effective.Count == 0) continue;
                var suffix = value.IsPercentage ? "%" : string.Empty;
                var formatted = string.Join(" / ", effective.Select(number => $"{number:0.##}{suffix}"));
                AddValueWithAliases(values, value.Name, formatted);
            }
        }

        if (openDotaValues is not null)
        {
            foreach (var value in openDotaValues)
            {
                if (!string.IsNullOrWhiteSpace(value.Key) && !string.IsNullOrWhiteSpace(value.ValueText))
                {
                    AddValueWithAliases(values, value.Key, value.ValueText!);
                }
            }
        }

        // 占位符精准匹配替换（防止因贪婪匹配吞噬中文描述）
        var resolved = ParameterRegex().Replace(text, match =>
        {
            var key = match.Groups.Cast<Group>().Skip(1).FirstOrDefault(group => group.Success)?.Value;
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;

            if (TryResolveKey(values, key, out var replacement))
            {
                if (match.Value.StartsWith('+') && !replacement.StartsWith('+') && !replacement.StartsWith('-'))
                {
                    return "+" + replacement;
                }
                return replacement;
            }

            // 占位符未能从字典中匹配到数值时，不向用户暴露代码字符串，安全降级为空
            return string.Empty;
        });

        // 还原转义的 %% 为 %，清理孤立符号与未解析残余
        resolved = resolved.Replace("%%", "%");
        resolved = Regex.Replace(resolved, @"(?i)%[a-zA-Z0-9_$]+%|\+?%?\{s:[a-zA-Z0-9_$]+\}%?", string.Empty);
        resolved = Regex.Replace(resolved, @"(?<=[，。、\s]|^)\+[%\s]*(?=[，。、\s]|$)", string.Empty);
        resolved = Regex.Replace(resolved, @"[ \t]+", " ");

        return resolved.Trim();
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

        if (placeholders.Count > 0 && values.Count == 1 && !Regex.IsMatch(resolved, @"[+\-−]?\d"))
        {
            resolved = ParameterRegex().Replace(text, _ => values[0]);
        }

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

    [GeneratedRegex(@"(?i)\+?%?\{s:([a-zA-Z0-9_$]+)\}%?|%([a-zA-Z0-9_$]+)%|\{\{([a-zA-Z0-9_$]+)\}\}|\{([a-zA-Z0-9_$]+)\}")]
    private static partial Regex ParameterRegex();

    private static void AddValueWithAliases(Dictionary<string, string> values, string? key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;
        var normalized = NormalizeKey(key);
        values.TryAdd(normalized, value);
        values.TryAdd(normalized.Replace('_', ' '), value);

        // 自动注入 bonus_ / tooltip_ / ability_ 别名
        if (normalized.StartsWith("bonus_"))
        {
            values.TryAdd(normalized[6..], value);
        }
        else
        {
            values.TryAdd("bonus_" + normalized, value);
        }

        if (normalized.StartsWith("tooltip_"))
        {
            values.TryAdd(normalized[8..], value);
        }
        else
        {
            values.TryAdd("tooltip_" + normalized, value);
        }

        if (normalized.StartsWith("ability_"))
        {
            values.TryAdd(normalized[8..], value);
        }
    }

    private static bool TryResolveKey(Dictionary<string, string> values, string key, out string replacement)
    {
        var normalized = NormalizeKey(key);

        // 1. 精确匹配
        if (values.TryGetValue(normalized, out replacement!)) return true;

        // 2. 剥离/添加常见前缀
        var stripped = normalized;
        if (stripped.StartsWith("bonus_")) stripped = stripped[6..];
        if (stripped.StartsWith("tooltip_")) stripped = stripped[8..];
        if (stripped.StartsWith("ability_")) stripped = stripped[8..];

        if (values.TryGetValue(stripped, out replacement!)) return true;
        if (values.TryGetValue("bonus_" + stripped, out replacement!)) return true;
        if (values.TryGetValue("tooltip_" + stripped, out replacement!)) return true;

        // 3. 剥离序号或变体后缀（如 $1, $2, _1, _2 等）
        if (stripped.Contains('$'))
        {
            var prefix = stripped.Split('$')[0];
            if (values.TryGetValue(prefix, out replacement!)) return true;
        }

        var noSuffix = Regex.Replace(stripped, @"(_pct|_percentage|_duration|_radius|_distance|_speed|_slow|_scale|_amount|_total|_bonus|_damage)$", string.Empty);
        if (noSuffix.Length > 0 && values.TryGetValue(noSuffix, out replacement!)) return true;

        // 4. 模糊词根包含查找
        foreach (var pair in values)
        {
            if (pair.Key.Contains(stripped) || stripped.Contains(pair.Key))
            {
                replacement = pair.Value;
                return true;
            }
        }

        replacement = string.Empty;
        return false;
    }

    private static string NormalizeKey(string key) => key.Trim().Trim('%', '{', '}')
        .Replace("s:", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Trim().ToLowerInvariant();

    private static string? Series(IEnumerable<double>? values)
    {
        var useful = values?.Where(value => value > 0).ToList();
        return useful is { Count: > 0 } ? string.Join(" / ", useful.Select(value => value % 1 == 0 ? value.ToString("0") : value.ToString("0.#"))) : null;
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
                // 过滤元数据及内部属性
                if (value.EffectiveValues.Count == 0 ||
                    value.Name.StartsWith("Ability", StringComparison.OrdinalIgnoreCase) ||
                    value.Name.StartsWith("linked_ad_", StringComparison.OrdinalIgnoreCase) ||
                    value.Name.StartsWith("shard_ability_", StringComparison.OrdinalIgnoreCase) ||
                    value.Name.StartsWith("scepter_ability_", StringComparison.OrdinalIgnoreCase) ||
                    value.Name.Equals("var_type", StringComparison.OrdinalIgnoreCase) ||
                    value.Name.Equals("facet_name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 过滤全为 0 的数值项
                if (value.EffectiveValues.All(num => num == 0))
                {
                    continue;
                }

                // 提取中文名称
                string label;
                if (!string.IsNullOrWhiteSpace(value.HeadingLoc))
                {
                    label = CleanStatHeading(value.HeadingLoc!);
                }
                else
                {
                    label = HumanizeStatName(value.Name);
                }

                if (string.IsNullOrWhiteSpace(label) || label.StartsWith("item_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var suffix = value.IsPercentage ? "%" : string.Empty;
                var valText = string.Join(" / ", value.EffectiveValues.Select(number => $"{number:0.##}{suffix}"));
                if (valText == "0" || valText == "0%" || string.IsNullOrWhiteSpace(valText)) continue;

                stats.Add(new AbilityDetailStat(label, valText));
            }
        }

        if (stats.Count == 0 && openDotaValues is not null)
        {
            foreach (var value in openDotaValues)
            {
                if (string.IsNullOrWhiteSpace(value.Key) || string.IsNullOrWhiteSpace(value.ValueText)) continue;
                if (value.Key.StartsWith("Ability", StringComparison.OrdinalIgnoreCase) || value.Key.Equals("var_type", StringComparison.OrdinalIgnoreCase)) continue;
                if (value.ValueText == "0" || value.ValueText == "0.0" || value.ValueText == "0 / 0 / 0 / 0") continue;

                var label = HumanizeStatName(value.Key);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    stats.Add(new AbilityDetailStat(label, value.ValueText));
                }
            }
        }

        return stats.Take(16).ToList();
    }

    public static string CleanStatHeading(string heading)
    {
        var cleaned = Regex.Replace(heading.Replace("<br>", " "), "<[^>]+>", string.Empty).Trim();
        cleaned = cleaned.TrimEnd(':', '：', ' ', '、', '-').Trim();

        // 替换 Valve 属性宏（如 +$str, +$agi, +$attack 等）
        var macro = cleaned.TrimStart('+', '-', '−', ' ', '%').ToLowerInvariant();
        var resolved = macro switch
        {
            "str" or "$str" => "力量",
            "agi" or "$agi" => "敏捷",
            "int" or "$int" => "智力",
            "all" or "$all" => "全属性",
            "attack" or "$attack" => "攻击速度",
            "damage" or "$damage" or "attack_damage" => "攻击力",
            "armor" or "$armor" => "护甲",
            "hp" or "$hp" or "health" => "生命值",
            "mana" or "$mana" => "魔法值",
            "hp_regen" or "$hp_regen" or "health_regen" => "生命恢复",
            "mana_regen" or "$mana_regen" => "魔法恢复",
            "move_speed" or "$move_speed" or "movespeed" or "$movespeed" => "移动速度",
            "evasion" or "$evasion" => "闪避",
            "spell_amp" or "$spell_amp" => "技能增强",
            "magic_resist" or "$magic_resist" or "magic_resistance" => "魔法抗性",
            "status_resist" or "$status_resist" or "status_resistance" => "状态抗性",
            "lifesteal" or "$lifesteal" => "吸血",
            "spell_lifesteal" or "$spell_lifesteal" => "技能吸血",
            "cast_range" or "$cast_range" => "施法距离",
            "attack_range" or "$attack_range" => "攻击距离",
            "cooldown_reduction" or "$cooldown_reduction" or "cdr" => "冷却缩减",
            "heal" or "$heal" => "治疗量",
            "charges" or "$charges" => "充能次数",
            "gold" or "$gold" => "金钱",
            "vision" or "day_vision" or "night_vision" => "视野范围",
            _ => string.Empty,
        };

        if (!string.IsNullOrEmpty(resolved)) return resolved;
        return string.IsNullOrWhiteSpace(cleaned) ? "属性" : cleaned.TrimStart('$', '+').Trim();
    }

    public static string HumanizeStatName(string name)
    {
        var normalized = name.ToLowerInvariant().Trim();
        if (normalized.StartsWith("bonus_")) normalized = normalized[6..];
        if (normalized.StartsWith("tooltip_")) normalized = normalized[8..];
        if (normalized.StartsWith("ability_")) normalized = normalized[8..];

        var known = normalized switch
        {
            "damage" or "attack_damage" => "伤害",
            "bonus_damage" => "额外伤害",
            "damage_per_second" or "dps" => "每秒伤害",
            "creep_damage" or "creep_damage_penalty" => "对小兵伤害",
            "duration" or "buff_duration" => "持续时间",
            "stun_duration" or "stun" => "眩晕时间",
            "slow_duration" => "减速时间",
            "silence_duration" => "沉默时间",
            "disarm_duration" => "缴械时间",
            "channel_time" or "channel_duration" => "持续施法时间",
            "radius" or "aoe_radius" or "area_of_effect" => "作用范围",
            "range" or "cast_range" => "施法距离",
            "attack_range" or "bonus_attack_range" => "攻击距离",
            "speed" or "projectile_speed" => "弹道速度",
            "movement_speed" or "move_speed" or "movespeed" => "移动速度",
            "attack_speed" => "攻击速度",
            "slow" or "movement_slow" or "ms_slow" => "移动减速",
            "attack_speed_slow" or "as_slow" => "攻击减速",
            "heal" or "heal_amount" or "health_restored" => "治疗量",
            "health" or "hp" or "bonus_health" => "生命加成",
            "mana" or "bonus_mana" => "魔法加成",
            "health_regen" or "hp_regen" => "生命恢复",
            "mana_regen" => "魔法恢复",
            "armor" or "bonus_armor" => "护甲加成",
            "magic_resistance" or "magic_resist" => "魔法抗性",
            "status_resistance" => "状态抗性",
            "evasion" => "闪避",
            "lifesteal" or "vampirism" => "吸血",
            "spell_lifesteal" or "spell_vampirism" => "技能吸血",
            "spell_amp" or "spell_amplification" => "技能增强",
            "cooldown_reduction" or "cdr" => "冷却缩减",
            "crit_chance" or "critical_chance" or "chance" or "proc_chance" => "触发几率",
            "crit_mult" or "crit_multiplier" => "致命一击伤害",
            "charges" or "max_charges" => "充能次数",
            "charge_restore_time" or "charge_time" => "充能时间",
            "all_stats" or "stats" => "全属性",
            "strength" or "str" => "力量",
            "agility" or "agi" => "敏捷",
            "intellect" or "intelligence" or "int" => "智力",
            "gold" or "bonus_gold" => "金币奖励",
            "xp" or "bonus_xp" => "经验奖励",
            "cleave_damage" => "分裂伤害",
            "cleave_distance" => "分裂距离",
            "bounce_count" or "bounces" => "弹跳次数",
            "targets" or "max_targets" or "target_count" => "目标数量",
            "knockback_distance" => "击退距离",
            "knockback_duration" => "击退时间",
            "tick_rate" or "tick_interval" or "interval" => "作用间隔",
            "mana_cost" or "mc" => "魔法消耗",
            "cooldown" or "cd" => "冷却时间",
            _ => string.Empty,
        };
        if (!string.IsNullOrEmpty(known)) return known;

        if (normalized.Contains("heal")) return "治疗效果";
        if (normalized.Contains("mana")) return "魔法效果";
        if (normalized.Contains("armor")) return "护甲效果";
        if (normalized.Contains("strength")) return "力量效果";
        if (normalized.Contains("agility")) return "敏捷效果";
        if (normalized.Contains("intellect")) return "智力效果";
        if (normalized.Contains("health") || normalized.Contains("hp")) return "生命效果";
        if (normalized.Contains("damage")) return "伤害";
        if (normalized.Contains("speed")) return "速度效果";
        if (normalized.Contains("slow")) return "减速效果";
        if (normalized.Contains("stun")) return "眩晕效果";
        if (normalized.Contains("crit")) return "暴击效果";
        if (normalized.Contains("chance")) return "触发概率";
        if (normalized.Contains("range")) return "距离范围";
        if (normalized.Contains("radius")) return "作用半径";
        if (normalized.Contains("count") || normalized.Contains("number")) return "数量";
        if (normalized.Contains("duration")) return "持续时间";
        if (normalized.Contains("cooldown")) return "冷却时间";
        if (normalized.Contains("cost")) return "消耗";

        // 将英文标识符拆分为美观易读的词组（如 split_shot_count -> Split Shot Count）
        var words = normalized.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w[1..] : string.Empty));
        return string.Join(' ', words);
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

/// <summary>支持 boolean、integer (0/1)、string ("true"/"false"/"1"/"0") 的灵活布尔转换器。</summary>
public sealed class FlexibleBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var num) ? num != 0 : reader.GetDouble() != 0;
            case JsonTokenType.String:
                var str = reader.GetString();
                if (bool.TryParse(str, out var b)) return b;
                if (int.TryParse(str, out var i)) return i != 0;
                return !string.IsNullOrWhiteSpace(str);
            case JsonTokenType.Null:
                return false;
            default:
                return false;
        }
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }
}
