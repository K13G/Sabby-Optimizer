using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class UpdateCenterService : IUpdateCenterService
{
    private readonly IAppLogger _logger;

    public UpdateCenterService(IAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<UpdateScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<AvailableUpdateInfo>();
        var windowsStatus = "Windows Update scan not available.";
        var appStatus = "WinGet is not installed.";

        try
        {
            var wua = await ScanWindowsUpdateAsync(cancellationToken).ConfigureAwait(false);
            all.AddRange(wua);
            var drivers = wua.Count(x => x.Kind == UpdateKind.Driver);
            var windows = wua.Count(x => x.Kind == UpdateKind.Windows);
            windowsStatus = $"Windows Update: {windows} Windows update{(windows == 1 ? string.Empty : "s")} • {drivers} driver update{(drivers == 1 ? string.Empty : "s")}";
        }
        catch (Exception ex)
        {
            windowsStatus = $"Windows Update scan failed: {ex.Message}";
            _logger.Warning(windowsStatus);
        }

        var wingetAvailable = false;
        try
        {
            var apps = await ScanWingetAsync(cancellationToken).ConfigureAwait(false);
            wingetAvailable = apps.Available;
            all.AddRange(apps.Updates);
            appStatus = apps.Available
                ? $"WinGet: {apps.Updates.Count} application update{(apps.Updates.Count == 1 ? string.Empty : "s")}" 
                : "WinGet is not installed or could not be started.";
        }
        catch (Exception ex)
        {
            appStatus = $"Application update scan failed: {ex.Message}";
            _logger.Warning(appStatus);
        }

        var ordered = all
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.Info($"Update scan complete: {ordered.Length} total • {ordered.Count(x => x.Kind == UpdateKind.Application)} apps • {ordered.Count(x => x.Kind == UpdateKind.Windows)} Windows • {ordered.Count(x => x.Kind == UpdateKind.Driver)} drivers.");
        return new UpdateScanResult(ordered, wingetAvailable, windowsStatus, appStatus);
    }

    public async Task<UpdateInstallResult> InstallAllAsync(
        IReadOnlyList<AvailableUpdateInfo> updates,
        bool includeOptionalWindowsUpdates,
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var eligible = updates
            .Where(x => x.Kind == UpdateKind.Application || x.Kind == UpdateKind.Driver || includeOptionalWindowsUpdates || !x.IsOptional)
            .OrderBy(x => x.Kind == UpdateKind.Application ? 0 : x.Kind == UpdateKind.Windows ? 1 : 2)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var completed = 0;
        var failed = 0;
        var skipped = updates.Count - eligible.Length;
        var rebootRequired = false;

        if (eligible.Length == 0)
        {
            progress?.Report(new UpdateProgressInfo(null, "Nothing to update", "Complete", 100, 100));
            return new UpdateInstallResult(0, 0, skipped, false, results);
        }

        for (var index = 0; index < eligible.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var update = eligible[index];
            var itemStart = 100d * index / eligible.Length;
            var itemSpan = 100d / eligible.Length;

            progress?.Report(new UpdateProgressInfo(update.Key, update.Title, "Starting", itemStart, 0, true));
            (bool Success, string Message, bool RebootRequired) outcome;

            if (update.Kind == UpdateKind.Application)
            {
                outcome = await InstallWingetUpdateAsync(update, (stage, itemPercent, indeterminate) =>
                {
                    var overall = itemStart + itemSpan * Math.Clamp(itemPercent, 0, 100) / 100d;
                    progress?.Report(new UpdateProgressInfo(update.Key, update.Title, stage, overall, itemPercent, indeterminate));
                }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                outcome = await InstallWindowsUpdateAsync(update, (stage, itemPercent) =>
                {
                    var overall = itemStart + itemSpan * Math.Clamp(itemPercent, 0, 100) / 100d;
                    progress?.Report(new UpdateProgressInfo(update.Key, update.Title, stage, overall, itemPercent));
                }, cancellationToken).ConfigureAwait(false);
            }

            rebootRequired |= outcome.RebootRequired;
            results[update.Key] = outcome.Message;
            if (outcome.Success) completed++;
            else failed++;
            _logger.Info($"Update {(outcome.Success ? "completed" : "failed")}: {update.Title} [{update.Kind}] • {outcome.Message}");

            progress?.Report(new UpdateProgressInfo(
                update.Key,
                update.Title,
                outcome.Success ? "Completed" : "Failed",
                100d * (index + 1) / eligible.Length,
                100));
        }

        return new UpdateInstallResult(completed, failed, skipped, rebootRequired, results);
    }

    private async Task<IReadOnlyList<AvailableUpdateInfo>> ScanWindowsUpdateAsync(CancellationToken cancellationToken)
    {
        const string script = @"
$ErrorActionPreference='Stop'
$session = New-Object -ComObject Microsoft.Update.Session
$session.ClientApplicationID = 'Sabby Optimizer'
$searcher = $session.CreateUpdateSearcher()
$searcher.Online = $true
$result = $searcher.Search(""IsInstalled=0 and IsHidden=0"")
$items = @()
for($i=0; $i -lt $result.Updates.Count; $i++) {
  $u = $result.Updates.Item($i)
  $optional = $false
  try { $optional = [bool]$u.BrowseOnly } catch {}
  $kind = if(([string]$u.Type) -eq 'Driver') {'Driver'} else {'Windows'}
  $current = ''
  $available = ''
  if($kind -eq 'Driver') {
    try { $available = [string]$u.DriverVerDate } catch {}
    try { if([string]::IsNullOrWhiteSpace($available)) { $available = [string]$u.DriverProvider } } catch {}
  } else {
    $kb = @($u.KBArticleIDs)
    if($kb.Count -gt 0) { $available = 'KB' + ($kb -join ', KB') }
  }
  $items += [pscustomobject]@{
    Key=('wua:' + [string]$u.Identity.UpdateID)
    Title=[string]$u.Title
    Kind=$kind
    CurrentVersion=$current
    AvailableVersion=$available
    Source='Windows Update'
    Optional=$optional
    Reboot=[bool]$u.RebootRequired
    NativeId=[string]$u.Identity.UpdateID
  }
}
ConvertTo-Json -InputObject $items -Compress -Depth 4";

        var result = await PowerShellUtility.RunAsync(script, 90000, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Windows Update Agent did not complete the search." : result.Error);

        return ParseWindowsUpdateJson(result.Output);
    }

    private static IReadOnlyList<AvailableUpdateInfo> ParseWindowsUpdateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<AvailableUpdateInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var items = new List<AvailableUpdateInfo>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray()) TryAddWindowsUpdate(element, items);
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                TryAddWindowsUpdate(doc.RootElement, items);
            }
            return items;
        }
        catch
        {
            return Array.Empty<AvailableUpdateInfo>();
        }
    }

    private static void TryAddWindowsUpdate(JsonElement element, List<AvailableUpdateInfo> target)
    {
        static string S(JsonElement e, string name) => e.TryGetProperty(name, out var p) ? p.ToString() : string.Empty;
        static bool B(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
        var key = S(element, "Key");
        var title = S(element, "Title");
        var kindText = S(element, "Kind");
        var nativeId = S(element, "NativeId");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(nativeId)) return;
        target.Add(new AvailableUpdateInfo(
            key,
            title,
            kindText.Equals("Driver", StringComparison.OrdinalIgnoreCase) ? UpdateKind.Driver : UpdateKind.Windows,
            S(element, "CurrentVersion"),
            S(element, "AvailableVersion"),
            S(element, "Source"),
            B(element, "Optional"),
            B(element, "Reboot"),
            nativeId));
    }

    private async Task<(bool Available, IReadOnlyList<AvailableUpdateInfo> Updates)> ScanWingetAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessCaptureAsync(
            "winget.exe",
            ["list", "--upgrade-available", "--include-unknown", "--accept-source-agreements", "--disable-interactivity"],
            TimeSpan.FromSeconds(60),
            cancellationToken).ConfigureAwait(false);

        if (result.StartFailed)
            return (false, Array.Empty<AvailableUpdateInfo>());

        var updates = ParseWingetUpgradeTable(result.Output);
        return (true, updates);
    }

    private static IReadOnlyList<AvailableUpdateInfo> ParseWingetUpgradeTable(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return Array.Empty<AvailableUpdateInfo>();
        output = Regex.Replace(output, "\x1B\\[[0-9;?]*[ -/]*[@-~]", string.Empty).Replace("\b", string.Empty);
        var lines = output.Replace("\r", string.Empty).Split('\n');
        var separatorIndex = Array.FindIndex(lines, line => Regex.Matches(line, "-{3,}").Count >= 4);
        if (separatorIndex < 0 || separatorIndex + 1 >= lines.Length) return Array.Empty<AvailableUpdateInfo>();

        var separator = lines[separatorIndex];
        var matches = Regex.Matches(separator, "-{3,}").Cast<Match>().ToArray();
        if (matches.Length < 4) return Array.Empty<AvailableUpdateInfo>();
        var starts = matches.Select(m => m.Index).ToArray();
        var list = new List<AvailableUpdateInfo>();

        for (var i = separatorIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Contains("upgrade", StringComparison.OrdinalIgnoreCase) && line.Contains("available", StringComparison.OrdinalIgnoreCase) && !line.Contains('.'))
                continue;
            if (line.TrimStart().StartsWith("The following", StringComparison.OrdinalIgnoreCase)) continue;

            string Col(int col)
            {
                if (col >= starts.Length || starts[col] >= line.Length) return string.Empty;
                var end = col + 1 < starts.Length ? Math.Min(starts[col + 1], line.Length) : line.Length;
                return line[starts[col]..end].Trim();
            }

            var name = Col(0);
            var id = Col(1);
            var current = Col(2);
            var available = Col(3);
            var source = starts.Length >= 5 ? Col(4) : "WinGet";
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || id.Equals("Id", StringComparison.OrdinalIgnoreCase)) continue;
            if (id.Contains(' ') && !id.Contains('.')) continue;

            list.Add(new AvailableUpdateInfo(
                $"winget:{id}",
                name,
                UpdateKind.Application,
                current,
                available,
                string.IsNullOrWhiteSpace(source) ? "WinGet" : source,
                false,
                false,
                id));
        }

        return list;
    }

    private async Task<(bool Success, string Message, bool RebootRequired)> InstallWingetUpdateAsync(
        AvailableUpdateInfo update,
        Action<string, double, bool> report,
        CancellationToken cancellationToken)
    {
        report("Preparing app update", 5, true);
        var result = await RunProcessStreamingAsync(
            "winget.exe",
            ["upgrade", "--id", update.NativeId, "--exact", "--include-unknown", "--silent", "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity"],
            TimeSpan.FromMinutes(20),
            line =>
            {
                var match = Regex.Match(line, @"(?<!\d)(\d{1,3})\s*%");
                if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct))
                    report("Installing application", Math.Clamp(pct, 8, 95), false);
                else if (line.Contains("Downloading", StringComparison.OrdinalIgnoreCase)) report("Downloading application", 25, true);
                else if (line.Contains("Installing", StringComparison.OrdinalIgnoreCase)) report("Installing application", 65, true);
            },
            cancellationToken).ConfigureAwait(false);

        if (result.StartFailed)
            return (false, "WinGet could not be started.", false);

        if (result.ExitCode == 0)
        {
            report("Application updated", 100, false);
            return (true, "Updated successfully through WinGet.", false);
        }

        var detail = Compact(result.Error, result.Output);
        return (false, string.IsNullOrWhiteSpace(detail) ? $"WinGet exited with code {result.ExitCode}." : detail, false);
    }

    private async Task<(bool Success, string Message, bool RebootRequired)> InstallWindowsUpdateAsync(
        AvailableUpdateInfo update,
        Action<string, double> report,
        CancellationToken cancellationToken)
    {
        var escapedId = update.NativeId.Replace("'", "''", StringComparison.Ordinal);
        var script = $@"
$ErrorActionPreference='Stop'
$session = New-Object -ComObject Microsoft.Update.Session
$session.ClientApplicationID = 'Sabby Optimizer'
$searcher = $session.CreateUpdateSearcher()
$searcher.Online = $true
$r = $searcher.Search(""UpdateID='{escapedId}' and IsInstalled=0"")
if($r.Updates.Count -lt 1) {{ Write-Output 'RESULT|SKIPPED|Update is no longer available.'; exit 0 }}
$u = $r.Updates.Item(0)
if(-not $u.EulaAccepted) {{ $u.AcceptEula() }}
$coll = New-Object -ComObject Microsoft.Update.UpdateColl
[void]$coll.Add($u)
Write-Output 'STAGE|DOWNLOAD'
if(-not $u.IsDownloaded) {{
  $downloader = $session.CreateUpdateDownloader()
  $downloader.Updates = $coll
  $dr = $downloader.Download()
  if([int]$dr.ResultCode -notin @(2,3)) {{ Write-Output ('RESULT|FAILED|Download result code ' + [int]$dr.ResultCode); exit 3 }}
}}
Write-Output 'STAGE|INSTALL'
$installer = $session.CreateUpdateInstaller()
$installer.Updates = $coll
$ir = $installer.Install()
$code = [int]$ir.ResultCode
$reboot = [bool]$ir.RebootRequired
if($code -in @(2,3)) {{ Write-Output ('RESULT|OK|' + $reboot); exit 0 }}
Write-Output ('RESULT|FAILED|Install result code ' + $code); exit 4";

        report("Finding Windows update", 5);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var result = await RunProcessStreamingAsync(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
            TimeSpan.FromMinutes(30),
            line =>
            {
                if (line.Equals("STAGE|DOWNLOAD", StringComparison.OrdinalIgnoreCase)) report("Downloading from Windows Update", 30);
                else if (line.Equals("STAGE|INSTALL", StringComparison.OrdinalIgnoreCase)) report("Installing Windows update", 70);
            },
            cancellationToken).ConfigureAwait(false);

        if (result.StartFailed)
            return (false, "Windows PowerShell could not be started.", false);

        var marker = result.Output.Replace("\r", string.Empty).Split('\n').LastOrDefault(x => x.StartsWith("RESULT|", StringComparison.OrdinalIgnoreCase));
        if (result.ExitCode == 0 && marker is not null)
        {
            var parts = marker.Split('|', 3);
            if (parts.Length >= 2 && parts[1].Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                var reboot = parts.Length >= 3 && bool.TryParse(parts[2], out var parsed) && parsed;
                report(reboot ? "Installed • restart required" : "Installed", 100);
                return (true, reboot ? "Installed through Windows Update. Restart required." : "Installed through Windows Update.", reboot);
            }
            if (parts.Length >= 2 && parts[1].Equals("SKIPPED", StringComparison.OrdinalIgnoreCase))
                return (true, parts.Length >= 3 ? parts[2] : "Update was no longer available.", false);
        }

        var message = marker is null ? Compact(result.Error, result.Output) : marker.Split('|', 3).ElementAtOrDefault(2) ?? marker;
        return (false, string.IsNullOrWhiteSpace(message) ? $"Windows Update install failed with exit code {result.ExitCode}." : message, false);
    }

    private sealed record ProcessCapture(bool StartFailed, int ExitCode, string Output, string Error);

    private static async Task<ProcessCapture> RunProcessCaptureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var result = await RunProcessStreamingAsync(fileName, arguments, timeout, line => output.AppendLine(line), cancellationToken, line => error.AppendLine(line)).ConfigureAwait(false);
        return new ProcessCapture(result.StartFailed, result.ExitCode, output.ToString(), error.ToString());
    }

    private sealed record StreamingResult(bool StartFailed, int ExitCode, string Output, string Error);

    private static async Task<StreamingResult> RunProcessStreamingAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        Action<string>? onOutput,
        CancellationToken cancellationToken,
        Action<string>? onError = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            output.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            error.AppendLine(e.Data);
            onError?.Invoke(e.Data);
            onOutput?.Invoke(e.Data);
        };

        try
        {
            if (!process.Start()) return new StreamingResult(true, -1, string.Empty, "Could not start process.");
        }
        catch (Exception ex)
        {
            return new StreamingResult(true, -1, string.Empty, ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await Task.Delay(60, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new StreamingResult(false, -2, output.ToString(), "The update operation timed out.");
        }

        return new StreamingResult(false, process.ExitCode, output.ToString(), error.ToString());
    }

    private static string Compact(params string[] values)
    {
        var combined = string.Join(Environment.NewLine, values.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        if (combined.Length <= 800) return combined;
        return combined[^800..];
    }
}
