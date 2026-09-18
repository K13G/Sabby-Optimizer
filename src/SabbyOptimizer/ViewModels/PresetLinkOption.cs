namespace PCTweaker.ViewModels;

public sealed record PresetLinkOption(Guid? Id, string Name)
{
    public override string ToString() => Name;
}
