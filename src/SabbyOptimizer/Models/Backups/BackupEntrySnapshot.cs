using PCTweaker.Models.Tweaks;

namespace PCTweaker.Models.Backups;

public sealed class BackupEntrySnapshot
{
    public string TweakId { get; set; } = string.Empty;
    public string TweakName { get; set; } = string.Empty;
    public TweakStateKind State { get; set; } = TweakStateKind.Unknown;
    public string DisplayText { get; set; } = "Unknown";
    public string Detail { get; set; } = string.Empty;
    public bool Restorable { get; set; }
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}
