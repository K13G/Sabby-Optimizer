namespace PCTweaker.Models;

public enum UpdateKind
{
    Application,
    Windows,
    Driver
}

public sealed record AvailableUpdateInfo(
    string Key,
    string Title,
    UpdateKind Kind,
    string CurrentVersion,
    string AvailableVersion,
    string Source,
    bool IsOptional,
    bool RequiresReboot,
    string NativeId);

public sealed record UpdateScanResult(
    IReadOnlyList<AvailableUpdateInfo> Updates,
    bool WingetAvailable,
    string WindowsUpdateStatus,
    string AppUpdateStatus);

public sealed record UpdateProgressInfo(
    string? Key,
    string CurrentItem,
    string Stage,
    double OverallPercent,
    double ItemPercent,
    bool ItemIndeterminate = false);

public sealed record UpdateInstallResult(
    int Completed,
    int Failed,
    int Skipped,
    bool RebootRequired,
    IReadOnlyDictionary<string, string> ItemResults);
