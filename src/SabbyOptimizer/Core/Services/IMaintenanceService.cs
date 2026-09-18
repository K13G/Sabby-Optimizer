using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public interface IMaintenanceService
{
    Task<IReadOnlyList<StartupAppInfo>> GetStartupAppsAsync(CancellationToken cancellationToken = default);
    Task<bool> SetStartupEnabledAsync(StartupAppInfo app, bool enabled, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OptionalServiceInfo>> GetOptionalServicesAsync(CancellationToken cancellationToken = default);
    Task<bool> SetServiceRunningAsync(OptionalServiceInfo service, bool running, CancellationToken cancellationToken = default);
    Task<CleanupAnalysis> AnalyzeTemporaryFilesAsync(CancellationToken cancellationToken = default);
    Task<CleanupAnalysis> CleanTemporaryFilesAsync(CancellationToken cancellationToken = default);
}
