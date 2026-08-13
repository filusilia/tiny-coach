using System.Collections.ObjectModel;

namespace AegisCoach.Native.Models;

public sealed class AbilityDetailStat
{
    public AbilityDetailStat(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public string Value { get; }
}

public sealed class HeroUpgradeCard
{
    public HeroUpgradeCard(string title, string subtitle, string description, bool isScepter)
    {
        Title = title;
        Subtitle = subtitle;
        Description = description;
        IsScepter = isScepter;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string Description { get; }
    public bool IsScepter { get; }
}

public sealed class BuildRecommendationGroup
{
    public BuildRecommendationGroup(string name, IEnumerable<BuildRecommendation> items)
    {
        Name = name;
        Items = new ObservableCollection<BuildRecommendation>(items);
    }

    public string Name { get; }
    public ObservableCollection<BuildRecommendation> Items { get; }
}

public sealed class ItemShopSection
{
    public ItemShopSection(string name, string subtitle, IEnumerable<ItemCategoryGroup> groups)
    {
        Name = name;
        Subtitle = subtitle;
        Groups = new ObservableCollection<ItemCategoryGroup>(groups);
    }

    public string Name { get; }
    public string Subtitle { get; }
    public ObservableCollection<ItemCategoryGroup> Groups { get; }
}
