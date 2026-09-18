using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public interface IUpdateCenterService
{
    Task<UpdateScanResult> ScanAsync(CancellationToken cancellationToken = default);
    Task<UpdateInstallResult> InstallAllAsync(
        IReadOnlyList<AvailableUpdateInfo> updates,
        bool includeOptionalWindowsUpdates,
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken cancellationToken = default);
}
