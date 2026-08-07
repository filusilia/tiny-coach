using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegisCoach.Native.Models;

namespace AegisCoach.Native.Services;

public sealed class DotaDataService
{
    public const string TargetPatch = "7.41e";
    private const string OpenDotaBase = "https://api.opendota.com/api";
    private const string DotaBase = "https://www.dota2.com/datafeed";
    private const string ExplorerBase = "https://api.opendota.com/api/explorer";
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LocalizationTtl = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };
    private readonly string _cacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AegisCoach");

    public DotaDataService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AegisCoach/0.1 Windows");
    }

    public async Task<DotaCatalog> LoadCatalogAsync(bool force, CancellationToken cancellationToken)
    {
        var cachePath = CachePath("catalog");
        if (!force && IsFresh(cachePath, CatalogTtl))
        {
            var cached = await ReadFileAsync<DotaCatalog>(cachePath, cancellationToken);
            if (cached is not null)
            {
                try
                {
                    var cachedHeroAbilitiesTask = GetOpenDotaAsync<Dictionary<string, HeroAbilityMap>>("/constants/hero_abilities", cancellationToken);
                    var cachedAbilitiesTask = GetOpenDotaAsync<Dictionary<string, AbilityDefinition>>("/constants/abilities", cancellationToken);
                    var cachedOfficialHeroesTask = TryGetDotaFeedAsync<HeroListData>("/herolist?language=schinese", cancellationToken);
                    await Task.WhenAll(cachedHeroAbilitiesTask, cachedAbilitiesTask, cachedOfficialHeroesTask);
                    cached.HeroAbilities = cachedHeroAbilitiesTask.Result;
                    cached.Abilities = cachedAbilitiesTask.Result;
                    var cachedNames = cachedOfficialHeroesTask.Result?.Heroes
                        .Where(hero => hero.Id > 0 && !string.IsNullOrWhiteSpace(hero.LocalizedName))
                        .ToDictionary(hero => hero.Id, hero => hero.LocalizedName) ?? [];
                    foreach (var hero in cached.Heroes)
                    {
                        if (cachedNames.TryGetValue(hero.Id, out var localizedName)) hero.LocalizedName = localizedName;
                    }
                    cached.Heroes = cached.Heroes.OrderBy(hero => hero.LocalizedName, StringComparer.CurrentCulture).ToList();
                }
                catch
                {
                    // Cached hero stats remain useful when optional ability data is unavailable.
                }

                return cached;
            }
        }

        var heroesTask = GetOpenDotaAsync<List<HeroStat>>("/heroStats", cancellationToken);
        var heroAbilitiesTask = GetOpenDotaAsync<Dictionary<string, HeroAbilityMap>>("/constants/hero_abilities", cancellationToken);
        var abilitiesTask = GetOpenDotaAsync<Dictionary<string, AbilityDefinition>>("/constants/abilities", cancellationToken);
        var officialHeroesTask = TryGetDotaFeedAsync<HeroListData>("/herolist?language=schinese", cancellationToken);

        await Task.WhenAll(heroesTask, heroAbilitiesTask, abilitiesTask, officialHeroesTask);
        var localizedNames = officialHeroesTask.Result?.Heroes.ToDictionary(hero => hero.Id, hero => hero.LocalizedName)
            ?? new Dictionary<int, string>();

        var catalog = new DotaCatalog
        {
            Heroes = heroesTask.Result
                .Select(hero =>
                {
                    hero.LocalizedName = localizedNames.GetValueOrDefault(hero.Id, hero.LocalizedName);
                    return hero;
                })
                .OrderBy(hero => hero.LocalizedName, StringComparer.CurrentCulture)
                .ToList(),
            HeroAbilities = heroAbilitiesTask.Result,
            Abilities = abilitiesTask.Result,
        };

        await WriteFileAsync(cachePath, catalog, cancellationToken);
        return catalog;
    }

    public async Task<LocalizedHeroData> LoadLocalizedHeroAsync(int heroId, bool force, CancellationToken cancellationToken)
    {
        var cachePath = CachePath($"hero-{heroId}-zh");
        if (!force && IsFresh(cachePath, LocalizationTtl))
        {
            var cached = await ReadFileAsync<LocalizedHeroData>(cachePath, cancellationToken);
            if (cached is not null) return cached;
        }

        var data = await GetDotaFeedAsync<HeroDetailData>(
            $"/herodata?language=schinese&hero_id={heroId}", cancellationToken);
        var hero = data.Heroes.FirstOrDefault() ?? throw new InvalidOperationException("官方接口没有返回该英雄的中文技能");
        await WriteFileAsync(cachePath, hero, cancellationToken);
        return hero;
    }

    public async Task<List<OfficialItemSummary>> LoadItemsAsync(bool force, CancellationToken cancellationToken)
    {
        var cachePath = CachePath("items");
        if (!force && IsFresh(cachePath, CatalogTtl))
        {
            var cached = await ReadFileAsync<List<OfficialItemSummary>>(cachePath, cancellationToken);
            if (cached is { Count: > 0 })
            {
                ItemCatalogSource = "缓存 · 当前目录过滤";
                return await EnrichRecipesAsync(FilterCurrentItems(cached), cancellationToken);
            }
        }

        var data = await TryGetDotaFeedAsync<ItemListData>("/itemlist?language=schinese", cancellationToken);
        var items = FilterCurrentItems(data?.Items ?? []);

        if (items.Count == 0)
        {
            items = await LoadOpenDotaItemsAsync(cancellationToken);
            ItemCatalogSource = "OpenDota 降级目录";
        }
        else
        {
            ItemCatalogSource = "官方中文目录";
        }

        items = await EnrichRecipesAsync(items, cancellationToken);
        await WriteFileAsync(cachePath, items, cancellationToken);
        return items;
    }

    public string ItemCatalogSource { get; private set; } = "未加载";

    public async Task<List<HeroBuildMetric>> LoadHeroBuildsAsync(
        int heroId,
        string rankId,
        bool force,
        CancellationToken cancellationToken)
    {
        var cachePath = CachePath($"hero-build-{heroId}-{rankId}");
        if (!force && IsFresh(cachePath, CatalogTtl))
        {
            var cached = await ReadFileAsync<List<HeroBuildMetric>>(cachePath, cancellationToken);
            if (cached is { Count: > 0 }) return cached;
        }

        var rankFilter = rankId == "all"
            ? string.Empty
            : $" AND m.avg_rank_tier >= {int.Parse(rankId) * 10} AND m.avg_rank_tier < {(int.Parse(rankId) + 1) * 10}";
        var sql = $@"
SELECT item_id,
       COUNT(*) AS picks,
       SUM(CASE WHEN ((player_slot < 128) = radiant_win) THEN 1 ELSE 0 END) AS wins
FROM (
    SELECT pm.player_slot,
           pm.hero_id,
           m.radiant_win,
           m.avg_rank_tier,
           m.start_time,
           unnest(ARRAY[pm.item_0, pm.item_1, pm.item_2, pm.item_3, pm.item_4, pm.item_5]) AS item_id
    FROM player_matches pm
    JOIN matches m ON pm.match_id = m.match_id
    WHERE pm.hero_id = {heroId}
      AND m.start_time > extract(epoch from now() - interval '30 days')
      {rankFilter.TrimStart()}
) recent_hero_items
WHERE item_id > 0
GROUP BY item_id
ORDER BY picks DESC
LIMIT 24";

        var metrics = (await GetExplorerAsync<HeroBuildMetricRow>(sql, cancellationToken))
            .Where(row => row.ItemId > 0 && row.Picks > 0)
            .Select(row => new HeroBuildMetric { ItemId = row.ItemId, Picks = row.Picks, Wins = row.Wins })
            .ToList();
        await WriteFileAsync(cachePath, metrics, cancellationToken);
        return metrics;
    }

    public async Task<OfficialItemData> LoadItemDetailsAsync(int itemId, bool force, CancellationToken cancellationToken)
    {
        var cachePath = CachePath($"item-{itemId}-zh");
        if (!force && IsFresh(cachePath, LocalizationTtl))
        {
            var cached = await ReadFileAsync<OfficialItemData>(cachePath, cancellationToken);
            if (cached is not null) return cached;
        }

        var data = await GetDotaFeedAsync<ItemDetailData>(
            $"/itemdata?language=schinese&item_id={itemId}", cancellationToken);
        var item = data.Items.FirstOrDefault() ?? throw new InvalidOperationException("官方接口没有返回该物品的中文资料");
        await WriteFileAsync(cachePath, item, cancellationToken);
        return item;
    }

    private async Task<List<OfficialItemSummary>> LoadOpenDotaItemsAsync(CancellationToken cancellationToken)
    {
        var constants = await GetOpenDotaAsync<Dictionary<string, OpenDotaItem>>("/constants/items", cancellationToken);
        var items = constants
            .Where(pair => pair.Value is not null)
            .Select(pair =>
            {
                var item = pair.Value!;
                var id = item.Id > 0 ? item.Id : int.TryParse(pair.Key, out var parsedId) ? parsedId : 0;
                var name = string.IsNullOrWhiteSpace(item.Name) ? $"item_{pair.Key}" : item.Name;
                var localizedName = string.IsNullOrWhiteSpace(item.LocalizedName) ? item.DisplayName : item.LocalizedName;
                var summary = new OfficialItemSummary
                {
                    Id = id,
                    Name = name,
                    LocalizedName = string.IsNullOrWhiteSpace(localizedName) ? name : localizedName,
                    EnglishName = string.IsNullOrWhiteSpace(item.DisplayName) ? name : item.DisplayName,
                };
                summary.RecipeComponents.AddRange((item.Components ?? [])
                    .Where(component => !string.IsNullOrWhiteSpace(component))
                    .GroupBy(component => component, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new ItemRecipeComponent { Name = group.Key, Count = group.Count() }));
                return summary;
            })
            .Where(IsCurrentItem)
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .OrderBy(item => item.LocalizedName, StringComparer.CurrentCulture)
            .ToList();
        var byName = items.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            for (var index = 0; index < item.RecipeComponents.Count; index++)
            {
                var component = item.RecipeComponents[index];
                if (byName.TryGetValue(component.Name, out var componentItem))
                {
                    item.RecipeComponents[index] = new ItemRecipeComponent
                    {
                        Id = componentItem.Id,
                        Name = componentItem.Name,
                        LocalizedName = componentItem.LocalizedName,
                        EnglishName = componentItem.EnglishName,
                        Count = component.Count,
                    };
                }
            }
        }
        return items;
    }

    private async Task<List<OfficialItemSummary>> EnrichRecipesAsync(
        List<OfficialItemSummary> items,
        CancellationToken cancellationToken)
    {
        try
        {
            var openDotaItems = await LoadOpenDotaItemsAsync(cancellationToken);
            var byId = openDotaItems.ToDictionary(item => item.Id);
            foreach (var item in items)
            {
                if (!byId.TryGetValue(item.Id, out var source) || source.RecipeComponents.Count == 0) continue;
                item.RecipeComponents.Clear();
                item.RecipeComponents.AddRange(source.RecipeComponents);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            // Recipe data is an enhancement; the current official item directory remains usable offline.
        }

        return items;
    }

    public static List<OfficialItemSummary> FilterCurrentItems(IEnumerable<OfficialItemSummary> items) =>
        items.Where(IsCurrentItem).GroupBy(item => item.Id).Select(group => group.First()).ToList();

    private static bool IsCurrentItem(OfficialItemSummary item)
    {
        if (item.Id <= 0 || item.IsRecipe || string.IsNullOrWhiteSpace(item.Name)) return false;
        var name = item.Name.ToLowerInvariant();
        if (name.Contains("_recipe_") || name.Contains("courier") || name.Contains("tango_single")) return false;
        return name is not "item_stout_shield" and not "item_iron_talon" and not "item_ring_of_aquila"
            and not "item_necronomicon" and not "item_necronomicon_2" and not "item_necronomicon_3"
            and not "item_pennant";
    }

    public async Task<Dictionary<int, ItemMetric>> LoadItemMetricsAsync(bool force, CancellationToken cancellationToken)
    {
        var cachePath = CachePath("item-metrics");
        if (!force && IsFresh(cachePath, CatalogTtl))
        {
            var cached = await ReadFileAsync<Dictionary<int, ItemMetric>>(cachePath, cancellationToken);
            if (cached is not null) return cached;
        }

        const string sql = @"
SELECT item_id,
       COUNT(*) AS picks,
       SUM(CASE WHEN ((player_slot < 128) = radiant_win) THEN 1 ELSE 0 END) AS wins
FROM (
    SELECT pm.player_slot,
           m.radiant_win,
           unnest(ARRAY[pm.item_0, pm.item_1, pm.item_2, pm.item_3, pm.item_4, pm.item_5]) AS item_id
    FROM player_matches pm
    JOIN matches m ON pm.match_id = m.match_id
    WHERE m.start_time > extract(epoch from now() - interval '30 days')
) recent_items
WHERE item_id > 0
GROUP BY item_id";

        var rows = await GetExplorerAsync<ItemMetricRow>(sql, cancellationToken);
        var metrics = rows
            .Where(row => row.ItemId > 0)
            .ToDictionary(row => row.ItemId, row => new ItemMetric { Picks = row.Picks, Wins = row.Wins });
        await WriteFileAsync(cachePath, metrics, cancellationToken);
        return metrics;
    }

    public void ClearCache()
    {
        if (!Directory.Exists(_cacheDirectory)) return;
        foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.json"))
        {
            try { File.Delete(file); } catch { /* Cache cleanup is best effort. */ }
        }
    }

    private string CachePath(string name) => Path.Combine(_cacheDirectory, $"{name}-v{TargetPatch}.json");

    private async Task<T> GetOpenDotaAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(OpenDotaBase + path, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("OpenDota 返回了空数据");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"OpenDota 数据格式无法解析：{path}。{exception.Message}", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException($"OpenDota 请求失败：{path}。{exception.Message}", exception);
        }
    }

    private async Task<List<T>> GetExplorerAsync<T>(string sql, CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = $"{ExplorerBase}?sql={Uri.EscapeDataString(sql)}";
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<ExplorerEnvelope<T>>(JsonOptions, cancellationToken);
            if (envelope?.Rows is null)
            {
                throw new InvalidOperationException("OpenDota Explorer 返回了空数据");
            }

            if (!string.IsNullOrWhiteSpace(envelope.Error))
            {
                throw new InvalidOperationException($"OpenDota Explorer 查询失败：{envelope.Error}");
            }

            return envelope.Rows;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"OpenDota Explorer 数据格式无法解析。{exception.Message}", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException($"OpenDota Explorer 请求失败：{exception.Message}", exception);
        }
    }

    private async Task<T> GetDotaFeedAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        try
        {
            using var response = await _httpClient.GetAsync(DotaBase + path, cancellationToken);
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<DatafeedEnvelope<T>>(JsonOptions, cancellationToken);
            if (envelope?.Result?.Data is null) throw new InvalidOperationException("Dota 2 官方接口返回了空数据");
            return envelope.Result.Data;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Dota 2 官方数据格式无法解析：{path}。{exception.Message}", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException($"Dota 2 官方接口请求失败：{path}。{exception.Message}", exception);
        }
    }

    private async Task<T?> TryGetDotaFeedAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await GetDotaFeedAsync<T>(path, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            Directory.CreateDirectory(_cacheDirectory);
            File.WriteAllText(
                Path.Combine(_cacheDirectory, "last-data-source-error.txt"),
                $"Dota 2 官方数据源不可用：{path}{Environment.NewLine}{exception.Message}");
            return null;
        }
    }

    private static bool IsFresh(string path, TimeSpan ttl)
    {
        return File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) <= ttl;
    }

    private async Task<T?> ReadFileAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (IOException) { return default; }
        catch (JsonException) { return default; }
    }

    private async Task WriteFileAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private sealed class DatafeedEnvelope<T>
    {
        [JsonPropertyName("result")] public DatafeedResult<T>? Result { get; set; }
    }

    private sealed class DatafeedResult<T>
    {
        [JsonPropertyName("data")] public T? Data { get; set; }
    }

    private sealed class HeroListData
    {
        private List<OfficialHeroSummary> _heroes = [];
        [JsonPropertyName("heroes")] public List<OfficialHeroSummary> Heroes { get => _heroes; set => _heroes = value ?? []; }
    }

    private sealed class HeroDetailData
    {
        private List<LocalizedHeroData> _heroes = [];
        [JsonPropertyName("heroes")] public List<LocalizedHeroData> Heroes { get => _heroes; set => _heroes = value ?? []; }
    }

    private sealed class ItemListData
    {
        private List<OfficialItemSummary> _items = [];
        [JsonPropertyName("itemabilities")] public List<OfficialItemSummary> Items { get => _items; set => _items = value ?? []; }
    }

    private sealed class ItemDetailData
    {
        private List<OfficialItemData> _items = [];
        [JsonPropertyName("items")] public List<OfficialItemData> Items { get => _items; set => _items = value ?? []; }
    }

    private sealed class ItemMetricRow
    {
        [JsonPropertyName("item_id")] public int ItemId { get; set; }
        [JsonPropertyName("picks")] public int Picks { get; set; }
        [JsonPropertyName("wins")] public int Wins { get; set; }
    }

    private sealed class HeroBuildMetricRow
    {
        [JsonPropertyName("item_id")] public int ItemId { get; set; }
        [JsonPropertyName("picks")] public int Picks { get; set; }
        [JsonPropertyName("wins")] public int Wins { get; set; }
    }

    private sealed class OpenDotaItem
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("dname")] public string DisplayName { get; set; } = string.Empty;
        [JsonPropertyName("localized_name")] public string LocalizedName { get; set; } = string.Empty;
        private List<string> _components = [];
        [JsonPropertyName("components")] public List<string> Components { get => _components; set => _components = value ?? []; }
    }

    private sealed class ExplorerEnvelope<T>
    {
        private List<T> _rows = [];
        [JsonPropertyName("rows")] public List<T> Rows { get => _rows; set => _rows = value ?? []; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}
