using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class LowLatencyRscTweakHandler : ITweakHandler
{
    private sealed record AdapterState(string Name, bool IPv4Enabled, bool IPv6Enabled);
    private readonly string _restoreFile;

    public LowLatencyRscTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "rsc-low-latency.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapter") &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapterRsc") &&
        PowerShellNetworkAccess.CommandExists("Set-NetAdapterRsc");

    public TweakDefinition Definition { get; } = new(
        "network.rsc-low-latency",
        "Low-Latency Receive Coalescing",
        "Disable RSC on supported active adapters for latency-sensitive testing while accepting a possible throughput/CPU-efficiency trade-off.",
        TweakCategory.Network,
        TweakSafetyLevel.Advanced,
        "Receive Segment Coalescing combines multiple received packets before the Windows network stack processes them. Microsoft recommends RSC for receive-heavy throughput workloads, but notes that low-latency, low-throughput workloads can sometimes benefit from RSC being off. Sabby therefore labels this as situational rather than a universal gaming improvement.",
        "IPv4 and IPv6 RSC enable state on connected physical adapters that advertise RSC support.",
        "Undo restores the exact IPv4 and IPv6 RSC states captured before Sabby disabled them.",
        true,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakDetectionResult.Unavailable("No connected physical adapter exposed RSC settings."));

        var allDisabled = states.All(x => !x.IPv4Enabled && !x.IPv6Enabled);
        return Task.FromResult(allDisabled
            ? new TweakDetectionResult(TweakStateKind.Applied, "RSC off", "RSC is disabled for IPv4 and IPv6 on all supported connected adapters.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, "RSC enabled", "At least one supported adapter still has receive segment coalescing enabled.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No RSC-capable connected adapter was found."));

        SavePreviousIfMissing(states);
        foreach (var state in states)
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterRsc -Name '{name}' -IPv4Enabled $false -IPv6Enabled $false -NoRestart");
            if (!result.Success)
            {
                Restore(states);
                return Task.FromResult(TweakOperationResult.Failed($"Could not disable RSC on {state.Name}. Previous RSC states were restored where possible. {result.Error}".Trim()));
            }
        }

        return Task.FromResult(TweakOperationResult.Completed(
            "RSC was disabled on supported connected adapters for low-latency testing. If throughput or CPU usage becomes worse, use Deactivate to restore the original state.",
            new TweakDetectionResult(TweakStateKind.Applied, "RSC off", "RSC is configured off on supported connected adapters.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No previous RSC state was recorded."));

        var failure = Restore(previous);
        if (!string.IsNullOrWhiteSpace(failure))
            return Task.FromResult(TweakOperationResult.Failed(failure));

        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed(
            "Previous receive segment coalescing states were restored.",
            new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous IPv4/IPv6 RSC states restored.", true, false)));
    }

    private static List<AdapterState> ReadStates()
    {
        const string script = "$items=@(Get-NetAdapter -IncludeHidden | Where-Object {$_.Status -eq 'Up' -and $_.HardwareInterface -eq $true} | ForEach-Object { $a=$_; try { $r=Get-NetAdapterRsc -Name $a.Name -ErrorAction Stop; [pscustomobject]@{Name=$a.Name;IPv4Enabled=[bool]$r.IPv4Enabled;IPv6Enabled=[bool]$r.IPv6Enabled} } catch {} }); ConvertTo-Json -InputObject $items -Compress -Depth 4";
        var result = PowerShellNetworkAccess.Run(script, 10000);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return new();
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            if (result.Output.TrimStart().StartsWith("[", StringComparison.Ordinal))
                return JsonSerializer.Deserialize<List<AdapterState>>(result.Output, options) ?? new();
            var one = JsonSerializer.Deserialize<AdapterState>(result.Output, options);
            return one is null ? new() : new List<AdapterState> { one };
        }
        catch { return new(); }
    }

    private static string? Restore(IEnumerable<AdapterState> states)
    {
        foreach (var state in states)
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var ipv4 = state.IPv4Enabled ? "$true" : "$false";
            var ipv6 = state.IPv6Enabled ? "$true" : "$false";
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterRsc -Name '{name}' -IPv4Enabled {ipv4} -IPv6Enabled {ipv6} -NoRestart");
            if (!result.Success)
                return $"Could not restore RSC on {state.Name}. {result.Error}".Trim();
        }
        return null;
    }

    private void SavePreviousIfMissing(List<AdapterState> states)
    {
        try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, JsonSerializer.Serialize(states)); } catch { }
    }

    private List<AdapterState> ReadPrevious()
    {
        try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<List<AdapterState>>(File.ReadAllText(_restoreFile)) ?? new() : new(); }
        catch { return new(); }
    }

    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }
}
