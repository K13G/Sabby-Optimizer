using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class ChecksumOffloadTweakHandler : ITweakHandler
{
    private sealed record AdapterState(string Name, string IpIPv4Enabled, string TcpIPv4Enabled, string TcpIPv6Enabled, string UdpIPv4Enabled, string UdpIPv6Enabled);
    private readonly string _restoreFile;

    public ChecksumOffloadTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "checksum-offload.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapterChecksumOffload") &&
        PowerShellNetworkAccess.CommandExists("Set-NetAdapterChecksumOffload");

    public TweakDefinition Definition { get; } = new(
        "network.checksum-offload",
        "Checksum Offload",
        "Keep IPv4/TCP/UDP checksum offloads enabled in both directions on supported connected adapters.",
        TweakCategory.Network,
        TweakSafetyLevel.Safe,
        "Microsoft recommends keeping address checksum offloads enabled because they reduce CPU work and are prerequisites for other stateless offload features. This card mainly repairs systems where a tweak pack disabled them.",
        "IPv4, TCPv4, TCPv6, UDPv4, and UDPv6 checksum-offload directions on connected physical adapters. Sabby's target is RxTxEnabled where supported.",
        "Undo restores the exact five checksum-offload direction values captured for each adapter before Sabby changed them.",
        true,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0) return Task.FromResult(TweakDetectionResult.Unavailable("No connected physical adapter exposed checksum-offload settings."));
        var all = states.All(IsFullyEnabled);
        return Task.FromResult(all
            ? new TweakDetectionResult(TweakStateKind.Applied, "Fully enabled", "Checksum offloads are RxTxEnabled on all supported connected adapters.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, "Partially disabled", "At least one supported checksum offload is not enabled for both receive and transmit.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0) return Task.FromResult(TweakOperationResult.Failed("No checksum-offload capable connected adapter was found."));
        SavePreviousIfMissing(states);
        foreach (var state in states)
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterChecksumOffload -Name '{name}' -IpIPv4Enabled RxTxEnabled -TcpIPv4Enabled RxTxEnabled -TcpIPv6Enabled RxTxEnabled -UdpIPv4Enabled RxTxEnabled -UdpIPv6Enabled RxTxEnabled -NoRestart");
            if (!result.Success) return Task.FromResult(TweakOperationResult.Failed($"Could not enable checksum offloads on {state.Name}. {result.Error}".Trim()));
        }
        return Task.FromResult(TweakOperationResult.Completed("Checksum offloads were enabled in both directions on supported adapters.", new TweakDetectionResult(TweakStateKind.Applied, "Fully enabled", "Checksum offloads are RxTxEnabled.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous.Count == 0) return Task.FromResult(TweakOperationResult.Failed("No previous checksum-offload state was recorded."));
        foreach (var s in previous)
        {
            var name = PowerShellNetworkAccess.Escape(s.Name);
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterChecksumOffload -Name '{name}' -IpIPv4Enabled {SafeDirection(s.IpIPv4Enabled)} -TcpIPv4Enabled {SafeDirection(s.TcpIPv4Enabled)} -TcpIPv6Enabled {SafeDirection(s.TcpIPv6Enabled)} -UdpIPv4Enabled {SafeDirection(s.UdpIPv4Enabled)} -UdpIPv6Enabled {SafeDirection(s.UdpIPv6Enabled)} -NoRestart");
            if (!result.Success) return Task.FromResult(TweakOperationResult.Failed($"Could not restore checksum offloads on {s.Name}. {result.Error}".Trim()));
        }
        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed("Previous checksum-offload states were restored.", new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous checksum-offload directions restored.", true, false)));
    }

    private static string SafeDirection(string value) => value is "Disabled" or "TxEnabled" or "RxEnabled" or "RxTxEnabled" ? value : "RxTxEnabled";
    private static bool IsFullyEnabled(AdapterState s) => s.IpIPv4Enabled == "RxTxEnabled" && s.TcpIPv4Enabled == "RxTxEnabled" && s.TcpIPv6Enabled == "RxTxEnabled" && s.UdpIPv4Enabled == "RxTxEnabled" && s.UdpIPv6Enabled == "RxTxEnabled";

    private static List<AdapterState> ReadStates()
    {
        const string script = "$items=@(Get-NetAdapter -IncludeHidden | Where-Object {$_.Status -eq 'Up' -and $_.HardwareInterface -eq $true} | ForEach-Object { $a=$_; try { $c=Get-NetAdapterChecksumOffload -Name $a.Name -ErrorAction Stop; [pscustomobject]@{Name=$a.Name;IpIPv4Enabled=[string]$c.IpIPv4Enabled;TcpIPv4Enabled=[string]$c.TcpIPv4Enabled;TcpIPv6Enabled=[string]$c.TcpIPv6Enabled;UdpIPv4Enabled=[string]$c.UdpIPv4Enabled;UdpIPv6Enabled=[string]$c.UdpIPv6Enabled} } catch {} }); ConvertTo-Json -InputObject $items -Compress -Depth 4";
        var result = PowerShellNetworkAccess.Run(script, 10000);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return new();
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            if (result.Output.TrimStart().StartsWith("[", StringComparison.Ordinal)) return JsonSerializer.Deserialize<List<AdapterState>>(result.Output, options) ?? new();
            var one = JsonSerializer.Deserialize<AdapterState>(result.Output, options); return one is null ? new() : [one];
        }
        catch { return new(); }
    }

    private void SavePreviousIfMissing(List<AdapterState> states) { try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, JsonSerializer.Serialize(states)); } catch { } }
    private List<AdapterState> ReadPrevious() { try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<List<AdapterState>>(File.ReadAllText(_restoreFile)) ?? new() : new(); } catch { return new(); } }
    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }
}
