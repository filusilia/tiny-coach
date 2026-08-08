using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegisCoach.Native.Data;
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
    private string _cacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AegisCoach",
        "cache");

    /// <summary>资料缓存目录，可在设置页自定义；修改后新的缓存读写生效。</summary>
    public string CacheDirectory
    {
        get => _cacheDirectory;
        set
        {
            if (!string.IsNullOrWhiteSpace(value)) _cacheDirectory = value;
        }
    }

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
                // 技能/天赋定义先读本地持久化缓存（离线也能拿到天赋数值），再尝试联网刷新。
                cached.HeroAbilities = await ReadFileAsync<Dictionary<string, HeroAbilityMap>>(
                    HeroAbilitiesCachePath, cancellationToken) ?? [];
                cached.Abilities = await ReadFileAsync<Dictionary<string, AbilityDefinition>>(
                    AbilitiesCachePath, cancellationToken) ?? [];
                try
                {
                    var cachedOfficialHeroesTask = TryGetDotaFeedAsync<HeroListData>("/herolist?language=schinese", cancellationToken);
                    var freshHeroAbilitiesTask = RefreshAbilitiesCacheAsync(cancellationToken);
                    await Task.WhenAll(cachedOfficialHeroesTask, freshHeroAbilitiesTask);
                    if (freshHeroAbilitiesTask.Result)
                    {
                        cached.HeroAbilities = await ReadFileAsync<Dictionary<string, HeroAbilityMap>>(
                            HeroAbilitiesCachePath, cancellationToken) ?? cached.HeroAbilities;
                        cached.Abilities = await ReadFileAsync<Dictionary<string, AbilityDefinition>>(
                            AbilitiesCachePath, cancellationToken) ?? cached.Abilities;
                    }

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
        await WriteFileAsync(HeroAbilitiesCachePath, catalog.HeroAbilities, cancellationToken);
        await WriteFileAsync(AbilitiesCachePath, catalog.Abilities, cancellationToken);
        return catalog;
    }

    private async Task<bool> RefreshAbilitiesCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var heroAbilitiesTask = GetOpenDotaAsync<Dictionary<string, HeroAbilityMap>>("/constants/hero_abilities", cancellationToken);
            var abilitiesTask = GetOpenDotaAsync<Dictionary<string, AbilityDefinition>>("/constants/abilities", cancellationToken);
            await Task.WhenAll(heroAbilitiesTask, abilitiesTask);
            await WriteFileAsync(HeroAbilitiesCachePath, heroAbilitiesTask.Result, cancellationToken);
            await WriteFileAsync(AbilitiesCachePath, abilitiesTask.Result, cancellationToken);
            return true;
        }
        catch
        {
            // 联网刷新失败时保留旧缓存，天赋数值仍可离线解析。
            return false;
        }
    }

    private string HeroAbilitiesCachePath => CachePath("hero-abilities");
    private string AbilitiesCachePath => CachePath("abilities");

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

        // OpenDota 当前 schema 已不含 avg_rank_tier，分段过滤会 400；统一按最近 30 天全分段统计。
        var sql = $@"
SELECT item_id,
       COUNT(*) AS picks,
       SUM(CASE WHEN ((player_slot < 128) = radiant_win) THEN 1 ELSE 0 END) AS wins
FROM (
    SELECT pm.player_slot,
           pm.hero_id,
           m.radiant_win,
           m.start_time,
           unnest(ARRAY[pm.item_0, pm.item_1, pm.item_2, pm.item_3, pm.item_4, pm.item_5]) AS item_id
    FROM player_matches pm
    JOIN matches m ON pm.match_id = m.match_id
    WHERE pm.hero_id = {heroId}
      AND m.start_time > extract(epoch from now() - interval '30 days')
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
        var allItems = constants
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
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .OrderBy(item => item.LocalizedName, StringComparer.CurrentCulture)
            .ToList();
        var items = allItems.Where(IsCurrentItem).ToList();
        foreach (var item in items) ApplyNeutralTier(item);
        // 用全量（含卷轴）建索引：合成路线组件与卷轴都从这里解析。
        var byName = allItems.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            for (var index = 0; index < item.RecipeComponents.Count; index++)
            {
                var component = item.RecipeComponents[index];
                var matched = byName.TryGetValue(component.Name, out var componentItem)
                    || byName.TryGetValue("item_" + component.Name, out componentItem);
                if (matched && componentItem is not null)
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

            // OpenDota 的 components 不含卷轴；这里按内部名补上卷轴组件，展示完整合成路线。
            if (item.RecipeComponents.Count > 0)
            {
                var shortName = item.Name.StartsWith("item_", StringComparison.OrdinalIgnoreCase) ? item.Name[5..] : item.Name;
                if (byName.TryGetValue("item_recipe_" + shortName, out var recipe) && recipe is not null)
                {
                    item.RecipeComponents.Add(new ItemRecipeComponent
                    {
                        Id = recipe.Id,
                        Name = recipe.Name,
                        LocalizedName = "卷轴",
                        EnglishName = recipe.EnglishName,
                        Count = 1,
                    });
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
                // 过滤掉已移除版本的组件（如旧合成件），但保留卷轴（合成路线需要展示）。
                item.RecipeComponents.AddRange(source.RecipeComponents
                    .Where(component =>
                        component.Name.StartsWith("item_recipe_", StringComparison.OrdinalIgnoreCase) ||
                        CurrentItemCatalog.IsCurrent(component.Name)));
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            // Recipe data is an enhancement; the current official item directory remains usable offline.
        }

        return items;
    }

    public static List<OfficialItemSummary> FilterCurrentItems(IEnumerable<OfficialItemSummary> items)
    {
        var filtered = items.Where(IsCurrentItem).GroupBy(item => item.Id).Select(group => group.First()).ToList();
        foreach (var item in filtered) ApplyNeutralTier(item);
        return filtered;
    }

    /// <summary>OpenDota 英雄物品热度：各阶段购买次数（key 为物品 ID）。</summary>
    public sealed class HeroItemPopularity
    {
        [JsonPropertyName("start_game_items")] public Dictionary<int, int> StartGameItems { get; set; } = [];
        [JsonPropertyName("early_game_items")] public Dictionary<int, int> EarlyGameItems { get; set; } = [];
        [JsonPropertyName("mid_game_items")] public Dictionary<int, int> MidGameItems { get; set; } = [];
        [JsonPropertyName("late_game_items")] public Dictionary<int, int> LateGameItems { get; set; } = [];
    }

    public sealed class HeroMatchupStat
    {
        [JsonPropertyName("hero_id")] public int HeroId { get; set; }
        [JsonPropertyName("games_played")] public int GamesPlayed { get; set; }
        [JsonPropertyName("wins")] public int Wins { get; set; }

        [JsonIgnore]
        public double WinRate => GamesPlayed > 0 ? Wins * 100d / GamesPlayed : 0;
    }

    public async Task<HeroItemPopularity> GetHeroItemPopularityAsync(int heroId, CancellationToken cancellationToken)
    {
        var result = await GetOpenDotaAsync<HeroItemPopularity>($"/heroes/{heroId}/itemPopularity", cancellationToken);
        return result ?? new HeroItemPopularity();
    }

    public async Task<List<HeroMatchupStat>> GetHeroMatchupsAsync(int heroId, CancellationToken cancellationToken)
    {
        var result = await GetOpenDotaAsync<List<HeroMatchupStat>>($"/heroes/{heroId}/matchups", cancellationToken);
        return result ?? [];
    }

    /// <summary>
    /// 以当前版本游戏文件（商店页签 + 中立分层 + 肉山/升级/特殊物品）为准判定物品是否属于当前版本。
    /// 官方数据源 itemlist 会包含全部历史定义，因此用白名单（而非黑名单）过滤。
    /// </summary>
    private static bool IsCurrentItem(OfficialItemSummary item)
    {
        if (item.Id <= 0 || item.IsRecipe || string.IsNullOrWhiteSpace(item.Name)) return false;
        var name = item.Name;
        if (name.Contains("_recipe_", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("item_tango_single", StringComparison.OrdinalIgnoreCase)) return false;
        return CurrentItemCatalog.IsCurrent(name);
    }

    /// <summary>为当前目录中的中立物品写入真实中立等级，供页面分类与展示。</summary>
    private static void ApplyNeutralTier(OfficialItemSummary item)
    {
        if (CurrentItemCatalog.IsNeutralItem(item.Name))
        {
            item.NeutralTier = CurrentItemCatalog.NeutralTier(item.Name);
        }
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
        var tempPath = path + ".tmp";
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            }

            // 先完整序列化到临时文件再原子替换，避免上次失败的截断文件长期污染缓存。
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* 尽力清理 */ }
            throw;
        }
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
