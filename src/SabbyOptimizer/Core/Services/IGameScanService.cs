using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public interface IGameScanService
{
    Task<IReadOnlyList<DetectedGame>> ScanAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default);
}
