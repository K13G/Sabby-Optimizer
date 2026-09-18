namespace PCTweaker.Models.Backups;

public sealed record BackupRestoreSummary(
    int Restored,
    int AlreadyMatched,
    int Skipped,
    int Failed,
    bool RequiresRestart,
    string Message);
