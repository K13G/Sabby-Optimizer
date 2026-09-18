namespace PCTweaker.Models;

public sealed record SystemMonitorSnapshot(
    DateTimeOffset Timestamp,
    TimeSpan SystemUptime,
    double CpuUsagePercent,
    double CpuCurrentMhz,
    double CpuMaxMhz,
    double MemoryUsagePercent,
    double MemoryUsedGb,
    double MemoryTotalGb,
    double DownloadMbps,
    double UploadMbps,
    string GpuName,
    double? GpuUsagePercent,
    double? GpuTemperatureC,
    double? GpuGraphicsClockMhz,
    double? GpuMemoryClockMhz,
    double? GpuPowerWatts,
    string SensorSource);

public sealed record AutomationEventItem(
    DateTimeOffset Timestamp,
    string Title,
    string Detail,
    bool IsActive)
{
    public string TimeText => Timestamp.LocalDateTime.ToString("h:mm:ss tt");
}
