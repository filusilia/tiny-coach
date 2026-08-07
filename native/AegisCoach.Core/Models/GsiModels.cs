using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AegisCoach.Core.Models;

// ============ Dota 2 GSI 原始 JSON DTO ============

public sealed class GsiSnapshot
{
    [JsonPropertyName("provider")] public GsiProvider? Provider { get; set; }
    [JsonPropertyName("map")] public GsiMap? Map { get; set; }
    [JsonPropertyName("player")] public GsiPlayer? Player { get; set; }
    [JsonPropertyName("hero")] public GsiHero? Hero { get; set; }
    [JsonPropertyName("abilities")] public Dictionary<string, GsiAbility>? Abilities { get; set; }
    [JsonPropertyName("items")] public Dictionary<string, GsiItem>? Items { get; set; }
    [JsonPropertyName("auth")] public Dictionary<string, string>? Auth { get; set; }
    [JsonPropertyName("draft")] public Dictionary<string, JsonElement>? Draft { get; set; }
}

public sealed class GsiProvider
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("appid")] public int AppId { get; set; }
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
}

public sealed class GsiMap
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("matchid")] public long MatchId { get; set; }
    [JsonPropertyName("gametime")] public double GameTime { get; set; }
    [JsonPropertyName("clock_time")] public double ClockTime { get; set; }
    [JsonPropertyName("daytime")] public bool Daytime { get; set; }
    [JsonPropertyName("radiant_score")] public int RadiantScore { get; set; }
    [JsonPropertyName("dire_score")] public int DireScore { get; set; }
    [JsonPropertyName("game_state")] public string? GameState { get; set; }
    [JsonPropertyName("win_team")] public string? WinTeam { get; set; }
    [JsonPropertyName("customgamename")] public string? CustomGameName { get; set; }
}

public sealed class GsiPlayer
{
    [JsonPropertyName("steamid")] public string? SteamId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("activity")] public string? Activity { get; set; }
    [JsonPropertyName("kills")] public int Kills { get; set; }
    [JsonPropertyName("deaths")] public int Deaths { get; set; }
    [JsonPropertyName("assists")] public int Assists { get; set; }
    [JsonPropertyName("last_hits")] public int LastHits { get; set; }
    [JsonPropertyName("denies")] public int Denies { get; set; }
    [JsonPropertyName("kill_streak")] public int KillStreak { get; set; }
    [JsonPropertyName("team_name")] public string? TeamName { get; set; }
    [JsonPropertyName("gold")] public int Gold { get; set; }
    [JsonPropertyName("gold_reliable")] public int GoldReliable { get; set; }
    [JsonPropertyName("gold_unreliable")] public int GoldUnreliable { get; set; }
    [JsonPropertyName("gpm")] public int Gpm { get; set; }
    [JsonPropertyName("xpm")] public int Xpm { get; set; }
    [JsonPropertyName("ultimate_state")] public string? UltimateState { get; set; }
    [JsonPropertyName("ultimate_cooldown")] public int UltimateCooldown { get; set; }
    [JsonPropertyName("respawn_seconds")] public int RespawnSeconds { get; set; }
    [JsonPropertyName("item_0")] public string? Item0 { get; set; }
    [JsonPropertyName("item_1")] public string? Item1 { get; set; }
    [JsonPropertyName("item_2")] public string? Item2 { get; set; }
    [JsonPropertyName("item_3")] public string? Item3 { get; set; }
    [JsonPropertyName("item_4")] public string? Item4 { get; set; }
    [JsonPropertyName("item_5")] public string? Item5 { get; set; }
}

public sealed class GsiHero
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("xp")] public long Xp { get; set; }
    [JsonPropertyName("alive")] public bool Alive { get; set; }
    [JsonPropertyName("respawn_seconds")] public int RespawnSeconds { get; set; }
    [JsonPropertyName("buyback_cost")] public int BuybackCost { get; set; }
    [JsonPropertyName("buyback_cooldown")] public int BuybackCooldown { get; set; }
    [JsonPropertyName("health")] public int Health { get; set; }
    [JsonPropertyName("max_health")] public int MaxHealth { get; set; }
    [JsonPropertyName("health_percent")] public double HealthPercent { get; set; }
    [JsonPropertyName("mana")] public int Mana { get; set; }
    [JsonPropertyName("max_mana")] public int MaxMana { get; set; }
    [JsonPropertyName("mana_percent")] public double ManaPercent { get; set; }
    [JsonPropertyName("silences")] public int Silences { get; set; }
    [JsonPropertyName("stuns")] public int Stuns { get; set; }
    [JsonPropertyName("disables")] public int Disables { get; set; }
    [JsonPropertyName("magic_immunity")] public bool MagicImmunity { get; set; }
}

public sealed class GsiAbility
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("can_cast")] public bool CanCast { get; set; }
    [JsonPropertyName("passive")] public bool Passive { get; set; }
    [JsonPropertyName("ability_active")] public bool AbilityActive { get; set; }
    [JsonPropertyName("ability_cooldown")] public double AbilityCooldown { get; set; }
    [JsonPropertyName("ability_charges")] public int AbilityCharges { get; set; }
    [JsonPropertyName("ultimate")] public bool Ultimate { get; set; }
}

public sealed class GsiItem
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("purchaser")] public int Purchaser { get; set; }
    [JsonPropertyName("item_level")] public int ItemLevel { get; set; }
    [JsonPropertyName("can_cast")] public bool CanCast { get; set; }
    [JsonPropertyName("cooldown")] public double Cooldown { get; set; }
    [JsonPropertyName("passive")] public bool Passive { get; set; }
    [JsonPropertyName("charges")] public int Charges { get; set; }
}

// ============ 归一化对局快照模型 ============

public enum GamePhase
{
    Unknown,
    PreGame,
    HeroSelection,
    StrategyTime,
    InProgress,
    PostGame,
    Disconnected,
}

public sealed class MatchSnapshot
{
    public long ProviderTimestamp { get; init; }
    public GamePhase Phase { get; init; }
    public long MatchId { get; init; }
    public double GameTime { get; init; }
    public double ClockTime { get; init; }
    public bool Daytime { get; init; }
    public int RadiantScore { get; init; }
    public int DireScore { get; init; }
    public string? WinTeam { get; init; }
    public string? MapName { get; init; }
    public string? CustomGameName { get; init; }
    public PlayerState Player { get; init; } = new();
    public HeroState Hero { get; init; } = new();
    public IReadOnlyList<AbilityState> Abilities { get; init; } = [];
    public IReadOnlyList<ItemState> Items { get; init; } = [];
    public bool AuthTokenPresent { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool InMatch => MatchId > 0 && Phase is GamePhase.InProgress or GamePhase.PostGame;

    [System.Text.Json.Serialization.JsonIgnore]
    public string GameTimeText => FormatGameTime(GameTime);

    [System.Text.Json.Serialization.JsonIgnore]
    public string PhaseText => Phase switch
    {
        GamePhase.PreGame => "赛前",
        GamePhase.HeroSelection => "选人阶段",
        GamePhase.StrategyTime => "策略阶段",
        GamePhase.InProgress => "对局中",
        GamePhase.PostGame => "赛后",
        GamePhase.Disconnected => "已断开",
        _ => "未知",
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string ScoreText => $"{RadiantScore} : {DireScore}";

    public static MatchSnapshot FromGsi(GsiSnapshot raw)
    {
        var map = raw.Map;
        var player = raw.Player;
        var hero = raw.Hero;

        var abilities = new List<AbilityState>();
        if (raw.Abilities is not null)
        {
            foreach (var (slot, ability) in raw.Abilities.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (ability is null || string.IsNullOrWhiteSpace(ability.Name)) continue;
                abilities.Add(new AbilityState
                {
                    Slot = slot,
                    Name = ability.Name,
                    Level = ability.Level,
                    Cooldown = ability.AbilityCooldown,
                    CanCast = ability.CanCast,
                    IsPassive = ability.Passive,
                    IsUltimate = ability.Ultimate,
                    Charges = ability.AbilityCharges,
                });
            }
        }

        var items = new List<ItemState>();
        if (raw.Items is not null)
        {
            foreach (var (slot, item) in raw.Items.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Name)) continue;
                items.Add(new ItemState
                {
                    Slot = slot,
                    Name = item.Name,
                    Cooldown = item.Cooldown,
                    CanCast = item.CanCast,
                    IsPassive = item.Passive,
                    Charges = item.Charges,
                });
            }
        }

        return new MatchSnapshot
        {
            ProviderTimestamp = raw.Provider?.Timestamp ?? 0,
            Phase = PhaseFromString(map?.GameState),
            MatchId = map?.MatchId ?? 0,
            GameTime = map?.GameTime ?? 0,
            ClockTime = map?.ClockTime ?? 0,
            Daytime = map?.Daytime ?? false,
            RadiantScore = map?.RadiantScore ?? 0,
            DireScore = map?.DireScore ?? 0,
            WinTeam = NormalizeTeam(map?.WinTeam),
            MapName = map?.Name,
            CustomGameName = map?.CustomGameName,
            Player = player is null ? new PlayerState() : new PlayerState
            {
                SteamId = player.SteamId,
                Name = player.Name,
                Activity = player.Activity,
                TeamName = NormalizeTeam(player.TeamName),
                Kills = player.Kills,
                Deaths = player.Deaths,
                Assists = player.Assists,
                LastHits = player.LastHits,
                Denies = player.Denies,
                KillStreak = player.KillStreak,
                Gold = player.Gold,
                GoldReliable = player.GoldReliable,
                GoldUnreliable = player.GoldUnreliable,
                Gpm = player.Gpm,
                Xpm = player.Xpm,
                UltimateState = player.UltimateState,
                UltimateCooldown = player.UltimateCooldown,
                RespawnSeconds = player.RespawnSeconds,
            },
            Hero = hero is null ? new HeroState() : new HeroState
            {
                Id = hero.Id,
                Name = hero.Name,
                Level = hero.Level,
                Xp = hero.Xp,
                Alive = hero.Alive,
                RespawnSeconds = hero.RespawnSeconds,
                BuybackCost = hero.BuybackCost,
                BuybackCooldown = hero.BuybackCooldown,
                Health = hero.Health,
                MaxHealth = hero.MaxHealth,
                HealthPercent = hero.HealthPercent,
                Mana = hero.Mana,
                MaxMana = hero.MaxMana,
                ManaPercent = hero.ManaPercent,
                Silences = hero.Silences,
                Stuns = hero.Stuns,
                Disables = hero.Disables,
                MagicImmunity = hero.MagicImmunity,
            },
            Abilities = abilities,
            Items = items,
            AuthTokenPresent = raw.Auth is { Count: > 0 },
        };
    }

    public static GamePhase PhaseFromString(string? state) => state switch
    {
        "DOTA_GAMERULES_STATE_HERO_SELECTION" => GamePhase.HeroSelection,
        "DOTA_GAMERULES_STATE_STRATEGY_TIME" => GamePhase.StrategyTime,
        "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS" => GamePhase.InProgress,
        "DOTA_GAMERULES_STATE_POST_GAME" => GamePhase.PostGame,
        "DOTA_GAMERULES_STATE_DISCONNECT" => GamePhase.Disconnected,
        null or "" => GamePhase.Unknown,
        _ => GamePhase.PreGame,
    };

    public static string FormatGameTime(double seconds)
    {
        var total = Math.Max(0, (int)seconds);
        return $"{total / 60:00}:{total % 60:00}";
    }

    private static string? NormalizeTeam(string? team) =>
        string.IsNullOrWhiteSpace(team) || team == "none" ? null : team;
}

public sealed class PlayerState
{
    public string? SteamId { get; init; }
    public string? Name { get; init; }
    public string? Activity { get; init; }
    public string? TeamName { get; init; }
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
    public int LastHits { get; init; }
    public int Denies { get; init; }
    public int KillStreak { get; init; }
    public int Gold { get; init; }
    public int GoldReliable { get; init; }
    public int GoldUnreliable { get; init; }
    public int Gpm { get; init; }
    public int Xpm { get; init; }
    public string? UltimateState { get; init; }
    public int UltimateCooldown { get; init; }
    public int RespawnSeconds { get; init; }
}

public sealed class HeroState
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public int Level { get; init; }
    public long Xp { get; init; }
    public bool Alive { get; init; }
    public int RespawnSeconds { get; init; }
    public int BuybackCost { get; init; }
    public int BuybackCooldown { get; init; }
    public int Health { get; init; }
    public int MaxHealth { get; init; }
    public double HealthPercent { get; init; }
    public int Mana { get; init; }
    public int MaxMana { get; init; }
    public double ManaPercent { get; init; }
    public int Silences { get; init; }
    public int Stuns { get; init; }
    public int Disables { get; init; }
    public bool MagicImmunity { get; init; }
}

public sealed class AbilityState
{
    public string Slot { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Level { get; init; }
    public double Cooldown { get; init; }
    public bool CanCast { get; init; }
    public bool IsPassive { get; init; }
    public bool IsUltimate { get; init; }
    public int Charges { get; init; }
}

public sealed class ItemState
{
    public string Slot { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double Cooldown { get; init; }
    public bool CanCast { get; init; }
    public bool IsPassive { get; init; }
    public int Charges { get; init; }
}

// ============ 教练提醒 ============

public enum AdviceSeverity
{
    Info,
    Warning,
    Critical,
}

public sealed class CoachAdvice : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public AdviceSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    private string _message = string.Empty;
    public string Message
    {
        get => _message;
        set
        {
            if (_message == value) return;
            _message = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
        }
    }
    public string Rule { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public IReadOnlyList<string> Evidence { get; set; } = [];
    public long MatchId { get; set; }
    public double GameTime { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string GameTimeText => MatchSnapshot.FormatGameTime(GameTime);

    [System.Text.Json.Serialization.JsonIgnore]
    public string SeverityText => Severity switch
    {
        AdviceSeverity.Critical => "严重",
        AdviceSeverity.Warning => "警告",
        _ => "提示",
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    [System.Text.Json.Serialization.JsonIgnore]
    public string EvidenceText => Evidence.Count > 0 ? string.Join(" · ", Evidence) : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
}




