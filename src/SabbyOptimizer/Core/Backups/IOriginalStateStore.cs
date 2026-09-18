using PCTweaker.Models.Backups;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Backups;

public interface IOriginalStateStore
{
    Task CaptureIfMissingAsync(
        TweakDefinition definition,
        TweakDetectionResult state,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupEntrySnapshot>> GetOriginalEntriesAsync(CancellationToken cancellationToken = default);
}
