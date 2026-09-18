using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Windows;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class StartupUpdateCoordinator
{
    private readonly IPhase21UpdateExtensionService _service;
    private readonly ISettingsService _settings;
    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;

    public StartupUpdateCoordinator(IPhase21UpdateExtensionService service, ISettingsService settings, IAppPaths paths, IAppLogger logger)
    {
        _service = service;
        _settings = settings;
        _paths = paths;
        _logger = logger;
    }

    public async Task CheckAndEnforceAsync(Window owner, CancellationToken cancellationToken = default)
    {
        // Never compete with the first frame. The check is network-bound and happens after the
        // app is already responsive.
        await Task.Delay(1400, cancellationToken);

        var feed = _settings.Current.StableUpdateFeedUrl;
        if (string.IsNullOrWhiteSpace(feed))
        {
            feed = SabbyUpdateDefaults.GetBuiltInStableFeedUrl();
            if (!string.IsNullOrWhiteSpace(feed))
            {
                _settings.Current.StableUpdateFeedUrl = feed;
                try { await _settings.SaveAsync(); } catch { }
            }
        }

        if (!_settings.Current.AutoCheckSabbyUpdates || string.IsNullOrWhiteSpace(feed))
            return;

        SabbyReleaseInfo release;
        try
        {
            release = await _service.CheckSabbyUpdateAsync(SabbyUpdateChannel.Stable, feed, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Startup update check failed without blocking Sabby: {ex.Message}");
            return;
        }

        if (!release.UpdateAvailable)
            return;

        if (!release.Mandatory)
        {
            UiNotificationHub.Publish(
                $"Sabby {release.LatestVersion} available",
                "Open Update & Extensions to download the new release.",
                UiNotificationKind.Info);
            return;
        }

        var choice = MessageBox.Show(owner,
            $"Sabby Optimizer {release.LatestVersion} is required before continuing.\n\n" +
            (string.IsNullOrWhiteSpace(release.ReleaseNotes) ? "This update includes the latest fixes and improvements." : release.ReleaseNotes) +
            "\n\nSabby will download one self-contained installer, verify it, preserve your settings, install it, and reopen the updated app. No .NET SDK, Inno Setup, Python, or other update tool is required on this PC.",
            "Sabby Optimizer - Update Required",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (choice != MessageBoxResult.OK)
        {
            _logger.Info("Required update was declined; closing the outdated Sabby version.");
            Application.Current.Shutdown(0);
            return;
        }

        UiNotificationHub.Publish(
            $"Downloading Sabby {release.LatestVersion}",
            "The verified self-contained update is downloading now.",
            UiNotificationKind.Info);

        var progress = new Progress<double>(_ => { });
        var staged = await _service.DownloadAndStageUpdateAsync(release, progress, cancellationToken);
        if (!staged.Success || string.IsNullOrWhiteSpace(staged.FilePath) || !File.Exists(staged.FilePath))
        {
            _logger.Warning($"Required update could not be staged: {staged.Message}");
            MessageBox.Show(owner,
                $"Sabby {release.LatestVersion} is required, but the installer could not be downloaded safely.\n\n{staged.Message}\n\nSabby will stay open so you are not locked out by a temporary network problem.",
                "Sabby Optimizer - Update Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Create a local safety snapshot before starting the installer. Program files and user data
        // are already separated, but this gives upgrades an additional rollback point for settings,
        // presets, game profiles, tweak state and other persistent Sabby data.
        var safetySnapshot = CreatePreUpdateSafetySnapshot(release.LatestVersion);
        if (string.IsNullOrWhiteSpace(safetySnapshot))
        {
            _logger.Warning("Required update was staged, but the pre-update user-data safety snapshot could not be created.");
            MessageBox.Show(owner,
                "The update downloaded successfully, but Sabby could not create its pre-update settings/data backup.\n\nThe installer was not started. Your current version will stay open so no settings are put at risk.",
                "Sabby Optimizer - Update Backup Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        UiNotificationHub.Publish(
            $"Installing Sabby {release.LatestVersion}",
            "Your settings were backed up. Approve the Windows administrator prompt to finish the update.",
            UiNotificationKind.Info);

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = staged.FilePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART"
            };
            Process.Start(start);
            Application.Current.Shutdown(0);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.Warning("Required update elevation was cancelled by the user.");
            MessageBox.Show(owner,
                "The update was not installed because the Windows administrator prompt was cancelled. This version is outdated and will now close. Reopen Sabby when you are ready to approve the update.",
                "Sabby Optimizer - Update Not Installed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not launch the staged Sabby update installer.", ex);
            MessageBox.Show(owner,
                $"The update was downloaded but its installer could not be launched.\n\n{ex.Message}",
                "Sabby Optimizer - Update Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    private string? CreatePreUpdateSafetySnapshot(string targetVersion)
    {
        try
        {
            var backupRoot = Path.Combine(_paths.AppDataDirectory, "UpdateBackups");
            Directory.CreateDirectory(backupRoot);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var safeVersion = string.Concat((targetVersion ?? "update").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            var zipPath = Path.Combine(backupRoot, $"before-{safeVersion}-{stamp}.zip");

            using var file = File.Create(zipPath);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false);

            if (Directory.Exists(_paths.UserDataDirectory))
            {
                foreach (var path in Directory.EnumerateFiles(_paths.UserDataDirectory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(path);
                        // Keep automatic update backups bounded. Large game/config payloads are not
                        // rewritten by the Sabby installer and are intentionally left in place.
                        if (info.Length > 64L * 1024 * 1024)
                            continue;

                        var relative = Path.GetRelativePath(_paths.UserDataDirectory, path).Replace('\\', '/');
                        archive.CreateEntryFromFile(path, relative, CompressionLevel.Optimal);
                    }
                    catch (IOException)
                    {
                        // A transiently locked nonessential file should not make the entire snapshot unusable.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Same policy as above; persistent core files are normally readable by this process.
                    }
                }
            }

            _logger.Info($"Pre-update user-data safety snapshot created: {zipPath}");
            return zipPath;
        }
        catch (Exception ex)
        {
            _logger.Error("Could not create the pre-update user-data safety snapshot.", ex);
            return null;
        }
    }

}
