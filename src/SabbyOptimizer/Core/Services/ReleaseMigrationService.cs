using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace PCTweaker.Core.Services;

/// <summary>
/// Phase 22 release migration. It snapshots the small, migration-sensitive parts of Sabby's
/// persistent user data before a version/schema transition and records the version that last
/// completed migration successfully. Large game-config backup payloads are deliberately left
/// untouched rather than copied on every upgrade.
/// </summary>
public sealed class ReleaseMigrationService
{
    private sealed class ReleaseState
    {
        public string Version { get; set; } = string.Empty;
        public int SettingsSchema { get; set; }
        public DateTimeOffset MigratedAtUtc { get; set; }
    }

    private static readonly string[] CriticalDirectories =
    [
        "Presets",
        "GameProfiles",
        "Extensions",
        "TweakState",
        "DriverState",
        "Maintenance",
        "Benchmarks"
    ];

    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly string _stateFile;
    private readonly string _backupDirectory;

    public ReleaseMigrationService(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        _stateFile = Path.Combine(paths.UserDataDirectory, "release-state.json");
        _backupDirectory = Path.Combine(paths.AppDataDirectory, "MigrationBackups");
    }

    public async Task RunAsync(ISettingsService settings, CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();
        var previous = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        var migrationNeeded = previous is null ||
                              !string.Equals(previous.Version, currentVersion, StringComparison.OrdinalIgnoreCase) ||
                              previous.SettingsSchema != settings.Current.SchemaVersion;
        if (!migrationNeeded)
            return;

        Directory.CreateDirectory(_backupDirectory);
        var sourceLabel = string.IsNullOrWhiteSpace(previous?.Version) ? "legacy" : previous!.Version;
        var backup = CreateCriticalMigrationBackup(sourceLabel, currentVersion);
        if (!string.IsNullOrWhiteSpace(backup))
            _logger.Info($"Phase 22 migration backup created: {backup}");

        // Normalize/save through the real settings service so schema defaults are applied using
        // the same atomic-write path as normal application settings.
        settings.Current.Normalize();
        await settings.SaveAsync(cancellationToken).ConfigureAwait(false);

        var completed = new ReleaseState
        {
            Version = currentVersion,
            SettingsSchema = settings.Current.SchemaVersion,
            MigratedAtUtc = DateTimeOffset.UtcNow
        };
        var temporary = _stateFile + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(completed, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, _stateFile, true);
        _logger.Info($"Release migration completed: {sourceLabel} -> {currentVersion}; settings schema {settings.Current.SchemaVersion}.");
    }

    private string? CreateCriticalMigrationBackup(string fromVersion, string toVersion)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var safeFrom = Sanitize(fromVersion);
            var safeTo = Sanitize(toVersion);
            var zipPath = Path.Combine(_backupDirectory, $"SabbyOptimizer-{safeFrom}-to-{safeTo}-{stamp}.zip");
            using var file = File.Create(zipPath);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false);

            AddFileIfPresent(archive, _paths.SettingsFile, "settings.json");
            AddFileIfPresent(archive, _stateFile, "release-state.json");

            foreach (var directoryName in CriticalDirectories)
            {
                var directory = Path.Combine(_paths.UserDataDirectory, directoryName);
                AddDirectory(archive, directory, directoryName);
            }

            return zipPath;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Release migration backup could not be created; existing user data was left untouched. {ex.Message}");
            return null;
        }
    }

    private static void AddDirectory(ZipArchive archive, string directory, string archiveRoot)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(path);
                // Migration state should remain lightweight; large game/config payloads are not
                // rewritten by schema migration and therefore do not need to be duplicated here.
                if (info.Length > 32L * 1024 * 1024)
                    continue;
                var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
                AddFileIfPresent(archive, path, $"{archiveRoot}/{relative}");
            }
            catch
            {
                // A locked/nonessential file should never block application startup.
            }
        }
    }

    private static void AddFileIfPresent(ZipArchive archive, string file, string entryName)
    {
        if (!File.Exists(file))
            return;
        archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
    }

    private async Task<ReleaseState?> ReadStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_stateFile))
                return null;
            await using var stream = File.OpenRead(_stateFile);
            return await JsonSerializer.DeserializeAsync<ReleaseState>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Release migration state could not be read; Sabby will perform a protected migration pass. {ex.Message}");
            return null;
        }
    }

    private static string GetCurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.23.0";

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
