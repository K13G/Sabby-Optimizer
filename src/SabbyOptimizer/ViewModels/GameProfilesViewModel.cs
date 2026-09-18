using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Win32;
using PCTweaker.Core.GameDetection;
using PCTweaker.Core.GameProfiles;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Presets;
using PCTweaker.Core.Services;
using PCTweaker.Models;
using PCTweaker.Models.GameDetection;
using PCTweaker.Models.GameProfiles;

namespace PCTweaker.ViewModels;

public sealed class GameProfilesViewModel : ViewModelBase, IDisposable
{
    private readonly IGameScanService _gameScanService;
    private readonly IGameProfileService _profileService;
    private readonly IPresetService _presetService;
    private readonly IGameDetectionService _gameDetectionService;
    private bool _isScanning;
    private bool _isSaving;
    private string _scanStatus = "Loading your saved game library...";
    private string _searchText = string.Empty;
    private string _detectionStatus = "Game detection is starting...";
    private double _scanProgress;

    public ObservableCollection<GameInstallViewModel> Games { get; } = new();
    public ObservableCollection<PresetLinkOption> PresetOptions { get; } = new();

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!SetProperty(ref _isScanning, value)) return;
            OnPropertyChanged(nameof(IsScanProgressVisible));
            RaiseCommandStates();
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set { if (SetProperty(ref _isSaving, value)) RaiseCommandStates(); }
    }

    public double ScanProgress
    {
        get => _scanProgress;
        private set => SetProperty(ref _scanProgress, Math.Clamp(value, 0, 100));
    }

    public bool IsScanProgressVisible => IsScanning;

    public string ScanStatus
    {
        get => _scanStatus;
        private set => SetProperty(ref _scanStatus, value);
    }

    public string DetectionStatus
    {
        get => _detectionStatus;
        private set => SetProperty(ref _detectionStatus, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                OnPropertyChanged(nameof(FilteredGames));
        }
    }

    public IReadOnlyList<GameInstallViewModel> FilteredGames
    {
        get
        {
            var search = SearchText.Trim();
            if (string.IsNullOrWhiteSpace(search)) return Games.ToList();
            return Games.Where(game =>
                    game.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    game.Platform.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    game.ExecutablePath.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public string GameCountText => $"{Games.Count} game{(Games.Count == 1 ? string.Empty : "s")} saved";

    public ICommand ScanGamesCommand { get; }
    public ICommand SaveProfilesCommand { get; }
    public ICommand AddManualGameCommand { get; }
    public ICommand ClearSearchCommand { get; }

    private AsyncRelayCommand ScanCommandImpl => (AsyncRelayCommand)ScanGamesCommand;
    private AsyncRelayCommand SaveCommandImpl => (AsyncRelayCommand)SaveProfilesCommand;

    public GameProfilesViewModel(IGameScanService gameScanService, IGameProfileService profileService, IPresetService presetService, IGameDetectionService gameDetectionService)
    {
        _gameScanService = gameScanService;
        _profileService = profileService;
        _presetService = presetService;
        _gameDetectionService = gameDetectionService;
        _gameDetectionService.StateChanged += OnGameDetectionStateChanged;

        ScanGamesCommand = new AsyncRelayCommand(ScanGamesAsync, () => !IsScanning && !IsSaving);
        SaveProfilesCommand = new AsyncRelayCommand(SaveProfilesAsync, () => !IsSaving && !IsScanning);
        AddManualGameCommand = new RelayCommand(AddManualGame, () => !IsScanning && !IsSaving);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
    }

    public async Task InitializeAsync()
    {
        var presets = await _presetService.GetAllAsync();
        PresetOptions.Clear();
        PresetOptions.Add(new PresetLinkOption(null, "No preset"));
        foreach (var preset in presets.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            PresetOptions.Add(new PresetLinkOption(preset.Id, preset.Name));

        var profiles = await _profileService.GetAllAsync();
        var library = await _profileService.GetLibraryInfoAsync();
        ReplaceGames(profiles);
        DetectionStatus = _gameDetectionService.IsMonitoring
            ? "Launch detection is active for enabled saved game profiles."
            : "Launch detection will start when Sabby Optimizer finishes loading.";

        if (library.HasCompletedScan)
        {
            var when = library.LastScanAtUtc?.ToLocalTime().ToString("g") ?? "previously";
            ScanStatus = $"Loaded {GameCountText} from your saved scan ({when}). You only need to scan again after installing or moving games.";
        }
        else
        {
            ScanStatus = Games.Count == 0
                ? "No saved scan yet. Deep scan checks launcher libraries plus game-like executables in Documents and common game folders."
                : $"Loaded {GameCountText}. Run Deep scan when you want to refresh the library.";
        }
    }

    private async Task ScanGamesAsync()
    {
        IsScanning = true;
        ScanProgress = 0;
        ScanStatus = "Scanning launcher libraries first, then checking game-like executables in Documents and common game folders...";
        try
        {
            var progress = new Progress<int>(value => ScanProgress = value);
            var detected = await _gameScanService.ScanAsync(progress);
            ScanStatus = $"Found {detected.Count} high-confidence game executable{(detected.Count == 1 ? string.Empty : "s")}. Saving the scan...";
            var merged = await _profileService.MergeScanResultsAsync(detected);
            ReplaceGames(merged);
            ScanProgress = 100;
            ScanStatus = Games.Count == 0
                ? "No high-confidence games were found. You can still add a game manually."
                : $"Scan saved. {GameCountText}. You do not need to scan again until your installed games change.";
            UiNotificationHub.Publish("Game scan complete", ScanStatus, Games.Count > 0 ? UiNotificationKind.Success : UiNotificationKind.Info);
        }
        catch (Exception ex)
        {
            ScanStatus = $"Game scan failed: {ex.Message}";
            UiNotificationHub.Publish("Game scan failed", ex.Message, UiNotificationKind.Warning);
        }
        finally { IsScanning = false; }
    }

    private async Task SaveProfilesAsync()
    {
        IsSaving = true;
        try
        {
            await _profileService.SaveAllAsync(Games.Select(game => game.ToDefinition()).ToArray());
            ScanStatus = $"Saved {GameCountText} locally on this PC.";
        }
        catch (Exception ex) { ScanStatus = $"Could not save game profiles: {ex.Message}"; }
        finally { IsSaving = false; }
    }

    private void AddManualGame()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add a game executable",
            Filter = "Windows executable (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        var executable = dialog.FileName;
        var profile = new GameProfileDefinition
        {
            Name = GetDisplayName(executable),
            Platform = "Manual",
            ExecutablePath = executable,
            InstallDirectory = Path.GetDirectoryName(executable) ?? string.Empty,
            SourceId = SafeFullPath(executable),
            Enabled = true
        };
        AddViewModel(profile);
        OnGamesChanged();
        _ = SaveProfilesSilentlyAsync();
        ScanStatus = "Manual game added and saved locally. Link a preset or change its executable if needed.";
    }

    private void RemoveGame(GameInstallViewModel game)
    {
        Games.Remove(game);
        OnGamesChanged();
        _ = SaveProfilesSilentlyAsync();
        ScanStatus = $"Removed {game.Name} from the local profile library.";
    }

    private void ReplaceGames(IEnumerable<GameProfileDefinition> profiles)
    {
        Games.Clear();
        foreach (var profile in profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)) AddViewModel(profile);
        OnGamesChanged();
    }

    private void AddViewModel(GameProfileDefinition profile)
    {
        var vm = new GameInstallViewModel(profile, RemoveGame);
        vm.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(FilteredGames));
            _ = SaveProfilesSilentlyAsync();
        };
        Games.Add(vm);
    }

    private async Task SaveProfilesSilentlyAsync()
    {
        try { await _profileService.SaveAllAsync(Games.Select(game => game.ToDefinition()).ToArray()); }
        catch { }
    }

    private void OnGamesChanged()
    {
        OnPropertyChanged(nameof(GameCountText));
        OnPropertyChanged(nameof(FilteredGames));
    }

    private void RaiseCommandStates()
    {
        ScanCommandImpl.RaiseCanExecuteChanged();
        SaveCommandImpl.RaiseCanExecuteChanged();
        if (AddManualGameCommand is RelayCommand relay) relay.RaiseCanExecuteChanged();
    }

    private void OnGameDetectionStateChanged(object? sender, GameRuntimeState state)
    {
        void ApplyState()
        {
            if (state.ProfileId == Guid.Empty)
            {
                DetectionStatus = state.Detail;
                return;
            }

            var game = Games.FirstOrDefault(item => item.Id == state.ProfileId);
            if (game is not null)
                game.UpdateRuntimeState(state.IsRunning, state.Status, state.Detail);

            var runningCount = Games.Count(item => item.IsRunning);
            DetectionStatus = runningCount > 0
                ? $"{runningCount} game{(runningCount == 1 ? string.Empty : "s")} detected as running. Linked presets and Smart tuning use the safe Phase 7 lifecycle."
                : "Launch detection is active. Waiting for an enabled saved game to start.";
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) ApplyState();
        else dispatcher.BeginInvoke(ApplyState);
    }

    private static string GetDisplayName(string executable)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executable);
            if (!string.IsNullOrWhiteSpace(info.ProductName) && !info.ProductName.Equals("Application", StringComparison.OrdinalIgnoreCase))
                return info.ProductName.Trim();
            if (!string.IsNullOrWhiteSpace(info.FileDescription) && !info.FileDescription.Equals("Application", StringComparison.OrdinalIgnoreCase))
                return info.FileDescription.Trim();
        }
        catch { }
        return Path.GetFileNameWithoutExtension(executable);
    }

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
    public void Dispose()
    {
        _gameDetectionService.StateChanged -= OnGameDetectionStateChanged;
    }

}
