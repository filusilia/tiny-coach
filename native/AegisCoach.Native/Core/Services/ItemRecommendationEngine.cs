namespace AegisCoach.Core.Services;

/// <summary>全场英雄的简要档案，供出装推荐引擎使用。</summary>
public sealed record HeroProfile(string InternalName, string LocalizedName, string PrimaryAttribute);

/// <summary>一场对局中的单名英雄（由 OpenDota 实时列表或 GSI 提供）。</summary>
public sealed record MatchHeroRef(int HeroId, bool IsEnemy);

public sealed record ItemSuggestion(string ItemName, string Reason, string Evidence);

public sealed record ItemRecommendationContext(
    IReadOnlyList<MatchHeroRef> Heroes,
    IReadOnlyDictionary<int, HeroProfile> Profiles,
    double GameTimeSeconds,
    int Gold);

/// <summary>
/// 基于全场英雄构成的确定性出装建议。规则只描述“敌方法术威胁多→优先魔免”这类
/// 样本级关联，不宣称必胜；输出每条建议都带证据（敌方英雄名单、时间、金币）。
/// </summary>
public static class ItemRecommendationEngine
{
    private static readonly HashSet<string> MagicBurstThreats = new(StringComparer.OrdinalIgnoreCase)
    {
        "npc_dota_hero_zuus", "npc_dota_hero_lina", "npc_dota_hero_leshrac", "npc_dota_hero_lion",
        "npc_dota_hero_lich", "npc_dota_hero_skywrath_mage", "npc_dota_hero_queenofpain",
        "npc_dota_hero_storm_spirit", "npc_dota_hero_invoker", "npc_dota_hero_tinker",
        "npc_dota_hero_puck", "npc_dota_hero_necrolyte", "npc_dota_hero_obsidian_destroyer",
        "npc_dota_hero_ancient_apparition", "npc_dota_hero_pugna", "npc_dota_hero_death_prophet",
        "npc_dota_hero_batrider", "npc_dota_hero_timbersaw", "npc_dota_hero_warlock",
    };

    private static readonly HashSet<string> HealSaveThreats = new(StringComparer.OrdinalIgnoreCase)
    {
        "npc_dota_hero_dazzle", "npc_dota_hero_oracle", "npc_dota_hero_warlock",
        "npc_dota_hero_witch_doctor", "npc_dota_hero_treant", "npc_dota_hero_wisp",
        "npc_dota_hero_keeper_of_the_light", "npc_dota_hero_abaddon", "npc_dota_hero_omniknight",
        "npc_dota_hero_winter_wyvern",
    };

    private static readonly HashSet<string> DisableThreats = new(StringComparer.OrdinalIgnoreCase)
    {
        "npc_dota_hero_lion", "npc_dota_hero_shadow_shaman", "npc_dota_hero_earthshaker",
        "npc_dota_hero_enigma", "npc_dota_hero_magnataur", "npc_dota_hero_doom_bringer",
        "npc_dota_hero_beastmaster", "npc_dota_hero_batrider", "npc_dota_hero_faceless_void",
        "npc_dota_hero_bane", "npc_dota_hero_pudge", "npc_dota_hero_legion_commander",
        "npc_dota_hero_spirit_breaker", "npc_dota_hero_axe",
    };

    public static IReadOnlyList<ItemSuggestion> Recommend(ItemRecommendationContext context)
    {
        var suggestions = new List<ItemSuggestion>();
        var enemies = context.Heroes
            .Where(hero => hero.IsEnemy)
            .Select(hero => context.Profiles.GetValueOrDefault(hero.HeroId))
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.InternalName))
            .Cast<HeroProfile>()
            .ToList();
        if (enemies.Count == 0) return suggestions;

        var timeText = FormatTime(context.GameTimeSeconds);
        var goldText = $"持有 {context.Gold:N0} 金";

        var magic = enemies.Where(profile => MagicBurstThreats.Contains(profile.InternalName)).ToList();
        var healers = enemies.Where(profile => HealSaveThreats.Contains(profile.InternalName)).ToList();
        var disablers = enemies.Where(profile => DisableThreats.Contains(profile.InternalName)).ToList();
        var agiCores = enemies
            .Where(profile => profile.PrimaryAttribute.Equals("agi", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (magic.Count >= 2 || (magic.Count >= 1 && disablers.Count >= 1))
        {
            suggestions.Add(new ItemSuggestion(
                "黑皇杖（BKB）",
                "敌方魔法爆发或强控较多，需要魔免来保证输出/生存",
                $"敌方法术威胁：{Names(magic.Concat(disablers))}；{timeText}，{goldText}"));
        }

        if (agiCores.Count >= 2)
        {
            suggestions.Add(new ItemSuggestion(
                "蝴蝶 / 天堂之戟 / 炎阳纹章",
                "敌方敏捷物理核心较多，优先补护甲与闪避",
                $"敌方敏捷物理：{Names(agiCores)}；{timeText}，{goldText}"));
        }

        if (healers.Count >= 1)
        {
            suggestions.Add(new ItemSuggestion(
                "否决挂穗",
                "敌方有治疗/救人英雄，用否决封住治疗",
                $"敌方治疗/救人：{Names(healers)}；{timeText}，{goldText}"));
        }

        if (disablers.Count >= 2)
        {
            suggestions.Add(new ItemSuggestion(
                "清莲宝珠（莲花）",
                "敌方单体控制多，莲花可以反制关键技能",
                $"敌方强控：{Names(disablers)}；{timeText}，{goldText}"));
        }

        return suggestions.Take(3).ToList();
    }

    private static string Names(IEnumerable<HeroProfile> profiles)
    {
        var names = profiles
            .Select(profile => string.IsNullOrWhiteSpace(profile.LocalizedName) ? profile.InternalName : profile.LocalizedName)
            .Distinct()
            .Take(4);
        return string.Join("、", names);
    }

    private static string FormatTime(double seconds)
    {
        var total = Math.Max(0, (int)seconds);
        return $"{total / 60:00}:{total % 60:00}";
    }
}
