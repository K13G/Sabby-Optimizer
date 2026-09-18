namespace PCTweaker.Models.Backups;

public sealed class OriginalStateDocument
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, BackupEntrySnapshot> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
