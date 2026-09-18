using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Models.GameProfiles;

namespace PCTweaker.ViewModels;

public sealed class GameInstallViewModel : ViewModelBase
{
    private const string GpuPreferenceKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    private readonly Action<GameInstallViewModel>? _removeRequested;
    private readonly string _name;
    private string _executablePath;
    private bool _isIncluded;
    private Guid? _linkedPresetId;
    private string _notes;
    private ImageSource? _icon;
    private bool _isRunning;
    private string _runtimeStatus = "Ready";
    private string _runtimeDetail = "Waiting for launch detection.";
    private bool _isSettingsOpen;
    private string _graphicsPreference;
    private string _processPriority;
    private string _gameSettingsStatus = "Settings are saved locally for this executable.";
    private bool _isApplyingSettings;
    private readonly DateTime _addedAtUtc;
    private DateTime _lastSeenUtc;

    public Guid Id { get; }
    public string Name => _name;
    public string Platform { get; private set; }
    public string InstallDirectory { get; private set; }
    public string SourceId { get; private set; }

    public IReadOnlyList<string> GraphicsPreferenceOptions { get; } =
        new[] { "System default", "Power saving", "High performance" };

    public IReadOnlyList<string> ProcessPriorityOptions { get; } =
        new[] { "Normal", "Above normal", "High" };

    public string ExecutablePath
    {
        get => _executablePath;
        private set
        {
            var oldPath = _executablePath;
            if (!SetProperty(ref _executablePath, value)) return;
            InstallDirectory = Path.GetDirectoryName(value) ?? string.Empty;
            OnPropertyChanged(nameof(InstallDirectory));
            RefreshIcon();

            if (!string.IsNullOrWhiteSpace(oldPath) && !oldPath.Equals(value, StringComparison.OrdinalIgnoreCase))
                TryRemoveGpuPreference(oldPath);
            GameSettingsStatus = "Executable changed. Click Apply to write and verify the selected game settings.";
        }
    }

    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }

    public Guid? LinkedPresetId
    {
        get => _linkedPresetId;
        set => SetProperty(ref _linkedPresetId, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value ?? string.Empty);
    }

    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            if (SetProperty(ref _icon, value)) OnPropertyChanged(nameof(HasIcon));
        }
    }

    public bool HasIcon => Icon is not null;

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public string RuntimeStatus
    {
        get => _runtimeStatus;
        private set => SetProperty(ref _runtimeStatus, value);
    }

    public string RuntimeDetail
    {
        get => _runtimeDetail;
        private set => SetProperty(ref _runtimeDetail, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        private set
        {
            if (!SetProperty(ref _isSettingsOpen, value)) return;
            OnPropertyChanged(nameof(SettingsChevron));
        }
    }

    public string SettingsChevron => IsSettingsOpen ? "\uE70E" : "\uE70D";

    public string GraphicsPreference
    {
        get => _graphicsPreference;
        set
        {
            var normalized = NormalizeGraphicsPreference(value);
            if (!SetProperty(ref _graphicsPreference, normalized)) return;
            GameSettingsStatus = $"Graphics preference staged: {normalized}. Click Apply to write and verify it.";
        }
    }

    public string ProcessPriority
    {
        get => _processPriority;
        set
        {
            var normalized = NormalizePriority(value);
            if (!SetProperty(ref _processPriority, normalized)) return;
            GameSettingsStatus = normalized.Equals("Normal", StringComparison.OrdinalIgnoreCase)
                ? "Process priority: Normal. No priority override will be applied."
                : $"Process priority: {normalized}. Sabby will apply it only while this exact game executable is running.";
        }
    }

    public string GameSettingsStatus
    {
        get => _gameSettingsStatus;
        private set => SetProperty(ref _gameSettingsStatus, value);
    }

    public bool IsApplyingSettings
    {
        get => _isApplyingSettings;
        private set => SetProperty(ref _isApplyingSettings, value);
    }

    public void UpdateRuntimeState(bool isRunning, string status, string detail)
    {
        IsRunning = isRunning;
        RuntimeStatus = string.IsNullOrWhiteSpace(status) ? (isRunning ? "Running" : "Ready") : status;
        RuntimeDetail = string.IsNullOrWhiteSpace(detail) ? "Waiting for launch detection." : detail;
    }

    public ICommand ToggleSettingsCommand { get; }
    public ICommand ChangeExecutableCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ApplySettingsCommand { get; }

    public GameInstallViewModel(GameProfileDefinition profile, Action<GameInstallViewModel>? removeRequested = null)
    {
        Id = profile.Id;
        _name = string.IsNullOrWhiteSpace(profile.Name) ? "Game" : profile.Name.Trim();
        Platform = profile.Platform;
        _executablePath = profile.ExecutablePath;
        InstallDirectory = profile.InstallDirectory;
        SourceId = profile.SourceId;
        _isIncluded = profile.Enabled;
        _linkedPresetId = profile.LinkedPresetId;
        _notes = profile.Notes;
        _graphicsPreference = NormalizeGraphicsPreference(profile.GraphicsPreference);
        _processPriority = NormalizePriority(profile.ProcessPriority);
        _removeRequested = removeRequested;
        _addedAtUtc = profile.AddedAtUtc;
        _lastSeenUtc = profile.LastSeenUtc;

        _graphicsPreference = DetectGraphicsPreference(_executablePath) ?? _graphicsPreference;

        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        ChangeExecutableCommand = new RelayCommand(ChangeExecutable);
        RemoveCommand = new RelayCommand(() => _removeRequested?.Invoke(this));
        ApplySettingsCommand = new AsyncRelayCommand(ApplySettingsAsync);
        RefreshIcon();
    }

    public GameProfileDefinition ToDefinition() => new()
    {
        SchemaVersion = 2,
        Id = Id,
        Name = Name,
        Platform = Platform,
        ExecutablePath = ExecutablePath,
        InstallDirectory = InstallDirectory,
        SourceId = SourceId,
        Enabled = IsIncluded,
        LinkedPresetId = LinkedPresetId,
        Notes = Notes,
        GraphicsPreference = GraphicsPreference,
        ProcessPriority = ProcessPriority,
        AddedAtUtc = _addedAtUtc,
        LastSeenUtc = _lastSeenUtc
    };

    private void ChangeExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Choose executable for {Name}",
            Filter = "Windows executable (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
            FileName = ExecutablePath
        };

        if (dialog.ShowDialog() == true)
            ExecutablePath = dialog.FileName;
    }

    private async Task ApplySettingsAsync()
    {
        IsApplyingSettings = true;
        GameSettingsStatus = "Applying and verifying game settings…";
        try
        {
            await Task.Yield();

            var graphicsOk = ApplyGraphicsPreference();
            var priorityResult = TryApplyProcessPriorityNow();
            var success = graphicsOk && priorityResult.Success;

            GameSettingsStatus = success
                ? $"✓ APPLIED & VERIFIED • Graphics: {GraphicsPreference} • Priority: {priorityResult.Message}"
                : $"Apply finished with a warning • Graphics: {(graphicsOk ? "verified" : "not verified")} • Priority: {priorityResult.Message}";

            UiNotificationHub.Publish(
                Name,
                GameSettingsStatus,
                success ? UiNotificationKind.Success : UiNotificationKind.Warning);
        }
        finally
        {
            IsApplyingSettings = false;
        }
    }

    private (bool Success, string Message) TryApplyProcessPriorityNow()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath))
            return (false, "executable path is missing");

        if (ProcessPriority.Equals("Normal", StringComparison.OrdinalIgnoreCase))
        {
            var runningNormal = FindExactGameProcess();
            if (runningNormal is null)
                return (true, "Normal saved for next launch");
            try
            {
                runningNormal.PriorityClass = ProcessPriorityClass.Normal;
                return (runningNormal.PriorityClass == ProcessPriorityClass.Normal, "Normal verified on running process");
            }
            catch (Exception ex) { return (false, $"could not set Normal: {ex.Message}"); }
            finally { runningNormal.Dispose(); }
        }

        var process = FindExactGameProcess();
        if (process is null)
            return (true, $"{ProcessPriority} saved; will apply automatically when this exact executable launches");

        try
        {
            var target = ProcessPriority.Equals("High", StringComparison.OrdinalIgnoreCase)
                ? ProcessPriorityClass.High
                : ProcessPriorityClass.AboveNormal;
            process.PriorityClass = target;
            var verified = process.PriorityClass == target;
            return (verified, verified ? $"{ProcessPriority} verified on running process" : $"Windows read-back did not confirm {ProcessPriority}");
        }
        catch (Exception ex) { return (false, $"could not set process priority: {ex.Message}"); }
        finally { process.Dispose(); }
    }

    private Process? FindExactGameProcess()
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(ExecutablePath);
            var expected = Path.GetFullPath(ExecutablePath);
            foreach (var candidate in Process.GetProcessesByName(name))
            {
                try
                {
                    var actual = candidate.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(actual) && Path.GetFullPath(actual).Equals(expected, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
                catch { }
                candidate.Dispose();
            }
        }
        catch { }
        return null;
    }

    private bool ApplyGraphicsPreference()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath)) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(GpuPreferenceKeyPath, writable: true);
            if (key is null)
            {
                GameSettingsStatus = "Windows graphics preference could not be opened for writing.";
                return false;
            }

            if (GraphicsPreference.Equals("System default", StringComparison.OrdinalIgnoreCase))
                key.DeleteValue(ExecutablePath, throwOnMissingValue: false);
            else
                key.SetValue(
                    ExecutablePath,
                    GraphicsPreference.Equals("High performance", StringComparison.OrdinalIgnoreCase)
                        ? "GpuPreference=2;"
                        : "GpuPreference=1;",
                    RegistryValueKind.String);

            var verified = DetectGraphicsPreference(ExecutablePath) ?? "System default";
            var success = verified.Equals(GraphicsPreference, StringComparison.OrdinalIgnoreCase);
            GameSettingsStatus = success
                ? $"✓ Windows graphics preference verified: {verified}."
                : $"Windows read-back returned {verified}; requested {GraphicsPreference}.";

            UiNotificationHub.Publish(
                Name,
                GameSettingsStatus,
                success ? UiNotificationKind.Success : UiNotificationKind.Warning);
            return success;
        }
        catch (Exception ex)
        {
            GameSettingsStatus = $"Windows graphics preference was not changed: {ex.Message}";
            UiNotificationHub.Publish(Name, GameSettingsStatus, UiNotificationKind.Warning);
            return false;
        }
    }

    private static string? DetectGraphicsPreference(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GpuPreferenceKeyPath, writable: false);
            var raw = key?.GetValue(executablePath)?.ToString();
            if (string.IsNullOrWhiteSpace(raw)) return "System default";
            if (raw.Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase)) return "High performance";
            if (raw.Contains("GpuPreference=1", StringComparison.OrdinalIgnoreCase)) return "Power saving";
            return "System default";
        }
        catch { return null; }
    }

    private static void TryRemoveGpuPreference(string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GpuPreferenceKeyPath, writable: true);
            key?.DeleteValue(executablePath, throwOnMissingValue: false);
        }
        catch { }
    }

    private static string NormalizeGraphicsPreference(string? value) => value switch
    {
        "Power saving" => "Power saving",
        "High performance" => "High performance",
        _ => "System default"
    };

    private static string NormalizePriority(string? value) => value switch
    {
        "Above normal" => "Above normal",
        "High" => "High",
        _ => "Normal"
    };

    private void RefreshIcon() => Icon = GameIconService.TryLoadIcon(ExecutablePath, Platform, SourceId);
}
