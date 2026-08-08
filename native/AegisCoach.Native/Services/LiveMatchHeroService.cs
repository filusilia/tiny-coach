using System.Text.Json;

namespace AegisCoach.Native.Services;

public sealed record LiveHero(int AccountId, int HeroId, int Team, string? PlayerName);

public sealed record LiveMatchInfo(
    string MatchId,
    int MyTeam,
    int RadiantScore,
    int DireScore,
    IReadOnlyList<LiveHero> Heroes);

/// <summary>
/// 从 OpenDota 公开实时对局列表按自己的 steamid 找到当前比赛，返回全场英雄。
/// 只读公开接口，不上传任何数据；人机/私人/未公开比赛不会出现在列表中。
/// </summary>
public sealed class LiveMatchHeroService
{
    private const string OpenDotaLive = "https://api.opendota.com/api/live";
    private const string OpenDotaMatch = "https://api.opendota.com/api/matches";
    private const string StratzGraphQl = "https://api.stratz.com/graphql";
    private const long AccountIdOffset = 76561197960265728L;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public LiveMatchHeroService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AegisCoach/0.2 Windows");
    }

    public async Task<LiveMatchInfo?> FindLiveMatchAsync(long steamId64, CancellationToken cancellationToken)
    {
        var accountId = steamId64 - AccountIdOffset;
        if (accountId <= 0) return null;

        using var response = await _httpClient.GetAsync(OpenDotaLive, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        foreach (var game in doc.RootElement.EnumerateArray())
        {
            if (!game.TryGetProperty("players", out var players)) continue;

            var myTeam = -1;
            foreach (var player in players.EnumerateArray())
            {
                if (player.TryGetProperty("account_id", out var account)
                    && account.TryGetInt64(out var id)
                    && id == accountId)
                {
                    myTeam = player.TryGetProperty("team", out var team) ? team.GetInt32() : -1;
                    break;
                }
            }

            if (myTeam < 0) continue;

            var heroes = new List<LiveHero>();
            foreach (var player in players.EnumerateArray())
            {
                var playerAccount = player.TryGetProperty("account_id", out var account) ? account.GetInt64() : 0;
                var heroId = player.TryGetProperty("hero_id", out var hero) ? hero.GetInt32() : 0;
                var team = player.TryGetProperty("team", out var teamValue) ? teamValue.GetInt32() : -1;
                var playerName = player.TryGetProperty("name", out var name) ? name.GetString() : null;
                heroes.Add(new LiveHero((int)playerAccount, heroId, team, playerName));
            }

            var matchId = game.TryGetProperty("match_id", out var match) ? match.ToString() ?? string.Empty : string.Empty;
            var radiantScore = game.TryGetProperty("radiant_score", out var radiant) ? radiant.GetInt32() : 0;
            var direScore = game.TryGetProperty("dire_score", out var dire) ? dire.GetInt32() : 0;
            return new LiveMatchInfo(matchId, myTeam, radiantScore, direScore, heroes);
        }

        return null;
    }

    /// <summary>
    /// 用 GSI 提供的比赛 ID 直查 OpenDota 比赛数据，返回全场英雄。
    /// OpenDota 收录后（热门对局对局中即可，普通公开对局通常在结束后收录）比实时列表更可靠，
    /// 且能覆盖实时列表之外的普通对局。
    /// </summary>
    public async Task<LiveMatchInfo?> FindMatchByIdAsync(string matchId, string myTeamName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(matchId) || !long.TryParse(matchId, out _)) return null;

        using var response = await _httpClient.GetAsync($"{OpenDotaMatch}/{matchId}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;
        if (!root.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var heroes = new List<LiveHero>();
        foreach (var player in players.EnumerateArray())
        {
            var accountId = player.TryGetProperty("account_id", out var account) && account.ValueKind == JsonValueKind.Number
                ? account.GetInt32()
                : 0;
            var heroId = player.TryGetProperty("hero_id", out var hero) && hero.ValueKind == JsonValueKind.Number
                ? hero.GetInt32()
                : 0;
            var slot = player.TryGetProperty("player_slot", out var slotValue) && slotValue.ValueKind == JsonValueKind.Number
                ? slotValue.GetInt32()
                : 0;
            var team = slot < 128 ? 0 : 1;
            var playerName = player.TryGetProperty("personaname", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
            heroes.Add(new LiveHero(accountId, heroId, team, playerName));
        }

        var myTeam = string.Equals(myTeamName, "dire", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var radiantScore = root.TryGetProperty("radiant_score", out var radiant) && radiant.ValueKind == JsonValueKind.Number
            ? radiant.GetInt32()
            : 0;
        var direScore = root.TryGetProperty("dire_score", out var dire) && dire.ValueKind == JsonValueKind.Number
            ? dire.GetInt32()
            : 0;
        return new LiveMatchInfo(matchId, myTeam, radiantScore, direScore, heroes);
    }

    /// <summary>
    /// 用 Stratz GraphQL 按比赛 ID 直查实时比赛数据。
    /// Stratz 对公开比赛做实时收录，对局中（含选人阶段）即可返回全场英雄；
    /// 需要免费 API 密钥（https://stratz.com/api），未填写密钥时不要调用本方法。
    /// </summary>
    public async Task<LiveMatchInfo?> FindMatchByStratzAsync(string matchId, string apiKey, string myTeamName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(matchId) || !long.TryParse(matchId, out _) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var query = "query { match(id: " + matchId + ") { id didEnd radiantScore direScore " +
                    "players { steamAccountId isRadiant heroId } } }";
        var payload = JsonSerializer.Serialize(new { query });
        using var request = new HttpRequestMessage(HttpMethod.Post, StratzGraphQl)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", "AegisCoach/0.2 Windows");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("match", out var match)
            || match.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!match.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var heroes = new List<LiveHero>();
        foreach (var player in players.EnumerateArray())
        {
            var heroId = 0;
            if (player.TryGetProperty("heroId", out var heroIdNode) && heroIdNode.ValueKind == JsonValueKind.Number)
            {
                heroId = heroIdNode.GetInt32();
            }

            var isRadiant = player.TryGetProperty("isRadiant", out var radiantNode)
                && radiantNode.ValueKind == JsonValueKind.True;
            var accountId = player.TryGetProperty("steamAccountId", out var accountNode) && accountNode.ValueKind == JsonValueKind.Number
                ? accountNode.GetInt32()
                : 0;
            heroes.Add(new LiveHero(accountId, heroId, isRadiant ? 0 : 1, null));
        }

        var myTeam = string.Equals(myTeamName, "dire", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var radiantScore = match.TryGetProperty("radiantScore", out var radiant) && radiant.ValueKind == JsonValueKind.Number
            ? radiant.GetInt32()
            : 0;
        var direScore = match.TryGetProperty("direScore", out var dire) && dire.ValueKind == JsonValueKind.Number
            ? dire.GetInt32()
            : 0;
        return new LiveMatchInfo(matchId, myTeam, radiantScore, direScore, heroes);
    }
}
