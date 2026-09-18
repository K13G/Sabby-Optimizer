using PCTweaker.Core.Navigation;

namespace PCTweaker.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 13;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public VisualStyle VisualStyle { get; set; } = PCTweaker.Models.VisualStyle.SabbyBlue;
    public double StyleIntensity { get; set; } = 100;
    public double AnimationSpeed { get; set; } = 100;
    public bool RememberLastPage { get; set; } = true;
    public AppPage LastPage { get; set; } = AppPage.Dashboard;

    public bool RememberWindowState { get; set; } = true;
    public bool StartMaximized { get; set; }
    public bool StartWithWindows { get; set; }
    public bool CloseToTray { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool GameDetectionEnabled { get; set; } = true;
    public bool SmartGameTuningTest { get; set; }
    public bool InitialRestorePointCreated { get; set; }
    public int CardColumns { get; set; } = 3;
    public bool AutoUpdateEnabled { get; set; }
    public bool IncludeOptionalWindowsUpdates { get; set; }

        public SabbyUpdateChannel SabbyUpdateChannel { get; set; } = SabbyUpdateChannel.Stable;
    public bool AutoCheckSabbyUpdates { get; set; } = true;
    public string StableUpdateFeedUrl { get; set; } = string.Empty;
    public string PreviewUpdateFeedUrl { get; set; } = string.Empty;
    public string NightlyUpdateFeedUrl { get; set; } = string.Empty;

        public bool MonitoringEnabled { get; set; } = false;
    public int MonitoringRefreshIntervalMs { get; set; } = 2000;
    public bool ThermalAlertsEnabled { get; set; } = true;
    public double ThermalWarningCelsius { get; set; } = 85;
    public Guid? AutomationFallbackPresetId { get; set; }

    public WindowSettings Window { get; set; } = new();

    public void Normalize()
    {
        SchemaVersion = 13;
        Window ??= new WindowSettings();
        Window.Normalize();

        if (!Enum.IsDefined(Theme)) Theme = ThemeMode.System;
        if (!Enum.IsDefined(VisualStyle)) VisualStyle = PCTweaker.Models.VisualStyle.SabbyBlue;
        if (!Enum.IsDefined(LastPage)) LastPage = AppPage.Dashboard;
        StyleIntensity = Math.Clamp(StyleIntensity, 0, 100);
        AnimationSpeed = Math.Clamp(AnimationSpeed, 0, 200);
        CardColumns = Math.Clamp(CardColumns, 1, 4);
        if (!Enum.IsDefined(SabbyUpdateChannel)) SabbyUpdateChannel = SabbyUpdateChannel.Stable;
        StableUpdateFeedUrl = PCTweaker.Core.Services.SabbyUpdateDefaults.NormalizeStableFeed(StableUpdateFeedUrl);
        PreviewUpdateFeedUrl ??= string.Empty;
        NightlyUpdateFeedUrl ??= string.Empty;
        MonitoringRefreshIntervalMs = Math.Clamp(MonitoringRefreshIntervalMs, 750, 10000);
        ThermalWarningCelsius = Math.Clamp(ThermalWarningCelsius, 60, 100);
    }
}
