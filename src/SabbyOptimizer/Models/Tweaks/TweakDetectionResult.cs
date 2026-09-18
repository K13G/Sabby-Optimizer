namespace PCTweaker.Models.Tweaks;

public sealed record TweakDetectionResult(
    TweakStateKind State,
    string DisplayText,
    string Detail,
    bool CanApply,
    bool CanUndo)
{
    public static TweakDetectionResult Unknown(string detail = "State has not been detected yet.") =>
        new(TweakStateKind.Unknown, "Unknown", detail, false, false);

    public static TweakDetectionResult Unavailable(string detail) =>
        new(TweakStateKind.Unavailable, "Unavailable", detail, false, false);

    public static TweakDetectionResult Error(string detail) =>
        new(TweakStateKind.Error, "Detection failed", detail, false, false);
}
