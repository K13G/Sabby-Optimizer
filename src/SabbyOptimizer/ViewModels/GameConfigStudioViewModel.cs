using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Win32;
using PCTweaker.Core.GameProfiles;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Models.GameProfiles;

namespace PCTweaker.ViewModels;

public sealed class GameConfigFileItemViewModel : ViewModelBase
{
    public GameConfigFileInfo Model { get; }
    public string DisplayName => Model.DisplayName;
    public string FilePath => Model.FilePath;
    public string LocationLabel => Model.LocationLabel;
    public string Kind => Model.Kind;
    public string SizeText => Model.SizeText;
    public string ModifiedText => Model.ModifiedText;
    public int RelevanceScore => Model.RelevanceScore;

    public GameConfigFileItemViewModel(GameConfigFileInfo model) => Model = model;
}

public sealed class GameConfigQuickEntryViewModel : ViewModelBase
{
    private string _value;
    public string Section { get; }
    public string Key { get; }
    public int LineIndex { get; }
    public string DisplayName => string.IsNullOrWhiteSpace(Section) ? Key : $"[{Section}] {Key}";

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? string.Empty);
    }

    public GameConfigQuickEntryViewModel(string section, string key, string value, int lineIndex)
    {
        Section = section;
        Key = key;
        _value = value;
        LineIndex = lineIndex;
    }
}

public sealed class GameConfigStudioViewModel : ViewModelBase
{
    private readonly IGameProfileService _profileService;
    private readonly GameConfigStudioService _configService;
    private readonly List<GameConfigFileItemViewModel> _allConfigFiles = new();

    private GameProfileDefinition? _selectedGame;
    private GameConfigFileItemViewModel? _selectedConfig;
    private GameConfigQuickEntryViewModel? _selectedQuickEntry;
    private string _fileSearchText = string.Empty;
    private string _editorText = string.Empty;
    private string _loadedText = string.Empty;
    private string _status = "Choose a saved game profile, then scan for configuration files.";
    private string _documentStatus = "No config file loaded.";
    private string _encodingText = string.Empty;
    private bool _isBusy;
    private bool _isDirty;
    private double _progress;
    private bool _loadingEditor;

    public ObservableCollection<GameProfileDefinition> Games { get; } = new();
    public ObservableCollection<GameConfigFileItemViewModel> ConfigFiles { get; } = new();
    public ObservableCollection<GameConfigQuickEntryViewModel> QuickEntries { get; } = new();

    public GameProfileDefinition? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (!SetProperty(ref _selectedGame, value)) return;
            ClearDocument();
            _allConfigFiles.Clear();
            ConfigFiles.Clear();
            Status = value is null
                ? "Choose a saved game profile."
                : $"{value.Name} selected. Scan for configs or add a text config manually.";
            RaiseCommandStates();
        }
    }

    public GameConfigFileItemViewModel? SelectedConfig
    {
        get => _selectedConfig;
        set
        {
            if (!SetProperty(ref _selectedConfig, value)) return;
            RaiseCommandStates();
            if (value is not null)
                _ = LoadSelectedAsync();
            else
                ClearDocument();
        }
    }

    public GameConfigQuickEntryViewModel? SelectedQuickEntry
    {
        get => _selectedQuickEntry;
        set
        {
            if (!SetProperty(ref _selectedQuickEntry, value)) return;
            ApplyQuickValueCommand.RaiseCanExecuteChanged();
        }
    }

    public string FileSearchText
    {
        get => _fileSearchText;
        set
        {
            if (!SetProperty(ref _fileSearchText, value ?? string.Empty)) return;
            RebuildConfigFilter();
        }
    }

    public string EditorText
    {
        get => _editorText;
        set
        {
            if (!SetProperty(ref _editorText, value ?? string.Empty)) return;
            if (!_loadingEditor)
            {
                IsDirty = !string.Equals(_editorText, _loadedText, StringComparison.Ordinal);
                DocumentStatus = IsDirty ? "Unsaved changes • Sabby will create a backup before writing." : "Loaded • no unsaved changes.";
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string DocumentStatus
    {
        get => _documentStatus;
        private set => SetProperty(ref _documentStatus, value);
    }

    public string EncodingText
    {
        get => _encodingText;
        private set => SetProperty(ref _encodingText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RaiseCommandStates();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (!SetProperty(ref _isDirty, value)) return;
            SaveCommand.RaiseCanExecuteChanged();
        }
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, Math.Clamp(value, 0, 100));
    }

    public string SelectedFilePath => SelectedConfig?.FilePath ?? "No file selected";
    public string SelectedGameSummary => SelectedGame is null
        ? "No game selected"
        : $"{SelectedGame.Platform} • {SelectedGame.ExecutablePath}";
    public string ConfigCountText => $"{ConfigFiles.Count} config file{(ConfigFiles.Count == 1 ? string.Empty : "s")}";
    public bool HasQuickEntries => QuickEntries.Count > 0;

    public AsyncRelayCommand RefreshGamesCommand { get; }
    public AsyncRelayCommand ScanConfigsCommand { get; }
    public RelayCommand AddConfigFileCommand { get; }
    public AsyncRelayCommand ReloadCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand BackupCommand { get; }
    public AsyncRelayCommand RestoreBackupCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand ApplyQuickValueCommand { get; }
    public RelayCommand ClearSearchCommand { get; }

    public GameConfigStudioViewModel(IGameProfileService profileService, GameConfigStudioService configService)
    {
        _profileService = profileService;
        _configService = configService;

        RefreshGamesCommand = new AsyncRelayCommand(RefreshGamesAsync, () => !IsBusy);
        ScanConfigsCommand = new AsyncRelayCommand(ScanConfigsAsync, () => !IsBusy && SelectedGame is not null);
        AddConfigFileCommand = new RelayCommand(AddConfigFile, () => !IsBusy && SelectedGame is not null);
        ReloadCommand = new AsyncRelayCommand(LoadSelectedAsync, () => !IsBusy && SelectedConfig is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && SelectedGame is not null && SelectedConfig is not null && IsDirty);
        BackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => !IsBusy && SelectedGame is not null && SelectedConfig is not null);
        RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, () => !IsBusy && SelectedGame is not null && SelectedConfig is not null);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => SelectedConfig is not null);
        ApplyQuickValueCommand = new RelayCommand(ApplyQuickValue, () => SelectedQuickEntry is not null && SelectedConfig is not null);
        ClearSearchCommand = new RelayCommand(() => FileSearchText = string.Empty);

        _ = RefreshGamesAsync();
    }

    private async Task RefreshGamesAsync()
    {
        IsBusy = true;
        Progress = 0;
        try
        {
            var previousId = SelectedGame?.Id;
            var profiles = await _profileService.GetAllAsync();
            Games.Clear();
            foreach (var game in profiles.Where(x => x.Enabled).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                Games.Add(game);
            SelectedGame = previousId.HasValue
                ? Games.FirstOrDefault(x => x.Id == previousId.Value) ?? Games.FirstOrDefault()
                : Games.FirstOrDefault();
            Progress = 100;
            Status = Games.Count == 0
                ? "No enabled game profiles are available. Add or scan games in Game Profiles first."
                : $"Loaded {Games.Count} enabled game profile{(Games.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not load game profiles: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task ScanConfigsAsync()
    {
        if (SelectedGame is null) return;
        IsBusy = true;
        Progress = 0;
        Status = $"Scanning safe text-config locations for {SelectedGame.Name}…";
        try
        {
            var progress = new Progress<double>(x => Progress = x);
            var files = await _configService.DiscoverAsync(SelectedGame, progress);
            _allConfigFiles.Clear();
            _allConfigFiles.AddRange(files.Select(x => new GameConfigFileItemViewModel(x)));
            RebuildConfigFilter();
            Progress = 100;
            Status = files.Count == 0
                ? "No high-confidence text config files were found automatically. Use Add config file if you know the exact file."
                : $"Found {files.Count} likely config file{(files.Count == 1 ? string.Empty : "s")}. Nothing was modified.";
        }
        catch (Exception ex)
        {
            Status = $"Config scan failed safely: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private void AddConfigFile()
    {
        if (SelectedGame is null) return;
        var dialog = new OpenFileDialog
        {
            Title = $"Add a text config for {SelectedGame.Name}",
            Filter = "Game config files|*.ini;*.cfg;*.conf;*.json;*.xml;*.yaml;*.yml;*.toml;*.properties;*.prefs;*.txt|All files|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Directory.Exists(SelectedGame.InstallDirectory) ? SelectedGame.InstallDirectory : null
        };
        if (dialog.ShowDialog() != true) return;
        if (!_configService.IsSupportedTextConfig(dialog.FileName))
        {
            Status = "That file type is not in Sabby's safe text-config allowlist. Binary/save-game files are intentionally not editable here.";
            return;
        }

        var info = new FileInfo(dialog.FileName);
        if (info.Length > 4 * 1024 * 1024)
        {
            Status = "That file is larger than 4 MB, so Sabby will not load it into the config editor.";
            return;
        }

        var model = new GameConfigFileInfo(info.FullName, info.Name, "Manual", info.Extension, info.Length, info.LastWriteTimeUtc, 100);
        var existing = _allConfigFiles.FirstOrDefault(x => x.FilePath.Equals(info.FullName, StringComparison.OrdinalIgnoreCase));
        var item = existing ?? new GameConfigFileItemViewModel(model);
        if (existing is null) _allConfigFiles.Insert(0, item);
        RebuildConfigFilter();
        SelectedConfig = item;
        Status = "Manual config added to this session. Sabby has not changed it.";
    }

    private async Task LoadSelectedAsync()
    {
        if (SelectedConfig is null) return;
        IsBusy = true;
        Progress = 15;
        try
        {
            var document = await _configService.LoadAsync(SelectedConfig.Model);
            _loadingEditor = true;
            EditorText = document.Text;
            _loadedText = document.Text;
            IsDirty = false;
            _loadingEditor = false;
            EncodingText = $"{SelectedConfig.Kind} • {document.EncodingName} • {SelectedConfig.SizeText}";
            DocumentStatus = $"{document.ValidationSummary} • last modified {SelectedConfig.ModifiedText}";
            OnPropertyChanged(nameof(SelectedFilePath));
            RebuildQuickEntries();
            Progress = 100;
        }
        catch (Exception ex)
        {
            _loadingEditor = false;
            DocumentStatus = $"Could not load config: {ex.Message}";
            Status = DocumentStatus;
        }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        if (SelectedGame is null || SelectedConfig is null) return;
        IsBusy = true;
        Progress = 15;
        DocumentStatus = "Creating backup, validating, writing, and verifying…";
        try
        {
            var result = await _configService.SaveAsync(SelectedGame.Id, SelectedConfig.Model, EditorText);
            Progress = 100;
            if (result.Success)
            {
                _loadedText = EditorText;
                IsDirty = false;
                DocumentStatus = $"✓ SAVED & VERIFIED • {DateTime.Now:h:mm:ss tt}";
                Status = result.Message;
                RebuildQuickEntries();
                UiNotificationHub.Publish("Config Studio", $"{SelectedGame.Name}: {SelectedConfig.DisplayName} saved and verified.", UiNotificationKind.Success);
            }
            else
            {
                DocumentStatus = $"✕ {result.Message}";
                Status = result.Message;
                UiNotificationHub.Publish("Config save blocked", result.Message, UiNotificationKind.Warning);
            }
        }
        finally { IsBusy = false; }
    }

    private async Task CreateBackupAsync()
    {
        if (SelectedGame is null || SelectedConfig is null) return;
        IsBusy = true;
        try
        {
            var path = await _configService.CreateBackupAsync(SelectedGame.Id, SelectedConfig.FilePath, "manual");
            Status = $"Backup created: {path}";
            UiNotificationHub.Publish("Config backup created", SelectedConfig.DisplayName, UiNotificationKind.Success);
        }
        catch (Exception ex) { Status = $"Backup failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task RestoreBackupAsync()
    {
        if (SelectedGame is null || SelectedConfig is null) return;
        IsBusy = true;
        DocumentStatus = "Restoring the latest Sabby config backup…";
        try
        {
            var result = await _configService.RestoreLatestBackupAsync(SelectedGame.Id, SelectedConfig.Model);
            Status = result.Message;
            if (result.Success)
            {
                await LoadSelectedAsync();
                UiNotificationHub.Publish("Config restored", SelectedConfig.DisplayName, UiNotificationKind.Success);
            }
        }
        finally { IsBusy = false; }
    }

    private void OpenFolder()
    {
        if (SelectedConfig is null) return;
        try { _configService.OpenContainingFolder(SelectedConfig.FilePath); }
        catch (Exception ex) { Status = $"Could not open the folder: {ex.Message}"; }
    }

    private void ApplyQuickValue()
    {
        if (SelectedQuickEntry is null) return;
        var newline = EditorText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = EditorText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (SelectedQuickEntry.LineIndex < 0 || SelectedQuickEntry.LineIndex >= lines.Count)
        {
            DocumentStatus = "The quick-setting line moved. Reload or refresh the file before applying that key.";
            return;
        }

        var line = lines[SelectedQuickEntry.LineIndex];
        var equals = line.IndexOf('=');
        if (equals <= 0)
        {
            DocumentStatus = "That quick setting is no longer a key=value line.";
            return;
        }

        var suffixComment = string.Empty;
        var currentValuePart = line[(equals + 1)..];
        var commentIndex = FindInlineCommentIndex(currentValuePart);
        if (commentIndex >= 0)
        {
            suffixComment = currentValuePart[commentIndex..];
        }

        lines[SelectedQuickEntry.LineIndex] = line[..(equals + 1)] + SelectedQuickEntry.Value + suffixComment;
        EditorText = string.Join(newline, lines);
        DocumentStatus = $"Staged {SelectedQuickEntry.DisplayName} = {SelectedQuickEntry.Value}. Click Save & verify to write it.";
        RebuildQuickEntries(preserveKey: SelectedQuickEntry.DisplayName);
    }

    private void RebuildQuickEntries(string? preserveKey = null)
    {
        preserveKey ??= SelectedQuickEntry?.DisplayName;
        QuickEntries.Clear();
        SelectedQuickEntry = null;
        if (SelectedConfig is null || !(SelectedConfig.Model.Extension.Equals(".ini", StringComparison.OrdinalIgnoreCase) ||
                                        SelectedConfig.Model.Extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase) ||
                                        SelectedConfig.Model.Extension.Equals(".conf", StringComparison.OrdinalIgnoreCase) ||
                                        SelectedConfig.Model.Extension.Equals(".properties", StringComparison.OrdinalIgnoreCase)))
        {
            OnPropertyChanged(nameof(HasQuickEntries));
            return;
        }

        var lines = EditorText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var section = string.Empty;
        for (var i = 0; i < lines.Length && QuickEntries.Count < 180; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
            {
                section = trimmed[1..^1].Trim();
                continue;
            }
            var equals = lines[i].IndexOf('=');
            if (equals <= 0) continue;
            var key = lines[i][..equals].Trim();
            if (key.Length == 0) continue;
            var value = lines[i][(equals + 1)..].Trim();
            var comment = FindInlineCommentIndex(value);
            if (comment >= 0) value = value[..comment].TrimEnd();
            QuickEntries.Add(new GameConfigQuickEntryViewModel(section, key, value, i));
        }
        if (!string.IsNullOrWhiteSpace(preserveKey))
            SelectedQuickEntry = QuickEntries.FirstOrDefault(x => x.DisplayName.Equals(preserveKey, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(HasQuickEntries));
    }

    private static int FindInlineCommentIndex(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if ((value[i] == ';' || value[i] == '#') && (i == 0 || char.IsWhiteSpace(value[i - 1]))) return i;
        }
        return -1;
    }

    private void RebuildConfigFilter()
    {
        var search = FileSearchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(search)
            ? _allConfigFiles
            : _allConfigFiles.Where(x => x.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                         x.FilePath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                         x.LocationLabel.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                         x.Kind.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        ConfigFiles.Clear();
        foreach (var item in filtered) ConfigFiles.Add(item);
        OnPropertyChanged(nameof(ConfigCountText));
    }

    private void ClearDocument()
    {
        _selectedConfig = null;
        OnPropertyChanged(nameof(SelectedConfig));
        _loadingEditor = true;
        EditorText = string.Empty;
        _loadedText = string.Empty;
        _loadingEditor = false;
        IsDirty = false;
        EncodingText = string.Empty;
        DocumentStatus = "No config file loaded.";
        QuickEntries.Clear();
        SelectedQuickEntry = null;
        OnPropertyChanged(nameof(SelectedFilePath));
        OnPropertyChanged(nameof(SelectedGameSummary));
        OnPropertyChanged(nameof(HasQuickEntries));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        RefreshGamesCommand.RaiseCanExecuteChanged();
        ScanConfigsCommand.RaiseCanExecuteChanged();
        if (AddConfigFileCommand is RelayCommand add) add.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        BackupCommand.RaiseCanExecuteChanged();
        RestoreBackupCommand.RaiseCanExecuteChanged();
        if (OpenFolderCommand is RelayCommand folder) folder.RaiseCanExecuteChanged();
        ApplyQuickValueCommand.RaiseCanExecuteChanged();
    }
}
