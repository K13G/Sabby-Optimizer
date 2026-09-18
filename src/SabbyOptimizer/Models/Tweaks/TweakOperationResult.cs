namespace PCTweaker.Models.Tweaks;

public sealed record TweakOperationResult(
    bool Success,
    string Message,
    TweakDetectionResult? VerifiedState = null,
    bool RequiresElevation = false,
    bool RequiresRestart = false)
{
    public static TweakOperationResult Failed(string message, bool requiresElevation = false) =>
        new(false, message, null, requiresElevation, false);

    public static TweakOperationResult Completed(
        string message,
        TweakDetectionResult verifiedState,
        bool requiresRestart = false) =>
        new(true, message, verifiedState, false, requiresRestart);
}
