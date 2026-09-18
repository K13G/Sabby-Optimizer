namespace PCTweaker.Models.GameProfiles;

public sealed class GameProfileDefinition
{
    public int SchemaVersion { get; set; } = 2;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Game";
    public string Platform { get; set; } = "Unknown";
    public string ExecutablePath { get; set; } = string.Empty;
    public string InstallDirectory { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public Guid? LinkedPresetId { get; set; }
    public string Notes { get; set; } = string.Empty;

    // Phase 15 per-game controls. These are Windows/Sabby-side settings only; Sabby does not
    // rewrite proprietary game configuration files or guess internal graphics options.
    public string GraphicsPreference { get; set; } = "System default";
    public string ProcessPriority { get; set; } = "Normal";

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
