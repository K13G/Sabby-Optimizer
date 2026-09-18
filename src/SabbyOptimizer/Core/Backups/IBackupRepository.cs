using PCTweaker.Models.Backups;

namespace PCTweaker.Core.Backups;

public interface IBackupRepository : IOriginalStateStore
{
    Task SaveSnapshotAsync(BackupSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default);
    Task<BackupSnapshot?> GetSnapshotAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteSnapshotAsync(Guid id, CancellationToken cancellationToken = default);
}
