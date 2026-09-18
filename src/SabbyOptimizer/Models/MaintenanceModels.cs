namespace PCTweaker.Models;

public sealed class StartupAppInfo
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public bool IsMachineWide { get; init; }
    public bool IsEnabled { get; set; }
}

public sealed class OptionalServiceInfo
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public string StartMode { get; init; } = "Unknown";
    public bool IsRunning => Status.Equals("Running", StringComparison.OrdinalIgnoreCase);
}

public sealed record CleanupAnalysis(long Bytes, int FileCount, int SkippedCount = 0)
{
    public string SizeText => Bytes < 1024 * 1024
        ? $"{Bytes / 1024d:0.0} KB"
        : Bytes < 1024L * 1024 * 1024
            ? $"{Bytes / 1024d / 1024d:0.0} MB"
            : $"{Bytes / 1024d / 1024d / 1024d:0.00} GB";
}
