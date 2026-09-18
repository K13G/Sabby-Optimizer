using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models;
using PCTweaker.Models.GameProfiles;

namespace PCTweaker.Core.GameProfiles;

public sealed class GameProfileService : IGameProfileService
{
    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public GameProfileService(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        Directory.CreateDirectory(_paths.GameProfilesDirectory);
    }

    public async Task<IReadOnlyList<GameProfileDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var removed = document.Profiles.RemoveAll(profile => profile.Platform != "Manual" && IsObviousNonGameProfile(profile));
            if (removed > 0)
            {
                document.ModifiedAtUtc = DateTime.UtcNow;
                await WriteUnsafeAsync(document, cancellationToken).ConfigureAwait(false);
                _logger.Info($"Removed {removed} obvious non-game profile(s) created by an older scanner.");
            }

            return document.Profiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.ExecutablePath))
                .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<GameScanLibraryInfo> GetLibraryInfoAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return new GameScanLibraryInfo(document.HasCompletedScan, document.LastScanAtUtc, document.Profiles.Count);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<GameProfileDefinition>> MergeScanResultsAsync(
        IEnumerable<DetectedGame> detectedGames,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var byPath = document.Profiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.ExecutablePath))
                .GroupBy(profile => NormalizePath(profile.ExecutablePath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var now = DateTime.UtcNow;
            foreach (var detected in detectedGames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = NormalizePath(detected.ExecutablePath);
                if (string.IsNullOrWhiteSpace(path)) continue;

                if (byPath.TryGetValue(path, out var existing))
                {
                    existing.LastSeenUtc = now;
                    existing.Name = detected.Name;
                    existing.Platform = detected.Platform;
                    existing.InstallDirectory = detected.InstallDirectory;
                    if (string.IsNullOrWhiteSpace(existing.SourceId)) existing.SourceId = detected.SourceId;
                    continue;
                }

                // Launcher manifests can change from bootstrapper -> real game executable between
                // scanner versions. Match the stable source id before creating a duplicate profile.
                existing = document.Profiles.FirstOrDefault(profile =>
                    !string.IsNullOrWhiteSpace(detected.SourceId) &&
                    profile.SourceId.Equals(detected.SourceId, StringComparison.OrdinalIgnoreCase) &&
                    profile.Platform.Equals(detected.Platform, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    existing.LastSeenUtc = now;
                    existing.Name = detected.Name;
                    existing.Platform = detected.Platform;
                    existing.ExecutablePath = detected.ExecutablePath;
                    existing.InstallDirectory = detected.InstallDirectory;
                    byPath[path] = existing;
                    continue;
                }

                var profile = new GameProfileDefinition
                {
                    Name = detected.Name,
                    Platform = detected.Platform,
                    ExecutablePath = detected.ExecutablePath,
                    InstallDirectory = detected.InstallDirectory,
                    SourceId = detected.SourceId,
                    AddedAtUtc = now,
                    LastSeenUtc = now,
                    Enabled = true
                };
                document.Profiles.Add(profile);
                byPath[path] = profile;
            }

            // Clean obvious utility/helper false positives left by older scanner versions.
            document.Profiles.RemoveAll(profile => profile.Platform != "Manual" && IsObviousNonGameProfile(profile));

            document.SchemaVersion = 2;
            document.ModifiedAtUtc = now;
            document.HasCompletedScan = true;
            document.LastScanAtUtc = now;
            await WriteUnsafeAsync(document, cancellationToken).ConfigureAwait(false);
            _logger.Info($"Game profile library merged. {document.Profiles.Count} profile(s) stored.");

            return document.Profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAllAsync(IEnumerable<GameProfileDefinition> profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            document.SchemaVersion = 2;
            document.ModifiedAtUtc = DateTime.UtcNow;
            document.Profiles = profiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.ExecutablePath))
                .GroupBy(profile => NormalizePath(profile.ExecutablePath), StringComparer.OrdinalIgnoreCase)
                .Select(group => Normalize(group.First()))
                .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await WriteUnsafeAsync(document, cancellationToken).ConfigureAwait(false);
            _logger.Info($"Game profiles saved: {document.Profiles.Count} profile(s).");
        }
        finally { _gate.Release(); }
    }

    private async Task<GameProfilesDocument> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.GameProfilesFile)) return new GameProfilesDocument();

        try
        {
            await using var stream = new FileStream(_paths.GameProfilesFile, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, true);
            var document = await JsonSerializer.DeserializeAsync<GameProfilesDocument>(stream, _json, cancellationToken).ConfigureAwait(false)
                ?? new GameProfilesDocument();
            document.SchemaVersion = 2;
            document.Profiles ??= new List<GameProfileDefinition>();

            var repairedPaths = 0;
            var normalized = new List<GameProfileDefinition>(document.Profiles.Count);
            foreach (var profile in document.Profiles)
            {
                var before = profile.ExecutablePath;
                var item = Normalize(profile);
                if (!string.Equals(before, item.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    repairedPaths++;
                normalized.Add(item);
            }
            document.Profiles = normalized;

            if (repairedPaths > 0)
            {
                document.ModifiedAtUtc = DateTime.UtcNow;
                await WriteUnsafeAsync(document, cancellationToken).ConfigureAwait(false);
                _logger.Info($"Repaired {repairedPaths} saved game executable path(s) to the real gameplay process.");
            }

            return document;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Game profile library could not be read and will be rebuilt: {ex.Message}");
            return new GameProfilesDocument();
        }
    }

    private async Task WriteUnsafeAsync(GameProfilesDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.GameProfilesDirectory);
        var temp = _paths.GameProfilesFile + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 32 * 1024, true))
        {
            await JsonSerializer.SerializeAsync(stream, document, _json, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(temp, _paths.GameProfilesFile, true);
    }

    private static GameProfileDefinition Normalize(GameProfileDefinition profile)
    {
        profile.SchemaVersion = 2;
        profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "Game" : profile.Name.Trim();
        profile.Platform = string.IsNullOrWhiteSpace(profile.Platform) ? "Unknown" : profile.Platform.Trim();
        profile.ExecutablePath = profile.ExecutablePath?.Trim() ?? string.Empty;
        profile.InstallDirectory = profile.InstallDirectory?.Trim() ?? string.Empty;
        profile.SourceId = profile.SourceId?.Trim() ?? string.Empty;
        profile.Notes = profile.Notes?.Trim() ?? string.Empty;
        profile.GraphicsPreference = string.IsNullOrWhiteSpace(profile.GraphicsPreference) ? "System default" : profile.GraphicsPreference.Trim();
        profile.ProcessPriority = string.IsNullOrWhiteSpace(profile.ProcessPriority) ? "Normal" : profile.ProcessPriority.Trim();
        RepairKnownExecutablePath(profile);
        return profile;
    }

    private static void RepairKnownExecutablePath(GameProfileDefinition profile)
    {
        if (!profile.Name.Contains("Fortnite", StringComparison.OrdinalIgnoreCase) &&
            !profile.ExecutablePath.Contains("Fortnite", StringComparison.OrdinalIgnoreCase))
            return;

        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.InstallDirectory))
            roots.Add(profile.InstallDirectory);

        if (!string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            try
            {
                var full = Path.GetFullPath(profile.ExecutablePath);
                var marker = $"{Path.DirectorySeparatorChar}FortniteGame{Path.DirectorySeparatorChar}";
                var index = full.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                    roots.Add(full[..index]);
            }
            catch { }
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(root, "FortniteGame", "Binaries", "Win64", "FortniteClient-Win64-Shipping.exe");
            if (!File.Exists(candidate))
                continue;

            profile.ExecutablePath = candidate;
            profile.InstallDirectory = root;
            return;
        }
    }

    private static bool IsObviousNonGameProfile(GameProfileDefinition profile)
    {
        var combined = $"{profile.Name} {Path.GetFileNameWithoutExtension(profile.ExecutablePath)}".ToLowerInvariant();
        var blocked = new[]
        {
            "crosshair", "installer", "cleanup", "touchup", "activation", "updater", "update helper",
            "core server", "server application", "uninstall", "setup", "crash reporter", "overlay",
            "launcher", "benchmark", "utility", "editor", "configuration tool", "steam game idler",
            "idler", "steamworks common redistributables", "steamvr", "redistributable", "runtime",
            "tutorial", "workshop tool", "mod manager", "dedicated server"
        };
        return blocked.Any(token => combined.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return Path.GetFullPath(path.Trim()); }
        catch { return path.Trim(); }
    }
}
