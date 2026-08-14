using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AegisCoach.Native.Data;
using AegisCoach.Native.Models;
using AegisCoach.Core.Services;

namespace AegisCoach.Native.Services;

public sealed partial class DotaDataService
{
    public const string TargetPatch = "7.41e";
    private const string OpenDotaBase = "https://api.opendota.com/api";
    private const string DotaBase = "https://www.dota2.com/datafeed";
    private const string ChinaDotaBase = "https://www.dota2.com.cn/datafeed";
    private const string ExplorerBase = "https://api.opendota.com/api/explorer";
    private const int MaxRequestAttempts = 3;
    private static readonly TimeSpan MinimumRequestSpacing = TimeSpan.FromMilliseconds(150);
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
    private readonly SemaphoreSlim _requestStartGate = new(1, 1);
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;
    private string _cacheDirectory = AegisCoachPaths.Cache;

    /// <summary>官网详情文件（data\items-details-official.json）的内存索引，按物品数字 ID 匹配。</summary>
    private readonly object _officialDetailsLock = new();
    private Dictionary<int, OfficialItemDetailsEntry>? _officialItemDetailsById;

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
                        if (string.IsNullOrWhiteSpace(hero.EnglishName) || LocalizedText.ContainsChinese(hero.EnglishName))
                        {
                            hero.EnglishName = hero.LocalizedName;
                        }
                        if (cachedNames.TryGetValue(hero.Id, out var localizedName)) hero.LocalizedName = localizedName;
                    }
                    cached.Heroes = cached.Heroes.OrderBy(hero => hero.LocalizedName, StringComparer.CurrentCulture).ToList();
                }
                catch
                {
                    // Cached hero stats remain useful when optional ability data is unavailable.
                }

                ApplyOfficialHeroOverrides(cached.Heroes);
                return cached;
            }
        }

        try
        {
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
                        hero.EnglishName = hero.LocalizedName;
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
            ApplyOfficialHeroOverrides(catalog.Heroes);
            CatalogSourceNote = "官方中文数据 + OpenDota";
            return catalog;
        }
        catch (Exception exception) when (!force)
        {
            // 缓存过期或损坏时仍尝试联网；网络失败则回退到过期缓存，保证离线可读。
            var stale = await ReadFileAsync<DotaCatalog>(cachePath, cancellationToken);
            if (stale is not null)
            {
                stale.HeroAbilities = await ReadFileAsync<Dictionary<string, HeroAbilityMap>>(
                    HeroAbilitiesCachePath, cancellationToken) ?? [];
                stale.Abilities = await ReadFileAsync<Dictionary<string, AbilityDefinition>>(
                    AbilitiesCachePath, cancellationToken) ?? [];
                ApplyOfficialHeroOverrides(stale.Heroes);
                CatalogSourceNote = $"缓存数据 · 网络暂不可用（{exception.Message}）";
                return stale;
            }

            throw;
        }
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
            if (cached is not null)
            {
                // 精简缓存可能缺天赋数值（special_values 为空），从技能 bonuses 补齐。
                EnrichTalentValues(cached);
                return cached;
            }
        }

        try
        {
            // 优先请求中国官网接口（www.dota2.com.cn/datafeed/hero），失败回退国际接口。
            LocalizedHeroData? hero = null;
            var lastError = new InvalidOperationException("官方接口没有返回该英雄的中文技能");
            try
            {
                hero = await GetChinaDotaHeroAsync(heroId, cancellationToken);
            }
            catch (Exception exception)
            {
                lastError = exception is InvalidOperationException invalid
                    ? invalid
                    : new InvalidOperationException($"中国官网接口失败（{exception.Message}）", exception);
            }

            hero ??= await GetDotaFeedAsync<HeroDetailData>(
                $"/herodata?language=schinese&hero_id={heroId}", cancellationToken)
                .ContinueWith(task =>
                {
                    if (task.IsFaulted) throw task.Exception!.GetBaseException();
                    return task.Result.Heroes.FirstOrDefault();
                }, cancellationToken);

            if (hero is null) throw lastError;
            await WriteFileAsync(cachePath, hero, cancellationToken);
            return hero;
        }
        catch (Exception exception) when (!force)
        {
            // 网络失败时回退到过期缓存，保证离线仍能查看技能中文资料。
            var stale = await ReadFileAsync<LocalizedHeroData>(cachePath, cancellationToken);
            if (stale is not null)
            {
                EnrichTalentValues(stale);
                return stale;
            }
            throw new InvalidOperationException($"无法加载该英雄的官方中文技能（{exception.Message}）", exception);
        }
    }

    /// <summary>
    /// 中国官网英雄详情接口：https://www.dota2.com.cn/datafeed/hero?hero_id={id}
    /// 官网返回 {"status":"success","result":{"heroes":{单个英雄}}}（heroes 是对象不是数组）。
    /// </summary>
    private async Task<LocalizedHeroData?> GetChinaDotaHeroAsync(int heroId, CancellationToken cancellationToken)
    {
        using var response = await GetWithRetryAsync($"{ChinaDotaBase}/hero?hero_id={heroId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        // 官网直返：result.heroes 为单个英雄对象（含 abilities/talents）。
        var direct = JsonSerializer.Deserialize<ChinaHeroDetailRoot>(content, JsonOptions);
        var hero = direct?.Result?.Heroes;
        if (hero is null || hero.Id <= 0)
        {
            // 兼容旧结构：result.data.heroes 数组。
            var envelope = JsonSerializer.Deserialize<DatafeedEnvelope<HeroDetailData>>(content, JsonOptions);
            hero = envelope?.Result?.Data?.Heroes.FirstOrDefault();
        }

        if (hero is not null) EnrichTalentValues(hero);
        return hero;
    }

    /// <summary>中国官网返回结构：{"status":"success","result":{"heroes":{单个英雄}}}。</summary>
    private sealed class ChinaHeroDetailRoot
    {
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("result")] public ChinaHeroDetailResult? Result { get; set; }
    }

    private sealed class ChinaHeroDetailResult
    {
        [JsonPropertyName("heroes")] public LocalizedHeroData? Heroes { get; set; }
    }

    /// <summary>
    /// 官网技能 special_values[].bonuses 提供了天赋数值（name=天赋 ID，value=加成量），
    /// 把这些数值按占位符变量名合并进对应天赋的 SpecialValues，天赋树即可显示具体数值。
    /// 例：死亡旋风 tree_damage_scale.bonuses 含 special_bonus_unique_timbersaw=75，
    /// 天赋文本 "+{s:bonus_tree_damage_scale} 死亡旋风树倒额外伤害" 解析为 "+75"。
    /// </summary>
    private static void EnrichTalentValues(LocalizedHeroData hero)
    {
        // 天赋ID -> (占位符变量名 -> 数值)。占位符变量名按 "bonus_" + special_value.name 推断。
        var bonusByTalent = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var ability in hero.Abilities)
        {
            foreach (var specialValue in ability.SpecialValues)
            {
                if (string.IsNullOrWhiteSpace(specialValue.Name)) continue;
                var placeholderKey = "bonus_" + specialValue.Name;
                foreach (var bonus in specialValue.Bonuses)
                {
                    if (string.IsNullOrWhiteSpace(bonus.Name)) continue;
                    if (!bonusByTalent.TryGetValue(bonus.Name, out var values))
                    {
                        values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                        bonusByTalent[bonus.Name] = values;
                    }

                    values.TryAdd(placeholderKey, bonus.Value);
                }
            }
        }

        if (bonusByTalent.Count == 0) return;

        foreach (var talent in hero.Talents)
        {
            if (string.IsNullOrWhiteSpace(talent.LocalizedName)) continue;
            if (!bonusByTalent.TryGetValue(talent.Name, out var values)) continue;

            var existing = talent.SpecialValues.Select(value => value.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in TalentPlaceholderRegex().Matches(talent.LocalizedName))
            {
                var key = match.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(key) || existing.Contains(key)) continue;
                if (!values.TryGetValue(key, out var value)) continue;
                talent.SpecialValues.Add(new AbilitySpecialValue
                {
                    Name = key,
                    Values = [value],
                });
                existing.Add(key);
            }
        }
    }

    [GeneratedRegex(@"{s:([^}]+)}")]
    private static partial Regex TalentPlaceholderRegex();

    /// <summary>批量刷新全部英雄的中文详情到本地缓存；返回成功更新的数量。
    /// 连续失败达到阈值时中止（网络不可用/官网下线时避免逐个英雄长时间重试）。</summary>
    public async Task<int> RefreshAllHeroDetailsAsync(IEnumerable<int> heroIds, CancellationToken cancellationToken)
    {
        var updated = 0;
        var consecutiveFailures = 0;
        const int abortAfterFailures = 3;
        foreach (var heroId in heroIds)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                await LoadLocalizedHeroAsync(heroId, force: true, cancellationToken);
                updated++;
                consecutiveFailures = 0;
            }
            catch
            {
                if (++consecutiveFailures >= abortAfterFailures) break;
            }

            try { await Task.Delay(MinimumRequestSpacing, cancellationToken); } catch (OperationCanceledException) { break; }
        }

        return updated;
    }

    /// <summary>清理不再属于当前英雄列表的旧详情缓存（如旧版本/废弃英雄），只保留最新英雄的缓存。</summary>
    public int PurgeStaleHeroDetailCaches(IReadOnlySet<int> currentHeroIds)
    {
        if (!Directory.Exists(_cacheDirectory)) return 0;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "hero-*-zh-v*.json"))
        {
            var name = Path.GetFileName(file);
            if (!TryParseHeroCacheId(name, out var heroId) || !currentHeroIds.Contains(heroId))
            {
                try { File.Delete(file); removed++; } catch { /* 尽力清理 */ }
            }
        }

        return removed;
    }

    private static bool TryParseHeroCacheId(string fileName, out int heroId)
    {
        heroId = 0;
        var match = Regex.Match(fileName, @"^hero-(d+)-zh-", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out heroId);
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
                var cachedItems = await EnrichRecipesAsync(FilterCurrentItems(cached), cancellationToken);
                ApplyOfficialItemOverrides(cachedItems);
                ApplyOfficialItemDetailOverrides(cachedItems);
                return cachedItems;
            }
        }

        try
        {
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
            ApplyOfficialItemOverrides(items);
            ApplyOfficialItemDetailOverrides(items);
            await WriteFileAsync(cachePath, items, cancellationToken);
            return items;
        }
        catch (Exception exception) when (!force)
        {
            // 网络失败时回退到过期缓存，保证物品目录离线可读。
            var stale = await ReadFileAsync<List<OfficialItemSummary>>(cachePath, cancellationToken);
            if (stale is { Count: > 0 })
            {
                ItemCatalogSource = "缓存 · 当前目录过滤（网络暂不可用）";
                var staleItems = await EnrichRecipesAsync(FilterCurrentItems(stale), cancellationToken);
                ApplyOfficialItemDetailOverrides(staleItems);
                return staleItems;
            }

            throw new InvalidOperationException($"无法加载物品资料（{exception.Message}）", exception);
        }
    }

    public string ItemCatalogSource { get; private set; } = "未加载";

    /// <summary>英雄目录数据来源说明；网络不可用时标记为缓存数据。</summary>
    public string CatalogSourceNote { get; private set; } = "官方中文数据 + OpenDota";

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

        try
        {
            var metrics = (await GetExplorerAsync<HeroBuildMetricRow>(sql, cancellationToken))
                .Where(row => row.ItemId > 0 && row.Picks > 0)
                .Select(row => new HeroBuildMetric { ItemId = row.ItemId, Picks = row.Picks, Wins = row.Wins })
                .ToList();
            await WriteFileAsync(cachePath, metrics, cancellationToken);
            return metrics;
        }
        catch (Exception exception) when (!force)
        {
            // 出装统计是增强数据；网络失败时回退过期缓存，仍为空则交给页面降级提示。
            var stale = await ReadFileAsync<List<HeroBuildMetric>>(cachePath, cancellationToken);
            if (stale is { Count: > 0 }) return stale;
            throw new InvalidOperationException($"无法加载推荐出装（{exception.Message}）", exception);
        }
    }

    public async Task<OfficialItemData> LoadItemDetailsAsync(int itemId, bool force, CancellationToken cancellationToken)
    {
        // 官网详情文件（data\items-details-official.json，可选覆盖层）始终优先：
        // 其 desc/attrib 已是带数值的最终中文文本，比网络 datafeed 更可靠。
        if (LoadOfficialItemDetails().TryGetValue(itemId, out var official))
        {
            return new OfficialItemData
            {
                Id = official.Id,
                Name = official.EnglishName,
                LocalizedName = official.DisplayName,
                Description = official.Description,
                Lore = official.Lore,
                Cost = official.Cost,
                Cooldowns = official.Cooldowns,
                ManaCosts = official.ManaCosts,
                AttribLines = ParseAttribLines(official.Attrib),
                Requirements = official.Requirements,
            };
        }

        var cachePath = CachePath($"item-{itemId}-zh");
        if (!force && IsFresh(cachePath, LocalizationTtl))
        {
            var cached = await ReadFileAsync<OfficialItemData>(cachePath, cancellationToken);
            if (cached is not null) return cached;
        }

        try
        {
            var data = await GetDotaFeedAsync<ItemDetailData>(
                $"/itemdata?language=schinese&item_id={itemId}", cancellationToken);
            var item = data.Items.FirstOrDefault() ?? throw new InvalidOperationException("官方接口没有返回该物品的中文资料");
            await WriteFileAsync(cachePath, item, cancellationToken);
            return item;
        }
        catch (Exception exception) when (!force)
        {
            // 网络失败时回退到过期缓存，保证物品详情离线可读。
            var stale = await ReadFileAsync<OfficialItemData>(cachePath, cancellationToken);
            if (stale is not null) return stale;
            throw new InvalidOperationException($"无法加载该物品的官方中文资料（{exception.Message}）", exception);
        }
    }

    /// <summary>读取官网详情文件（data\items-details-official.json，itemdata 结构）并按物品 ID 建索引。
    /// 文件不存在或解析失败时返回空表，不影响网络/缓存路径。</summary>
    private Dictionary<int, OfficialItemDetailsEntry> LoadOfficialItemDetails()
    {
        if (_officialItemDetailsById is not null) return _officialItemDetailsById;
        lock (_officialDetailsLock)
        {
            if (_officialItemDetailsById is not null) return _officialItemDetailsById;
            var index = new Dictionary<int, OfficialItemDetailsEntry>();
            try
            {
                var path = Path.Combine(OfficialDataDirectory, "items-details-official.json");
                if (!File.Exists(path)) return _officialItemDetailsById = index;
                var json = File.ReadAllText(path);
                var root = JsonSerializer.Deserialize<OfficialItemDetailsRoot>(json, JsonOptions);
                if (root?.ItemData is null) return _officialItemDetailsById = index;
                foreach (var entry in root.ItemData.Values)
                {
                    if (entry.Id > 0) index.TryAdd(entry.Id, entry);
                }
            }
            catch
            {
                // 详情数据文件是可选项，失败不影响既有逻辑。
            }

            _officialItemDetailsById = index;
            return index;
        }
    }

    /// <summary>把官网 attrib 的 HTML（每行 &lt;p&gt;…&lt;/p&gt;）解析成纯文本属性行（如 "+攻击力：9"）。</summary>
    private static List<string> ParseAttribLines(string? attribHtml)
    {
        if (string.IsNullOrWhiteSpace(attribHtml)) return [];
        var lines = new List<string>();
        foreach (Match match in AttribParagraphRegex().Matches(attribHtml))
        {
            var text = StripHtmlTags(match.Groups[1].Value).Trim();
            if (text.Length > 0) lines.Add(text);
        }

        if (lines.Count == 0)
        {
            foreach (var raw in attribHtml.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var text = StripHtmlTags(raw).Trim();
                if (text.Length > 0) lines.Add(text);
            }
        }

        return lines;
    }

    private static string StripHtmlTags(string html) =>
        HtmlTagRegex().Replace(html, string.Empty).Replace("&nbsp;", " ").Trim();

    [GeneratedRegex("<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AttribParagraphRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

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
                var localizedName = LocalizedText.ChineseOr(
                    item.LocalizedName,
                    LocalizedText.ChineseOr(item.DisplayName, "中文名称待同步"));
                var summary = new OfficialItemSummary
                {
                    Id = id,
                    Name = name,
                    LocalizedName = localizedName,
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
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TimeoutException)
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
        var cachePath = CachePath($"hero-popularity-{heroId}");
        if (IsFresh(cachePath, CatalogTtl))
        {
            var cached = await ReadFileAsync<HeroItemPopularity>(cachePath, cancellationToken);
            if (cached is not null) return cached;
        }

        try
        {
            var result = await GetOpenDotaAsync<HeroItemPopularity>($"/heroes/{heroId}/itemPopularity", cancellationToken);
            if (result is not null) await WriteFileAsync(cachePath, result, cancellationToken);
            return result ?? new HeroItemPopularity();
        }
        catch
        {
            var stale = await ReadFileAsync<HeroItemPopularity>(cachePath, cancellationToken);
            if (stale is not null) return stale;
            throw;
        }
    }

    public async Task<List<HeroMatchupStat>> GetHeroMatchupsAsync(int heroId, CancellationToken cancellationToken)
    {
        var cachePath = CachePath($"hero-matchups-{heroId}");
        if (IsFresh(cachePath, CatalogTtl))
        {
            var cached = await ReadFileAsync<List<HeroMatchupStat>>(cachePath, cancellationToken);
            if (cached is not null) return cached;
        }

        try
        {
            var result = await GetOpenDotaAsync<List<HeroMatchupStat>>($"/heroes/{heroId}/matchups", cancellationToken);
            if (result is { Count: > 0 }) await WriteFileAsync(cachePath, result, cancellationToken);
            return result ?? [];
        }
        catch
        {
            var stale = await ReadFileAsync<List<HeroMatchupStat>>(cachePath, cancellationToken);
            if (stale is not null) return stale;
            throw;
        }
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

        try
        {
            var rows = await GetExplorerAsync<ItemMetricRow>(sql, cancellationToken);
            var metrics = rows
                .Where(row => row.ItemId > 0)
                .ToDictionary(row => row.ItemId, row => new ItemMetric { Picks = row.Picks, Wins = row.Wins });
            await WriteFileAsync(cachePath, metrics, cancellationToken);
            return metrics;
        }
        catch (Exception exception) when (!force)
        {
            // 物品统计是增强数据；网络失败时回退过期缓存，仍为空则交给页面降级提示。
            var stale = await ReadFileAsync<Dictionary<int, ItemMetric>>(cachePath, cancellationToken);
            if (stale is not null) return stale;
            throw new InvalidOperationException($"无法加载物品统计（{exception.Message}）", exception);
        }
    }

    public void ClearCache()
    {
        if (!Directory.Exists(_cacheDirectory)) return;
        foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.json"))
        {
            try { File.Delete(file); } catch { /* Cache cleanup is best effort. */ }
        }
    }

    /// <summary>
    /// 官网（dota2.com.cn）数据文件目录：文档\AegisCoach\data\。
    /// 用户把官网物品/英雄列表 JSON 保存为 items-official.json / heroes-official.json 后，
    /// 应用会用它补齐物品价格、官网图片 URL 与中文名，图片由 ImageCacheService 下载到本地缓存。
    /// </summary>
    public static string OfficialDataDirectory => Path.Combine(AegisCoachPaths.Root, "data");

    /// <summary>应用官网物品数据：价格 + 官网图片 URL。数据文件不存在时静默跳过。</summary>
    public static void ApplyOfficialItemOverrides(IEnumerable<OfficialItemSummary> items)
    {
        try
        {
            var path = Path.Combine(OfficialDataDirectory, "items-official.json");
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var response = JsonSerializer.Deserialize<OfficialItemListResponse>(json, JsonOptions);
            var entries = response?.Result.Basic.Concat(response.Result.Upgrade).Concat(response.Result.Neutral)
                .SelectMany(section => section.Items)
                .Where(entry => int.TryParse(entry.ItemId, out _))
                .ToList();
            if (entries is null || entries.Count == 0) return;
            var byId = entries.GroupBy(entry => int.Parse(entry.ItemId)).ToDictionary(group => group.Key, group => group.First());
            foreach (var item in items)
            {
                if (!byId.TryGetValue(item.Id, out var official)) continue;
                if (official.CostValue > 0) item.Cost = official.CostValue;
                if (!string.IsNullOrWhiteSpace(official.ImgUrl)) item.ImageUrlOverride = official.ImgUrl;
                if (string.IsNullOrWhiteSpace(item.LocalizedName) && !string.IsNullOrWhiteSpace(official.NameLoc))
                {
                    item.LocalizedName = official.NameLoc;
                }
            }
        }
        catch
        {
            // 官网数据文件是可选项，失败不影响既有目录。
        }
    }

    /// <summary>用官网详情文件（items-details-official.json）的价格覆盖目录数据（OpenDota 降级目录价格缺失）。
    /// 这样合成路线能算出卷轴价格（总价 - 组件价）。</summary>
    private void ApplyOfficialItemDetailOverrides(List<OfficialItemSummary> items)
    {
        if (items.Count == 0) return;
        var index = LoadOfficialItemDetails();
        if (index.Count == 0) return;
        foreach (var item in items)
        {
            if (index.TryGetValue(item.Id, out var entry) && entry.Cost > 0)
            {
                item.Cost = entry.Cost;
            }
        }
    }

    /// <summary>应用官网英雄数据：官网图片 URL + 中文名。数据文件不存在时静默跳过。</summary>
    public static void ApplyOfficialHeroOverrides(IEnumerable<HeroStat> heroes)
    {
        try
        {
            var path = Path.Combine(OfficialDataDirectory, "heroes-official.json");
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var response = JsonSerializer.Deserialize<OfficialHeroListResponse>(json, JsonOptions);
            var byId = response?.Result.Heroes.Where(hero => hero.Id > 0).ToDictionary(hero => hero.Id);
            if (byId is null || byId.Count == 0) return;
            foreach (var hero in heroes)
            {
                if (!byId.TryGetValue(hero.Id, out var official)) continue;
                if (!string.IsNullOrWhiteSpace(official.CropsImg)) hero.ImageUrlOverride = official.CropsImg;
                if (string.IsNullOrWhiteSpace(hero.LocalizedName) && !string.IsNullOrWhiteSpace(official.NameLoc))
                {
                    hero.LocalizedName = official.NameLoc;
                }
            }
        }
        catch
        {
            // 官网数据文件是可选项。
        }
    }

    /// <summary>官网数据文件提供的物品图片 URL（用于预下载缓存）。</summary>
    public static IEnumerable<string> OfficialItemImageUrls()
    {
        try
        {
            var path = Path.Combine(OfficialDataDirectory, "items-official.json");
            if (!File.Exists(path)) return [];
            var json = File.ReadAllText(path);
            var response = JsonSerializer.Deserialize<OfficialItemListResponse>(json, JsonOptions);
            return response?.Result.Basic.Concat(response.Result.Upgrade).Concat(response.Result.Neutral)
                .SelectMany(section => section.Items)
                .Select(entry => entry.ImgUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private string CachePath(string name) => Path.Combine(_cacheDirectory, $"{name}-v{TargetPatch}.json");

    private async Task<T> GetOpenDotaAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await GetWithRetryAsync(OpenDotaBase + path, cancellationToken);
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
            throw new InvalidOperationException($"OpenDota {DescribeHttpFailure(exception)}：{path}", exception);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException($"OpenDota 请求超时：{path}", exception);
        }
    }

    private async Task<List<T>> GetExplorerAsync<T>(string sql, CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = $"{ExplorerBase}?sql={Uri.EscapeDataString(sql)}";
            using var response = await GetWithRetryAsync(requestUri, cancellationToken);
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
            throw new InvalidOperationException($"OpenDota Explorer {DescribeHttpFailure(exception)}", exception);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException("OpenDota Explorer 请求超时", exception);
        }
    }

    private async Task<T> GetDotaFeedAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        try
        {
            using var response = await GetWithRetryAsync(DotaBase + path, cancellationToken);
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
            throw new InvalidOperationException($"Dota 2 官方接口{DescribeHttpFailure(exception)}：{path}", exception);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException($"Dota 2 官方接口请求超时：{path}", exception);
        }
    }

    private async Task<T?> TryGetDotaFeedAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await GetDotaFeedAsync<T>(path, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TimeoutException)
        {
            Directory.CreateDirectory(_cacheDirectory);
            AtomicFile.WriteAllText(
                Path.Combine(_cacheDirectory, "last-data-source-error.txt"),
                $"Dota 2 官方数据源不可用：{path}{Environment.NewLine}{exception.Message}");
            return null;
        }
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(string uri, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < MaxRequestAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForRequestSlotAsync(cancellationToken);
            try
            {
                var response = await _httpClient.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt == MaxRequestAttempts - 1)
                {
                    return response;
                }

                var delay = RetryDelay(response, attempt);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException exception) when (attempt < MaxRequestAttempts - 1)
            {
                lastException = exception;
                await Task.Delay(RetryDelay(null, attempt), cancellationToken);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = exception;
                if (attempt == MaxRequestAttempts - 1)
                {
                    throw new TimeoutException("远程数据源在 30 秒内没有响应", exception);
                }

                await Task.Delay(RetryDelay(null, attempt), cancellationToken);
            }
        }

        throw lastException ?? new HttpRequestException("远程数据源请求失败");
    }

    private async Task WaitForRequestSlotAsync(CancellationToken cancellationToken)
    {
        await _requestStartGate.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextRequestAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            _nextRequestAt = DateTimeOffset.UtcNow + MinimumRequestSpacing;
        }
        finally
        {
            _requestStartGate.Release();
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || statusCode == HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static TimeSpan RetryDelay(HttpResponseMessage? response, int attempt)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delta;
        }

        if (retryAfter?.Date is DateTimeOffset date && date > DateTimeOffset.UtcNow)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay;
        }

        return TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt));
    }

    private static string DescribeHttpFailure(HttpRequestException exception) => exception.StatusCode switch
    {
        HttpStatusCode.TooManyRequests => "请求过于频繁，请稍后重试",
        HttpStatusCode.RequestTimeout => "请求超时",
        >= HttpStatusCode.InternalServerError => "服务暂时不可用",
        null => $"网络连接失败（{exception.Message}）",
        _ => $"请求失败（HTTP {(int)exception.StatusCode.Value}）",
    };

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
        catch (JsonException)
        {
            QuarantineCorruptCache(path);
            return default;
        }
        catch (NotSupportedException)
        {
            QuarantineCorruptCache(path);
            return default;
        }
    }

    private async Task WriteFileAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
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

    private static void QuarantineCorruptCache(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch
        {
            // A damaged cache is optional; a failed quarantine must not block a fresh download.
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
