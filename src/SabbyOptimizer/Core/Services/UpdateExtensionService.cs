using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCTweaker.Core.Tweaks;
using PCTweaker.Core.Tweaks.Handlers;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class UpdateExtensionService : IUpdateExtensionService
{
    private sealed class ReleaseManifest
    {
        public string Version { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public string? Sha256 { get; set; }
        public string? SignerThumbprint { get; set; }
        public string? ReleaseNotes { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public bool Mandatory { get; set; }
    }

    private static readonly Regex SafeIdRegex = new("^[a-zA-Z0-9._-]{1,80}$", RegexOptions.Compiled);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;

    public string ExtensionsDirectory { get; }
    public string StagedUpdatesDirectory { get; }

    public UpdateExtensionService(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        ExtensionsDirectory = Path.Combine(paths.UserDataDirectory, "Extensions");
        StagedUpdatesDirectory = Path.Combine(paths.UserDataDirectory, "Updates", "Staged");
        Directory.CreateDirectory(ExtensionsDirectory);
        Directory.CreateDirectory(StagedUpdatesDirectory);
    }

    public IReadOnlyList<ITweakHandler> LoadEnabledHandlers()
    {
        var handlers = new List<ITweakHandler>();
        var loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(ExtensionsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var manifest = ReadManifest(file);
                var validation = ValidateManifest(manifest);
                if (!validation.Success || !manifest.Enabled) continue;
                foreach (var rule in manifest.Rules)
                {
                    var handler = new RegistryExtensionTweakHandler(manifest, rule, _paths);
                    if (!loadedIds.Add(handler.Definition.Id))
                    {
                        _logger.Warning($"Duplicate extension tweak ID '{handler.Definition.Id}' was skipped.");
                        continue;
                    }
                    handlers.Add(handler);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Tweak-rule extension '{Path.GetFileName(file)}' was skipped: {ex.Message}");
            }
        }
        return handlers;
    }

    public Task<IReadOnlyList<InstalledTweakExtensionInfo>> GetExtensionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = new List<InstalledTweakExtensionInfo>();
        foreach (var file in Directory.EnumerateFiles(ExtensionsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var manifest = ReadManifest(file);
                var validation = ValidateManifest(manifest);
                list.Add(new InstalledTweakExtensionInfo(
                    manifest.Id,
                    string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileNameWithoutExtension(file) : manifest.Name,
                    manifest.Version,
                    manifest.Author,
                    manifest.Description,
                    manifest.Enabled,
                    manifest.Rules.Count,
                    file,
                    validation.Success,
                    validation.Message,
                    manifest.UpdateManifestUrl));
            }
            catch (Exception ex)
            {
                list.Add(new InstalledTweakExtensionInfo(
                    Path.GetFileNameWithoutExtension(file), Path.GetFileName(file), "?", "Unknown", string.Empty,
                    false, 0, file, false, ex.Message, null));
            }
        }
        return Task.FromResult<IReadOnlyList<InstalledTweakExtensionInfo>>(list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<(bool Success, string Message)> ImportExtensionAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return (false, "Choose an existing extension JSON file.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var manifest = ReadManifest(sourcePath);
            var validation = ValidateManifest(manifest);
            if (!validation.Success) return (false, validation.Message);

            // Imported rules begin disabled even if a downloaded manifest says otherwise. This keeps
            // extension installation separate from permission to change Windows settings.
            manifest.Enabled = false;
            var destination = Path.Combine(ExtensionsDirectory, $"{manifest.Id}.json");
            await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken).ConfigureAwait(false);
            return (true, $"Imported {manifest.Name} v{manifest.Version}. It is disabled until you explicitly enable it. Restart Sabby after enabling so its rules enter the tweak catalog.");
        }
        catch (Exception ex)
        {
            return (false, $"Extension import failed safely: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> SetExtensionEnabledAsync(string extensionId, bool enabled, CancellationToken cancellationToken = default)
    {
        var file = FindExtensionFile(extensionId);
        if (file is null) return (false, "The extension file no longer exists.");
        try
        {
            var manifest = ReadManifest(file);
            var validation = ValidateManifest(manifest);
            if (!validation.Success) return (false, validation.Message);
            manifest.Enabled = enabled;
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken).ConfigureAwait(false);
            return (true, enabled
                ? $"{manifest.Name} is enabled. Restart Sabby to load its {manifest.Rules.Count} rule(s)."
                : $"{manifest.Name} is disabled. Restart Sabby to remove its rules from the tweak catalog.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not update extension state: {ex.Message}");
        }
    }

    public Task<(bool Success, string Message)> RemoveExtensionAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = FindExtensionFile(extensionId);
        if (file is null) return Task.FromResult((false, "The extension file no longer exists."));
        try
        {
            File.Delete(file);
            return Task.FromResult((true, "Extension file removed. Restart Sabby if its rules were loaded in this session."));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"Could not remove extension: {ex.Message}"));
        }
    }

    public async Task<string> CreateExampleExtensionAsync(CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(ExtensionsDirectory, "example-safe-rule.json");
        if (!File.Exists(file))
        {
            var example = new TweakRuleExtensionManifest
            {
                Id = "example.safe-rule",
                Name = "Example Safe Rule",
                Version = "1.0.0",
                Author = "Sabby Optimizer",
                Description = "Disabled example showing the Sabby declarative extension format. It writes only a harmless current-user example value when explicitly enabled and applied.",
                Enabled = false,
                Rules =
                [
                    new TweakRuleExtensionRule
                    {
                        Id = "example-value",
                        Name = "Example extension value",
                        Description = "Demonstrates an HKCU declarative registry rule without executing scripts.",
                        Category = "Advanced",
                        Safety = "Safe",
                        RegistryHive = "HKCU",
                        RegistryPath = @"Software\SabbyOptimizer\ExtensionExample",
                        ValueName = "Enabled",
                        ValueType = "DWord",
                        ApplyValue = "1"
                    }
                ]
            };
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(example, JsonOptions), cancellationToken).ConfigureAwait(false);
        }
        return file;
    }

    public async Task<SabbyReleaseInfo> CheckSabbyUpdateAsync(SabbyUpdateChannel channel, string? feedUrl, CancellationToken cancellationToken = default)
    {
        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var currentText = $"{current.Major}.{current.Minor}.{current.Build}";
        if (string.IsNullOrWhiteSpace(feedUrl))
            return new SabbyReleaseInfo(false, false, false, currentText, "—", $"{channel} channel selected, but no manifest feed is configured yet.", null, null, null, null, null);

        try
        {
            var json = await ReadTextFromUrlOrFileAsync(feedUrl.Trim(), cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<ReleaseManifest>(json, JsonOptions) ?? throw new InvalidDataException("Update manifest was empty.");
            if (!Version.TryParse(manifest.Version, out var latest))
                throw new InvalidDataException("Update manifest version is invalid.");

            var available = latest.CompareTo(current) > 0;
            return new SabbyReleaseInfo(
                true,
                available,
                available && manifest.Mandatory,
                currentText,
                manifest.Version,
                available ? $"{channel} update {manifest.Version} is available." : $"You are up to date on the {channel} channel.",
                manifest.DownloadUrl,
                manifest.Sha256,
                manifest.SignerThumbprint,
                manifest.ReleaseNotes,
                manifest.PublishedAt);
        }
        catch (Exception ex)
        {
            return new SabbyReleaseInfo(true, false, false, currentText, "?", $"Could not read the {channel} channel: {ex.Message}", null, null, null, null, null);
        }
    }

    public async Task<(bool Success, string Message, string? FilePath)> DownloadAndStageUpdateAsync(SabbyReleaseInfo release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!release.UpdateAvailable || string.IsNullOrWhiteSpace(release.DownloadUrl))
            return (false, "No downloadable Sabby update is currently selected.", null);

        try
        {
            Directory.CreateDirectory(StagedUpdatesDirectory);
            var uriText = release.DownloadUrl.Trim();
            var fileName = GuessFileName(uriText, release.LatestVersion);
            var destination = Path.Combine(StagedUpdatesDirectory, fileName);

            if (Uri.TryCreate(uriText, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = File.Create(destination);
                var buffer = new byte[128 * 1024];
                long readTotal = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read <= 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    readTotal += read;
                    if (total is > 0) progress?.Report(Math.Clamp(readTotal * 100d / total.Value, 0, 100));
                }
            }
            else
            {
                var source = NormalizeFilePath(uriText);
                if (!File.Exists(source)) return (false, "The update download path does not exist.", null);
                File.Copy(source, destination, true);
                progress?.Report(100);
            }

            if (!string.IsNullOrWhiteSpace(release.Sha256))
            {
                await using var stream = File.OpenRead(destination);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
                var expected = release.Sha256.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
                if (!hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(destination);
                    return (false, "Downloaded update failed SHA-256 verification and was deleted.", null);
                }
            }

            if (!string.IsNullOrWhiteSpace(release.SignerThumbprint) &&
                new[] { ".exe", ".msi", ".msix", ".appx" }.Contains(Path.GetExtension(destination), StringComparer.OrdinalIgnoreCase))
            {
                var signature = await VerifyAuthenticodeSignatureAsync(destination, release.SignerThumbprint, cancellationToken).ConfigureAwait(false);
                if (!signature.Success)
                {
                    File.Delete(destination);
                    return (false, $"Downloaded update failed publisher-signature verification and was deleted. {signature.Message}", null);
                }
            }

            progress?.Report(100);
            return (true, $"Update {release.LatestVersion} downloaded and staged safely. Sabby did not overwrite the running executable.", destination);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return (false, $"Update download failed safely: {ex.Message}", null);
        }
    }

    private static async Task<(bool Success, string Message)> VerifyAuthenticodeSignatureAsync(string path, string expectedThumbprint, CancellationToken cancellationToken)
    {
        try
        {
            var safePath = path.Replace("'", "''", StringComparison.Ordinal);
            var expected = expectedThumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
            var script = $@"$ErrorActionPreference='Stop'; $s=Get-AuthenticodeSignature -LiteralPath '{safePath}'; if($s.Status -ne 'Valid'){{ throw ('Signature status: ' + $s.Status) }}; if($null -eq $s.SignerCertificate){{ throw 'No signer certificate.' }}; $t=($s.SignerCertificate.Thumbprint -replace '\s',''); if($t -ne '{expected}'){{ throw ('Signer thumbprint mismatch: ' + $t) }}; Write-Output 'OK'";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("-NoLogo");
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(encoded);
            using var process = Process.Start(start);
            if (process is null) return (false, "Could not start signature verification.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = (await outputTask.ConfigureAwait(false)).Trim();
            var error = (await errorTask.ConfigureAwait(false)).Trim();
            return process.ExitCode == 0 && output.Contains("OK", StringComparison.OrdinalIgnoreCase)
                ? (true, "Authenticode signature and signer identity verified.")
                : (false, string.IsNullOrWhiteSpace(error) ? "Windows did not validate the expected signer." : error);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static TweakRuleExtensionManifest ReadManifest(string file) =>
        JsonSerializer.Deserialize<TweakRuleExtensionManifest>(File.ReadAllText(file), JsonOptions)
        ?? throw new InvalidDataException("Extension manifest is empty.");

    private static (bool Success, string Message) ValidateManifest(TweakRuleExtensionManifest manifest)
    {
        if (!SafeIdRegex.IsMatch(manifest.Id)) return (false, "Extension ID must use only letters, numbers, dot, underscore, or dash.");
        if (string.IsNullOrWhiteSpace(manifest.Name)) return (false, "Extension name is required.");
        if (manifest.Rules.Count == 0) return (false, "Extension has no tweak rules.");
        if (manifest.Rules.Count > 100) return (false, "Extension contains more than 100 rules and was blocked.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in manifest.Rules)
        {
            if (!SafeIdRegex.IsMatch(rule.Id)) return (false, $"Rule ID '{rule.Id}' is invalid.");
            if (!ids.Add(rule.Id)) return (false, $"Duplicate rule ID '{rule.Id}'.");
            if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.RegistryPath) || string.IsNullOrWhiteSpace(rule.ValueName))
                return (false, $"Rule '{rule.Id}' is missing required fields.");
            if (!rule.RegistryHive.Equals("HKCU", StringComparison.OrdinalIgnoreCase) &&
                !rule.RegistryHive.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) &&
                !rule.RegistryHive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) &&
                !rule.RegistryHive.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
                return (false, $"Rule '{rule.Id}' uses an unsupported registry hive.");
            if (!new[] { "dword", "qword", "string" }.Contains(rule.ValueType.Trim().ToLowerInvariant()))
                return (false, $"Rule '{rule.Id}' ValueType must be DWord, QWord, or String.");

            var normalizedPath = rule.RegistryPath.TrimStart('\\').ToLowerInvariant();
            if (normalizedPath.StartsWith("sam\\") || normalizedPath.StartsWith("security\\") || normalizedPath.StartsWith("bcd00000000\\"))
                return (false, $"Rule '{rule.Id}' targets a protected registry area and was blocked.");
            if (normalizedPath.Contains("windows defender", StringComparison.OrdinalIgnoreCase) || normalizedPath.Contains("tamperprotection", StringComparison.OrdinalIgnoreCase))
                return (false, $"Rule '{rule.Id}' targets security-protection settings and was blocked from the extension format.");
        }
        return (true, $"Validated {manifest.Rules.Count} declarative registry rule(s). No scripts or executables are allowed.");
    }

    private string? FindExtensionFile(string extensionId)
    {
        return Directory.EnumerateFiles(ExtensionsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path =>
            {
                try { return ReadManifest(path).Id.Equals(extensionId, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });
    }

    private static async Task<string> ReadTextFromUrlOrFileAsync(string source, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return await Http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        var path = NormalizeFilePath(source);
        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeFilePath(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile) return uri.LocalPath;
        return Environment.ExpandEnvironmentVariables(source.Trim('"'));
    }

    private static string GuessFileName(string source, string version)
    {
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                var name = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            var file = Path.GetFileName(source);
            if (!string.IsNullOrWhiteSpace(file)) return file;
        }
        catch { }
        return $"SabbyOptimizer-{version}.zip";
    }
}
