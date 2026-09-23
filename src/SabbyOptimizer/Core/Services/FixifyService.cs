using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class FixifyService
{
    private readonly IMaintenanceService _maintenance;
    private readonly GpuDriverIntegrationService _gpuDriverIntegration;
    private readonly IAppLogger _logger;

    public IReadOnlyList<FixifyToolDefinition> Tools { get; } = new[]
    {
        new FixifyToolDefinition("sfc", "System File Repair", "Windows",
            "Scan protected Windows system files and repair damaged copies when Windows can restore them.",
            "Runs sfc /scannow and reports the command's exit result.", LongRunning: true),
        new FixifyToolDefinition("dism-restore", "Windows Image Repair", "Windows",
            "Repair the Windows component store used by servicing and System File Checker.",
            "Runs DISM /Online /Cleanup-Image /RestoreHealth and captures the result.", LongRunning: true),
        new FixifyToolDefinition("dism-check", "Component Store Check", "Diagnostics",
            "Quickly ask DISM whether Windows has already marked the component store as corrupted.",
            "Runs DISM /Online /Cleanup-Image /CheckHealth."),
        new FixifyToolDefinition("component-analyze", "Component Store Analysis", "Diagnostics",
            "Analyze WinSxS/component-store usage and whether Windows recommends component cleanup.",
            "Runs DISM /Online /Cleanup-Image /AnalyzeComponentStore.", LongRunning: true),
        new FixifyToolDefinition("disk-scan", "System Drive Scan", "Storage",
            "Perform an online scan of the Windows system drive without scheduling an offline repair.",
            "Runs CHKDSK /scan on the system drive.", LongRunning: true),
        new FixifyToolDefinition("flush-dns", "Flush DNS Cache", "Network",
            "Clear Windows' local DNS resolver cache when stale name-resolution entries are suspected.",
            "Runs ipconfig /flushdns."),
        new FixifyToolDefinition("winsock-reset", "Reset Winsock", "Network",
            "Reset the Windows Winsock catalog when socket providers or networking become corrupted.",
            "Runs netsh winsock reset. A restart is normally required.", RequiresRestart: true),
        new FixifyToolDefinition("tcpip-reset", "Reset TCP/IP Stack", "Network",
            "Reset Windows TCP/IP configuration defaults when the network stack is damaged.",
            "Runs netsh int ip reset. A restart is normally required.", RequiresRestart: true),
        new FixifyToolDefinition("update-services", "Restart Update Services", "Windows Update",
            "Restart Windows Update and BITS without deleting update history or caches.",
            "Stops and starts wuauserv and BITS, then verifies their service states."),
        new FixifyToolDefinition("search-service", "Restart Windows Search", "Windows",
            "Restart Windows Search when indexing/search is stuck without rebuilding the index.",
            "Stops and starts WSearch, then verifies its service state."),
        new FixifyToolDefinition("temp-clean", "Clear TEMP Folders", "Cleanup",
            "Remove all currently deletable files from user %TEMP% and Windows TEMP.",
            "Deletes unlocked temporary files and skips files that Windows reports as in use or protected."),
        new FixifyToolDefinition("dism-scan", "Deep Component Store Scan", "Diagnostics",
            "Perform DISM ScanHealth for a deeper component-store corruption check than CheckHealth.",
            "Runs DISM /Online /Cleanup-Image /ScanHealth and captures the verified exit result.", LongRunning: true),
        new FixifyToolDefinition("component-clean", "Component Store Cleanup", "Cleanup",
            "Ask Windows servicing to remove superseded component-store files using its supported cleanup routine.",
            "Runs DISM /Online /Cleanup-Image /StartComponentCleanup. This does not use /ResetBase.", LongRunning: true),
        new FixifyToolDefinition("defender-update", "Update Defender Signatures", "Security",
            "Ask Microsoft Defender to update malware definitions before troubleshooting suspicious behavior.",
            "Runs the built-in Update-MpSignature PowerShell cmdlet."),
        new FixifyToolDefinition("audio-services", "Restart Windows Audio", "Windows",
            "Restart Windows Audio and Audio Endpoint Builder and verify both services return to Running.",
            "Restarts Audiosrv and AudioEndpointBuilder."),
        new FixifyToolDefinition("spooler-service", "Restart Print Spooler", "Windows",
            "Restart Print Spooler when queued jobs or the print pipeline stops responding.",
            "Restarts the Spooler service and verifies its state."),
        new FixifyToolDefinition("dhcp-service", "Restart DHCP Client", "Network",
            "Restart the DHCP Client when Windows has a stale or incorrect lease state.",
            "Restarts Dhcp and verifies it is running."),
        new FixifyToolDefinition("defender-quick", "Microsoft Defender Quick Scan", "Security",
            "Run a Microsoft Defender quick scan using the installed Windows security provider.",
            "Runs Start-MpScan -ScanType QuickScan. Scan duration depends on the PC.", LongRunning: true),
        new FixifyToolDefinition("device-problems", "Device Problem Scan", "Diagnostics",
            "List Plug and Play devices that Windows currently reports with a problem status.",
            "Runs pnputil /enum-devices /problem. This is diagnostic and does not uninstall or modify drivers."),
        new FixifyToolDefinition("power-requests", "Power Request Check", "Diagnostics",
            "Show apps, drivers, and services currently asking Windows to prevent sleep/display power transitions.",
            "Runs powercfg /requests and reports the current Windows power requests."),
        new FixifyToolDefinition("dhcp-renew", "Renew DHCP Lease", "Network",
            "Release and renew DHCP configuration when the adapter has a stale or incorrect lease.",
            "Runs ipconfig /release followed by ipconfig /renew. The network can disconnect briefly."),
        new FixifyToolDefinition("register-dns", "Register DNS Records", "Network",
            "Ask Windows to refresh dynamic DNS registration for configured network interfaces.",
            "Runs ipconfig /registerdns."),
        new FixifyToolDefinition("nvidia-guide", "NVIDIA Quality-Safe Gaming Profile", "GPU",
            "Apply Sabby's supported NVIDIA quality-safe performance profile and verify the result.",
            "Uses Windows per-game High performance GPU preference plus NVIDIA's public NVAPI Driver Settings path when NVIDIA hardware is detected. Image Scaling and texture-filter quality reductions remain disabled."),
        new FixifyToolDefinition("amd-guide", "AMD Quality-Safe Gaming Profile", "GPU",
            "Apply Sabby's supported quality-safe game GPU preference on AMD systems and verify it.",
            "Uses supported Windows per-game graphics preference. Sabby does not invent or write undocumented Radeon profile data."),
        new FixifyToolDefinition("intel-guide", "Intel Quality-Safe Gaming Profile", "GPU",
            "Apply Sabby's supported quality-safe per-game GPU preference on Intel systems and verify it.",
            "Uses supported Windows per-game graphics preference while leaving proprietary Intel image controls application-controlled.")
    };

    public FixifyService(IMaintenanceService maintenance, GpuDriverIntegrationService gpuDriverIntegration, IAppLogger logger)
    {
        _maintenance = maintenance;
        _gpuDriverIntegration = gpuDriverIntegration;
        _logger = logger;
    }

    public async Task<FixifyRunResult> RunAsync(
        string id,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return id switch
            {
                "sfc" => await RunSingleAsync("sfc.exe", ["/scannow"], TimeSpan.FromMinutes(45), "System File Checker finished.", progress, cancellationToken).ConfigureAwait(false),
                "dism-restore" => await RunSingleAsync("dism.exe", ["/Online", "/Cleanup-Image", "/RestoreHealth"], TimeSpan.FromMinutes(60), "Windows image repair finished.", progress, cancellationToken).ConfigureAwait(false),
                "dism-check" => await RunSingleAsync("dism.exe", ["/Online", "/Cleanup-Image", "/CheckHealth"], TimeSpan.FromMinutes(10), "Component store check finished.", progress, cancellationToken).ConfigureAwait(false),
                "component-analyze" => await RunSingleAsync("dism.exe", ["/Online", "/Cleanup-Image", "/AnalyzeComponentStore"], TimeSpan.FromMinutes(30), "Component store analysis finished.", progress, cancellationToken).ConfigureAwait(false),
                "disk-scan" => await RunSingleAsync("chkdsk.exe", [GetSystemDrive(), "/scan"], TimeSpan.FromMinutes(45), "System drive scan finished.", progress, cancellationToken).ConfigureAwait(false),
                "flush-dns" => await RunSingleAsync("ipconfig.exe", ["/flushdns"], TimeSpan.FromSeconds(15), "DNS resolver cache was flushed.", progress, cancellationToken).ConfigureAwait(false),
                "winsock-reset" => (await RunSingleAsync("netsh.exe", ["winsock", "reset"], TimeSpan.FromSeconds(30), "Winsock reset completed.", progress, cancellationToken).ConfigureAwait(false)) with { RequiresRestart = true },
                "tcpip-reset" => (await RunSingleAsync("netsh.exe", ["int", "ip", "reset"], TimeSpan.FromSeconds(45), "TCP/IP reset completed.", progress, cancellationToken).ConfigureAwait(false)) with { RequiresRestart = true },
                "audio-services" => await RestartServicesAsync(["Audiosrv", "AudioEndpointBuilder"], cancellationToken).ConfigureAwait(false),
                "spooler-service" => await RestartServicesAsync(["Spooler"], cancellationToken).ConfigureAwait(false),
                "dhcp-service" => await RestartServicesAsync(["Dhcp"], cancellationToken).ConfigureAwait(false),
                "update-services" => await RestartServicesAsync(["wuauserv", "bits"], cancellationToken).ConfigureAwait(false),
                "search-service" => await RestartServicesAsync(["WSearch"], cancellationToken).ConfigureAwait(false),
                "temp-clean" => await CleanTempAsync(cancellationToken).ConfigureAwait(false),
                "dism-scan" => await RunSingleAsync("dism.exe", ["/Online", "/Cleanup-Image", "/ScanHealth"], TimeSpan.FromMinutes(40), "Deep component-store scan finished.", progress, cancellationToken).ConfigureAwait(false),
                "component-clean" => await RunSingleAsync("dism.exe", ["/Online", "/Cleanup-Image", "/StartComponentCleanup"], TimeSpan.FromMinutes(60), "Component-store cleanup finished.", progress, cancellationToken).ConfigureAwait(false),
                "defender-update" => await RunPowerShellAsync("Update-MpSignature -ErrorAction Stop; 'Microsoft Defender signatures updated.'", TimeSpan.FromMinutes(10), "Defender signatures were updated.", progress, cancellationToken).ConfigureAwait(false),
                "defender-quick" => await RunPowerShellAsync("Start-MpScan -ScanType QuickScan -ErrorAction Stop; 'Microsoft Defender quick scan completed.'", TimeSpan.FromMinutes(90), "Microsoft Defender quick scan completed.", progress, cancellationToken).ConfigureAwait(false),
                "device-problems" => await RunSingleAsync("pnputil.exe", ["/enum-devices", "/problem"], TimeSpan.FromMinutes(5), "Windows device-problem scan completed.", progress, cancellationToken).ConfigureAwait(false),
                "power-requests" => await RunSingleAsync("powercfg.exe", ["/requests"], TimeSpan.FromSeconds(30), "Windows power requests were queried.", progress, cancellationToken).ConfigureAwait(false),
                "dhcp-renew" => await RenewDhcpAsync(cancellationToken).ConfigureAwait(false),
                "register-dns" => await RunSingleAsync("ipconfig.exe", ["/registerdns"], TimeSpan.FromSeconds(30), "Dynamic DNS registration was requested.", progress, cancellationToken).ConfigureAwait(false),
                "nvidia-guide" => await RunGpuQualityProfileAsync("NVIDIA", progress, cancellationToken).ConfigureAwait(false),
                "amd-guide" => await RunGpuQualityProfileAsync("AMD", progress, cancellationToken).ConfigureAwait(false),
                "intel-guide" => await RunGpuQualityProfileAsync("Intel", progress, cancellationToken).ConfigureAwait(false),
                _ => new FixifyRunResult(false, "Unknown repair tool.", id)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Fixify tool failed: {id}.", ex);
            return new FixifyRunResult(false, "The repair failed safely.", ex.Message);
        }
    }

    private Task<FixifyRunResult> RunPowerShellAsync(string script, TimeSpan timeout, string successSummary, IProgress<double>? progress, CancellationToken cancellationToken) =>
        RunSingleAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], timeout, successSummary, progress, cancellationToken);

    private async Task<FixifyRunResult> RenewDhcpAsync(CancellationToken cancellationToken)
    {
        var release = await RunProcessAsync("ipconfig.exe", ["/release"], TimeSpan.FromSeconds(30), null, cancellationToken).ConfigureAwait(false);
        var renew = await RunProcessAsync("ipconfig.exe", ["/renew"], TimeSpan.FromMinutes(2), null, cancellationToken).ConfigureAwait(false);
        var details = CompactOutput(release.Output + Environment.NewLine + renew.Output, release.Error + Environment.NewLine + renew.Error);
        return renew.Success
            ? new FixifyRunResult(true, "DHCP lease was renewed.", details)
            : new FixifyRunResult(false, "DHCP renewal did not complete successfully.", details);
    }

    private async Task<FixifyRunResult> RunGpuQualityProfileAsync(string expectedVendor, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(5);
        var detected = await _gpuDriverIntegration.DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!detected.Vendor.Equals(expectedVendor, StringComparison.OrdinalIgnoreCase))
        {
            return new FixifyRunResult(false, $"{expectedVendor} GPU profile is not applicable on this PC.",
                $"Detected GPU vendor: {detected.Vendor}. Use the GPU Drivers page for the profile supported by this hardware.");
        }

        progress?.Report(15);
        var nested = new Progress<double>(value => progress?.Report(15 + (value * 0.85)));
        var result = await _gpuDriverIntegration.ApplyQualitySafeAsync(nested, cancellationToken).ConfigureAwait(false);
        progress?.Report(100);
        return result.Success
            ? new FixifyRunResult(true, $"{expectedVendor} quality-safe performance profile applied and verified.", result.Message)
            : new FixifyRunResult(false, $"{expectedVendor} profile could not be fully verified.", result.Message);
    }

    private async Task<FixifyRunResult> CleanTempAsync(CancellationToken cancellationToken)
    {
        var result = await _maintenance.CleanTemporaryFilesAsync(cancellationToken).ConfigureAwait(false);
        var summary = result.FileCount == 0
            ? "No removable TEMP files were deleted."
            : $"Removed {result.FileCount:N0} TEMP file(s) ({result.SizeText}).";
        var details = result.SkippedCount == 0
            ? "No locked files were encountered."
            : $"{result.SkippedCount:N0} locked/in-use item(s) were skipped.";
        return new FixifyRunResult(true, summary, details);
    }

    private async Task<FixifyRunResult> RestartServicesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var details = new List<string>();
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunProcessAsync("sc.exe", ["stop", name], TimeSpan.FromSeconds(15), null, cancellationToken).ConfigureAwait(false);
            await Task.Delay(350, cancellationToken).ConfigureAwait(false);
            var start = await RunProcessAsync("sc.exe", ["start", name], TimeSpan.FromSeconds(20), null, cancellationToken).ConfigureAwait(false);
            await Task.Delay(450, cancellationToken).ConfigureAwait(false);
            var query = await RunProcessAsync("sc.exe", ["query", name], TimeSpan.FromSeconds(10), null, cancellationToken).ConfigureAwait(false);
            var running = query.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
            details.Add($"{name}: {(running ? "Running" : "Not running")}");
            if (!running && !start.Success)
                return new FixifyRunResult(false, $"{name} could not be restarted.", string.Join(Environment.NewLine, details));
        }

        return new FixifyRunResult(true, "Service restart verified.", string.Join(Environment.NewLine, details));
    }

    private async Task<FixifyRunResult> RunSingleAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string successSummary,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(fileName, arguments, timeout, progress, cancellationToken).ConfigureAwait(false);
        var details = CompactOutput(result.Output, result.Error);
        _logger.Info($"Fixify command {fileName} {string.Join(" ", arguments)} -> Exit {result.ExitCode}.");
        return result.Success
            ? new FixifyRunResult(true, successSummary, details)
            : new FixifyRunResult(false, $"{Path.GetFileNameWithoutExtension(fileName)} returned exit code {result.ExitCode}.", details);
    }

    private static string GetSystemDrive()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        return string.IsNullOrWhiteSpace(root) ? "C:" : root.TrimEnd(Path.DirectorySeparatorChar);
    }

    private static string CompactOutput(string output, string error)
    {
        var combined = string.Join(Environment.NewLine, new[] { output, error }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        if (combined.Length <= 1600) return combined;
        return combined[^1600..];
    }

    private sealed record ProcessResult(bool Success, int ExitCode, string Output, string Error);

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
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

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var error = new StringBuilder();
        var progressGate = new object();
        double lastProgress = -1;

        void ParseProgress(string? line)
        {
            if (progress is null || string.IsNullOrWhiteSpace(line)) return;
            foreach (Match match in Regex.Matches(line, @"(?<!\d)(\d{1,3}(?:\.\d+)?)\s*%"))
            {
                if (!double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                    continue;
                value = Math.Clamp(value, 0, 100);
                lock (progressGate)
                {
                    if (value < lastProgress && lastProgress > 90) continue;
                    if (Math.Abs(value - lastProgress) < 0.25) continue;
                    lastProgress = value;
                }
                progress.Report(value);
            }
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            output.AppendLine(e.Data);
            ParseProgress(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            error.AppendLine(e.Data);
            ParseProgress(e.Data);
        };

        if (!process.Start())
            return new ProcessResult(false, -1, string.Empty, "Could not start the repair command.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            // Give async output callbacks a brief chance to flush their final lines.
            await Task.Delay(60, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(false, -2, output.ToString(), "The command timed out.");
        }

        if (process.ExitCode == 0)
            progress?.Report(100);

        return new ProcessResult(process.ExitCode == 0, process.ExitCode, output.ToString(), error.ToString());
    }
}
