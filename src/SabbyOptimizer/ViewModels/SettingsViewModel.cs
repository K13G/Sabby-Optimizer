using System.Windows.Input;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IAppearanceService _appearance;
    private readonly IAppStartupService _startupService;

    public AppUpdateViewModel UpdateManager { get; }

    private ThemeMode _selectedTheme;
    private VisualStyle _selectedVisualStyle;
    private double _styleIntensity;
    private double _animationSpeed;
    private bool _rememberLastPage;
    private bool _rememberWindowState;
    private bool _startMaximized;
    private bool _startWithWindows;
    private bool _closeToTray;
    private bool _minimizeToTray;
    private bool _gameDetectionEnabled;
    private bool _smartGameTuningTest;
    private int _cardColumns;
    private string _visualStyleSearchText = string.Empty;
    private string _selectedEventFilter = "All styles";
    private VisualStyleSortOption _selectedSortOption;
    private bool _isStylePickerOpen;
    private bool _isResetConfirmationOpen;
    private string _statusMessage = "Settings are saved locally on this PC.";

    public IReadOnlyList<ThemeMode> AvailableThemes { get; } = Enum.GetValues<ThemeMode>();

    public IReadOnlyList<VisualStyleOption> AvailableVisualStyles { get; } =
    [
        new(VisualStyle.SabbyBlue, "Electric Blue", "Clean blue with a crisp cyan edge.", false, false, string.Empty, 1, 1),
        new(VisualStyle.NeonPulse, "Neon", "Cyan, violet, and pink with a smooth color cycle.", true, false, string.Empty, 2, 2),
        new(VisualStyle.AuroraFlow, "Aurora", "Emerald, blue, and violet with a slow flowing shift.", true, false, string.Empty, 3, 3),
        new(VisualStyle.PurpleFlux, "Ultra Violet", "Deep violet with a sharp magenta highlight.", false, false, string.Empty, 4, 4),
        new(VisualStyle.EmeraldCircuit, "Toxic Green", "Bright emerald with a clean hardware edge.", false, false, string.Empty, 5, 5),
        new(VisualStyle.SunsetDrive, "Solar", "Warm coral and gold with a smooth glow.", false, false, string.Empty, 6, 6),
        new(VisualStyle.BloodBath, "Blood Bath", "Dark crimson droplets move through the interface.", true, true, "Halloween", 7, 7),
        new(VisualStyle.Ice, "Arctic", "Cold blue-white accents with a crisp frozen look.", false, false, string.Empty, 8, 8),
        new(VisualStyle.Inferno, "Inferno", "Red and orange with a controlled fire-like shift.", true, false, string.Empty, 9, 9),
        new(VisualStyle.Gold, "Gold", "Dark gold with a bright metallic highlight.", false, false, string.Empty, 10, 10),
        new(VisualStyle.Midnight, "Nightfall", "Deep blue-violet built for a dark-room look.", false, false, string.Empty, 11, 11),
        new(VisualStyle.Spectrum, "RGB", "A smooth red, blue, and violet color cycle.", true, false, string.Empty, 12, 12),
        new(VisualStyle.Crimson, "Crimson", "Clean deep red without the Halloween effects.", false, false, string.Empty, 13, 13),
        new(VisualStyle.HyperBlue, "Hyper Blue", "Electric blue and cyan with a fast clean sweep.", true, false, string.Empty, 14, 14),
        new(VisualStyle.Plasma, "Plasma", "Purple, pink, and cyan with a fluid animated shift.", true, false, string.Empty, 15, 15),
        new(VisualStyle.Carbon, "Carbon", "Neutral metallic gray for a minimal performance look.", false, false, string.Empty, 16, 16),
        new(VisualStyle.CyberMint, "Cyber Mint", "Mint-green and ice-blue with a clean animated circuit glow.", true, false, string.Empty, 17, 4),
        new(VisualStyle.ObsidianGold, "Obsidian Gold", "Black-metal styling with restrained premium gold accents.", false, false, string.Empty, 18, 5),
        new(VisualStyle.DeepOcean, "Deep Ocean", "Dark ocean blue through cyan with a slow underwater flow.", true, false, string.Empty, 19, 7),
        new(VisualStyle.Voltage, "Voltage", "Electric yellow, cyan, and cobalt with a sharp animated pulse.", true, false, string.Empty, 20, 2),
        new(VisualStyle.LaserRed, "Laser Red", "Clean performance red with a moving hot-red highlight.", true, false, string.Empty, 21, 6),
        new(VisualStyle.Glacier, "Glacier", "Muted icy blue with bright frost-white highlights.", false, false, string.Empty, 22, 10),
        new(VisualStyle.Matrix, "Matrix", "Deep green with a slow emerald terminal-style cycle.", true, false, string.Empty, 23, 8),
        new(VisualStyle.CottonCandy, "Cotton Candy", "Soft cyan, pink, and violet with a smooth animated blend.", true, false, string.Empty, 24, 9),
        new(VisualStyle.Royal, "Royal", "Rich royal blue and violet with a polished static accent.", false, false, string.Empty, 25, 11),
        new(VisualStyle.Tangerine, "Tangerine", "Bright orange with warm amber contrast for a clean energetic look.", false, false, string.Empty, 26, 12),
        new(VisualStyle.Ultraviolet, "Ultraviolet Flow", "Deep ultraviolet, electric purple, and blue in a slow animated sweep.", true, false, string.Empty, 27, 3),
        new(VisualStyle.Monochrome, "Monochrome", "Neutral white and graphite accents for a distraction-free layout.", false, false, string.Empty, 28, 13),
        new(VisualStyle.SapphirePulse, "Sapphire Pulse", "Deep sapphire and cyan with a slow low-overhead pulse.", true, false, string.Empty, 29, 4),
        new(VisualStyle.CherryNeon, "Cherry Neon", "Cherry red, hot pink, and soft white with a restrained neon cycle.", true, false, string.Empty, 30, 7),
        new(VisualStyle.CosmicWave, "Cosmic Wave", "Indigo, violet, and electric blue flowing through a dark-space palette.", true, false, string.Empty, 31, 3),
        new(VisualStyle.ToxicWave, "Toxic Wave", "Acid green and emerald with a controlled animated shift.", true, false, string.Empty, 32, 8),
        new(VisualStyle.EmberGlow, "Ember Glow", "Deep ember red and warm orange with a subtle animated glow.", true, false, string.Empty, 33, 9),
        new(VisualStyle.FrostPulse, "Frost Pulse", "Ice blue, white, and cool cyan with a calm pulse.", true, false, string.Empty, 34, 5),
        new(VisualStyle.RoseQuartz, "Rose Quartz", "Clean rose and blush accents for a softer static style.", false, false, string.Empty, 35, 12),
        new(VisualStyle.LimeCircuit, "Lime Circuit", "Sharp lime and emerald hardware accents.", false, false, string.Empty, 36, 10),
        new(VisualStyle.IceFire, "Ice + Fire", "Blue-to-red contrast with a slow alternating color cycle.", true, false, string.Empty, 37, 11),
        new(VisualStyle.Eclipse, "Eclipse", "Near-black graphite with restrained violet highlights.", false, false, string.Empty, 38, 2),
        new(VisualStyle.Vaporwave, "Vaporwave", "Magenta, cyan, and violet with a smooth retro-future blend.", true, false, string.Empty, 39, 6),
        new(VisualStyle.Nova, "Nova", "Bright gold, orange, and white in a slow solar pulse.", true, false, string.Empty, 40, 9),
        new(VisualStyle.OceanNeon, "Ocean Neon", "Deep navy through turquoise and cyan with a quiet flow.", true, false, string.Empty, 41, 5),
        new(VisualStyle.Sandstorm, "Sandstorm", "Warm amber, bronze, and pale gold in a clean static palette.", false, false, string.Empty, 42, 14)
    ];

    public IReadOnlyList<VisualStyleSortOption> SortOptions { get; } =
    [
        new(VisualStyleSortMode.Recommended, "Recommended"),
        new(VisualStyleSortMode.Newest, "Newest"),
        new(VisualStyleSortMode.Oldest, "Oldest"),
        new(VisualStyleSortMode.NameAscending, "Name A–Z"),
        new(VisualStyleSortMode.NameDescending, "Name Z–A"),
        new(VisualStyleSortMode.StaticFirst, "Static first"),
        new(VisualStyleSortMode.AnimatedFirst, "Animated first"),
        new(VisualStyleSortMode.EventFirst, "Event first")
    ];

    public IReadOnlyList<string> EventFilters { get; } =
    [
        "All styles",
        "Animated",
        "Static",
        "Event styles",
        "Non-event styles",
        "Halloween"
    ];

    public ThemeMode SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetProperty(ref _selectedTheme, value)) return;
            _theme.Apply(value);
            _appearance.Apply(_selectedVisualStyle, _styleIntensity, _animationSpeed);
            StatusMessage = "Previewing appearance. Save settings to keep it.";
        }
    }

    public VisualStyle SelectedVisualStyle
    {
        get => _selectedVisualStyle;
        set
        {
            if (!SetProperty(ref _selectedVisualStyle, value)) return;
            _appearance.Apply(value, _styleIntensity, _animationSpeed);
            OnPropertyChanged(nameof(SelectedVisualStyleOption));
            OnPropertyChanged(nameof(StylePickerSelection));
            StatusMessage = "Previewing appearance. Save settings to keep it.";
        }
    }

    public VisualStyleOption? SelectedVisualStyleOption =>
        AvailableVisualStyles.FirstOrDefault(option => option.Value == SelectedVisualStyle);

    public VisualStyleOption? StylePickerSelection
    {
        get => SelectedVisualStyleOption;
        set
        {
            if (value is null) return;
            SelectedVisualStyle = value.Value;
            IsStylePickerOpen = false;
        }
    }

    public double StyleIntensity
    {
        get => _styleIntensity;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _styleIntensity, clamped)) return;
            _appearance.Apply(_selectedVisualStyle, clamped, _animationSpeed);
            OnPropertyChanged(nameof(StyleIntensityText));
            StatusMessage = "Previewing appearance. Save settings to keep it.";
        }
    }

    public string StyleIntensityText => $"{StyleIntensity:0}%";

    public double AnimationSpeed
    {
        get => _animationSpeed;
        set
        {
            var clamped = Math.Clamp(value, 0, 200);
            if (!SetProperty(ref _animationSpeed, clamped)) return;
            _appearance.Apply(_selectedVisualStyle, _styleIntensity, clamped);
            OnPropertyChanged(nameof(AnimationSpeedText));
            StatusMessage = clamped <= 0
                ? "Animations frozen at their current frame. Save settings to keep it."
                : "Previewing animation speed. Save settings to keep it.";
        }
    }

    public string AnimationSpeedText => AnimationSpeed <= 0 ? "Frozen" : $"{AnimationSpeed:0}%";

    public bool RememberLastPage
    {
        get => _rememberLastPage;
        set
        {
            if (!SetProperty(ref _rememberLastPage, value)) return;
            _settings.Current.RememberLastPage = value;
            _ = SaveBehaviorSettingAsync();
        }
    }

    public bool RememberWindowState
    {
        get => _rememberWindowState;
        set
        {
            if (!SetProperty(ref _rememberWindowState, value)) return;
            _settings.Current.RememberWindowState = value;
            _ = SaveBehaviorSettingAsync();
        }
    }

    public bool StartMaximized
    {
        get => _startMaximized;
        set
        {
            if (!SetProperty(ref _startMaximized, value)) return;
            _settings.Current.StartMaximized = value;
            _ = SaveBehaviorSettingAsync();
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetProperty(ref _startWithWindows, value)) return;
            _settings.Current.StartWithWindows = value;
            _startupService.SetEnabled(value);
            _ = SaveBehaviorSettingAsync();
        }
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set
        {
            if (!SetProperty(ref _closeToTray, value)) return;
            _settings.Current.CloseToTray = value;
            _ = SaveBehaviorSettingAsync();
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (!SetProperty(ref _minimizeToTray, value)) return;
            _settings.Current.MinimizeToTray = value;
            _ = SaveBehaviorSettingAsync();
        }
    }


    public bool GameDetectionEnabled
    {
        get => _gameDetectionEnabled;
        set
        {
            if (!SetProperty(ref _gameDetectionEnabled, value)) return;
            _settings.Current.GameDetectionEnabled = value;
            _ = SaveBehaviorSettingAsync();
        }
    }

    public bool SmartGameTuningTest
    {
        get => _smartGameTuningTest;
        set
        {
            if (!SetProperty(ref _smartGameTuningTest, value)) return;
            _settings.Current.SmartGameTuningTest = value;
            _ = SaveBehaviorSettingAsync();
        }
    }


    public IReadOnlyList<int> CardColumnOptions { get; } = [1, 2, 3, 4];

    public int CardColumns
    {
        get => _cardColumns;
        set
        {
            var clamped = Math.Clamp(value, 1, 4);
            if (!SetProperty(ref _cardColumns, clamped)) return;
            _settings.Current.CardColumns = clamped;
            if (System.Windows.Application.Current is not null)
                System.Windows.Application.Current.Resources["CardColumns"] = clamped;
            _ = SaveBehaviorSettingAsync();
        }
    }

    public string VisualStyleSearchText
    {
        get => _visualStyleSearchText;
        set
        {
            if (!SetProperty(ref _visualStyleSearchText, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(FilteredVisualStyles));
            OnPropertyChanged(nameof(FilteredStyleCountText));
        }
    }

    public string SelectedEventFilter
    {
        get => _selectedEventFilter;
        set
        {
            if (!SetProperty(ref _selectedEventFilter, value ?? "All styles")) return;
            OnPropertyChanged(nameof(FilteredVisualStyles));
            OnPropertyChanged(nameof(FilteredStyleCountText));
        }
    }

    public VisualStyleSortOption SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (value is null || !SetProperty(ref _selectedSortOption, value)) return;
            OnPropertyChanged(nameof(FilteredVisualStyles));
            OnPropertyChanged(nameof(FilteredStyleCountText));
        }
    }

    public IReadOnlyList<VisualStyleOption> FilteredVisualStyles => BuildFilteredVisualStyles();
    public string FilteredStyleCountText => $"{FilteredVisualStyles.Count} style{(FilteredVisualStyles.Count == 1 ? string.Empty : "s")}";

    public bool IsStylePickerOpen
    {
        get => _isStylePickerOpen;
        set => SetProperty(ref _isStylePickerOpen, value);
    }

    public bool IsResetConfirmationOpen
    {
        get => _isResetConfirmationOpen;
        set => SetProperty(ref _isResetConfirmationOpen, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand CancelResetCommand { get; }
    public ICommand ConfirmResetCommand { get; }

    public SettingsViewModel(
        ISettingsService settings,
        IThemeService theme,
        IAppearanceService appearance,
        IAppStartupService startupService,
        IUpdateExtensionService updateService)
    {
        _settings = settings;
        _theme = theme;
        _appearance = appearance;
        _startupService = startupService;
        UpdateManager = new AppUpdateViewModel(updateService, settings);
        _selectedTheme = settings.Current.Theme;
        _selectedVisualStyle = settings.Current.VisualStyle;
        _styleIntensity = settings.Current.StyleIntensity;
        _animationSpeed = settings.Current.AnimationSpeed;
        _rememberLastPage = settings.Current.RememberLastPage;
        _rememberWindowState = settings.Current.RememberWindowState;
        _startMaximized = settings.Current.StartMaximized;
        _startWithWindows = settings.Current.StartWithWindows;
        _closeToTray = settings.Current.CloseToTray;
        _minimizeToTray = settings.Current.MinimizeToTray;
        _gameDetectionEnabled = settings.Current.GameDetectionEnabled;
        _smartGameTuningTest = settings.Current.SmartGameTuningTest;
        _cardColumns = settings.Current.CardColumns;
        _selectedSortOption = SortOptions[0];

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        ResetCommand = new RelayCommand(() => IsResetConfirmationOpen = true);
        CancelResetCommand = new RelayCommand(() => IsResetConfirmationOpen = false);
        ConfirmResetCommand = new AsyncRelayCommand(ResetAsync);
    }

    private IReadOnlyList<VisualStyleOption> BuildFilteredVisualStyles()
    {
        IEnumerable<VisualStyleOption> query = AvailableVisualStyles;
        var search = VisualStyleSearchText.Trim();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(option => option.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase));

        query = SelectedEventFilter switch
        {
            "Animated" => query.Where(option => option.IsAnimated),
            "Static" => query.Where(option => !option.IsAnimated),
            "Event styles" => query.Where(option => option.IsEvent),
            "Non-event styles" => query.Where(option => !option.IsEvent),
            "Halloween" => query.Where(option => option.IsEvent && option.EventName.Equals("Halloween", StringComparison.OrdinalIgnoreCase)),
            _ => query
        };

        query = SelectedSortOption.Value switch
        {
            VisualStyleSortMode.Newest => query.OrderByDescending(option => option.AddedOrder),
            VisualStyleSortMode.Oldest => query.OrderBy(option => option.AddedOrder),
            VisualStyleSortMode.NameAscending => query.OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase),
            VisualStyleSortMode.NameDescending => query.OrderByDescending(option => option.Name, StringComparer.OrdinalIgnoreCase),
            VisualStyleSortMode.StaticFirst => query.OrderBy(option => option.IsAnimated).ThenBy(option => option.RecommendedOrder),
            VisualStyleSortMode.AnimatedFirst => query.OrderByDescending(option => option.IsAnimated).ThenBy(option => option.RecommendedOrder),
            VisualStyleSortMode.EventFirst => query.OrderByDescending(option => option.IsEvent).ThenBy(option => option.RecommendedOrder),
            _ => query.OrderBy(option => option.RecommendedOrder)
        };

        return query.ToList();
    }

    private async Task SaveBehaviorSettingAsync()
    {
        try
        {
            await _settings.SaveAsync();
            StatusMessage = "Application behavior saved locally on this PC.";
        }
        catch
        {
            StatusMessage = "Could not save the application behavior setting.";
        }
    }

    private async Task SaveAsync()
    {
        _settings.Current.Theme = SelectedTheme;
        _settings.Current.VisualStyle = SelectedVisualStyle;
        _settings.Current.StyleIntensity = StyleIntensity;
        _settings.Current.AnimationSpeed = AnimationSpeed;
        _settings.Current.RememberLastPage = RememberLastPage;
        _settings.Current.RememberWindowState = RememberWindowState;
        _settings.Current.StartMaximized = StartMaximized;
        _settings.Current.StartWithWindows = StartWithWindows;
        _settings.Current.CloseToTray = CloseToTray;
        _settings.Current.MinimizeToTray = MinimizeToTray;
        _settings.Current.GameDetectionEnabled = GameDetectionEnabled;
        _settings.Current.SmartGameTuningTest = SmartGameTuningTest;
        _settings.Current.CardColumns = CardColumns;

        _startupService.SetEnabled(StartWithWindows);
        // Theme/style setters already preview the selected appearance. Saving should persist that
        // selection without advancing/restarting an animated palette (which previously made Save
        // look like it randomly changed colors).
        await _settings.SaveAsync();
        StatusMessage = $"Saved at {DateTime.Now:t}.";
    }

    private async Task ResetAsync()
    {
        IsResetConfirmationOpen = false;
        await _settings.ResetAsync();

        _selectedTheme = _settings.Current.Theme;
        _selectedVisualStyle = _settings.Current.VisualStyle;
        _styleIntensity = _settings.Current.StyleIntensity;
        _animationSpeed = _settings.Current.AnimationSpeed;
        _rememberLastPage = _settings.Current.RememberLastPage;
        _rememberWindowState = _settings.Current.RememberWindowState;
        _startMaximized = _settings.Current.StartMaximized;
        _startWithWindows = _settings.Current.StartWithWindows;
        _closeToTray = _settings.Current.CloseToTray;
        _minimizeToTray = _settings.Current.MinimizeToTray;
        _gameDetectionEnabled = _settings.Current.GameDetectionEnabled;
        _smartGameTuningTest = _settings.Current.SmartGameTuningTest;
        _cardColumns = _settings.Current.CardColumns;
        _visualStyleSearchText = string.Empty;
        _selectedEventFilter = "All styles";
        _selectedSortOption = SortOptions[0];

        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(SelectedVisualStyle));
        OnPropertyChanged(nameof(SelectedVisualStyleOption));
        OnPropertyChanged(nameof(StylePickerSelection));
        OnPropertyChanged(nameof(StyleIntensity));
        OnPropertyChanged(nameof(StyleIntensityText));
        OnPropertyChanged(nameof(AnimationSpeed));
        OnPropertyChanged(nameof(AnimationSpeedText));
        OnPropertyChanged(nameof(RememberLastPage));
        OnPropertyChanged(nameof(RememberWindowState));
        OnPropertyChanged(nameof(StartMaximized));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(CloseToTray));
        OnPropertyChanged(nameof(MinimizeToTray));
        OnPropertyChanged(nameof(GameDetectionEnabled));
        OnPropertyChanged(nameof(SmartGameTuningTest));
        OnPropertyChanged(nameof(CardColumns));
        OnPropertyChanged(nameof(VisualStyleSearchText));
        OnPropertyChanged(nameof(SelectedEventFilter));
        OnPropertyChanged(nameof(SelectedSortOption));
        OnPropertyChanged(nameof(FilteredVisualStyles));
        OnPropertyChanged(nameof(FilteredStyleCountText));

        _startupService.SetEnabled(_settings.Current.StartWithWindows);
        _theme.Apply(SelectedTheme);
        _appearance.Apply(SelectedVisualStyle, StyleIntensity, AnimationSpeed);
        if (System.Windows.Application.Current is not null) System.Windows.Application.Current.Resources["CardColumns"] = CardColumns;
        StatusMessage = "App settings reset to defaults.";
    }
}
