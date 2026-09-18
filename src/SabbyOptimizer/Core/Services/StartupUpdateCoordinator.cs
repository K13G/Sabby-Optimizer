using System.Windows;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class StartupUpdateCoordinator
{
    private readonly IPhase21UpdateExtensionService _service;
    private readonly ISettingsService _settings;
    private readonly IAppLogger _logger;

    public StartupUpdateCoordinator(IPhase21UpdateExtensionService service, ISettingsService settings, IAppLogger logger)
    {
        _service = service;
        _settings = settings;
        _logger = logger;
    }

    public async Task CheckAndEnforceAsync(Window owner, CancellationToken cancellationToken = default)
    {
        await Task.Delay(2200, cancellationToken);

        var feed = SabbyUpdateDefaults.NormalizeStableFeed(_settings.Current.StableUpdateFeedUrl);
        if (!string.Equals(_settings.Current.StableUpdateFeedUrl, feed, StringComparison.Ordinal))
        {
            _settings.Current.StableUpdateFeedUrl = feed;
            try { await _settings.SaveAsync(); } catch { }
        }

        if (!_settings.Current.AutoCheckSabbyUpdates || string.IsNullOrWhiteSpace(feed))
            return;

        var release = await _service.CheckSabbyUpdateAsync(SabbyUpdateChannel.Stable, feed, cancellationToken);
        if (!release.UpdateAvailable) return;

        if (!release.Mandatory)
        {
            UiNotificationHub.Publish($"Sabby {release.LatestVersion} available",
                "Open Settings > Sabby updates when you are ready.", UiNotificationKind.Info);
            return;
        }

        var choice = MessageBox.Show(owner,
            $"Sabby Optimizer {release.LatestVersion} is required before continuing.\n\n" +
            (string.IsNullOrWhiteSpace(release.ReleaseNotes) ? "This update includes the latest fixes and improvements." : release.ReleaseNotes) +
            "\n\nPress OK and the visible app will close immediately. The lightweight updater downloads the verified installer, installs it, and reopens Sabby. Your settings remain in Local AppData.",
            "Sabby Optimizer - Update Required",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (choice != MessageBoxResult.OK)
        {
            Application.Current.Shutdown(0);
            return;
        }

        if (!FastUpdateHelper.TryStart(release, out var error))
        {
            _logger.Warning($"Fast updater could not start: {error}");
            MessageBox.Show(owner, $"Sabby could not start the update helper.\n\n{error}",
                "Sabby Optimizer - Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Application.Current.Shutdown(0);
    }
}
