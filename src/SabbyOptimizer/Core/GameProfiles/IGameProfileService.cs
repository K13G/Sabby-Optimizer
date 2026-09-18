using PCTweaker.Models;
using PCTweaker.Models.GameProfiles;

namespace PCTweaker.Core.GameProfiles;

public interface IGameProfileService
{
    Task<IReadOnlyList<GameProfileDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<GameScanLibraryInfo> GetLibraryInfoAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GameProfileDefinition>> MergeScanResultsAsync(IEnumerable<DetectedGame> detectedGames, CancellationToken cancellationToken = default);
    Task SaveAllAsync(IEnumerable<GameProfileDefinition> profiles, CancellationToken cancellationToken = default);
}
