namespace PCTweaker.Models.Backups;

public sealed class BackupSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Snapshot";
    public string Kind { get; set; } = "Manual";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string AppVersion { get; set; } = string.Empty;
    public List<BackupEntrySnapshot> Entries { get; set; } = new();
}
