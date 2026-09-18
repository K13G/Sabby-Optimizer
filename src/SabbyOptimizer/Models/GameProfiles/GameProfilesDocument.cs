namespace PCTweaker.Models.GameProfiles;

public sealed class GameProfilesDocument
{
    public int SchemaVersion { get; set; } = 2;
    public DateTime ModifiedAtUtc { get; set; } = DateTime.UtcNow;
    public bool HasCompletedScan { get; set; }
    public DateTime? LastScanAtUtc { get; set; }
    public List<GameProfileDefinition> Profiles { get; set; } = new();
}
