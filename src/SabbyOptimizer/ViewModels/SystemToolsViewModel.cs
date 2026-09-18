using System.Collections.ObjectModel;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class StartupAppItemViewModel : ViewModelBase
{
    private readonly IMaintenanceService _service;
    private bool _isEnabled;
    private bool _isBusy;
    private string _verificationText = "Detected from Windows startup locations.";

    public event EventHandler? StateChanged;

    public StartupAppInfo Model { get; }
    public string Name => Model.Name;
    public string Command => Model.Command;
    public string Source => Model.Source;
    public string Scope => Model.IsMachineWide ? "All users" : "Current user";
    public bool IsEnabled => _isEnabled;
    public string StateText => IsEnabled ? "Enabled" : "Disabled";
    public string ActionText => IsEnabled ? "Disable" : "Enable";

    // More green means it is generally safer/more useful to keep this app out of automatic
    // startup. More red means leave it enabled unless the user explicitly knows they do not
    // need its login-time behavior.
    public int DisableScore => GetDisableScore(Name, Command);
    public double DisableGreenWidth => 92d * DisableScore / 100d;
    public double DisableRedWidth => 92d - DisableGreenWidth;
    public string DisableRecommendation => DisableScore switch
    {
        >= 85 => "SAFE TO DISABLE",
        >= 70 => "GOOD TO DISABLE",
        >= 50 => "OPTIONAL",
        >= 30 => "KEEP IF USED",
        _ => "KEEP ENABLED"
    };
    public bool IsSafeForBulkDisable => DisableScore >= 75;

    public string VerificationText
    {
        get => _verificationText;
        private set => SetProperty(ref _verificationText, value);
    }

    public AsyncRelayCommand ToggleCommand { get; }

    public StartupAppItemViewModel(IMaintenanceService service, StartupAppInfo model)
    {
        _service = service;
        Model = model;
        _isEnabled = model.IsEnabled;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => !_isBusy);
    }

    public async Task<bool> SetEnabledAsync(bool target)
    {
        if (_isBusy) return false;
        if (_isEnabled == target) return true;

        _isBusy = true;
        ToggleCommand.RaiseCanExecuteChanged();
        try
        {
            VerificationText = target ? "Enabling and verifying…" : "Disabling and verifying…";

            if (!await _service.SetStartupEnabledAsync(Model, target))
            {
                VerificationText = "Not applied • Windows rejected the change or the startup entry is protected.";
                return false;
            }

            var detected = (await _service.GetStartupAppsAsync())
                .FirstOrDefault(x => x.Id.Equals(Model.Id, StringComparison.OrdinalIgnoreCase));

            if (detected is null || detected.IsEnabled != target)
            {
                VerificationText = detected is null
                    ? "Not verified • the startup entry could not be found after the change."
                    : $"Not verified • Windows still reports {(detected.IsEnabled ? "Enabled" : "Disabled")}.";
                return false;
            }

            _isEnabled = target;
            Model.IsEnabled = target;
            VerificationText = target ? "✓ Verified enabled by re-scan" : "✓ Verified disabled by re-scan";
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(ActionText));
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally
        {
            _isBusy = false;
            ToggleCommand.RaiseCanExecuteChanged();
        }
    }

    private Task ToggleAsync() => SetEnabledAsync(!_isEnabled);

    private static int GetDisableScore(string name, string command)
    {
        var text = $"{name} {command}".ToLowerInvariant();
        if (text.Contains("securityhealth") || text.Contains("windows defender") || text.Contains("security health")) return 5;
        if (text.Contains("rtkaud") || text.Contains("realtek") || text.Contains("audio service")) return 20;
        if (text.Contains("driver") || text.Contains("touchpad") || text.Contains("hotkey")) return 25;
        if (text.Contains("onedrive") || text.Contains("proton drive") || text.Contains("dropbox") || text.Contains("google drive")) return 55;
        if (text.Contains("steam") || text.Contains("discord") || text.Contains("crosshair") || text.Contains("icue") || text.Contains("steelseries") || text.Contains("spotify")) return 80;
        if (text.Contains("update") || text.Contains("updater") || text.Contains("autolaunch") || text.Contains("launcher") || text.Contains("java")) return 90;
        return 60;
    }
}

public sealed class OptionalServiceItemViewModel : ViewModelBase
{
    private readonly IMaintenanceService _service;
    private string _status;
    private bool _isBusy;
    private string _verificationText = "Detected from Windows Service Control Manager.";

    public OptionalServiceInfo Model { get; }
    public string Name => Model.DisplayName;
    public string ServiceName => Model.Name;
    public string Description => Model.Description;
    public string StartMode => Model.StartMode;
    public string Status => _status;
    public bool IsRunning => Status.Equals("Running", StringComparison.OrdinalIgnoreCase);
    public string ActionText => IsRunning ? "Stop" : "Start";
    public int DisableScore => ServiceName.ToLowerInvariant() switch
    {
        "wsearch" => 35,
        "sysmain" => 35,
        "spooler" => 45,
        "bthserv" => 45,
        "xblauthmanager" => 55,
        "xblgamesave" => 55,
        _ => 25
    };
    public double DisableGreenWidth => 92d * DisableScore / 100d;
    public double DisableRedWidth => 92d - DisableGreenWidth;
    public string DisableRecommendation => DisableScore switch
    {
        >= 75 => "SAFE TO STOP",
        >= 55 => "OPTIONAL",
        >= 35 => "KEEP IF USED",
        _ => "KEEP RUNNING"
    };

    public string VerificationText
    {
        get => _verificationText;
        private set => SetProperty(ref _verificationText, value);
    }

    public AsyncRelayCommand ToggleCommand { get; }

    public OptionalServiceItemViewModel(IMaintenanceService service, OptionalServiceInfo model)
    {
        _service = service;
        Model = model;
        _status = model.Status;
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => !_isBusy && !StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase));
    }

    private async Task ToggleAsync()
    {
        _isBusy = true;
        ToggleCommand.RaiseCanExecuteChanged();
        try
        {
            var targetRunning = !IsRunning;
            VerificationText = targetRunning ? "Starting and verifying…" : "Stopping and verifying…";

            if (!await _service.SetServiceRunningAsync(Model, targetRunning))
            {
                VerificationText = "Not applied • administrator permission or the service policy blocked the change.";
                return;
            }

            var detected = (await _service.GetOptionalServicesAsync())
                .FirstOrDefault(x => x.Name.Equals(Model.Name, StringComparison.OrdinalIgnoreCase));

            if (detected is null)
            {
                VerificationText = "Not verified • Windows did not return the service after the change.";
                return;
            }

            _status = detected.Status;
            Model.Status = _status;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(ActionText));

            VerificationText = IsRunning == targetRunning
                ? (targetRunning ? "✓ Verified running by SCM read-back" : "✓ Verified stopped by SCM read-back")
                : $"Not verified • Windows reports {_status}.";
        }
        finally
        {
            _isBusy = false;
            ToggleCommand.RaiseCanExecuteChanged();
        }
    }
}

public sealed class SystemToolsViewModel : ViewModelBase
{
    private readonly IMaintenanceService _service;
    private readonly List<StartupAppItemViewModel> _allStartupApps = new();
    private string _cleanupStatus = "Analyze temporary files before cleaning.";
    private string _startupFilter = "All";
    private string _safeBulkStatus = "Disable all only targets startup entries Sabby classifies as safe to remove from automatic login startup.";
    private bool _isBusy;
    private bool _suppressStartupStateRefresh;

    public ObservableCollection<StartupAppItemViewModel> StartupApps { get; } = new();
    public ObservableCollection<OptionalServiceItemViewModel> Services { get; } = new();

    public string CleanupStatus
    {
        get => _cleanupStatus;
        private set => SetProperty(ref _cleanupStatus, value);
    }

    public string SafeBulkStatus
    {
        get => _safeBulkStatus;
        private set => SetProperty(ref _safeBulkStatus, value);
    }

    public string StartupFilterSummary => $"{StartupApps.Count:N0} shown • {_startupFilter}";

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AnalyzeCleanupCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public AsyncRelayCommand DisableAllSafeCommand { get; }
    public RelayCommand ShowAllStartupCommand { get; }
    public RelayCommand ShowEnabledStartupCommand { get; }
    public RelayCommand ShowDisabledStartupCommand { get; }

    public SystemToolsViewModel(IMaintenanceService service)
    {
        _service = service;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !_isBusy);
        AnalyzeCleanupCommand = new AsyncRelayCommand(AnalyzeCleanupAsync, () => !_isBusy);
        CleanCommand = new AsyncRelayCommand(CleanAsync, () => !_isBusy);
        DisableAllSafeCommand = new AsyncRelayCommand(DisableAllSafeAsync, () => !_isBusy);
        ShowAllStartupCommand = new RelayCommand(() => SetStartupFilter("All"));
        ShowEnabledStartupCommand = new RelayCommand(() => SetStartupFilter("Enabled"));
        ShowDisabledStartupCommand = new RelayCommand(() => SetStartupFilter("Disabled"));
        _ = InitializeAsync();
    }

    public async Task InitializeAsync() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        _isBusy = true;
        RaiseCommandStates();
        try
        {
            var startupTask = _service.GetStartupAppsAsync();
            var servicesTask = _service.GetOptionalServicesAsync();
            await Task.WhenAll(startupTask, servicesTask);

            foreach (var existing in _allStartupApps)
                existing.StateChanged -= OnStartupStateChanged;
            _allStartupApps.Clear();

            foreach (var app in startupTask.Result)
            {
                var item = new StartupAppItemViewModel(_service, app);
                item.StateChanged += OnStartupStateChanged;
                _allStartupApps.Add(item);
            }
            RebuildStartupView();

            Services.Clear();
            foreach (var service in servicesTask.Result)
                Services.Add(new OptionalServiceItemViewModel(_service, service));
        }
        finally
        {
            _isBusy = false;
            RaiseCommandStates();
        }
    }

    private void SetStartupFilter(string filter)
    {
        _startupFilter = filter;
        RebuildStartupView();
    }

    private void OnStartupStateChanged(object? sender, EventArgs e)
    {
        if (_suppressStartupStateRefresh) return;

        // Do not Clear + repopulate the ItemsControl after every toggle. That was what made
        // the outer page ScrollViewer jump to a different position after disabling an item.
        if (_startupFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            OnPropertyChanged(nameof(StartupFilterSummary));
            return;
        }

        if (sender is StartupAppItemViewModel item)
        {
            var belongs = _startupFilter.Equals("Enabled", StringComparison.OrdinalIgnoreCase) ? item.IsEnabled : !item.IsEnabled;
            if (!belongs)
                StartupApps.Remove(item);
            OnPropertyChanged(nameof(StartupFilterSummary));
        }
    }

    private void RebuildStartupView()
    {
        var query = _allStartupApps.AsEnumerable();
        if (_startupFilter.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.IsEnabled);
        else if (_startupFilter.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => !x.IsEnabled);

        StartupApps.Clear();
        foreach (var item in query.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Source, StringComparer.OrdinalIgnoreCase))
            StartupApps.Add(item);

        OnPropertyChanged(nameof(StartupFilterSummary));
    }

    private async Task DisableAllSafeAsync()
    {
        _isBusy = true;
        _suppressStartupStateRefresh = true;
        RaiseCommandStates();
        try
        {
            var targets = _allStartupApps.Where(x => x.IsEnabled && x.IsSafeForBulkDisable).ToArray();
            if (targets.Length == 0)
            {
                SafeBulkStatus = "No enabled startup entries currently meet Sabby's conservative SAFE ONLY threshold.";
                return;
            }

            var changed = 0;
            var failed = 0;
            foreach (var target in targets)
            {
                if (await target.SetEnabledAsync(false)) changed++;
                else failed++;
            }

            SafeBulkStatus = $"SAFE ONLY complete • {changed} verified disabled • {failed} unchanged/blocked. SecurityHealth, audio/driver helpers, and uncertain entries were preserved.";
        }
        finally
        {
            _suppressStartupStateRefresh = false;
            if (!_startupFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
                RebuildStartupView();
            else
                OnPropertyChanged(nameof(StartupFilterSummary));
            _isBusy = false;
            RaiseCommandStates();
        }
    }

    private async Task AnalyzeCleanupAsync()
    {
        _isBusy = true;
        RaiseCommandStates();
        try
        {
            CleanupStatus = "Scanning safe temporary-file locations…";
            var result = await _service.AnalyzeTemporaryFilesAsync();
            CleanupStatus = result.FileCount == 0
                ? "No removable temporary files were found. Locked/in-use files are left alone."
                : $"{result.SizeText} across {result.FileCount:N0} temporary files can be cleaned now.";
        }
        finally
        {
            _isBusy = false;
            RaiseCommandStates();
        }
    }

    private async Task CleanAsync()
    {
        _isBusy = true;
        RaiseCommandStates();
        try
        {
            CleanupStatus = "Cleaning all unlocked files from user TEMP and Windows TEMP…";
            var result = await _service.CleanTemporaryFilesAsync();
            CleanupStatus = result.FileCount == 0
                ? $"Nothing removable was deleted. {result.SkippedCount:N0} locked/in-use item(s) were skipped."
                : $"Cleaned {result.SizeText} across {result.FileCount:N0} temporary files. {result.SkippedCount:N0} locked/in-use item(s) were skipped.";
        }
        finally
        {
            _isBusy = false;
            RaiseCommandStates();
        }
    }

    private void RaiseCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        AnalyzeCleanupCommand.RaiseCanExecuteChanged();
        CleanCommand.RaiseCanExecuteChanged();
        DisableAllSafeCommand.RaiseCanExecuteChanged();
    }
}
