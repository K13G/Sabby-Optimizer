using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public interface ISystemRestoreService
{
    Task<IReadOnlyList<RestorePointInfo>> GetRestorePointsAsync(CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> EnsureInitialRestorePointAsync(CancellationToken cancellationToken = default);
    void OpenSystemRestore();
}
