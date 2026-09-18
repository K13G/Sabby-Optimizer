using PCTweaker.Models.Tweaks;

namespace PCTweaker.Models.Presets;

public sealed class PresetEntry
{
    public string TweakId { get; set; } = string.Empty;
    public string TweakName { get; set; } = string.Empty;
    public TweakStateKind DesiredState { get; set; } = TweakStateKind.NotApplied;
}
