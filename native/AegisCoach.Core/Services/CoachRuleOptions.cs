namespace AegisCoach.Core.Services;

/// <summary>
/// User-tunable thresholds and switches for deterministic coach rules.
/// </summary>
public sealed class CoachRuleOptions
{
    public bool EnableGameState { get; set; } = true;
    public bool EnableLowHealth { get; set; } = true;
    public bool EnableLowMana { get; set; } = true;
    public bool EnableUltimateReady { get; set; } = true;
    public bool EnableDeath { get; set; } = true;
    public bool EnableControlLocked { get; set; } = true;
    public bool EnableBuybackWindow { get; set; } = true;
    public bool EnableFightReady { get; set; } = true;
    public bool EnableSmoke { get; set; } = true;
    public bool EnableSmokedState { get; set; } = true;
    public bool EnableNightFall { get; set; } = true;
    public bool EnableBuildings { get; set; } = true;
    public bool EnableRoshan { get; set; } = true;
    public bool EnableItemReady { get; set; } = true;
    public bool EnableAbilityReady { get; set; } = true;
    public bool EnableMapAwareness { get; set; } = true;

    public double LowHealthPercent { get; set; } = 25;
    public double LowManaPercent { get; set; } = 20;
    public double FightReadyHealthPercent { get; set; } = 70;
    public double FightReadyManaPercent { get; set; } = 50;

    public Dictionary<string, int> CooldownSeconds { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["game_state"] = 30,
        ["low_health"] = 15,
        ["low_mana"] = 20,
        ["ult_ready"] = 30,
        ["died"] = 10,
        ["control_locked"] = 12,
        ["buyback_window"] = 20,
        ["fight_ready"] = 45,
        ["smoke_purchased"] = 30,
        ["smoke_used"] = 45,
        ["smoked_active"] = 60,
        ["night_fall"] = 120,
        ["building_attacked"] = 25,
        ["building_destroyed"] = 20,
        ["roshan_killed"] = 300,
        ["roshan_respawned"] = 120,
        ["item_ready"] = 20,
        ["ability_ready"] = 18,
        ["map_awareness"] = 300,
    };

    public void Normalize()
    {
        LowHealthPercent = Math.Clamp(LowHealthPercent, 1, 99);
        LowManaPercent = Math.Clamp(LowManaPercent, 1, 99);
        FightReadyHealthPercent = Math.Clamp(FightReadyHealthPercent, 1, 100);
        FightReadyManaPercent = Math.Clamp(FightReadyManaPercent, 1, 100);

        CooldownSeconds ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in CooldownSeconds.Keys.ToArray())
        {
            CooldownSeconds[key] = Math.Clamp(CooldownSeconds[key], 1, 3600);
        }
    }
}
