using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using PCTweaker.Core.Services;
using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private HardwareInfo _hardware;
    private string _hardwareStatus = "Detecting hardware quietly in the background…";
    private readonly SystemMonitoringService? _monitoring;
    private double _cpuPercent, _memoryPercent, _gpuPercent, _performanceReadiness, _networkReadiness, _privacyReadiness, _storagePercent, _systemReadiness;
    private string _cpuText = "—", _memoryText = "—", _gpuText = "—", _networkText = "Waiting for sample…", _storageText = "—";

    public ObservableCollection<double> CpuBars { get; } = CreateBars();
    public ObservableCollection<double> MemoryBars { get; } = CreateBars();
    public ObservableCollection<double> GpuBars { get; } = CreateBars();
    public ObservableCollection<double> NetworkBars { get; } = CreateBars();

    public string AppDataPath { get; }
    public string LogsPath { get; }
    public HardwareInfo Hardware { get => _hardware; private set => SetProperty(ref _hardware, value); }
    public string HardwareStatus { get => _hardwareStatus; private set => SetProperty(ref _hardwareStatus, value); }
    public double CpuPercent { get => _cpuPercent; private set => SetProperty(ref _cpuPercent, value); }
    public double MemoryPercent { get => _memoryPercent; private set => SetProperty(ref _memoryPercent, value); }
    public double GpuPercent { get => _gpuPercent; private set => SetProperty(ref _gpuPercent, value); }
    public double PerformanceReadiness { get => _performanceReadiness; private set => SetProperty(ref _performanceReadiness, value); }
    public double NetworkReadiness { get => _networkReadiness; private set => SetProperty(ref _networkReadiness, value); }
    public double PrivacyReadiness { get => _privacyReadiness; private set => SetProperty(ref _privacyReadiness, value); }
    public string CpuText { get => _cpuText; private set => SetProperty(ref _cpuText, value); }
    public string MemoryText { get => _memoryText; private set => SetProperty(ref _memoryText, value); }
    public string GpuText { get => _gpuText; private set => SetProperty(ref _gpuText, value); }
    public string NetworkText { get => _networkText; private set => SetProperty(ref _networkText, value); }
    public double StoragePercent { get => _storagePercent; private set => SetProperty(ref _storagePercent, value); }
    public double SystemReadiness { get => _systemReadiness; private set => SetProperty(ref _systemReadiness, value); }
    public string StorageText { get => _storageText; private set => SetProperty(ref _storageText, value); }

    public DashboardViewModel(IAppPaths paths, HardwareInfo initialHardware, SystemMonitoringService? monitoring = null)
    {
        AppDataPath = paths.UserDataDirectory;
        LogsPath = paths.LogsDirectory;
        _hardware = initialHardware;
        _monitoring = monitoring;
        UpdateReadiness(initialHardware);

        if (_monitoring is not null)
        {
            _monitoring.SampleUpdated += OnMonitoringSampleUpdated;
            _ = StartMonitoringLaterAsync(_monitoring);
        }
    }

    public DashboardViewModel(IAppPaths paths, IHardwareInfoService hardwareInfoService, SystemMonitoringService? monitoring = null)
        : this(paths, hardwareInfoService.GetHardwareInfo(), monitoring)
    {
        HardwareStatus = "Hardware detection complete.";
    }

    public void UpdateHardware(HardwareInfo hardware)
    {
        Hardware = hardware;
        HardwareStatus = "Hardware detection complete.";
        UpdateReadiness(hardware);
    }

    private static async Task StartMonitoringLaterAsync(SystemMonitoringService monitoring)
    {
        await Task.Delay(900).ConfigureAwait(false);
        monitoring.Start(2200);
    }

    private void OnMonitoringSampleUpdated(object? sender, SystemMonitorSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        _ = dispatcher.BeginInvoke(new Action(() => ApplySnapshot(snapshot)), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ApplySnapshot(SystemMonitorSnapshot snapshot)
    {
        CpuPercent = Math.Clamp(snapshot.CpuUsagePercent, 0, 100);
        MemoryPercent = Math.Clamp(snapshot.MemoryUsagePercent, 0, 100);
        GpuPercent = Math.Clamp(snapshot.GpuUsagePercent ?? 0, 0, 100);
        CpuText = $"{CpuPercent:0}% • {(snapshot.CpuCurrentMhz > 0 ? $"{snapshot.CpuCurrentMhz:N0} MHz" : "clock n/a")}";
        MemoryText = $"{MemoryPercent:0}% • {snapshot.MemoryUsedGb:0.0}/{snapshot.MemoryTotalGb:0.0} GB";
        GpuText = snapshot.GpuUsagePercent is double gpu
            ? $"{gpu:0}% • {(snapshot.GpuTemperatureC is double temp ? $"{temp:0}°C" : "temp n/a")}"
            : "Sensor unavailable";
        NetworkText = $"{snapshot.DownloadMbps:0.0} ↓ / {snapshot.UploadMbps:0.0} ↑ Mbps";
        PushBar(CpuBars, CpuPercent);
        PushBar(MemoryBars, MemoryPercent);
        PushBar(GpuBars, GpuPercent);
        PushBar(NetworkBars, Math.Clamp(snapshot.DownloadMbps + snapshot.UploadMbps, 0, 100));
        UpdateStorage();
        SystemReadiness = Math.Round((PerformanceReadiness + NetworkReadiness + PrivacyReadiness + (100 - StoragePercent)) / 4d, 0);
    }

    private void UpdateReadiness(HardwareInfo hardware)
    {
        var detected = new[] { hardware.Processor, hardware.Graphics, hardware.Memory, hardware.Motherboard, hardware.Network, hardware.SystemDrive }
            .Count(x => !IsPlaceholder(x));
        PerformanceReadiness = detected >= 5 ? 92 : Math.Clamp(detected * 16, 18, 88);
        NetworkReadiness = IsPlaceholder(hardware.Network) ? 35 : 91;
        PrivacyReadiness = 78;
        UpdateStorage();
        SystemReadiness = Math.Round((PerformanceReadiness + NetworkReadiness + PrivacyReadiness + (100 - StoragePercent)) / 4d, 0);
    }

    private void UpdateStorage()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root)) return;
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0) return;
            var used = Math.Clamp(100d - ((double)drive.AvailableFreeSpace / drive.TotalSize * 100d), 0, 100);
            StoragePercent = used;
            StorageText = $"{used:0}% used • {drive.AvailableFreeSpace / 1_073_741_824d:0.0} GB free";
        }
        catch
        {
            StoragePercent = 0;
            StorageText = "Storage sensor unavailable";
        }
    }

    private static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("Loading", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Unavailable", StringComparison.OrdinalIgnoreCase);

    private static ObservableCollection<double> CreateBars() => new(Enumerable.Repeat(8d, 24));

    private static void PushBar(ObservableCollection<double> bars, double percent)
    {
        bars.RemoveAt(0);
        bars.Add(8 + Math.Clamp(percent, 0, 100) * 0.42);
    }
}
