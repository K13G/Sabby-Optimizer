using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Win32;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class TweakExtensionItemViewModel : ViewModelBase
{
    private readonly IPhase21UpdateExtensionService _service;
    private bool _enabled;
    private bool _isBusy;
    private string _status;

    public InstalledTweakExtensionInfo Model { get; }
    public string Id => Model.Id;
    public string Name => Model.Name;
    public string VersionText => $"v{Model.Version}";
    public string Author => Model.Author;
    public string Description => Model.Description;
    public int RuleCount => Model.RuleCount;
    public bool Valid => Model.Valid;
    public string ValidationMessage => Model.ValidationMessage;
    public string RulesText => $"{RuleCount} declarative registry rule{(RuleCount == 1 ? string.Empty : "s")}";
    public string FileName => Path.GetFileName(Model.FilePath);

    public bool Enabled
    {
        get => _enabled;
        private set
        {
            if (!SetProperty(ref _enabled, value)) return;
            OnPropertyChanged(nameof(StateLabel));
        }
    }
    public string StateLabel => !Valid ? "BLOCKED" : Enabled ? "ENABLED" : "DISABLED";
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { ToggleCommand.RaiseCanExecuteChanged(); RemoveCommand.RaiseCanExecuteChanged(); } } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public AsyncRelayCommand ToggleCommand { get; }
    public AsyncRelayCommand RemoveCommand { get; }
    public event EventHandler? Changed;

    public TweakExtensionItemViewModel(IPhase21UpdateExtensionService service, InstalledTweakExtensionInfo model)
    {
        _service = service;
        Model = model;
        _enabled = model.Enabled;
        _status = model.Valid ? model.ValidationMessage : $"Blocked: {model.ValidationMessage}";
        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => !IsBusy && Valid);
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => !IsBusy);
    }

    private async Task ToggleAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _service.SetExtensionEnabledAsync(Id, !Enabled);
            Status = result.Message;
            if (result.Success)
            {
                Enabled = !Enabled;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally { IsBusy = false; }
    }

    private async Task RemoveAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _service.RemoveExtensionAsync(Id);
            Status = result.Message;
            if (result.Success) Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { IsBusy = false; }
    }
}

public sealed class ExtensionsViewModel : ViewModelBase
{
    private readonly IPhase21UpdateExtensionService _service;
    private readonly ISettingsService _settings;
    private bool _isBusy;
    private SabbyUpdateChannel _selectedChannel;
    private bool _autoCheck;
    private string _feedUrl = string.Empty;
    private string _updateStatus = "Sabby checks the official GitHub stable channel automatically.";
    private string _releaseNotes = "No channel check has been run yet.";
    private string _extensionStatus = "Loading tweak-rule extensions…";
    private double _downloadProgress;
    private SabbyReleaseInfo? _release;
    private string? _stagedFile;

    public ObservableCollection<TweakExtensionItemViewModel> Extensions { get; } = new();
    public IReadOnlyList<SabbyUpdateChannel> Channels { get; } = [SabbyUpdateChannel.Stable];

    public SabbyUpdateChannel SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (_selectedChannel == value) return;
            SaveFeedForChannel(_selectedChannel, FeedUrl);
            if (!SetProperty(ref _selectedChannel, value)) return;
            _settings.Current.SabbyUpdateChannel = value;
            _feedUrl = GetFeedUrl(value);
            OnPropertyChanged(nameof(FeedUrl));
            UpdateStatus = $"{value} channel selected.";
            _ = SaveSettingsAsync();
        }
    }

    public bool AutoCheck
    {
        get => _autoCheck;
        set
        {
            if (!SetProperty(ref _autoCheck, value)) return;
            _settings.Current.AutoCheckSabbyUpdates = value;
            _ = SaveSettingsAsync();
        }
    }

    public string FeedUrl
    {
        get => _feedUrl;
        set
        {
            if (!SetProperty(ref _feedUrl, value ?? string.Empty)) return;
            SaveFeedForChannel(_selectedChannel, _feedUrl);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            CheckChannelCommand.RaiseCanExecuteChanged();
            StageUpdateCommand.RaiseCanExecuteChanged();
            RefreshExtensionsCommand.RaiseCanExecuteChanged();
            ImportExtensionCommand.RaiseCanExecuteChanged();
            CreateExampleCommand.RaiseCanExecuteChanged();
        }
    }

    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }
    public string ReleaseNotes { get => _releaseNotes; private set => SetProperty(ref _releaseNotes, value); }
    public string ExtensionStatus { get => _extensionStatus; private set => SetProperty(ref _extensionStatus, value); }
    public double DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, Math.Clamp(value, 0, 100)); }
    public string CurrentVersion => _release?.CurrentVersion ?? typeof(ExtensionsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public string LatestVersion => _release?.LatestVersion ?? "—";
    public bool UpdateAvailable => _release?.UpdateAvailable == true;
    public string UpdateButtonText => _stagedFile is null ? "Download update" : "Open downloaded update";
    public string ChannelHelp => "Official releases come from the Sabby GitHub stable feed. Auto-check is enabled by default, and required releases prompt before you continue using an outdated build.";

    public AsyncRelayCommand CheckChannelCommand { get; }
    public AsyncRelayCommand StageUpdateCommand { get; }
    public AsyncRelayCommand RefreshExtensionsCommand { get; }
    public AsyncRelayCommand ImportExtensionCommand { get; }
    public AsyncRelayCommand CreateExampleCommand { get; }
    public RelayCommand OpenExtensionsFolderCommand { get; }

    public ExtensionsViewModel(IPhase21UpdateExtensionService service, ISettingsService settings)
    {
        _service = service;
        _settings = settings;
        _selectedChannel = SabbyUpdateChannel.Stable;
        _settings.Current.SabbyUpdateChannel = SabbyUpdateChannel.Stable;
        _autoCheck = settings.Current.AutoCheckSabbyUpdates;
        var builtInFeed = SabbyUpdateDefaults.GetBuiltInStableFeedUrl();
        _feedUrl = string.IsNullOrWhiteSpace(builtInFeed) ? GetFeedUrl(SabbyUpdateChannel.Stable) : builtInFeed;
        _settings.Current.StableUpdateFeedUrl = _feedUrl;

        CheckChannelCommand = new AsyncRelayCommand(CheckChannelAsync, () => !IsBusy);
        StageUpdateCommand = new AsyncRelayCommand(StageUpdateAsync, () => !IsBusy && (_release?.UpdateAvailable == true || _stagedFile is not null));
        RefreshExtensionsCommand = new AsyncRelayCommand(RefreshExtensionsAsync, () => !IsBusy);
        ImportExtensionCommand = new AsyncRelayCommand(ImportExtensionAsync, () => !IsBusy);
        CreateExampleCommand = new AsyncRelayCommand(CreateExampleAsync, () => !IsBusy);
        OpenExtensionsFolderCommand = new RelayCommand(OpenExtensionsFolder);

        _ = InitializeAsync();
    }

    public async Task AutoCheckIfEnabledAsync()
    {
        if (!AutoCheck || IsBusy) return;
        await CheckChannelAsync();
    }

    private async Task InitializeAsync()
    {
        await RefreshExtensionsAsync();
    }

    private async Task CheckChannelAsync()
    {
        IsBusy = true;
        try
        {
            SaveCurrentFeedUrl();
            await _settings.SaveAsync();
            UpdateStatus = $"Checking {SelectedChannel} channel…";
            _release = await _service.CheckSabbyUpdateAsync(SelectedChannel, FeedUrl);
            var requiredSuffix = _release.Mandatory && _release.UpdateAvailable ? " • REQUIRED" : string.Empty;
            UpdateStatus = string.IsNullOrWhiteSpace(_release.SignerThumbprint)
                ? _release.Status + requiredSuffix
                : $"{_release.Status}{requiredSuffix} • Signed-publisher verification required.";
            ReleaseNotes = string.IsNullOrWhiteSpace(_release.ReleaseNotes)
                ? (_release.UpdateAvailable ? "No release notes were supplied by this feed." : "No newer release notes to show.")
                : _release.ReleaseNotes!;
            _stagedFile = null;
            DownloadProgress = 0;
            RaiseReleaseProperties();
        }
        finally { IsBusy = false; }
    }

    private async Task StageUpdateAsync()
    {
        if (_stagedFile is not null && File.Exists(_stagedFile))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_stagedFile}\"") { UseShellExecute = true });
            return;
        }
        if (_release is null || !_release.UpdateAvailable) return;

        IsBusy = true;
        DownloadProgress = 0;
        try
        {
            UpdateStatus = $"Downloading Sabby {_release.LatestVersion}…";
            var progress = new Progress<double>(value => DownloadProgress = value);
            var result = await _service.DownloadAndStageUpdateAsync(_release, progress);
            UpdateStatus = result.Message;
            if (result.Success)
            {
                _stagedFile = result.FilePath;
                UiNotificationHub.Publish("Sabby update staged", result.Message, UiNotificationKind.Success);
            }
            else
            {
                UiNotificationHub.Publish("Sabby update", result.Message, UiNotificationKind.Warning);
            }
            RaiseReleaseProperties();
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshExtensionsAsync()
    {
        IsBusy = true;
        try
        {
            var rows = await _service.GetExtensionsAsync();
            Extensions.Clear();
            foreach (var row in rows)
            {
                var vm = new TweakExtensionItemViewModel(_service, row);
                vm.Changed += (_, _) => _ = RefreshExtensionsAsync();
                Extensions.Add(vm);
            }
            var enabled = rows.Count(x => x.Enabled && x.Valid);
            var blocked = rows.Count(x => !x.Valid);
            ExtensionStatus = $"{rows.Count} extension{(rows.Count == 1 ? string.Empty : "s")} installed • {enabled} enabled • {blocked} blocked by validation. Catalog changes take effect after restart.";
        }
        finally { IsBusy = false; }
    }

    private async Task ImportExtensionAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Sabby tweak-rule extension",
            Filter = "Sabby extension JSON (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var result = await _service.ImportExtensionAsync(dialog.FileName);
            ExtensionStatus = result.Message;
            UiNotificationHub.Publish("Tweak-rule extension", result.Message, result.Success ? UiNotificationKind.Success : UiNotificationKind.Warning);
        }
        finally { IsBusy = false; }
        await RefreshExtensionsAsync();
    }

    private async Task CreateExampleAsync()
    {
        IsBusy = true;
        try
        {
            var file = await _service.CreateExampleExtensionAsync();
            ExtensionStatus = $"Example extension created: {file}";
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }
        finally { IsBusy = false; }
        await RefreshExtensionsAsync();
    }

    private void OpenExtensionsFolder()
    {
        Directory.CreateDirectory(_service.ExtensionsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", _service.ExtensionsDirectory) { UseShellExecute = true });
    }

    private void SaveCurrentFeedUrl() => SaveFeedForChannel(SelectedChannel, FeedUrl);

    private void SaveFeedForChannel(SabbyUpdateChannel channel, string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        switch (channel)
        {
            case SabbyUpdateChannel.Stable: _settings.Current.StableUpdateFeedUrl = trimmed; break;
            case SabbyUpdateChannel.Preview: _settings.Current.PreviewUpdateFeedUrl = trimmed; break;
            case SabbyUpdateChannel.Nightly: _settings.Current.NightlyUpdateFeedUrl = trimmed; break;
        }
    }

    private string GetFeedUrl(SabbyUpdateChannel channel) => channel switch
    {
        SabbyUpdateChannel.Stable => SabbyUpdateDefaults.NormalizeStableFeed(_settings.Current.StableUpdateFeedUrl),
        SabbyUpdateChannel.Preview => _settings.Current.PreviewUpdateFeedUrl,
        SabbyUpdateChannel.Nightly => _settings.Current.NightlyUpdateFeedUrl,
        _ => SabbyUpdateDefaults.GetBuiltInStableFeedUrl()
    };

    private async Task SaveSettingsAsync()
    {
        try { SaveCurrentFeedUrl(); await _settings.SaveAsync(); } catch { }
    }

    private void RaiseReleaseProperties()
    {
        OnPropertyChanged(nameof(CurrentVersion));
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(UpdateAvailable));
        OnPropertyChanged(nameof(UpdateButtonText));
        StageUpdateCommand.RaiseCanExecuteChanged();
    }
}
