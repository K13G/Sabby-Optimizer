using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class LargeSendOffloadTweakHandler : ITweakHandler
{
    private sealed record AdapterState(string Name, bool IPv4Enabled, bool IPv6Enabled);
    private readonly string _restoreFile;

    public LargeSendOffloadTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "large-send-offload.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapter") &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapterLso") &&
        PowerShellNetworkAccess.CommandExists("Set-NetAdapterLso");

    public TweakDefinition Definition { get; } = new(
        "network.lso",
        "Large Send Offload",
        "Keep LSO enabled on supported active adapters to reduce CPU segmentation work and improve high-throughput sends.",
        TweakCategory.Network,
        TweakSafetyLevel.Safe,
        "Large Send Offload lets the NIC segment large TCP sends instead of making the CPU split every frame. Microsoft documents it as a way to increase high-end send speed and reduce processor usage. This is primarily a throughput/CPU-efficiency setting, not a guaranteed ping reduction.",
        "LSOv2 IPv4 and IPv6 enable state on connected physical adapters that expose LSO.",
        "Undo restores the exact IPv4 and IPv6 LSO states captured for each adapter before Sabby changed them.",
        true,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakDetectionResult.Unavailable("No connected physical adapter exposed Large Send Offload settings."));

        var allEnabled = states.All(x => x.IPv4Enabled && x.IPv6Enabled);
        return Task.FromResult(allEnabled
            ? new TweakDetectionResult(TweakStateKind.Applied, "LSO enabled", "Large Send Offload is enabled for IPv4 and IPv6 on all supported connected adapters.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, "LSO partly off", "At least one supported connected adapter has IPv4 or IPv6 Large Send Offload disabled.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No LSO-capable connected adapter was found."));

        SavePreviousIfMissing(states);
        foreach (var state in states)
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterLso -Name '{name}' -IPv4Enabled $true -IPv6Enabled $true -NoRestart");
            if (!result.Success)
            {
                Restore(states);
                return Task.FromResult(TweakOperationResult.Failed($"Could not enable LSO on {state.Name}. Previous LSO states were restored where possible. {result.Error}".Trim()));
            }
        }

        return Task.FromResult(TweakOperationResult.Completed(
            "Large Send Offload was enabled on supported connected adapters. Some drivers may require reconnecting/restarting the adapter before the new state is operational.",
            new TweakDetectionResult(TweakStateKind.Applied, "LSO enabled", "LSO is configured on supported connected adapters.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No previous LSO state was recorded."));

        var failure = Restore(previous);
        if (!string.IsNullOrWhiteSpace(failure))
            return Task.FromResult(TweakOperationResult.Failed(failure));

        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed(
            "Previous Large Send Offload states were restored.",
            new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous IPv4/IPv6 LSO states restored.", true, false)));
    }

    private static List<AdapterState> ReadStates()
    {
        const string script = "$items=@(Get-NetAdapter -IncludeHidden | Where-Object {$_.Status -eq 'Up' -and $_.HardwareInterface -eq $true} | ForEach-Object { $a=$_; try { $l=Get-NetAdapterLso -Name $a.Name -ErrorAction Stop; [pscustomobject]@{Name=$a.Name;IPv4Enabled=[bool]$l.IPv4Enabled;IPv6Enabled=[bool]$l.IPv6Enabled} } catch {} }); ConvertTo-Json -InputObject $items -Compress -Depth 4";
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
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterLso -Name '{name}' -IPv4Enabled {ipv4} -IPv6Enabled {ipv6} -NoRestart");
            if (!result.Success)
                return $"Could not restore LSO on {state.Name}. {result.Error}".Trim();
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
