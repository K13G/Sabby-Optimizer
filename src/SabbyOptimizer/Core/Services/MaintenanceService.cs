using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class MaintenanceService : IMaintenanceService
{
    private sealed record DisabledStartupRecord(
        string Id,
        string Name,
        string Command,
        string Source,
        string Location,
        bool IsMachineWide,
        int RegistryValueKind,
        string? DisabledFilePath);

    private sealed record ServiceDefinition(string Name, string Description);

    private static readonly ServiceDefinition[] OptionalServices =
    [
        new("WSearch", "Windows indexing and fast Start/File Explorer search. Stopping it is reversible but can make searches slower."),
        new("SysMain", "Windows memory preloading/caching. Useful on many PCs; Sabby only offers temporary start/stop control."),
        new("Spooler", "Required for printing and many PDF/virtual-printer workflows."),
        new("bthserv", "Bluetooth support. Stop only when Bluetooth devices are not needed."),
        new("XblAuthManager", "Xbox Live authentication used by some Microsoft Store/Xbox games."),
        new("XblGameSave", "Xbox Live cloud game-save support used by some Xbox/Microsoft Store titles.")
    ];

    private readonly IAppLogger _logger;
    private readonly string _disabledStartupFile;

    public MaintenanceService(IAppPaths paths, IAppLogger logger)
    {
        _logger = logger;
        var directory = Path.Combine(paths.UserDataDirectory, "Maintenance");
        Directory.CreateDirectory(directory);
        _disabledStartupFile = Path.Combine(directory, "disabled-startup.json");
    }

    public Task<IReadOnlyList<StartupAppInfo>> GetStartupAppsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<StartupAppInfo>>(() => ReadStartupApps(cancellationToken), cancellationToken);

    public Task<bool> SetStartupEnabledAsync(StartupAppInfo app, bool enabled, CancellationToken cancellationToken = default) =>
        Task.Run(() => SetStartupEnabled(app, enabled, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<OptionalServiceInfo>> GetOptionalServicesAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<OptionalServiceInfo>();
        foreach (var definition in OptionalServices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var service = await ReadServiceAsync(definition, cancellationToken).ConfigureAwait(false);
            if (service is not null)
                results.Add(service);
        }
        return results;
    }

    public async Task<bool> SetServiceRunningAsync(OptionalServiceInfo service, bool running, CancellationToken cancellationToken = default)
    {
        var verb = running ? "start" : "stop";
        var result = await RunProcessAsync("sc.exe", [verb, service.Name], 10000, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.Warning($"Service {verb} failed for {service.Name}: {result.Error}");
            return false;
        }

        // Give SCM a short moment to settle before the UI refreshes.
        await Task.Delay(450, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task<CleanupAnalysis> AnalyzeTemporaryFilesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ScanTemporaryFiles(delete: false, cancellationToken), cancellationToken);

    public Task<CleanupAnalysis> CleanTemporaryFilesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ScanTemporaryFiles(delete: true, cancellationToken), cancellationToken);

    private IReadOnlyList<StartupAppInfo> ReadStartupApps(CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, StartupAppInfo>(StringComparer.OrdinalIgnoreCase);

        ReadRunKey(results, RegistryHive.CurrentUser, RegistryView.Registry64,
            @"Software\Microsoft\Windows\CurrentVersion\Run", "Registry • Current user", false, cancellationToken);
        ReadRunKey(results, RegistryHive.LocalMachine, RegistryView.Registry64,
            @"Software\Microsoft\Windows\CurrentVersion\Run", "Registry • All users", true, cancellationToken);
        ReadRunKey(results, RegistryHive.LocalMachine, RegistryView.Registry32,
            @"Software\Microsoft\Windows\CurrentVersion\Run", "Registry • All users (32-bit)", true, cancellationToken);

        ReadStartupFolder(results, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Startup folder • Current user", false, cancellationToken);
        ReadStartupFolder(results, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Startup folder • All users", true, cancellationToken);

        foreach (var disabled in ReadDisabledStartupRecords())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!results.ContainsKey(disabled.Id))
            {
                results[disabled.Id] = new StartupAppInfo
                {
                    Id = disabled.Id,
                    Name = disabled.Name,
                    Command = disabled.Command,
                    Source = disabled.Source,
                    Location = disabled.Location,
                    IsMachineWide = disabled.IsMachineWide,
                    IsEnabled = false
                };
            }
        }

        return results.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ReadRunKey(
        IDictionary<string, StartupAppInfo> results,
        RegistryHive hive,
        RegistryView view,
        string path,
        string source,
        bool machineWide,
        CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key is null) return;

            foreach (var name in key.GetValueNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var command = key.GetValue(name, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? string.Empty;
                var location = $"REG|{hive}|{view}|{path}";
                var id = $"{location}|{name}";
                results[id] = new StartupAppInfo
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? "(Default)" : name,
                    Command = command,
                    Source = source,
                    Location = location,
                    IsMachineWide = machineWide,
                    IsEnabled = true
                };
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Startup registry scan failed ({source}): {ex.Message}");
        }
    }

    private static void ReadStartupFolder(
        IDictionary<string, StartupAppInfo> results,
        string folder,
        string source,
        bool machineWide,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (path.EndsWith(".sabby-disabled", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var id = $"FILE|{path}";
                results[id] = new StartupAppInfo
                {
                    Id = id,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Command = path,
                    Source = source,
                    Location = $"FILE|{path}",
                    IsMachineWide = machineWide,
                    IsEnabled = true
                };
            }
        }
        catch
        {
            // Startup-folder enumeration is best effort.
        }
    }

    private bool SetStartupEnabled(StartupAppInfo app, bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var records = ReadDisabledStartupRecords();

        try
        {
            if (!enabled)
            {
                if (!app.IsEnabled) return true;

                if (app.Location.StartsWith("REG|", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = app.Location.Split('|', 4);
                    if (parts.Length != 4) return false;
                    var hive = Enum.Parse<RegistryHive>(parts[1]);
                    var view = Enum.Parse<RegistryView>(parts[2]);
                    var registryPath = parts[3];
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(registryPath, writable: true);
                    if (key is null) return false;
                    var kind = key.GetValueKind(app.Name);
                    var raw = key.GetValue(app.Name, string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? app.Command;
                    key.DeleteValue(app.Name, throwOnMissingValue: false);
                    records.RemoveAll(x => x.Id.Equals(app.Id, StringComparison.OrdinalIgnoreCase));
                    records.Add(new DisabledStartupRecord(app.Id, app.Name, raw, app.Source, app.Location, app.IsMachineWide, (int)kind, null));
                }
                else if (app.Location.StartsWith("FILE|", StringComparison.OrdinalIgnoreCase))
                {
                    var original = app.Location[5..];
                    if (!File.Exists(original)) return false;
                    var disabledPath = original + ".sabby-disabled";
                    if (File.Exists(disabledPath)) File.Delete(disabledPath);
                    File.Move(original, disabledPath);
                    records.RemoveAll(x => x.Id.Equals(app.Id, StringComparison.OrdinalIgnoreCase));
                    records.Add(new DisabledStartupRecord(app.Id, app.Name, app.Command, app.Source, app.Location, app.IsMachineWide, -1, disabledPath));
                }
                else return false;
            }
            else
            {
                var record = records.FirstOrDefault(x => x.Id.Equals(app.Id, StringComparison.OrdinalIgnoreCase));
                if (record is null) return app.IsEnabled;

                if (record.Location.StartsWith("REG|", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = record.Location.Split('|', 4);
                    if (parts.Length != 4) return false;
                    var hive = Enum.Parse<RegistryHive>(parts[1]);
                    var view = Enum.Parse<RegistryView>(parts[2]);
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.CreateSubKey(parts[3], writable: true);
                    key.SetValue(record.Name, record.Command, (RegistryValueKind)Math.Max(0, record.RegistryValueKind));
                }
                else if (record.Location.StartsWith("FILE|", StringComparison.OrdinalIgnoreCase))
                {
                    var original = record.Location[5..];
                    if (string.IsNullOrWhiteSpace(record.DisabledFilePath) || !File.Exists(record.DisabledFilePath)) return false;
                    if (File.Exists(original)) return false;
                    File.Move(record.DisabledFilePath, original);
                }
                else return false;

                records.RemoveAll(x => x.Id.Equals(app.Id, StringComparison.OrdinalIgnoreCase));
            }

            WriteDisabledStartupRecords(records);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to change startup state for {app.Name}.", ex);
            return false;
        }
    }

    private async Task<OptionalServiceInfo?> ReadServiceAsync(ServiceDefinition definition, CancellationToken cancellationToken)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{definition.Name}");
            if (key is null) return null;

            var display = key.GetValue("DisplayName")?.ToString();
            if (string.IsNullOrWhiteSpace(display)) display = definition.Name;
            var start = Convert.ToInt32(key.GetValue("Start", 3));
            var startMode = start switch { 2 => "Automatic", 3 => "Manual", 4 => "Disabled", 0 => "Boot", 1 => "System", _ => $"Mode {start}" };

            var query = await RunProcessAsync("sc.exe", ["query", definition.Name], 6000, cancellationToken).ConfigureAwait(false);
            var status = query.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ? "Running" :
                         query.Output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase) ? "Stopped" : "Unknown";

            return new OptionalServiceInfo
            {
                Name = definition.Name,
                DisplayName = display,
                Description = definition.Description,
                Status = status,
                StartMode = startMode
            };
        }
        catch (Exception ex)
        {
            _logger.Warning($"Service scan failed for {definition.Name}: {ex.Message}");
            return null;
        }
    }

    private CleanupAnalysis ScanTemporaryFiles(bool delete, CancellationToken cancellationToken)
    {
        // User TEMP (%TEMP%) plus the machine-wide Windows TEMP folder.  We intentionally
        // delete only contents, never the root directories themselves. Locked/in-use files
        // are skipped instead of forcing handles closed.
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddExistingRoot(roots, Path.GetTempPath());
        AddExistingRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows)) AddExistingRoot(roots, Path.Combine(windows, "Temp"));

        long bytes = 0;
        var count = 0;
        var skipped = 0;

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = EnumerateFilesSafe(root, cancellationToken).ToArray();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists) continue;
                    var length = info.Length;
                    if (delete)
                        File.Delete(file);
                    bytes += length;
                    count++;
                }
                catch
                {
                    skipped++;
                }
            }

            if (delete)
                DeleteEmptyDirectories(root, cancellationToken);
        }

        return new CleanupAnalysis(bytes, count, skipped);
    }

    private static void DeleteEmptyDirectories(string root, CancellationToken cancellationToken)
    {
        try
        {
            var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length)
                .ToArray();
            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory, recursive: false);
                }
                catch
                {
                    // Protected/in-use directories are intentionally left alone.
                }
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch
            {
                continue;
            }

            foreach (var file in files) yield return file;
            foreach (var child in directories)
            {
                try
                {
                    var attrs = File.GetAttributes(child);
                    if ((attrs & FileAttributes.ReparsePoint) == 0)
                        pending.Push(child);
                }
                catch { }
            }
        }
    }

    private static void AddExistingRoot(ISet<string> roots, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            roots.Add(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar));
    }

    private List<DisabledStartupRecord> ReadDisabledStartupRecords()
    {
        try
        {
            return File.Exists(_disabledStartupFile)
                ? JsonSerializer.Deserialize<List<DisabledStartupRecord>>(File.ReadAllText(_disabledStartupFile)) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    private void WriteDisabledStartupRecords(List<DisabledStartupRecord> records)
    {
        var temp = _disabledStartupFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _disabledStartupFile, overwrite: true);
    }

    private sealed record ProcessResult(bool Success, string Output, string Error);

    private static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);

            using var process = Process.Start(info);
            if (process is null) return new(false, string.Empty, "Could not start process.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMilliseconds);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return new(process.ExitCode == 0, output, error.Trim());
        }
        catch (Exception ex)
        {
            return new(false, string.Empty, ex.Message);
        }
    }
}
