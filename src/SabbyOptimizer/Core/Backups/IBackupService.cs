using PCTweaker.Models.Backups;

namespace PCTweaker.Core.Backups;

public interface IBackupService
{
    Task EnsureOriginalBaselineAsync(CancellationToken cancellationToken = default);
    Task<BackupSnapshot> CreateSnapshotAsync(string? name = null, string kind = "Manual", CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupEntrySnapshot>> GetOriginalEntriesAsync(CancellationToken cancellationToken = default);
    Task<BackupRestoreSummary> RestoreSnapshotAsync(Guid snapshotId, CancellationToken cancellationToken = default);
    Task<BackupRestoreSummary> RestoreSnapshotEntryAsync(Guid snapshotId, string tweakId, CancellationToken cancellationToken = default);
    Task<BackupRestoreSummary> RestoreOriginalsAsync(CancellationToken cancellationToken = default);
    Task DeleteSnapshotAsync(Guid snapshotId, CancellationToken cancellationToken = default);
}
