namespace PCTweaker.Models.Tweaks;

public sealed record TweakCompatibilityResult(
    bool IsCompatible,
    string Message,
    string? Warning = null)
{
    public static TweakCompatibilityResult Compatible(string message = "Compatibility checks passed.", string? warning = null) =>
        new(true, message, warning);

    public static TweakCompatibilityResult Blocked(string message) =>
        new(false, message, null);
}
