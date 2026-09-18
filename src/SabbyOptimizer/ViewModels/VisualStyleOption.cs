using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class VisualStyleOption
{
    public VisualStyle Value { get; }
    public string Name { get; }
    public string Description { get; }
    public bool IsAnimated { get; }
    public bool IsEvent { get; }
    public string EventName { get; }
    public int AddedOrder { get; }
    public int RecommendedOrder { get; }

    public string BadgeText => IsEvent
        ? $"{(IsAnimated ? "ANIMATED" : "STATIC")} / EVENT"
        : IsAnimated ? "ANIMATED" : "STATIC";

    public string EventDisplay => IsEvent ? $"Event / holiday: {EventName}" : string.Empty;

    public string SearchText =>
        $"{Name} {Description} {BadgeText} {EventName} {(IsEvent ? "event holiday" : string.Empty)}";

    public VisualStyleOption(
        VisualStyle value,
        string name,
        string description,
        bool isAnimated,
        bool isEvent,
        string eventName,
        int addedOrder,
        int recommendedOrder)
    {
        Value = value;
        Name = name;
        Description = description;
        IsAnimated = isAnimated;
        IsEvent = isEvent;
        EventName = eventName;
        AddedOrder = addedOrder;
        RecommendedOrder = recommendedOrder;
    }

    public override string ToString() => Name;
}
