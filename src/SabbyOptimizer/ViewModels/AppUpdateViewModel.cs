using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class AppUpdateViewModel : ViewModelBase
{
    private readonly IUpdateExtensionService _service;
    private readonly ISettingsService _settings;
    private bool _isBusy;
    private double _downloadProgress;
    private string _updateStatus = "Automatic update checks are enabled.";
    private string _releaseNotes = "Sabby uses the official K13G stable channel.";
    private SabbyReleaseInfo? _release;

    public AppUpdateViewModel(IUpdateExtensionService service, ISettingsService settings)
    {
        _service = service;
        _settings = settings;
        _settings.Current.SabbyUpdateChannel = SabbyUpdateChannel.Stable;
        _settings.Current.StableUpdateFeedUrl = SabbyUpdateDefaults.OfficialStableFeedUrl;
        _settings.Current.AutoCheckSabbyUpdates = true;
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsBusy);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => !IsBusy && UpdateAvailable);
        _ = InitializeAsync();
    }

    public string CurrentVersion => _release?.CurrentVersion ?? typeof(AppUpdateViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public string LatestVersion => _release?.LatestVersion ?? "Checking…";
    public bool UpdateAvailable => _release?.UpdateAvailable == true;
    public string UpdateButtonText => UpdateAvailable ? "Update now" : "Up to date";
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }
    public string ReleaseNotes { get => _releaseNotes; private set => SetProperty(ref _releaseNotes, value); }
    public double DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, Math.Clamp(value, 0, 100)); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            CheckForUpdatesCommand.RaiseCanExecuteChanged();
            InstallUpdateCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(UpdateButtonText));
        }
    }

    public AsyncRelayCommand CheckForUpdatesCommand { get; }
    public AsyncRelayCommand InstallUpdateCommand { get; }

    private async Task InitializeAsync()
    {
        try { await _settings.SaveAsync(); } catch { }
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        IsBusy = true;
        DownloadProgress = 0;
        try
        {
            _settings.Current.SabbyUpdateChannel = SabbyUpdateChannel.Stable;
            _settings.Current.StableUpdateFeedUrl = SabbyUpdateDefaults.OfficialStableFeedUrl;
            _settings.Current.AutoCheckSabbyUpdates = true;
            try { await _settings.SaveAsync(); } catch { }

            UpdateStatus = "Checking the K13G stable channel…";
            _release = await _service.CheckSabbyUpdateAsync(SabbyUpdateChannel.Stable, SabbyUpdateDefaults.OfficialStableFeedUrl);
            UpdateStatus = _release.Status + (_release.UpdateAvailable && _release.Mandatory ? " • REQUIRED" : string.Empty);
            ReleaseNotes = string.IsNullOrWhiteSpace(_release.ReleaseNotes)
                ? (_release.UpdateAvailable ? "A newer Sabby release is ready." : "You are running the latest stable release.")
                : _release.ReleaseNotes!;
            OnPropertyChanged(nameof(CurrentVersion));
            OnPropertyChanged(nameof(LatestVersion));
            OnPropertyChanged(nameof(UpdateAvailable));
            OnPropertyChanged(nameof(UpdateButtonText));
            InstallUpdateCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex) { UpdateStatus = $"Update check failed safely: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task InstallUpdateAsync()
    {
        if (_release is null || !_release.UpdateAvailable)
        {
            await CheckForUpdatesAsync();
            if (_release is null || !_release.UpdateAvailable) return;
        }

        IsBusy = true;
        UpdateStatus = "Starting Sabby's fast updater…";
        if (!FastUpdateHelper.TryStart(_release, out var error))
        {
            UpdateStatus = error;
            IsBusy = false;
            return;
        }

        // Keep the visible optimizer open. The helper places a dimmed overlay over Sabby
        // while it downloads/verifies and Setup closes the app only when installation begins.
    }
}
