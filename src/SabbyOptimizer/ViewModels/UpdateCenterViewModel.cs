using System.Collections.ObjectModel;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class UpdateItemViewModel : ViewModelBase
{
    private string _status = "Available";
    private double _progress;
    private bool _isIndeterminate;

    public AvailableUpdateInfo Model { get; }
    public string Key => Model.Key;
    public string Title => Model.Title;
    public string KindLabel => Model.Kind switch
    {
        UpdateKind.Application => "APP",
        UpdateKind.Driver => "DRIVER",
        _ => "WINDOWS"
    };
    public string Source => Model.Source;
    public string VersionText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Model.CurrentVersion) && !string.IsNullOrWhiteSpace(Model.AvailableVersion))
                return $"{Model.CurrentVersion} → {Model.AvailableVersion}";
            if (!string.IsNullOrWhiteSpace(Model.AvailableVersion))
                return $"Available: {Model.AvailableVersion}";
            return "Update available";
        }
    }
    public string OptionalText => Model.IsOptional ? "OPTIONAL" : string.Empty;
    public bool IsOptional => Model.IsOptional;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, Math.Clamp(value, 0, 100));
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }

    public UpdateItemViewModel(AvailableUpdateInfo model) => Model = model;
}

public sealed class UpdateCenterViewModel : ViewModelBase
{
    private readonly IUpdateCenterService _updateService;
    private readonly ISettingsService _settings;
    private CancellationTokenSource? _operationCts;
    private bool _isBusy;
    private bool _autoUpdateEnabled;
    private bool _includeOptionalWindowsUpdates;
    private string _status = "Ready to check Windows, drivers, and installed applications.";
    private string _currentOperation = "Nothing is running.";
    private string _windowsStatus = "Not scanned yet.";
    private string _appStatus = "Not scanned yet.";
    private double _overallProgress;
    private bool _overallIndeterminate;
    private bool _autoRunStarted;

    public ObservableCollection<UpdateItemViewModel> Updates { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            CheckCommand.RaiseCanExecuteChanged();
            UpdateAllCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanUpdate));
        }
    }

    public bool CanUpdate => !IsBusy && Updates.Count > 0;

    public bool AutoUpdateEnabled
    {
        get => _autoUpdateEnabled;
        set
        {
            if (!SetProperty(ref _autoUpdateEnabled, value)) return;
            _settings.Current.AutoUpdateEnabled = value;
            _ = SaveSettingsAsync();
        }
    }

    public bool IncludeOptionalWindowsUpdates
    {
        get => _includeOptionalWindowsUpdates;
        set
        {
            if (!SetProperty(ref _includeOptionalWindowsUpdates, value)) return;
            _settings.Current.IncludeOptionalWindowsUpdates = value;
            _ = SaveSettingsAsync();
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string CurrentOperation
    {
        get => _currentOperation;
        private set => SetProperty(ref _currentOperation, value);
    }

    public string WindowsStatus
    {
        get => _windowsStatus;
        private set => SetProperty(ref _windowsStatus, value);
    }

    public string AppStatus
    {
        get => _appStatus;
        private set => SetProperty(ref _appStatus, value);
    }

    public double OverallProgress
    {
        get => _overallProgress;
        private set => SetProperty(ref _overallProgress, Math.Clamp(value, 0, 100));
    }

    public bool OverallIndeterminate
    {
        get => _overallIndeterminate;
        private set => SetProperty(ref _overallIndeterminate, value);
    }

    public string CountText
    {
        get
        {
            var apps = Updates.Count(x => x.Model.Kind == UpdateKind.Application);
            var windows = Updates.Count(x => x.Model.Kind == UpdateKind.Windows);
            var drivers = Updates.Count(x => x.Model.Kind == UpdateKind.Driver);
            return $"{Updates.Count} available • {apps} apps • {windows} Windows • {drivers} drivers";
        }
    }

    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand UpdateAllCommand { get; }
    public RelayCommand CancelCommand { get; }

    public UpdateCenterViewModel(IUpdateCenterService updateService, ISettingsService settings)
    {
        _updateService = updateService;
        _settings = settings;
        _autoUpdateEnabled = settings.Current.AutoUpdateEnabled;
        _includeOptionalWindowsUpdates = settings.Current.IncludeOptionalWindowsUpdates;

        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !IsBusy);
        UpdateAllCommand = new AsyncRelayCommand(UpdateAllAsync, () => CanUpdate);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public async Task RunAutoUpdateIfEnabledAsync()
    {
        if (_autoRunStarted || !AutoUpdateEnabled) return;
        _autoRunStarted = true;
        try
        {
            await Task.Delay(1200);
            if (IsBusy) return;
            await CheckAsync();
            if (Updates.Count > 0)
                await UpdateAllAsync();
        }
        catch (Exception ex)
        {
            Status = $"Automatic update did not complete: {ex.Message}";
        }
    }

    private async Task CheckAsync()
    {
        StartOperation();
        OverallIndeterminate = true;
        OverallProgress = 0;
        Status = "Checking for updates…";
        CurrentOperation = "Scanning Windows Update and WinGet. This can take a little while.";
        try
        {
            var result = await _updateService.ScanAsync(_operationCts!.Token);
            Updates.Clear();
            foreach (var update in result.Updates)
                Updates.Add(new UpdateItemViewModel(update));

            WindowsStatus = result.WindowsUpdateStatus;
            AppStatus = result.AppUpdateStatus;
            OverallIndeterminate = false;
            OverallProgress = 100;
            Status = Updates.Count == 0 ? "✓ Everything Sabby can check is up to date." : $"Found {Updates.Count} update{(Updates.Count == 1 ? string.Empty : "s")}.";
            CurrentOperation = Updates.Count == 0
                ? "No supported updates are waiting."
                : "Review the list or choose Update all.";
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(CanUpdate));
            UpdateAllCommand.RaiseCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            Status = "Update scan cancelled.";
            CurrentOperation = "Cancelled.";
        }
        finally
        {
            FinishOperation();
        }
    }

    private async Task UpdateAllAsync()
    {
        if (Updates.Count == 0) return;
        StartOperation();
        OverallIndeterminate = false;
        OverallProgress = 0;
        Status = "Updating supported software, Windows components, and drivers…";
        CurrentOperation = "Preparing update queue…";

        foreach (var item in Updates)
        {
            item.Status = item.Model.IsOptional && item.Model.Kind == UpdateKind.Windows && !IncludeOptionalWindowsUpdates
                ? "Optional • skipped by current setting"
                : "Queued";
            item.Progress = 0;
            item.IsIndeterminate = false;
        }

        var progress = new Progress<UpdateProgressInfo>(p =>
        {
            OverallProgress = p.OverallPercent;
            OverallIndeterminate = false;
            CurrentOperation = string.IsNullOrWhiteSpace(p.CurrentItem) ? p.Stage : $"{p.Stage}: {p.CurrentItem}";
            if (!string.IsNullOrWhiteSpace(p.Key))
            {
                var item = Updates.FirstOrDefault(x => x.Key.Equals(p.Key, StringComparison.OrdinalIgnoreCase));
                if (item is not null)
                {
                    item.Status = p.Stage;
                    item.Progress = p.ItemPercent;
                    item.IsIndeterminate = p.ItemIndeterminate;
                }
            }
        });

        try
        {
            var models = Updates.Select(x => x.Model).ToArray();
            var result = await _updateService.InstallAllAsync(models, IncludeOptionalWindowsUpdates, progress, _operationCts!.Token);

            foreach (var item in Updates)
            {
                if (result.ItemResults.TryGetValue(item.Key, out var itemResult))
                {
                    item.Status = itemResult;
                    item.Progress = itemResult.Contains("fail", StringComparison.OrdinalIgnoreCase) ? item.Progress : 100;
                    item.IsIndeterminate = false;
                }
            }

            OverallProgress = 100;
            Status = result.Failed == 0
                ? $"✓ Update pass complete • {result.Completed} updated • {result.Skipped} skipped"
                : $"Update pass finished • {result.Completed} updated • {result.Failed} failed • {result.Skipped} skipped";
            CurrentOperation = result.RebootRequired
                ? "A restart is required to finish one or more Windows/driver updates. Sabby will not restart the PC automatically."
                : "Update pass finished. Re-scan to confirm there is nothing else waiting.";
            UiNotificationHub.Publish("Sabby Updates", Status, result.Failed == 0 ? UiNotificationKind.Success : UiNotificationKind.Warning);
        }
        catch (OperationCanceledException)
        {
            Status = "Update operation cancelled.";
            CurrentOperation = "Cancelled. An installer that had already started may still finish its own transaction.";
        }
        finally
        {
            FinishOperation();
        }
    }

    private void StartOperation()
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        IsBusy = true;
    }

    private void FinishOperation()
    {
        OverallIndeterminate = false;
        IsBusy = false;
        _operationCts?.Dispose();
        _operationCts = null;
    }

    private void Cancel() => _operationCts?.Cancel();

    private async Task SaveSettingsAsync()
    {
        try { await _settings.SaveAsync(); }
        catch { }
    }
}
