using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class VisualStyleSortOption
{
    public VisualStyleSortMode Value { get; }
    public string Name { get; }

    public VisualStyleSortOption(VisualStyleSortMode value, string name)
    {
        Value = value;
        Name = name;
    }

    public override string ToString() => Name;
}
