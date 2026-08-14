using System.Text.Json.Serialization;

namespace AegisCoach.Native.Models;

/// <summary>官网（www.dota2.com.cn）物品列表 JSON 结构：{"status":"success","result":{"basic":[...],"upgrade":[...],"neutral":[...]}}</summary>
public sealed class OfficialItemListResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("result")] public OfficialItemListResult Result { get; set; } = new();
}

public sealed class OfficialItemListResult
{
    [JsonPropertyName("basic")] public List<OfficialItemSection> Basic { get; set; } = [];
    [JsonPropertyName("upgrade")] public List<OfficialItemSection> Upgrade { get; set; } = [];
    [JsonPropertyName("neutral")] public List<OfficialItemSection> Neutral { get; set; } = [];
}

public sealed class OfficialItemSection
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("name_en")] public string NameEn { get; set; } = string.Empty;
    [JsonPropertyName("items")] public List<OfficialItemEntry> Items { get; set; } = [];
}

public sealed class OfficialItemEntry
{
    [JsonPropertyName("item_id")] public string ItemId { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("name_loc")] public string NameLoc { get; set; } = string.Empty;
    [JsonPropertyName("cost")] public string Cost { get; set; } = string.Empty;
    [JsonPropertyName("img")] public string Img { get; set; } = string.Empty;
    [JsonPropertyName("img_url")] public string ImgUrl { get; set; } = string.Empty;

    [JsonIgnore]
    public int CostValue => int.TryParse(Cost, out var value) ? value : 0;
}

/// <summary>官网（www.dota2.com.cn）英雄列表 JSON 结构：{"status":"success","result":{"heroes":[...]}}</summary>
public sealed class OfficialHeroListResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("result")] public OfficialHeroListResult Result { get; set; } = new();
}

public sealed class OfficialHeroListResult
{
    [JsonPropertyName("heroes")] public List<OfficialHeroEntry> Heroes { get; set; } = [];
}

public sealed class OfficialHeroEntry
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("name_loc")] public string NameLoc { get; set; } = string.Empty;
    [JsonPropertyName("name_english_loc")] public string NameEnglishLoc { get; set; } = string.Empty;
    [JsonPropertyName("primary_attr")] public int PrimaryAttr { get; set; }
    [JsonPropertyName("complexity")] public int Complexity { get; set; }
    [JsonPropertyName("index_img")] public string IndexImg { get; set; } = string.Empty;
    [JsonPropertyName("crops_img")] public string CropsImg { get; set; } = string.Empty;
}
/// <summary>官网（www.dota2.com.cn）物品详情 JSON 根结构：{"itemdata":{"blink":{...}}}。
/// 该接口的 desc/attrib 已是带数值的中文最终文本（含 <h1>主动：技能名</h1> 与 <p>属性行</p>），
/// 不需要像 datafeed 那样再做占位符解析。</summary>
public sealed class OfficialItemDetailsRoot
{
    [JsonPropertyName("itemdata")]
    public Dictionary<string, OfficialItemDetailsEntry> ItemData { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OfficialItemDetailsEntry
{
    [JsonPropertyName("id")] public string IdText { get; set; } = string.Empty;
    [JsonPropertyName("img")] public string Img { get; set; } = string.Empty;
    [JsonPropertyName("dname")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("en")] public string EnglishName { get; set; } = string.Empty;
    [JsonPropertyName("qual")] public string Qual { get; set; } = string.Empty;
    [JsonPropertyName("cost")] public string CostText { get; set; } = string.Empty;
    [JsonPropertyName("desc")] public string? Description { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("attrib")] public string? Attrib { get; set; }
    [JsonPropertyName("mc")] public string ManaCostText { get; set; } = string.Empty;
    [JsonPropertyName("cd")] public string CooldownText { get; set; } = string.Empty;
    [JsonPropertyName("lore")] public string? Lore { get; set; }
    [JsonPropertyName("components")] public string? Components { get; set; }
    [JsonPropertyName("created")] public string? Created { get; set; }
    [JsonPropertyName("img_url")] public string? ImageUrl { get; set; }

    /// <summary>合成配方（官网 requirements 数组，如 ["magic_stick*","branches","branches","recipe_magic_wand"]）。
    /// 元素可能带 "*" 表示商店默认配方；recipe_ 前缀为卷轴。</summary>
    private List<string> _requirements = [];
    [JsonPropertyName("requirements")] public List<string> Requirements { get => _requirements; set => _requirements = value ?? []; }

    [JsonIgnore] public int Id => int.TryParse(IdText, out var value) ? value : 0;
    [JsonIgnore] public int Cost => int.TryParse(CostText, out var value) ? value : 0;

    /// <summary>官网冷却文本（如 "15"、"9 8 7"、"0"）拆成数值列表。</summary>
    [JsonIgnore]
    public List<double> Cooldowns => ParseSeries(CooldownText);

    /// <summary>官网魔法消耗文本（如 "0"、"120 140 160"）拆成数值列表。</summary>
    [JsonIgnore]
    public List<double> ManaCosts => ParseSeries(ManaCostText);

    private static List<double> ParseSeries(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.Split(new[] { ' ', '/', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => double.TryParse(part, out var value) ? value : 0)
            .Where(value => value > 0)
            .ToList();
    }
}

