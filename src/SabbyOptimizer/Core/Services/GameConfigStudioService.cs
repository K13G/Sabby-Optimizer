using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using PCTweaker.Models.GameProfiles;

namespace PCTweaker.Core.Services;

public sealed class GameConfigStudioService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ini", ".cfg", ".conf", ".json", ".xml", ".yaml", ".yml", ".toml", ".properties", ".prefs", ".txt"
    };

    private static readonly string[] StrongNames =
    [
        "config", "settings", "options", "preferences", "graphics", "video", "render", "engine", "input", "controls", "gameusersettings", "scalability", "user"
    ];

    private static readonly string[] IgnoreDirectoryNames =
    [
        "cache", "caches", "temp", "tmp", "logs", "crash", "crashes", "screenshots", "movies", "binaries", "redist", "redistributables", "node_modules", ".git"
    ];

    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly string _backupRoot;

    public GameConfigStudioService(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        _backupRoot = Path.Combine(paths.UserDataDirectory, "GameConfigBackups");
        Directory.CreateDirectory(_backupRoot);
    }

    public Task<IReadOnlyList<GameConfigFileInfo>> DiscoverAsync(
        GameProfileDefinition profile,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<GameConfigFileInfo>>(() => DiscoverCore(profile, progress, cancellationToken), cancellationToken);

    public async Task<GameConfigDocument> LoadAsync(GameConfigFileInfo file, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(file.FilePath))
            throw new FileNotFoundException("The config file no longer exists.", file.FilePath);

        var info = new FileInfo(file.FilePath);
        if (info.Length > 4 * 1024 * 1024)
            throw new InvalidOperationException("Sabby only edits text configuration files up to 4 MB to keep the editor responsive and safe.");

        var encoding = DetectEncoding(file.FilePath);
        var text = await File.ReadAllTextAsync(file.FilePath, encoding, cancellationToken).ConfigureAwait(false);
        var validation = ValidateText(file.Extension, text, out var error)
            ? "Structure check passed"
            : $"Structure warning: {error}";
        return new GameConfigDocument(file, text, encoding.WebName, validation);
    }

    public async Task<(bool Success, string Message, string BackupPath)> SaveAsync(
        Guid profileId,
        GameConfigFileInfo file,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(file.FilePath))
            return (false, "The config file no longer exists.", string.Empty);

        if (!ValidateText(file.Extension, text, out var validationError))
            return (false, $"Save blocked because the edited {file.Kind} structure is invalid: {validationError}", string.Empty);

        string backup;
        try
        {
            backup = await CreateBackupAsync(profileId, file.FilePath, "before-save", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (false, $"Save cancelled because Sabby could not create a backup first: {ex.Message}", string.Empty);
        }

        var encoding = DetectEncoding(file.FilePath);
        var temp = file.FilePath + ".sabby-write.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, text, encoding, cancellationToken).ConfigureAwait(false);
            File.Move(temp, file.FilePath, true);

            var readBack = await File.ReadAllTextAsync(file.FilePath, encoding, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(readBack, text, StringComparison.Ordinal))
                throw new IOException("Read-back verification did not match the editor contents.");

            return (true, $"Saved and verified • backup created at {backup}", backup);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                File.Copy(backup, file.FilePath, true);
            }
            catch { }
            _logger.Warning($"Config save failed for {file.FilePath}: {ex.Message}");
            return (false, $"Save failed and Sabby attempted to restore the pre-save backup: {ex.Message}", backup);
        }
    }

    public async Task<string> CreateBackupAsync(Guid profileId, string filePath, string reason, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("Config file not found.", filePath);
        var fileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(filePath))))[..16];
        var directory = Path.Combine(_backupRoot, profileId.ToString("N"), fileHash);
        Directory.CreateDirectory(directory);
        var safeReason = string.Concat(reason.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        var backup = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmssfff}_{safeReason}_{Path.GetFileName(filePath)}.bak");
        await using var source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, true);
        await using var destination = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        return backup;
    }

    public async Task<(bool Success, string Message)> RestoreLatestBackupAsync(Guid profileId, GameConfigFileInfo file, CancellationToken cancellationToken = default)
    {
        var fileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file.FilePath))))[..16];
        var directory = Path.Combine(_backupRoot, profileId.ToString("N"), fileHash);
        if (!Directory.Exists(directory))
            return (false, "No Sabby backup exists for this config file yet.");

        var latest = Directory.EnumerateFiles(directory, "*.bak", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (latest is null)
            return (false, "No Sabby backup exists for this config file yet.");

        try
        {
            if (File.Exists(file.FilePath))
                await CreateBackupAsync(profileId, file.FilePath, "before-restore", cancellationToken).ConfigureAwait(false);
            var temp = file.FilePath + ".sabby-restore.tmp";
            File.Copy(latest, temp, true);
            File.Move(temp, file.FilePath, true);
            return (true, $"Restored latest Sabby backup: {Path.GetFileName(latest)}");
        }
        catch (Exception ex)
        {
            return (false, $"Could not restore the backup: {ex.Message}");
        }
    }

    public void OpenContainingFolder(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        var args = File.Exists(filePath) ? $"/select,\"{filePath}\"" : $"\"{Path.GetDirectoryName(filePath)}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    public bool IsSupportedTextConfig(string path) =>
        !string.IsNullOrWhiteSpace(path) && SupportedExtensions.Contains(Path.GetExtension(path));

    public static bool ValidateText(string extension, string text, out string error)
    {
        error = string.Empty;
        try
        {
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                using (JsonDocument.Parse(text)) { }
            else if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
                _ = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private IReadOnlyList<GameConfigFileInfo> DiscoverCore(GameProfileDefinition profile, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var tokens = BuildTokens(profile);
        var candidates = new Dictionary<string, GameConfigFileInfo>(StringComparer.OrdinalIgnoreCase);
        var roots = BuildRoots(profile);
        var rootIndex = 0;

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rootIndex++;
            if (!Directory.Exists(root.Path))
            {
                progress?.Report(100d * rootIndex / roots.Count);
                continue;
            }

            if (root.IsInstallRoot)
                ScanTree(root.Path, root.Label, tokens, candidates, maxDepth: 4, requireTokenInPath: false, cancellationToken);
            else
            {
                foreach (var match in FindMatchingDirectories(root.Path, tokens, 3, cancellationToken))
                    ScanTree(match, root.Label, tokens, candidates, maxDepth: 4, requireTokenInPath: false, cancellationToken);
            }

            progress?.Report(100d * rootIndex / roots.Count);
        }

        return candidates.Values
            .OrderByDescending(x => x.RelevanceScore)
            .ThenByDescending(x => x.LastWriteTimeUtc)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(220)
            .ToArray();
    }

    private static List<(string Path, string Label, bool IsInstallRoot)> BuildRoots(GameProfileDefinition profile)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<(string, string, bool)>();
        void Add(string? path, string label, bool install = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { path = Path.GetFullPath(path); } catch { return; }
            if (roots.Any(x => x.Item1.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
            roots.Add((path, label, install));
        }

        Add(profile.InstallDirectory, "Game folder", true);
        Add(Path.Combine(docs, "My Games"), "Documents / My Games");
        Add(docs, "Documents");
        Add(Path.Combine(user, "Saved Games"), "Saved Games");
        Add(local, "AppData / Local");
        Add(roaming, "AppData / Roaming");
        Add(Path.Combine(user, "AppData", "LocalLow"), "AppData / LocalLow");
        return roots;
    }

    private static string[] BuildTokens(GameProfileDefinition profile)
    {
        var installName = string.Empty;
        try
        {
            if (!string.IsNullOrWhiteSpace(profile.InstallDirectory))
                installName = new DirectoryInfo(profile.InstallDirectory).Name;
        }
        catch { }
        var raw = new[] { profile.Name, Path.GetFileNameWithoutExtension(profile.ExecutablePath), installName };
        return raw
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(x => x!.Split([' ', '-', '_', '.', ':'], StringSplitOptions.RemoveEmptyEntries))
            .Select(NormalizeToken)
            .Where(x => x.Length >= 3 && x is not "game" and not "win64" and not "shipping" and not "content")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static IEnumerable<string> FindMatchingDirectories(string root, string[] tokens, int maxDepth, CancellationToken cancellationToken)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;
        while (queue.Count > 0 && visited < 3500)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, depth) = queue.Dequeue();
            visited++;
            if (depth > 0 && PathMatchesTokens(current, tokens)) yield return current;
            if (depth >= maxDepth) continue;
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(current); }
            catch { continue; }
            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (ShouldSkipDirectory(name)) continue;
                queue.Enqueue((dir, depth + 1));
            }
        }
    }

    private static void ScanTree(
        string root,
        string label,
        string[] tokens,
        Dictionary<string, GameConfigFileInfo> results,
        int maxDepth,
        bool requireTokenInPath,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;
        while (queue.Count > 0 && results.Count < 500 && visited < 2200)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, depth) = queue.Dequeue();
            visited++;
            if (requireTokenInPath && !PathMatchesTokens(current, tokens)) continue;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(current); }
            catch { files = Array.Empty<string>(); }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(file);
                if (!SupportedExtensions.Contains(ext)) continue;
                FileInfo info;
                try { info = new FileInfo(file); }
                catch { continue; }
                if (info.Length > 4 * 1024 * 1024) continue;

                var score = Score(file, root, tokens);
                if (score < 12) continue;
                var full = info.FullName;
                results[full] = new GameConfigFileInfo(full, info.Name, label, ext, info.Length, info.LastWriteTimeUtc, score);
            }

            if (depth >= maxDepth) continue;
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(current); }
            catch { continue; }
            foreach (var dir in dirs)
            {
                if (ShouldSkipDirectory(Path.GetFileName(dir))) continue;
                queue.Enqueue((dir, depth + 1));
            }
        }
    }

    private static int Score(string file, string root, string[] tokens)
    {
        var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        var path = file.ToLowerInvariant();
        var score = 10;
        if (StrongNames.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) score += 50;
        if (path.Contains("saved\\config", StringComparison.OrdinalIgnoreCase) || path.Contains("/saved/config", StringComparison.OrdinalIgnoreCase)) score += 45;
        if (path.Contains("settings", StringComparison.OrdinalIgnoreCase) || path.Contains("config", StringComparison.OrdinalIgnoreCase)) score += 24;
        if (PathMatchesTokens(file, tokens)) score += 28;
        if (Path.GetExtension(file).Equals(".ini", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(file).Equals(".cfg", StringComparison.OrdinalIgnoreCase)) score += 12;
        if (name.Contains("readme") || name.Contains("license") || name.Contains("changelog") || name.Contains("credits")) score -= 80;
        return score;
    }

    private static bool PathMatchesTokens(string path, string[] tokens)
    {
        var normalized = NormalizeToken(path);
        return tokens.Any(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeToken(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool ShouldSkipDirectory(string name) => IgnoreDirectoryNames.Any(x => name.Equals(x, StringComparison.OrdinalIgnoreCase));

    private static Encoding DetectEncoding(string path)
    {
        Span<byte> bom = stackalloc byte[4];
        try
        {
            using var stream = File.OpenRead(path);
            var read = stream.Read(bom);
            if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true);
            if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
            if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;
            if (read >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00) return Encoding.UTF32;
        }
        catch { }
        return new UTF8Encoding(false);
    }
}
