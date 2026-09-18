using System.Windows;
using Microsoft.Win32;
using PCTweaker.Models;
using AppThemeMode = PCTweaker.Models.ThemeMode;

namespace PCTweaker.Core.Services;

public sealed class ThemeService : IThemeService
{
    private const string DarkThemePath = "Resources/Themes/DarkTheme.xaml";
    private const string DarknessThemePath = "Resources/Themes/DarknessTheme.xaml";
    private const string LightThemePath = "Resources/Themes/LightTheme.xaml";

    private readonly IAppLogger _logger;

    public AppThemeMode ActiveTheme { get; private set; }

    public ThemeService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void Apply(AppThemeMode mode)
    {
        var effective = mode == AppThemeMode.System ? ReadSystemTheme() : mode;
        var source = effective switch
        {
            AppThemeMode.Light => LightThemePath,
            AppThemeMode.Darkness => DarknessThemePath,
            _ => DarkThemePath
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true);

        var replacement = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
        if (existing is not null)
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries[index] = replacement;
        }
        else
        {
            dictionaries.Insert(0, replacement);
        }

        ActiveTheme = mode;
        _logger.Info($"Theme applied: {mode} (effective: {effective}).");
    }

    private static AppThemeMode ReadSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int light && light == 0 ? AppThemeMode.Dark : AppThemeMode.Light;
        }
        catch
        {
            return AppThemeMode.Dark;
        }
    }
}
