using System.Text.Json;
using System.Text.Json.Serialization;

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var json = await http.GetStringAsync("https://api.opendota.com/api/constants/abilities");
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

try
{
    var parsed = JsonSerializer.Deserialize<Dictionary<string, AbilityDefinition>>(json, options);
    Console.WriteLine($"deserialized: {parsed?.Count} abilities");
    var bytes = JsonSerializer.SerializeToUtf8Bytes(parsed);
    Console.WriteLine($"reserialize OK: {bytes.Length} bytes");
}
catch (Exception ex)
{
    Console.WriteLine("FAIL: " + ex);
}

// 模拟物品合成路线解析：组件前缀归一化 + 追加卷轴。
var itemJson = await http.GetStringAsync("https://api.opendota.com/api/constants/items");
using var itemsDoc = JsonDocument.Parse(itemJson);
var names = new List<(string Key, int Id, string Name)>();
foreach (var prop in itemsDoc.RootElement.EnumerateObject())
{
    var id = prop.Value.TryGetProperty("id", out var idValue) && idValue.TryGetInt32(out var parsedId) ? parsedId : 0;
    var name = "item_" + prop.Name;
    names.Add((prop.Name, id, name));
}
var byName = names.ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
var bkbComponents = new List<string>();
if (itemsDoc.RootElement.TryGetProperty("black_king_bar", out var bkb))
{
    if (bkb.TryGetProperty("components", out var comps))
    {
        foreach (var comp in comps.EnumerateArray())
        {
            var key = comp.GetString() ?? string.Empty;
            if (key.Length == 0) continue;
            var found = byName.ContainsKey(key) ? key : byName.ContainsKey("item_" + key) ? "item_" + key : null;
            bkbComponents.Add(found is null ? $"{key}(未解析)" : $"{found}(id={byName[found].Id})");
        }
    }

    var shortName = "black_king_bar";
    if (byName.TryGetValue("item_recipe_" + shortName, out var recipe))
    {
        bkbComponents.Add($"item_recipe_{shortName}(id={recipe.Id}, 卷轴)");
    }
}

Console.WriteLine("BKB 合成路线: " + string.Join(" + ", bkbComponents));

// 模拟 HeroStat 物化转换器：解析 heroStats 后重写。
var heroJson = await http.GetStringAsync("https://api.opendota.com/api/heroStats");
using var heroesDoc = JsonDocument.Parse(heroJson);
var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "id", "name", "localized_name", "primary_attr", "attack_type", "img", "icon",
};
var heroes = new List<HeroStatSafe>();
foreach (var element in heroesDoc.RootElement.EnumerateArray())
{
    var all = new Dictionary<string, object?>(StringComparer.Ordinal);
    foreach (var property in element.EnumerateObject())
    {
        all[property.Name] = ToPrimitive(property.Value);
    }

    var stat = new HeroStatSafe
    {
        Id = (int)(all.GetValueOrDefault("id") is long idLong ? idLong : 0),
        Name = all.GetValueOrDefault("name") as string ?? string.Empty,
        LocalizedName = all.GetValueOrDefault("localized_name") as string ?? string.Empty,
        PrimaryAttribute = all.GetValueOrDefault("primary_attr") as string ?? string.Empty,
    };
    foreach (var key in known) all.Remove(key);
    stat.Stats = all;
    heroes.Add(stat);
}
var heroBytes = JsonSerializer.SerializeToUtf8Bytes(heroes);
var heroRoundTrip = JsonSerializer.Deserialize<List<HeroStatSafe>>(heroBytes, options);
Console.WriteLine($"heroes: {heroes.Count} parsed, reserialize {heroBytes.Length} bytes, " +
    $"roundtrip {heroRoundTrip?.Count}, 1_pick of first = {heroes[0].GetInt("1_pick")}, roles={string.Join(",", heroes[0].Roles)}");

static object? ToPrimitive(JsonElement element) => element.ValueKind switch
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

public sealed class HeroStatSafe
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LocalizedName { get; set; } = string.Empty;
    public string PrimaryAttribute { get; set; } = string.Empty;
    public Dictionary<string, object?> Stats { get; set; } = [];

    public int GetInt(string key) => Stats.GetValueOrDefault(key) switch
    {
        long value => (int)value,
        double value => (int)value,
        _ => 0,
    };

    public IEnumerable<string> Roles => Stats.GetValueOrDefault("roles") is List<object?> list
        ? list.OfType<string>()
        : [];
}

public sealed class AbilityDefinition
{
    [JsonPropertyName("dname")] public string? DisplayName { get; set; }
    [JsonPropertyName("behavior"), JsonConverter(typeof(FlexibleStringConverter))] public string? Behavior { get; set; }
    [JsonPropertyName("damage_type"), JsonConverter(typeof(FlexibleStringConverter))] public string? DamageType { get; set; }
    private List<AbilityAttribute> _attributes = [];
    [JsonPropertyName("attrib")] public List<AbilityAttribute> Attributes { get => _attributes; set => _attributes = value ?? []; }
    [JsonPropertyName("cd"), JsonConverter(typeof(FlexibleStringConverter))] public string? Cooldown { get; set; }
}

public sealed class AbilityAttribute
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("value"), JsonConverter(typeof(FlexibleStringConverter))] public string? ValueText { get; set; }
}

public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String: return reader.GetString();
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
                        JsonValueKind.Number => item.GetDouble().ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                        _ => null,
                    };
                    if (!string.IsNullOrWhiteSpace(text)) parts.Add(text!);
                }
                return parts.Count > 0 ? string.Join(" / ", parts) : null;
            }
            default: return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
