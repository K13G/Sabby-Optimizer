using System.Windows;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class StartupUpdateCoordinator
{
    private readonly IUpdateExtensionService _service;
    private readonly ISettingsService _settings;
    private readonly IAppLogger _logger;

    public StartupUpdateCoordinator(IUpdateExtensionService service, ISettingsService settings, IAppLogger logger)
    {
        _service = service;
        _settings = settings;
        _logger = logger;
    }

    public async Task CheckAndEnforceAsync(Window owner, CancellationToken cancellationToken = default)
    {
        await Task.Delay(700, cancellationToken);

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
            "\n\nPress OK to update. Sabby stays visible under a faded update overlay while the installer downloads and verifies. The app closes only when installation is ready, then reopens automatically. Your settings remain in Local AppData.",
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

        // Do not close here. The updater overlays this window during download/verification.
        // Inno Setup closes/replaces Sabby only after the verified installer is ready.
    }
}
