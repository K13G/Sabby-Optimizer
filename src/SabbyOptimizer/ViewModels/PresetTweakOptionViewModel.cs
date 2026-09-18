using PCTweaker.Core.Mvvm;
using PCTweaker.Models.Presets;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.ViewModels;

public sealed class PresetTweakOptionViewModel : ObservableObject
{
    private string _selectedState;

    public string TweakId { get; }
    public string Name { get; }
    public string Description { get; }
    public string Category { get; }
    public IReadOnlyList<string> StateChoices { get; } = new[] { "Ignore", "Applied", "Not applied" };

    public string SelectedState
    {
        get => _selectedState;
        set => SetProperty(ref _selectedState, value ?? "Ignore");
    }

    public PresetTweakOptionViewModel(TweakDefinition definition, PresetEntry? existing)
    {
        TweakId = definition.Id;
        Name = definition.Name;
        Description = definition.ShortDescription;
        Category = definition.Category.ToString();
        _selectedState = existing?.DesiredState switch
        {
            TweakStateKind.Applied => "Applied",
            TweakStateKind.NotApplied => "Not applied",
            _ => "Ignore"
        };
    }

    public PresetEntry? ToEntry()
    {
        var desired = SelectedState switch
        {
            "Applied" => TweakStateKind.Applied,
            "Not applied" => TweakStateKind.NotApplied,
            _ => (TweakStateKind?)null
        };

        return desired.HasValue
            ? new PresetEntry { TweakId = TweakId, TweakName = Name, DesiredState = desired.Value }
            : null;
    }
}
