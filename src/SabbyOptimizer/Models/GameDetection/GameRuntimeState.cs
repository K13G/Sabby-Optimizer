namespace PCTweaker.Models.GameDetection;

public sealed class GameRuntimeState : EventArgs
{
    public Guid ProfileId { get; }
    public bool IsRunning { get; }
    public string Status { get; }
    public string Detail { get; }
    public string? AppliedPresetName { get; }
    public bool SmartTuningUsed { get; }
    public DateTime? StartedAtUtc { get; }

    public GameRuntimeState(
        Guid profileId,
        bool isRunning,
        string status,
        string detail,
        string? appliedPresetName = null,
        bool smartTuningUsed = false,
        DateTime? startedAtUtc = null)
    {
        ProfileId = profileId;
        IsRunning = isRunning;
        Status = status;
        Detail = detail;
        AppliedPresetName = appliedPresetName;
        SmartTuningUsed = smartTuningUsed;
        StartedAtUtc = startedAtUtc;
    }
}
