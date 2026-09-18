using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class GameScanService : IGameScanService
{
    private readonly IAppLogger _logger;

    private static readonly string[] ExcludedExecutableTokens =
    {
        "unins", "uninstall", "setup", "installer", "installhelper", "cleanup", "touchup", "activation",
        "crash", "report", "helper", "updater", "update", "patcher", "repair", "redist", "vc_redist",
        "dxsetup", "unitycrashhandler", "launcherpatcher", "easyanticheat", "battleye", "cefprocess",
        "chromium", "bootstrapper", "prereq", "diagnostic", "diagnostics", "telemetry", "service",
        "server", "crosshair", "overlay", "tray", "webview", "benchmark", "editor", "configurator",
        "launcher", "maintenance", "crashhandler", "redistributable", "prerequisite", "autoupdate",
        "updateclient", "installercleanup", "installertouchup", "activationui", "coreserver", "utility",
        "idler", "steamworks", "commonredist", "redistributable", "steamvr", "vrserver", "vrmonitor",
        "vrcompositor", "tutorial", "dedicated", "modmanager", "mod_manager", "configtool", "crashpad"
    };

    private static readonly string[] ExcludedPathTokens =
    {
        "\\__installer\\", "\\installer\\", "\\installers\\", "\\_commonredist\\",
        "\\redistributable", "\\prereq", "\\support\\", "\\crashreport", "\\engine\\binaries\\thirdparty\\"
    };

    private static readonly HashSet<string> ExcludedSteamAppIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "228980",   // Steamworks Common Redistributables
        "250820",   // SteamVR platform/runtime
        "1070560",  // Steam Linux Runtime
        "1391110",  // Steam Linux Runtime - Soldier
        "1628350"   // Steam Linux Runtime - Sniper
    };

    public GameScanService(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<DetectedGame>> ScanAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    private IReadOnlyList<DetectedGame> Scan(IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var results = new List<DetectedGame>();
        progress?.Report(2);

        ScanEpic(results, cancellationToken); progress?.Report(10);
        ScanSteam(results, cancellationToken); progress?.Report(24);
        ScanRoblox(results, cancellationToken); progress?.Report(31);
        ScanKnownStandaloneGames(results, cancellationToken); progress?.Report(38);
        ScanGogRegistry(results, cancellationToken); progress?.Report(45);
        ScanDeepGameRoots(results, progress, cancellationToken);

        var deduped = results
            .Where(game => File.Exists(game.ExecutablePath))
            .GroupBy(game => SafeFullPath(game.ExecutablePath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => SourcePriority(item.Platform))
                .First())
            .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        progress?.Report(100);
        _logger.Info($"Deep game scan completed with {deduped.Count} executable(s) found.");
        return deduped;
    }

    private void ScanEpic(List<DetectedGame> results, CancellationToken cancellationToken)
    {
        try
        {
            var manifests = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (!Directory.Exists(manifests))
                return;

            foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(file));
                    var root = json.RootElement;
                    var name = GetString(root, "DisplayName");
                    var install = GetString(root, "InstallLocation");
                    var launch = GetString(root, "LaunchExecutable");

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(install) || string.IsNullOrWhiteSpace(launch) || IsObviousNonGameTitle(name))
                        continue;

                    var executable = ResolveEpicGameExecutable(name, install, launch, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
                        results.Add(new DetectedGame(name, "Epic Games", executable, install, Path.GetFileNameWithoutExtension(file)));
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Skipped an Epic manifest that could not be read: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Epic Games scan failed: {ex.Message}");
        }
    }

    private static string? ResolveEpicGameExecutable(string name, string install, string launch, CancellationToken cancellationToken)
    {
        // Epic manifests can point at a launcher/bootstrapper instead of the long-running game
        // process. Prefer the actual gameplay executable so per-game Windows settings attach to
        // the process that renders the game.
        if (name.Contains("Fortnite", StringComparison.OrdinalIgnoreCase))
        {
            var fortnite = Path.Combine(install, "FortniteGame", "Binaries", "Win64", "FortniteClient-Win64-Shipping.exe");
            if (File.Exists(fortnite))
                return fortnite;
        }

        var manifestExecutable = Path.IsPathRooted(launch) ? launch : Path.Combine(install, launch);
        var manifestName = Path.GetFileNameWithoutExtension(manifestExecutable);
        var manifestLooksLikeBootstrapper =
            ExcludedExecutableTokens.Any(token => manifestName.Contains(token, StringComparison.OrdinalIgnoreCase));

        if (!manifestLooksLikeBootstrapper && File.Exists(manifestExecutable))
            return manifestExecutable;

        var discovered = FindLikelyGameExecutable(install, name, cancellationToken, acceptManifestSource: true);
        return discovered ?? (File.Exists(manifestExecutable) ? manifestExecutable : null);
    }

    private void ScanSteam(List<DetectedGame> results, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var root in GetSteamLibraryRoots())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var steamApps = Path.Combine(root, "steamapps");
                if (!Directory.Exists(steamApps))
                    continue;

                foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var text = File.ReadAllText(manifest);
                        var appId = Path.GetFileNameWithoutExtension(manifest).Replace("appmanifest_", string.Empty, StringComparison.OrdinalIgnoreCase);
                        var name = ReadVdfValue(text, "name");
                        var installDir = ReadVdfValue(text, "installdir");
                        if (ExcludedSteamAppIds.Contains(appId) ||
                            string.IsNullOrWhiteSpace(name) ||
                            string.IsNullOrWhiteSpace(installDir) ||
                            IsObviousNonGameTitle(name))
                            continue;

                        var directory = Path.Combine(steamApps, "common", installDir);
                        if (!Directory.Exists(directory))
                            continue;

                        var executable = FindLikelyGameExecutable(directory, name, cancellationToken, acceptManifestSource: true);
                        if (executable is not null)
                        {
                            results.Add(new DetectedGame(name, "Steam", executable, directory, appId));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Skipped a Steam manifest that could not be read: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Steam scan failed: {ex.Message}");
        }
    }

    private void ScanRoblox(List<DetectedGame> results, CancellationToken cancellationToken)
    {
        try
        {
            var versions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
            if (!Directory.Exists(versions))
                return;

            cancellationToken.ThrowIfCancellationRequested();
            var latest = Directory.EnumerateDirectories(versions)
                .Select(path => Path.Combine(path, "RobloxPlayerBeta.exe"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (latest is not null)
                results.Add(new DetectedGame("Roblox", "Roblox", latest, Path.GetDirectoryName(latest) ?? versions, "roblox-player"));
        }
        catch (Exception ex)
        {
            _logger.Warning($"Roblox scan failed: {ex.Message}");
        }
    }

    private void ScanKnownStandaloneGames(List<DetectedGame> results, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = new[]
        {
            ("Minecraft Launcher", "Minecraft", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Minecraft Launcher", "MinecraftLauncher.exe")),
            ("Minecraft Launcher", "Minecraft", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Minecraft Launcher", "MinecraftLauncher.exe"))
        };

        foreach (var (name, platform, executable) in candidates)
        {
            if (File.Exists(executable))
                results.Add(new DetectedGame(name, platform, executable, Path.GetDirectoryName(executable) ?? string.Empty, "minecraft-launcher"));
        }
    }

    private void ScanGogRegistry(List<DetectedGame> results, CancellationToken cancellationToken)
    {
        var registryPaths = new[]
        {
            @"SOFTWARE\GOG.com\Games",
            @"SOFTWARE\WOW6432Node\GOG.com\Games"
        };

        foreach (var registryPath in registryPaths)
        {
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(registryPath);
                if (root is null)
                    continue;

                foreach (var subName in root.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var gameKey = root.OpenSubKey(subName);
                    if (gameKey is null)
                        continue;

                    var name = gameKey.GetValue("gameName") as string ?? gameKey.GetValue("name") as string;
                    var install = gameKey.GetValue("path") as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(install) || !Directory.Exists(install) || IsObviousNonGameTitle(name))
                        continue;

                    var executable = FindLikelyGameExecutable(install, name, cancellationToken, acceptManifestSource: true);
                    if (executable is not null)
                        results.Add(new DetectedGame(name, "GOG", executable, install, subName));
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"GOG registry scan skipped: {ex.Message}");
            }
        }
    }

    private void ScanDeepGameRoots(List<DetectedGame> results, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

        var roots = new List<(string Path, string Source, int Depth, int MaxExecutables)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documents", 6, 1800),
            (Path.Combine(user, "Games"), "User Games", 6, 1500),
            (Path.Combine(user, "Desktop"), "Desktop", 4, 500),
            (Path.Combine(roaming, "itch", "apps"), "itch.io", 7, 2200),
            (Path.Combine(local, "itch", "apps"), "itch.io", 7, 2200),
            (Path.Combine(systemDrive, "Games"), "Games Folder", 6, 2200),
            (Path.Combine(systemDrive, "XboxGames"), "Xbox", 6, 2200),
            (Path.Combine(systemDrive, "GOG Games"), "GOG", 6, 1800),
            (Path.Combine(systemDrive, "Riot Games"), "Riot", 6, 1200),
            (Path.Combine(programFiles, "Riot Games"), "Riot", 6, 1200),
            (Path.Combine(programFiles, "EA Games"), "EA", 6, 1800),
            (Path.Combine(programFilesX86, "Ubisoft", "Ubisoft Game Launcher", "games"), "Ubisoft", 6, 1800)
        };

        // Search conventional game folders on every mounted fixed drive without crawling entire drives.
        // This catches standalone/itch/custom games on D:, E:, etc. while keeping the scan bounded.
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
            {
                var basePath = drive.RootDirectory.FullName;
                roots.Add((Path.Combine(basePath, "Games"), "Games Folder", 7, 2400));
                roots.Add((Path.Combine(basePath, "XboxGames"), "Xbox", 7, 2400));
                roots.Add((Path.Combine(basePath, "GOG Games"), "GOG", 7, 2000));
                roots.Add((Path.Combine(basePath, "Riot Games"), "Riot", 7, 1600));
                roots.Add((Path.Combine(basePath, "itch"), "itch.io", 7, 2200));
                roots.Add((Path.Combine(basePath, "Itch Games"), "itch.io", 7, 2200));
            }
        }
        catch
        {
            // Drive enumeration is best-effort.
        }

        var scanRoots = roots
            .Where(item => !string.IsNullOrWhiteSpace(item.Path) && Directory.Exists(item.Path))
            .GroupBy(item => SafeFullPath(item.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        // Independent roots can be inspected concurrently. Keep the degree deliberately low so
        // scanning is faster without turning it into a disk-thrashing full-drive crawler.
        var completedRoots = 0;
        var totalRoots = Math.Max(1, scanRoots.Length);
        Parallel.ForEach(
            scanRoots,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 4, 8)
            },
            root =>
            {
                var localResults = new List<DetectedGame>();
                ScanDirectoryTree(localResults, root.Path, root.Source, root.Depth, root.MaxExecutables, cancellationToken);
                if (localResults.Count > 0)
                {
                    lock (results)
                        results.AddRange(localResults);
                }
                var done = Interlocked.Increment(ref completedRoots);
                progress?.Report(45 + (int)Math.Round(50d * done / totalRoots));
            });
    }

    private void ScanDirectoryTree(
        List<DetectedGame> results,
        string root,
        string source,
        int maxDepth,
        int maxExecutables,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((root, 0));
        var executableCount = 0;
        var visitedDirectories = 0;
        const int maxDirectories = 6000;

        while (queue.Count > 0 && executableCount < maxExecutables && visitedDirectories < maxDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = queue.Dequeue();
            visitedDirectories++;

            try
            {
                foreach (var exe in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    executableCount++;
                    if (executableCount > maxExecutables)
                        break;

                    if (!LooksLikeGameExecutable(exe, source, out var displayName))
                        continue;

                    results.Add(new DetectedGame(
                        displayName,
                        source,
                        exe,
                        Path.GetDirectoryName(exe) ?? directory,
                        SafeFullPath(exe)));
                }
            }
            catch
            {
                // Access denied and transient folders are normal during a deep scan.
            }

            if (depth >= maxDepth)
                continue;

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ShouldSkipDirectory(child))
                        continue;
                    queue.Enqueue((child, depth + 1));
                }
            }
            catch
            {
                // Continue with the rest of the scan tree.
            }
        }
    }

    private static bool LooksLikeGameExecutable(string executable, string source, out string displayName)
    {
        displayName = Path.GetFileNameWithoutExtension(executable);
        var fileName = displayName.ToLowerInvariant();
        var normalizedPath = executable.Replace('/', '\\').ToLowerInvariant();

        if (ExcludedExecutableTokens.Any(token => fileName.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
            ExcludedPathTokens.Any(token => normalizedPath.Contains(token, StringComparison.OrdinalIgnoreCase)))
            return false;

        long fileSize;
        try { fileSize = new FileInfo(executable).Length; }
        catch { return false; }
        if (fileSize < 128 * 1024)
            return false;

        var directory = Path.GetDirectoryName(executable) ?? string.Empty;
        var score = 0;
        var hasStrongGameMarker = false;

        if (source is "itch.io" or "Xbox" or "Riot" or "EA" or "Ubisoft" or "GOG")
            score += 2;

        try
        {
            if (File.Exists(Path.Combine(directory, "UnityPlayer.dll"))) { score += 9; hasStrongGameMarker = true; }
            if (Directory.EnumerateDirectories(directory, "*_Data", SearchOption.TopDirectoryOnly).Any()) { score += 8; hasStrongGameMarker = true; }
            if (File.Exists(Path.Combine(directory, Path.GetFileNameWithoutExtension(executable) + ".pck"))) { score += 9; hasStrongGameMarker = true; }
            if (File.Exists(Path.Combine(directory, "data.win"))) { score += 9; hasStrongGameMarker = true; }
            if (File.Exists(Path.Combine(directory, "GameAssembly.dll"))) { score += 7; hasStrongGameMarker = true; }
            if (File.Exists(Path.Combine(directory, "steam_api64.dll")) || File.Exists(Path.Combine(directory, "steam_api.dll"))) { score += 5; hasStrongGameMarker = true; }
            if (executable.Contains("\\Binaries\\Win64\\", StringComparison.OrdinalIgnoreCase)) { score += 7; hasStrongGameMarker = true; }
            if (Directory.Exists(Path.Combine(directory, "Content")) && Directory.Exists(Path.Combine(directory, "Binaries"))) { score += 6; hasStrongGameMarker = true; }

            // Electron/Chromium desktop utilities commonly have these markers. Do not treat them
            // as games unless an independent game-engine marker was also found.
            var looksElectron = File.Exists(Path.Combine(directory, "chrome_elf.dll")) ||
                                File.Exists(Path.Combine(directory, "resources", "app.asar"));
            if (looksElectron && !hasStrongGameMarker)
                score -= 8;
        }
        catch { }

        if (fileSize >= 3 * 1024 * 1024) score += 1;
        if (fileSize >= 25 * 1024 * 1024) score += 2;

        try
        {
            var version = FileVersionInfo.GetVersionInfo(executable);
            var product = CleanVersionName(version.ProductName);
            var description = CleanVersionName(version.FileDescription);
            var company = CleanVersionName(version.CompanyName);
            var originalName = CleanVersionName(version.OriginalFilename);
            var internalName = CleanVersionName(version.InternalName);

            if (!string.IsNullOrWhiteSpace(product))
            {
                displayName = product;
                score += 2;
            }
            else if (!string.IsNullOrWhiteSpace(description))
            {
                displayName = description;
                score += 1;
            }

            // Product/description/company/original-name metadata is embedded in the executable's
            // VERSIONINFO resource. Treat it as supporting evidence, not proof: utility keywords
            // can immediately outweigh a generic product name.
            var combined = $"{product} {description} {company} {originalName} {internalName}".ToLowerInvariant();
            if (combined.Contains("game")) score += 2;
            if (combined.Contains("shipping")) score += 2;
            if (ExcludedExecutableTokens.Any(token => combined.Contains(token, StringComparison.OrdinalIgnoreCase))) score -= 14;
            if ((combined.Contains("utility") || combined.Contains("desktop app") || combined.Contains("overlay")) && !hasStrongGameMarker) score -= 10;
        }
        catch { }

        displayName = CleanDisplayName(displayName, executable);

        if (source is "Documents" or "Desktop" or "User Games" or "Games Folder")
            return score >= 7;

        return score >= 5;
    }

    private static bool IsObviousNonGameTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        var value = title.ToLowerInvariant();
        var blocked = new[]
        {
            "crosshair", "wallpaper", "benchmark", "installer", "updater", "update tool", "activation",
            "server", "overlay", "editor", "sdk", "dedicated server", "configuration tool", "launcher",
            "steamworks common redistributables", "steamvr", "steam game idler", "idler", "redistributable",
            "runtime", "workshop tool", "mod manager", "crash reporter", "tutorial"
        };
        return blocked.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldSkipDirectory(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var skip = new[]
        {
            ".git", ".vs", "node_modules", "packages", "obj", "bin", "redist", "redistributables",
            "crashdumps", "logs", "cache", "temp", "tmp", "$recycle.bin", "__installer", "installer",
            "installers", "support", "prereq", "prerequisites", "_commonredist", "crashreportclient"
        };

        return skip.Any(item => name.Equals(item, StringComparison.OrdinalIgnoreCase));
    }

    private HashSet<string> GetSteamLibraryRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string steamPath && Directory.Exists(steamPath))
                roots.Add(steamPath.Replace('/', Path.DirectorySeparatorChar));
        }
        catch { }

        var defaultSteam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (Directory.Exists(defaultSteam))
            roots.Add(defaultSteam);

        foreach (var steamRoot in roots.ToArray())
        {
            try
            {
                var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryFile))
                    continue;

                var text = File.ReadAllText(libraryFile);
                foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                {
                    var path = match.Groups["path"].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(path))
                        roots.Add(path);
                }
            }
            catch { }
        }

        return roots;
    }

    private static string? FindLikelyGameExecutable(string directory, string gameName, CancellationToken cancellationToken, bool acceptManifestSource)
    {
        var tokens = Regex.Matches(gameName.ToLowerInvariant(), "[a-z0-9]+")
            .Cast<Match>()
            .Select(match => match.Value)
            .Where(token => token.Length >= 3)
            .ToArray();

        var candidates = new List<(string Path, int Score)>();
        var count = 0;
        try
        {
            foreach (var exe in EnumerateFilesSafe(directory, "*.exe", 6))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > 400) break;

                var fileName = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                var normalizedPath = exe.Replace('/', '\\').ToLowerInvariant();
                if (ExcludedExecutableTokens.Any(value => fileName.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
                    ExcludedPathTokens.Any(value => normalizedPath.Contains(value, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var score = acceptManifestSource ? 2 : 0;
                var strongMarker = false;
                foreach (var token in tokens)
                    if (fileName.Contains(token, StringComparison.OrdinalIgnoreCase)) score += 6;

                if (exe.Contains("\\Binaries\\Win64\\", StringComparison.OrdinalIgnoreCase) ||
                    exe.Contains("\\Win64\\", StringComparison.OrdinalIgnoreCase) ||
                    exe.Contains("\\x64\\", StringComparison.OrdinalIgnoreCase)) score += 3;

                var exeDirectory = Path.GetDirectoryName(exe) ?? directory;
                try
                {
                    if (File.Exists(Path.Combine(exeDirectory, "UnityPlayer.dll"))) { score += 8; strongMarker = true; }
                    if (File.Exists(Path.Combine(exeDirectory, "GameAssembly.dll"))) { score += 6; strongMarker = true; }
                    if (File.Exists(Path.Combine(exeDirectory, "steam_api64.dll")) || File.Exists(Path.Combine(exeDirectory, "steam_api.dll"))) { score += 4; strongMarker = true; }
                    if (exe.Contains("\\Binaries\\Win64\\", StringComparison.OrdinalIgnoreCase)) { score += 6; strongMarker = true; }
                    if (File.Exists(Path.Combine(exeDirectory, "resources", "app.asar")) && !File.Exists(Path.Combine(exeDirectory, "UnityPlayer.dll"))) score -= 8;
                }
                catch { }

                if (fileName.Contains("launcher", StringComparison.OrdinalIgnoreCase)) score -= 5;
                try
                {
                    var size = new FileInfo(exe).Length;
                    if (size >= 3 * 1024 * 1024) score += 1;
                    if (size >= 25 * 1024 * 1024) score += 2;

                    var version = FileVersionInfo.GetVersionInfo(exe);
                    var metadata = $"{version.ProductName} {version.FileDescription} {version.OriginalFilename} {version.InternalName}".ToLowerInvariant();
                    if (ExcludedExecutableTokens.Any(token => metadata.Contains(token, StringComparison.OrdinalIgnoreCase))) score -= 14;
                    if ((metadata.Contains("utility") || metadata.Contains("overlay") || metadata.Contains("installer")) && !strongMarker) score -= 10;
                    if (metadata.Contains("game")) score += 2;
                }
                catch { }

                candidates.Add((exe, score));
            }
        }
        catch { }

        return candidates
            .Where(candidate => candidate.Score >= (acceptManifestSource ? 8 : 7))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path.Length)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, int maxDepth)
    {
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (directory, depth) = queue.Dequeue();
            IEnumerable<string> files = Array.Empty<string>();
            try { files = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly); }
            catch { }
            foreach (var file in files) yield return file;
            if (depth >= maxDepth) continue;
            IEnumerable<string> directories = Array.Empty<string>();
            try { directories = Directory.EnumerateDirectories(directory); }
            catch { }
            foreach (var child in directories)
                if (!ShouldSkipDirectory(child)) queue.Enqueue((child, depth + 1));
        }
    }

    private static string? ReadVdfValue(string text, string key)
    {
        var match = Regex.Match(text, $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"(?<value>[^\\\"]*)\\\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string CleanVersionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = value.Trim();
        if (cleaned.Equals("Application", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("Windows Application", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return cleaned;
    }

    private static string CleanDisplayName(string value, string executable)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? Path.GetFileNameWithoutExtension(executable) : value.Trim();
        foreach (var suffix in new[]
        {
            "-WinGDK-Shipping", "-Win64-Shipping", "-Win32-Shipping", "-Shipping",
            " Shipping", " WinGDK", " Win64", " Win32", " x64", " Application"
        })
        {
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[..^suffix.Length].Trim();
        }
        return string.IsNullOrWhiteSpace(cleaned) ? Path.GetFileNameWithoutExtension(executable) : cleaned;
    }

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    private static int SourcePriority(string platform) => platform switch
    {
        "Steam" => 10,
        "Epic Games" => 9,
        "GOG" => 8,
        "Xbox" => 8,
        "EA" => 7,
        "Ubisoft" => 7,
        "Riot" => 7,
        "itch.io" => 6,
        "Roblox" => 6,
        "Minecraft" => 6,
        _ => 1
    };
}
