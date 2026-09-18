using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class D0PacketCoalescingTweakHandler : ITweakHandler
{
    private sealed record AdapterState(string Name, string D0PacketCoalescing);
    private readonly string _restoreFile;

    public D0PacketCoalescingTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "d0-packet-coalescing.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapterPowerManagement") &&
        PowerShellNetworkAccess.CommandExists("Disable-NetAdapterPowerManagement") &&
        PowerShellNetworkAccess.CommandExists("Enable-NetAdapterPowerManagement");

    public TweakDefinition Definition { get; } = new(
        "network.d0-packet-coalescing",
        "NIC Packet Coalescing",
        "Disable supported D0 packet coalescing on active physical adapters for a latency-first desktop profile.",
        TweakCategory.Network,
        TweakSafetyLevel.Advanced,
        "D0 packet coalescing is a Windows network-adapter power feature that can combine packets to reduce wakeups. Disabling it can favor immediate packet delivery at the cost of additional CPU/power activity. Sabby exposes it only when the driver reports support.",
        "The D0PacketCoalescing power-management feature on supported connected physical network adapters.",
        "Undo restores the exact Enabled/Disabled state recorded for every supported adapter.",
        true,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var supported = ReadStates().Where(x => !IsUnsupported(x.D0PacketCoalescing)).ToList();
        if (supported.Count == 0) return Task.FromResult(TweakDetectionResult.Unavailable("No connected physical adapter reports D0 packet coalescing support."));
        var anyEnabled = supported.Any(x => IsEnabled(x.D0PacketCoalescing));
        return Task.FromResult(anyEnabled
            ? new TweakDetectionResult(TweakStateKind.NotApplied, "Coalescing on", "At least one supported active NIC has D0 packet coalescing enabled.", true, false)
            : new TweakDetectionResult(TweakStateKind.Applied, "Coalescing off", "D0 packet coalescing is disabled on supported active NICs.", false, File.Exists(_restoreFile)));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        var supported = states.Where(x => !IsUnsupported(x.D0PacketCoalescing)).ToList();
        if (supported.Count == 0) return Task.FromResult(TweakOperationResult.Failed("No active adapter exposes D0 packet coalescing."));
        SavePreviousIfMissing(states);
        foreach (var state in supported)
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var result = PowerShellNetworkAccess.Run($"Disable-NetAdapterPowerManagement -Name '{name}' -D0PacketCoalescing -NoRestart -Confirm:$false");
            if (!result.Success) return Task.FromResult(TweakOperationResult.Failed($"Could not disable packet coalescing on {state.Name}. {result.Error}".Trim()));
        }
        return Task.FromResult(TweakOperationResult.Completed("D0 packet coalescing was disabled on supported active adapters.", new TweakDetectionResult(TweakStateKind.Applied, "Coalescing off", "D0 packet coalescing is disabled.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous.Count == 0) return Task.FromResult(TweakOperationResult.Failed("No previous packet-coalescing state was recorded."));
        foreach (var state in previous.Where(x => !IsUnsupported(x.D0PacketCoalescing)))
        {
            var cmd = IsEnabled(state.D0PacketCoalescing) ? "Enable-NetAdapterPowerManagement" : "Disable-NetAdapterPowerManagement";
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var result = PowerShellNetworkAccess.Run($"{cmd} -Name '{name}' -D0PacketCoalescing -NoRestart -Confirm:$false");
            if (!result.Success) return Task.FromResult(TweakOperationResult.Failed($"Could not restore packet coalescing on {state.Name}. {result.Error}".Trim()));
        }
        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed("The previous D0 packet-coalescing states were restored.", new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous packet-coalescing states restored.", true, false)));
    }

    private static bool IsEnabled(string value) => value.Equals("Enabled", StringComparison.OrdinalIgnoreCase);
    private static bool IsUnsupported(string value) => string.IsNullOrWhiteSpace(value) || value.Equals("Unsupported", StringComparison.OrdinalIgnoreCase);

    private static List<AdapterState> ReadStates()
    {
        const string script = "$items=@(Get-NetAdapter -Physical | Where-Object {$_.Status -eq 'Up'} | ForEach-Object { try { $p=Get-NetAdapterPowerManagement -Name $_.Name -ErrorAction Stop; [pscustomobject]@{Name=$_.Name;D0PacketCoalescing=[string]$p.D0PacketCoalescing} } catch {} }); ConvertTo-Json -InputObject $items -Compress";
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
