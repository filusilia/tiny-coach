using System.Text.Json;
using System.Net;
using AegisCoach.Core.Models;

namespace AegisCoach.Core.Services;

/// <summary>
/// 按比赛 ID 从 OpenDota 拉取整场解析数据（公开接口，无需密钥），
/// 提取本方玩家的装备购买时间、击杀/阵亡时间线、买活、团战、视野、经济分位与双方阵容，
/// 供赛后 AI 复盘使用。数据边界：OpenDota 不含阵亡前逐秒状态，该信息来自本地 GSI 回放。
/// </summary>
public sealed class ApiMatchAnalyzer
{
    private const string OpenDotaMatchBase = "https://api.opendota.com/api/matches";
    private const int MaxRequestAttempts = 3;
    private const long AccountIdOffset = 76561197960265728L;

    private static readonly HashSet<string> Consumables = new(StringComparer.OrdinalIgnoreCase)
    {
        "tango", "branches", "clarity", "flask", "ward_sentry", "ward_observer",
        "smoke_of_deceit", "tpscroll", "blood_grenade", "enchanted_mango", "mango",
        "faerie_fire", "dust", "bottle", "infused_raindrop",
    };

    private readonly HttpClient _httpClient;

    public ApiMatchAnalyzer(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AegisCoach/0.2 Windows");
    }

    /// <summary>
    /// 拉取并分析一场比赛。steamId64 用于定位“我”；留空时若 expectedHeroId 唯一匹配也能定位。
    /// heroInternalNameResolver 把 hero_id 转成 npc_dota_hero_x（用于从 kills_log 反推阵亡时间）；
    /// itemNameResolver 把 OpenDota 短名（如 blink）转成中文名。
    /// </summary>
    public async Task<ApiMatchSummary?> FetchAsync(
        long matchId,
        string? steamId64,
        int expectedHeroId,
        Func<int, string>? heroInternalNameResolver,
        Func<string, string>? itemNameResolver,
        Func<int, string>? itemIdResolver,
        CancellationToken cancellationToken)
    {
        if (matchId <= 0)
            return new ApiMatchSummary { MatchId = matchId, Error = "比赛 ID 无效" };

        try
        {
            using var response = await GetWithRetryAsync($"{OpenDotaMatchBase}/{matchId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ApiMatchSummary
                {
                    MatchId = matchId,
                    Error = response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "OpenDota 未收录该比赛（对局可能未公开，或刚结束尚未解析）"
                        : $"接口返回 {(int)response.StatusCode}",
                };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return Analyze(document.RootElement, matchId, steamId64, expectedHeroId, heroInternalNameResolver, itemNameResolver, itemIdResolver);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new ApiMatchSummary
            {
                MatchId = matchId,
                Error = exception is TaskCanceledException ? "请求超时" : "网络请求失败",
            };
        }
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(string uri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxRequestAttempts; attempt++)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                var transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;
                if (!transient || attempt == MaxRequestAttempts - 1) return response;
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < MaxRequestAttempts - 1)
            {
                // Retry transient connection failures below.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)), cancellationToken);
        }

        throw new HttpRequestException("OpenDota 请求失败");
    }

    public ApiMatchSummary? Analyze(
        JsonElement root,
        long matchId,
        string? steamId64,
        int expectedHeroId,
        Func<int, string>? heroInternalNameResolver,
        Func<string, string>? itemNameResolver,
        Func<int, string>? itemIdResolver)
    {
        if (!root.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
        {
            return new ApiMatchSummary { MatchId = matchId, Error = "比赛数据缺少 players 字段（可能尚未完整解析）" };
        }

        var myAccountId = ToAccountId(steamId64);
        var myIndex = FindMyPlayer(players, myAccountId, expectedHeroId);
        if (myIndex < 0)
        {
            return new ApiMatchSummary
            {
                MatchId = matchId,
                Error = "未找到你在该场比赛中的玩家数据（Steam ID 不匹配，或比赛不在公开解析范围内）",
            };
        }

        var summary = new ApiMatchSummary
        {
            MatchId = matchId,
            RadiantWin = GetBool(root, "radiant_win"),
            DurationSeconds = GetDouble(root, "duration"),
            RadiantScore = GetInt(root, "radiant_score"),
            DireScore = GetInt(root, "dire_score"),
            GameMode = GetInt(root, "game_mode").ToString(),
            RankTier = GetNullableInt(players[myIndex], "rank_tier"),
        };

        var myPlayer = players[myIndex];
        summary.HeroId = GetInt(myPlayer, "hero_id");
        summary.AccountId = GetInt(myPlayer, "account_id");
        summary.IsRadiant = GetInt(myPlayer, "player_slot") < 128;
        summary.Kills = GetInt(myPlayer, "kills");
        summary.Deaths = GetInt(myPlayer, "deaths");
        summary.Assists = GetInt(myPlayer, "assists");
        summary.LastHits = GetInt(myPlayer, "last_hits");
        summary.Denies = GetInt(myPlayer, "denies");
        summary.Gpm = GetInt(myPlayer, "gold_per_min");
        summary.Xpm = GetInt(myPlayer, "xp_per_min");
        summary.Level = GetInt(myPlayer, "level");
        summary.NetWorth = GetInt(myPlayer, "net_worth");
        summary.HeroDamage = GetDouble(myPlayer, "hero_damage");
        summary.TowerDamage = GetDouble(myPlayer, "tower_damage");
        summary.HeroHealing = GetDouble(myPlayer, "hero_healing");
        summary.ActionsPerMinute = GetDouble(myPlayer, "actions_per_min");
        summary.Pings = GetInt(myPlayer, "pings");
        summary.Lane = GetString(myPlayer, "lane");
        summary.LaneRole = GetInt(myPlayer, "lane_role");
        summary.LaneEfficiencyPct = GetNullableDouble(myPlayer, "lane_efficiency_pct");
        summary.RoshansKilled = GetInt(myPlayer, "roshans_killed");
        summary.ObserversPlaced = GetInt(myPlayer, "obs_placed");
        summary.SentriesPlaced = GetInt(myPlayer, "sen_placed");
        summary.ObserverLogCount = GetArrayCount(myPlayer, "obs_log");
        summary.TeamfightParticipation = GetDouble(myPlayer, "teamfight_participation");
        summary.FinalItems = GetMainItems(myPlayer, itemNameResolver, itemIdResolver);

        var heroInternalName = heroInternalNameResolver?.Invoke(summary.HeroId) ?? string.Empty;
        ExtractKillsAndDeaths(summary, players, myIndex, heroInternalName, itemNameResolver);
        ExtractItemTimings(summary, myPlayer, itemNameResolver);
        ExtractBuybacks(summary, myPlayer);
        ExtractEconomy(summary, myPlayer);
        ExtractBenchmarks(summary, myPlayer);
        ExtractTeamfights(summary, root, myPlayer);
        ExtractTowersTaken(summary, root, myPlayer);
        ExtractAllPlayers(summary, players, itemNameResolver, itemIdResolver);
        return summary;
    }

    private static int FindMyPlayer(JsonElement players, int? myAccountId, int expectedHeroId)
    {
        for (var index = 0; index < players.GetArrayLength(); index++)
        {
            var player = players[index];
            var accountId = GetInt(player, "account_id");
            if (myAccountId is not null && accountId == myAccountId) return index;
        }

        if (myAccountId is null && expectedHeroId > 0)
        {
            var matches = 0;
            var found = -1;
            for (var index = 0; index < players.GetArrayLength(); index++)
            {
                if (GetInt(players[index], "hero_id") != expectedHeroId) continue;
                matches++;
                found = index;
            }

            if (matches == 1) return found;
        }

        return -1;
    }

    private static void ExtractKillsAndDeaths(
        ApiMatchSummary summary,
        JsonElement players,
        int myIndex,
        string heroInternalName,
        Func<string, string>? itemNameResolver)
    {
        var mySlot = GetInt(players[myIndex], "player_slot");
        foreach (var player in players.EnumerateArray())
        {
            if (!player.TryGetProperty("kills_log", out var killsLog) || killsLog.ValueKind != JsonValueKind.Array) continue;
            foreach (var entry in killsLog.EnumerateArray())
            {
                var time = GetDouble(entry, "time");
                var victim = GetString(entry, "key") ?? string.Empty;
                if (GetInt(player, "player_slot") == mySlot)
                {
                    if (time > 0)
                    {
                        summary.KillsLog.Add(new KillEvent
                        {
                            TimeSeconds = time,
                            VictimHero = TitleCase(victim),
                        });
                    }
                }
                else if (!string.IsNullOrWhiteSpace(heroInternalName)
                         && string.Equals(victim, heroInternalName, StringComparison.OrdinalIgnoreCase)
                         && time > 0)
                {
                    summary.DeathTimes.Add(time);
                }
            }
        }
        summary.DeathTimes.Sort();

        summary.KilledBy = ReadHeroCounts(players[myIndex], "killed_by");
        summary.Killed = ReadHeroCounts(players[myIndex], "killed");
    }

    private static List<HeroCount> ReadHeroCounts(JsonElement player, string propertyName)
    {
        var result = new List<HeroCount>();
        if (!player.TryGetProperty(propertyName, out var counts) || counts.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in counts.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number)
            {
                result.Add(new HeroCount { HeroName = TitleCase(property.Name), Count = property.Value.GetInt32() });
            }
        }
        return result.OrderByDescending(item => item.Count).ToList();
    }

    private static void ExtractItemTimings(
        ApiMatchSummary summary,
        JsonElement player,
        Func<string, string>? itemNameResolver)
    {
        if (!player.TryGetProperty("purchase_time", out var purchases)
            || purchases.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in purchases.EnumerateObject())
        {
            var key = property.Name;
            if (key.StartsWith("recipe_", StringComparison.OrdinalIgnoreCase)) continue;
            if (Consumables.Contains(key)) continue;
            if (property.Value.ValueKind != JsonValueKind.Number) continue;
            var time = property.Value.GetDouble();
            if (time < 0) continue;
            summary.ItemTimings.Add(new ItemTiming
            {
                TimeSeconds = time,
                ItemName = itemNameResolver?.Invoke(key) ?? TitleCase(key),
            });
        }
        summary.ItemTimings = summary.ItemTimings.OrderBy(item => item.TimeSeconds).ToList();
    }

    private static void ExtractBuybacks(ApiMatchSummary summary, JsonElement player)
    {
        if (!player.TryGetProperty("buyback_log", out var buybacks) || buybacks.ValueKind != JsonValueKind.Array) return;
        foreach (var entry in buybacks.EnumerateArray())
        {
            var time = GetDouble(entry, "time");
            if (time > 0) summary.BuybackTimes.Add(time);
        }
        summary.BuybackTimes.Sort();
    }

    private static void ExtractEconomy(ApiMatchSummary summary, JsonElement player)
    {
        var gold = player.TryGetProperty("gold_t", out var goldT) ? goldT : default;
        var xp = player.TryGetProperty("xp_t", out var xpT) ? xpT : default;
        var lh = player.TryGetProperty("lh_t", out var lhT) ? lhT : default;
        var minutes = Math.Max(Math.Max(ArrayLength(gold), ArrayLength(xp)), ArrayLength(lh));
        for (var minute = 0; minute < minutes; minute++)
        {
            summary.Economy.Add(new EconomyPoint
            {
                Minute = minute,
                Gold = GetArrayNumber(gold, minute),
                Xp = GetArrayNumber(xp, minute),
                LastHits = (int)GetArrayNumber(lh, minute),
            });
        }
    }

    private static void ExtractBenchmarks(ApiMatchSummary summary, JsonElement player)
    {
        if (!player.TryGetProperty("benchmarks", out var benchmarks) || benchmarks.ValueKind != JsonValueKind.Object) return;
        foreach (var metric in benchmarks.EnumerateObject())
        {
            if (metric.Value.ValueKind != JsonValueKind.Object) continue;
            if (metric.Value.TryGetProperty("pct", out var pct) && pct.ValueKind == JsonValueKind.Number)
            {
                summary.Benchmarks[metric.Name] = pct.GetDouble();
            }
        }
    }

    private static void ExtractTeamfights(ApiMatchSummary summary, JsonElement root, JsonElement myPlayer)
    {
        if (!root.TryGetProperty("teamfights", out var fights) || fights.ValueKind != JsonValueKind.Array) return;
        var slots = root.TryGetProperty("players", out var players)
            ? players.EnumerateArray().Select(GetIntFromSlot).OrderBy(slot => slot).ToList()
            : new List<int>();
        var mySlot = GetInt(myPlayer, "player_slot");
        var myFightIndex = slots.IndexOf(mySlot);

        foreach (var fight in fights.EnumerateArray())
        {
            var tf = new TeamFightSummary
            {
                StartTime = GetDouble(fight, "start"),
                EndTime = GetDouble(fight, "end"),
                TotalDeaths = GetInt(fight, "deaths"),
            };
            if (myFightIndex >= 0
                && fight.TryGetProperty("players", out var fightPlayers)
                && fightPlayers.ValueKind == JsonValueKind.Array
                && myFightIndex < fightPlayers.GetArrayLength())
            {
                var mine = fightPlayers[myFightIndex];
                tf.Participated = true;
                tf.MyDamage = GetDouble(mine, "damage");
                tf.MyDeaths = GetInt(mine, "deaths");
                tf.MyBuybacks = GetInt(mine, "buybacks");
                tf.GoldDelta = GetDouble(mine, "gold_delta");
                tf.XpDelta = GetDouble(mine, "xp_delta");
                tf.MyDeathPositions = mine.TryGetProperty("deaths_pos", out var positions)
                    ? positions.ValueKind == JsonValueKind.Array ? positions.GetArrayLength() : 0
                    : 0;
            }
            summary.Teamfights.Add(tf);
        }
    }

    private static void ExtractTowersTaken(ApiMatchSummary summary, JsonElement root, JsonElement myPlayer)
    {
        if (!root.TryGetProperty("objectives", out var objectives) || objectives.ValueKind != JsonValueKind.Array) return;
        var mySlot = GetInt(myPlayer, "player_slot");
        foreach (var objective in objectives.EnumerateArray())
        {
            var type = GetString(objective, "type") ?? string.Empty;
            if (type != "building_kill") continue;
            if (!objective.TryGetProperty("player_slot", out var slotValue)
                || slotValue.ValueKind != JsonValueKind.Number
                || slotValue.GetInt32() != mySlot)
            {
                continue;
            }
            summary.TowersTaken.Add(new TowerKill
            {
                TimeSeconds = GetDouble(objective, "time"),
                Building = ShortenBuilding(GetString(objective, "key") ?? string.Empty),
            });
        }
    }

    private static void ExtractAllPlayers(
        ApiMatchSummary summary,
        JsonElement players,
        Func<string, string>? itemNameResolver,
        Func<int, string>? itemIdResolver)
    {
        foreach (var player in players.EnumerateArray())
        {
            summary.AllPlayers.Add(new PlayerSummary
            {
                AccountId = GetInt(player, "account_id"),
                HeroId = GetInt(player, "hero_id"),
                IsRadiant = GetInt(player, "player_slot") < 128,
                Kills = GetInt(player, "kills"),
                Deaths = GetInt(player, "deaths"),
                Assists = GetInt(player, "assists"),
                Gpm = GetInt(player, "gold_per_min"),
                Xpm = GetInt(player, "xp_per_min"),
                LastHits = GetInt(player, "last_hits"),
                Items = GetMainItems(player, itemNameResolver, itemIdResolver),
            });
        }
    }

    private static List<string> GetMainItems(
        JsonElement player,
        Func<string, string>? itemNameResolver,
        Func<int, string>? itemIdResolver)
    {
        var result = new List<string>();
        for (var index = 0; index <= 5; index++)
        {
            if (!player.TryGetProperty($"item_{index}", out var value)) continue;
            if (value.ValueKind == JsonValueKind.String)
            {
                var name = value.GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Add(itemNameResolver?.Invoke(name) ?? TitleCase(name));
            }
            else if (value.ValueKind == JsonValueKind.Number)
            {
                var itemId = value.GetInt32();
                if (itemId <= 0) continue;
                result.Add(itemIdResolver?.Invoke(itemId) ?? $"道具{itemId}");
            }
        }
        return result;
    }

    private static int? ToAccountId(string? steamId64)
    {
        if (string.IsNullOrWhiteSpace(steamId64)) return null;
        if (!long.TryParse(steamId64, out var steamId)) return null;
        var accountId = steamId - AccountIdOffset;
        return accountId is > 0 and <= int.MaxValue ? (int)accountId : null;
    }

    private static string ShortenBuilding(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return key;
        var value = key;
        value = value.Replace("npc_dota_badguys_", "", StringComparison.OrdinalIgnoreCase);
        value = value.Replace("npc_dota_goodguys_", "", StringComparison.OrdinalIgnoreCase);
        return value.Replace('_', ' ');
    }

    private static string TitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var last = parts.Length > 0 ? parts[^1] : value;
        if (last.Length > 0)
        {
            last = char.ToUpperInvariant(last[0]) + last[1..];
        }
        return last;
    }

    private static int GetIntFromSlot(JsonElement player) => GetInt(player, "player_slot");

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static int? GetNullableInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static double GetDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;

    private static double? GetNullableDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static bool? GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.True
                ? true
                : value.ValueKind == JsonValueKind.False ? false : null
            : null;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetArrayCount(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;

    private static int ArrayLength(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.GetArrayLength() : 0;

    private static double GetArrayNumber(JsonElement element, int index) =>
        element.ValueKind == JsonValueKind.Array
        && index < element.GetArrayLength()
        && element[index].ValueKind == JsonValueKind.Number
            ? element[index].GetDouble()
            : 0;
}
