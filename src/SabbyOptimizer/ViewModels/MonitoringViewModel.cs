using System.Collections.ObjectModel;
using System.Windows;
using PCTweaker.Core.GameDetection;
using PCTweaker.Core.GameProfiles;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Presets;
using PCTweaker.Core.Services;
using PCTweaker.Models;
using PCTweaker.Models.GameDetection;

namespace PCTweaker.ViewModels;

public sealed record MonitoringPresetOption(Guid? Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class MonitoringViewModel : ViewModelBase
{
    private readonly SystemMonitoringService _monitor;
    private readonly ISettingsService _settings;
    private readonly IGameDetectionService _gameDetection;
    private readonly IGameProfileService _gameProfiles;
    private readonly IPresetService _presets;
    private Dictionary<Guid, string> _gameNames = new();
    private DateTimeOffset _lastThermalAlert = DateTimeOffset.MinValue;

    private string _clockText = "--:--:--";
    private string _dateText = "Waiting for first sample…";
    private string _uptimeText = "—";
    private string _cpuUsageText = "—";
    private string _cpuClockText = "—";
    private double _cpuUsagePercent;
    private string _memoryText = "—";
    private double _memoryUsagePercent;
    private string _gpuName = "—";
    private string _gpuUsageText = "—";
    private double _gpuUsagePercent;
    private string _gpuTemperatureText = "Sensor unavailable";
    private double _gpuTemperatureC;
    private string _gpuClockText = "—";
    private string _gpuPowerText = "—";
    private string _sensorSource = "Waiting for sensor discovery…";
    private string _networkText = "—";
    private string _activeGame = "No game detected";
    private string _activeProfile = "No automatic profile active";
    private string _automationStatus = "Automation is ready.";
    private string _monitorStatus = "Monitoring is starting…";
    private bool _monitoringEnabled;
    private bool _isMonitoringRunning;
    private bool _autoGameProfilesEnabled;
    private bool _smartGameTuningEnabled;
    private bool _thermalAlertsEnabled;
    private double _thermalWarningCelsius;
    private int _selectedRefreshIntervalMs;
    private MonitoringPresetOption? _selectedFallbackPreset;

    public ObservableCollection<AutomationEventItem> RecentEvents { get; } = new();
    public ObservableCollection<MonitoringPresetOption> FallbackPresets { get; } = new();
    public IReadOnlyList<int> RefreshIntervals { get; } = [1000, 2000, 3000, 5000, 10000];

    public string ClockText { get => _clockText; private set => SetProperty(ref _clockText, value); }
    public string DateText { get => _dateText; private set => SetProperty(ref _dateText, value); }
    public string UptimeText { get => _uptimeText; private set => SetProperty(ref _uptimeText, value); }
    public string CpuUsageText { get => _cpuUsageText; private set => SetProperty(ref _cpuUsageText, value); }
    public string CpuClockText { get => _cpuClockText; private set => SetProperty(ref _cpuClockText, value); }
    public double CpuUsagePercent { get => _cpuUsagePercent; private set => SetProperty(ref _cpuUsagePercent, value); }
    public string MemoryText { get => _memoryText; private set => SetProperty(ref _memoryText, value); }
    public double MemoryUsagePercent { get => _memoryUsagePercent; private set => SetProperty(ref _memoryUsagePercent, value); }
    public string GpuName { get => _gpuName; private set => SetProperty(ref _gpuName, value); }
    public string GpuUsageText { get => _gpuUsageText; private set => SetProperty(ref _gpuUsageText, value); }
    public double GpuUsagePercent { get => _gpuUsagePercent; private set => SetProperty(ref _gpuUsagePercent, value); }
    public string GpuTemperatureText { get => _gpuTemperatureText; private set => SetProperty(ref _gpuTemperatureText, value); }
    public double GpuTemperatureC { get => _gpuTemperatureC; private set { if (SetProperty(ref _gpuTemperatureC, value)) OnPropertyChanged(nameof(IsGpuHot)); } }
    public bool IsGpuHot => GpuTemperatureC > 0 && GpuTemperatureC >= ThermalWarningCelsius;
    public string GpuClockText { get => _gpuClockText; private set => SetProperty(ref _gpuClockText, value); }
    public string GpuPowerText { get => _gpuPowerText; private set => SetProperty(ref _gpuPowerText, value); }
    public string SensorSource { get => _sensorSource; private set => SetProperty(ref _sensorSource, value); }
    public string NetworkText { get => _networkText; private set => SetProperty(ref _networkText, value); }
    public string ActiveGame { get => _activeGame; private set => SetProperty(ref _activeGame, value); }
    public string ActiveProfile { get => _activeProfile; private set => SetProperty(ref _activeProfile, value); }
    public string AutomationStatus { get => _automationStatus; private set => SetProperty(ref _automationStatus, value); }
    public string MonitorStatus { get => _monitorStatus; private set => SetProperty(ref _monitorStatus, value); }

    public bool MonitoringEnabled { get => _monitoringEnabled; set => SetProperty(ref _monitoringEnabled, value); }
    public bool IsMonitoringRunning
    {
        get => _isMonitoringRunning;
        private set
        {
            if (!SetProperty(ref _isMonitoringRunning, value)) return;
            OnPropertyChanged(nameof(MonitoringStateLabel));
            OnPropertyChanged(nameof(MonitoringStateDetail));
            StartMonitoringCommand?.RaiseCanExecuteChanged();
            StopMonitoringCommand?.RaiseCanExecuteChanged();
        }
    }
    public string MonitoringStateLabel => IsMonitoringRunning ? "LIVE" : "STOPPED";
    public string MonitoringStateDetail => IsMonitoringRunning
        ? $"Continuous sensor polling is active every {NormalizeInterval(SelectedRefreshIntervalMs) / 1000d:0.#} sec."
        : "Live sensor polling is paused. Game-profile automation is controlled separately below.";
    public bool AutoGameProfilesEnabled { get => _autoGameProfilesEnabled; set => SetProperty(ref _autoGameProfilesEnabled, value); }
    public bool SmartGameTuningEnabled { get => _smartGameTuningEnabled; set => SetProperty(ref _smartGameTuningEnabled, value); }
    public bool ThermalAlertsEnabled { get => _thermalAlertsEnabled; set => SetProperty(ref _thermalAlertsEnabled, value); }
    public double ThermalWarningCelsius
    {
        get => _thermalWarningCelsius;
        set
        {
            if (SetProperty(ref _thermalWarningCelsius, Math.Clamp(value, 60, 100)))
                OnPropertyChanged(nameof(IsGpuHot));
        }
    }
    public int SelectedRefreshIntervalMs
    {
        get => _selectedRefreshIntervalMs;
        set
        {
            if (SetProperty(ref _selectedRefreshIntervalMs, value))
            {
                OnPropertyChanged(nameof(RefreshIntervalText));
                OnPropertyChanged(nameof(MonitoringStateDetail));
            }
        }
    }
    public MonitoringPresetOption? SelectedFallbackPreset { get => _selectedFallbackPreset; set => SetProperty(ref _selectedFallbackPreset, value); }

    public string RefreshIntervalText => $"{SelectedRefreshIntervalMs / 1000d:0.#} sec";

    public AsyncRelayCommand SaveAutomationCommand { get; }
    public AsyncRelayCommand RefreshNowCommand { get; }
    public AsyncRelayCommand StartMonitoringCommand { get; }
    public AsyncRelayCommand StopMonitoringCommand { get; }

    public MonitoringViewModel(
        SystemMonitoringService monitor,
        ISettingsService settings,
        IGameDetectionService gameDetection,
        IGameProfileService gameProfiles,
        IPresetService presets)
    {
        _monitor = monitor;
        _settings = settings;
        _gameDetection = gameDetection;
        _gameProfiles = gameProfiles;
        _presets = presets;

        MonitoringEnabled = settings.Current.MonitoringEnabled;
        AutoGameProfilesEnabled = settings.Current.GameDetectionEnabled;
        SmartGameTuningEnabled = settings.Current.SmartGameTuningTest;
        ThermalAlertsEnabled = settings.Current.ThermalAlertsEnabled;
        ThermalWarningCelsius = settings.Current.ThermalWarningCelsius;
        SelectedRefreshIntervalMs = NormalizeInterval(settings.Current.MonitoringRefreshIntervalMs);

        SaveAutomationCommand = new AsyncRelayCommand(SaveAutomationAsync);
        RefreshNowCommand = new AsyncRelayCommand(RefreshNowAsync);
        StartMonitoringCommand = new AsyncRelayCommand(StartMonitoringAsync, () => !IsMonitoringRunning);
        StopMonitoringCommand = new AsyncRelayCommand(StopMonitoringAsync, () => IsMonitoringRunning);

        _monitor.SampleUpdated += OnSampleUpdated;
        _monitor.RunningStateChanged += OnMonitoringRunningStateChanged;
        IsMonitoringRunning = _monitor.IsRunning;
        _gameDetection.StateChanged += OnGameStateChanged;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var games = await _gameProfiles.GetAllAsync();
            _gameNames = games.ToDictionary(x => x.Id, x => x.Name);

            var presets = await _presets.GetAllAsync();
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                FallbackPresets.Clear();
                FallbackPresets.Add(new MonitoringPresetOption(null, "No fallback preset"));
                foreach (var preset in presets.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    FallbackPresets.Add(new MonitoringPresetOption(preset.Id, preset.Name));
                SelectedFallbackPreset = FallbackPresets.FirstOrDefault(x => x.Id == _settings.Current.AutomationFallbackPresetId)
                    ?? FallbackPresets[0];
            });

            IsMonitoringRunning = _monitor.IsRunning;
            MonitorStatus = IsMonitoringRunning
                ? $"Live monitoring is running • {NormalizeInterval(SelectedRefreshIntervalMs) / 1000d:0.#} sec refresh."
                : MonitoringEnabled
                    ? "Monitoring is enabled for launch and will start after the main workspace is visible."
                    : "Live monitoring is disabled in your saved automation settings.";
        }
        catch (Exception ex)
        {
            MonitorStatus = $"Monitoring initialization failed safely: {ex.Message}";
        }
    }

    private async Task SaveAutomationAsync()
    {
        _settings.Current.MonitoringEnabled = MonitoringEnabled;
        _settings.Current.MonitoringRefreshIntervalMs = NormalizeInterval(SelectedRefreshIntervalMs);
        _settings.Current.GameDetectionEnabled = AutoGameProfilesEnabled;
        _settings.Current.SmartGameTuningTest = SmartGameTuningEnabled;
        _settings.Current.ThermalAlertsEnabled = ThermalAlertsEnabled;
        _settings.Current.ThermalWarningCelsius = ThermalWarningCelsius;
        _settings.Current.AutomationFallbackPresetId = SelectedFallbackPreset?.Id;
        await _settings.SaveAsync();

        if (MonitoringEnabled)
            await _monitor.RestartAsync(_settings.Current.MonitoringRefreshIntervalMs);
        else
            await _monitor.StopAsync();

        AutomationStatus = AutoGameProfilesEnabled
            ? "✓ Automation saved. Linked game profiles take priority; the fallback preset is only used when a game has no linked preset. Previous tweak states are restored when the game closes."
            : "Automation saved. Automatic game profile application is currently disabled.";
        UiNotificationHub.Publish("Monitoring & Automation", AutomationStatus, UiNotificationKind.Success);
    }

    private async Task RefreshNowAsync()
    {
        try
        {
            var snapshot = await _monitor.SampleNowAsync();
            ApplySnapshot(snapshot);
            MonitorStatus = $"Live sample updated at {snapshot.Timestamp.LocalDateTime:h:mm:ss tt}.";
        }
        catch (Exception ex)
        {
            MonitorStatus = $"Could not refresh monitoring safely: {ex.Message}";
        }
    }

    private async Task StartMonitoringAsync()
    {
        MonitoringEnabled = true;
        MonitorStatus = "Starting live monitoring…";
        await _monitor.RestartAsync(NormalizeInterval(SelectedRefreshIntervalMs));
        IsMonitoringRunning = _monitor.IsRunning;
        _settings.Current.MonitoringEnabled = true;
        _settings.Current.MonitoringRefreshIntervalMs = NormalizeInterval(SelectedRefreshIntervalMs);
        await _settings.SaveAsync();
        var snapshot = await _monitor.SampleNowAsync();
        ApplySnapshot(snapshot);
        MonitorStatus = IsMonitoringRunning
            ? $"Live monitoring is running • {NormalizeInterval(SelectedRefreshIntervalMs) / 1000d:0.#} sec refresh."
            : "Monitoring did not enter the running state.";
        UiNotificationHub.Publish("Monitoring", MonitorStatus, IsMonitoringRunning ? UiNotificationKind.Success : UiNotificationKind.Warning);
    }

    private async Task StopMonitoringAsync()
    {
        MonitorStatus = "Stopping live monitoring…";
        await _monitor.StopAsync();
        IsMonitoringRunning = _monitor.IsRunning;
        MonitoringEnabled = false;
        _settings.Current.MonitoringEnabled = false;
        await _settings.SaveAsync();
        MonitorStatus = "Live sensor polling is stopped. Saved game-profile automation settings were not deleted.";
        UiNotificationHub.Publish("Monitoring", MonitorStatus, UiNotificationKind.Info);
    }

    private void OnMonitoringRunningStateChanged(object? sender, bool running)
    {
        if (Application.Current?.Dispatcher is null) return;
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            IsMonitoringRunning = running;
            MonitorStatus = running
                ? $"Live monitoring is running • {NormalizeInterval(SelectedRefreshIntervalMs) / 1000d:0.#} sec refresh."
                : "Live sensor polling is stopped.";
        });
    }

    private void OnSampleUpdated(object? sender, SystemMonitorSnapshot snapshot)
    {
        if (Application.Current?.Dispatcher is null) return;
        _ = Application.Current.Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(SystemMonitorSnapshot sample)
    {
        ClockText = sample.Timestamp.LocalDateTime.ToString("h:mm:ss tt");
        DateText = sample.Timestamp.LocalDateTime.ToString("dddd, MMMM d, yyyy");
        UptimeText = FormatUptime(sample.SystemUptime);
        CpuUsagePercent = sample.CpuUsagePercent;
        CpuUsageText = $"{sample.CpuUsagePercent:F1}% load";
        CpuClockText = sample.CpuCurrentMhz > 0
            ? $"{sample.CpuCurrentMhz / 1000d:F2} GHz current • {sample.CpuMaxMhz / 1000d:F2} GHz base/max reported"
            : "Clock source unavailable";
        MemoryUsagePercent = sample.MemoryUsagePercent;
        MemoryText = $"{sample.MemoryUsedGb:F1} / {sample.MemoryTotalGb:F1} GB • {sample.MemoryUsagePercent:F0}%";
        GpuName = sample.GpuName;
        GpuUsagePercent = sample.GpuUsagePercent ?? 0;
        GpuUsageText = sample.GpuUsagePercent is double use ? $"{use:F0}% GPU load" : "Utilization unavailable";
        GpuTemperatureC = sample.GpuTemperatureC ?? 0;
        GpuTemperatureText = sample.GpuTemperatureC is double temp ? $"{temp:F0} °C" : "Temperature unavailable";
        GpuClockText = sample.GpuGraphicsClockMhz is double core
            ? $"{core:F0} MHz core" + (sample.GpuMemoryClockMhz is double mem ? $" • {mem:F0} MHz memory" : string.Empty)
            : "GPU clocks unavailable";
        GpuPowerText = sample.GpuPowerWatts is double watts ? $"{watts:F1} W" : "Power unavailable";
        SensorSource = sample.SensorSource + " • CPU temperature is not guessed when Windows/vendor APIs do not expose a trustworthy CPU sensor.";
        NetworkText = $"↓ {sample.DownloadMbps:F2} Mbps   ↑ {sample.UploadMbps:F2} Mbps";
        MonitorStatus = _monitor.IsRunning ? $"Live • updated {sample.Timestamp.LocalDateTime:h:mm:ss tt}" : $"Manual sample • {sample.Timestamp.LocalDateTime:h:mm:ss tt}";

        if (ThermalAlertsEnabled && sample.GpuTemperatureC is double gpuTemp && gpuTemp >= ThermalWarningCelsius && DateTimeOffset.UtcNow - _lastThermalAlert > TimeSpan.FromMinutes(5))
        {
            _lastThermalAlert = DateTimeOffset.UtcNow;
            UiNotificationHub.Publish("GPU temperature warning", $"GPU temperature reached {gpuTemp:F0} °C. Thermal warning is set to {ThermalWarningCelsius:F0} °C.", UiNotificationKind.Warning);
        }
    }

    private void OnGameStateChanged(object? sender, GameRuntimeState state)
    {
        if (Application.Current?.Dispatcher is null) return;
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var gameName = state.ProfileId != Guid.Empty && _gameNames.TryGetValue(state.ProfileId, out var name) ? name : "Game monitor";
            if (state.IsRunning)
            {
                ActiveGame = gameName;
                ActiveProfile = !string.IsNullOrWhiteSpace(state.AppliedPresetName)
                    ? state.AppliedPresetName + (state.SmartTuningUsed ? " + Smart tuning" : string.Empty)
                    : state.SmartTuningUsed ? "Smart tuning" : "No preset changes";
            }
            else if (state.ProfileId != Guid.Empty && string.Equals(ActiveGame, gameName, StringComparison.OrdinalIgnoreCase))
            {
                ActiveGame = "No game detected";
                ActiveProfile = "Previous supported states restored";
            }

            RecentEvents.Insert(0, new AutomationEventItem(DateTimeOffset.Now, gameName, $"{state.Status} • {state.Detail}", state.IsRunning));
            while (RecentEvents.Count > 12) RecentEvents.RemoveAt(RecentEvents.Count - 1);
        });
    }

    private static int NormalizeInterval(int value)
    {
        var options = new[] { 1000, 2000, 3000, 5000, 10000 };
        return options.OrderBy(x => Math.Abs(x - value)).First();
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1) return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1) return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{uptime.Minutes}m {uptime.Seconds}s";
    }
}
