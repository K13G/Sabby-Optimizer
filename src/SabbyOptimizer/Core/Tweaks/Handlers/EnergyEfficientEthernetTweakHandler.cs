using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class EnergyEfficientEthernetTweakHandler : ITweakHandler, ITweakCompatibilityProvider
{
    private sealed record AdapterState(string Name, string RegistryValue);
    private readonly string _restoreFile;

    public EnergyEfficientEthernetTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "network-eee.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapterAdvancedProperty") &&
        PowerShellNetworkAccess.CommandExists("Set-NetAdapterAdvancedProperty");

    public TweakDefinition Definition { get; } = new(
        "network.eee",
        "Energy Efficient Ethernet",
        "Disable standardized IEEE 802.3az Energy Efficient Ethernet on supported active wired adapters for a latency/consistency-focused desktop profile.",
        TweakCategory.Network,
        TweakSafetyLevel.Moderate,
        "Windows defines *EEE as the standardized Energy Efficient Ethernet driver keyword. EEE reduces power use by allowing low-power link behavior. Disabling it can avoid those power-state transitions on a plugged-in performance desktop, but it can increase power use and is not guaranteed to reduce game ping.",
        "The standardized *EEE advanced property on connected physical Ethernet adapters that actually expose it.",
        "Undo restores the exact per-adapter *EEE value recorded before Sabby changed it.",
        true,
        false,
        true,
        true);

    public Task<TweakCompatibilityResult> CheckCompatibilityAsync(bool applying, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakCompatibilityResult.Blocked("No connected physical Ethernet adapter exposes the standardized *EEE property."));
        return Task.FromResult(TweakCompatibilityResult.Compatible(
            $"{states.Count} compatible Ethernet adapter{(states.Count == 1 ? string.Empty : "s")} detected.",
            applying ? "The Ethernet link may briefly reconnect while the driver applies the new property." : null));
    }

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakDetectionResult.Unavailable("No connected physical Ethernet adapter exposes Energy Efficient Ethernet."));

        var allDisabled = states.All(x => x.RegistryValue == "0");
        return Task.FromResult(allDisabled
            ? new TweakDetectionResult(TweakStateKind.Applied, "EEE disabled", "Energy Efficient Ethernet is disabled on all supported connected wired adapters.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, "EEE enabled", "At least one supported connected wired adapter has Energy Efficient Ethernet enabled.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No compatible Ethernet adapter was found."));

        SavePreviousIfMissing(states);
        foreach (var state in states)
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterAdvancedProperty -Name '{name}' -RegistryKeyword '*EEE' -RegistryValue 0 -Confirm:$false");
            if (!result.Success)
                return Task.FromResult(TweakOperationResult.Failed($"Could not disable Energy Efficient Ethernet on {state.Name}. {result.Error}".Trim()));
        }

        return Task.FromResult(TweakOperationResult.Completed(
            "Energy Efficient Ethernet was disabled on supported active wired adapters.",
            new TweakDetectionResult(TweakStateKind.Applied, "EEE disabled", "Energy Efficient Ethernet is disabled on supported adapters.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No previous Energy Efficient Ethernet state was recorded."));

        foreach (var state in previous)
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var value = PowerShellNetworkAccess.Escape(state.RegistryValue);
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterAdvancedProperty -Name '{name}' -RegistryKeyword '*EEE' -RegistryValue '{value}' -Confirm:$false");
            if (!result.Success)
                return Task.FromResult(TweakOperationResult.Failed($"Could not restore Energy Efficient Ethernet on {state.Name}. {result.Error}".Trim()));
        }

        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed(
            "Energy Efficient Ethernet values were restored to their previous per-adapter state.",
            new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous Energy Efficient Ethernet values restored.", true, false)));
    }

    private static List<AdapterState> ReadStates()
    {
        const string script = "$items=@(Get-NetAdapter -Physical | Where-Object {$_.Status -eq 'Up' -and $_.InterfaceType -eq 6} | ForEach-Object { $a=$_; $p=Get-NetAdapterAdvancedProperty -Name $a.Name -RegistryKeyword '*EEE' -AllProperties -ErrorAction SilentlyContinue | Select-Object -First 1; if($p){ [pscustomobject]@{Name=$a.Name;RegistryValue=[string]($p.RegistryValue | Select-Object -First 1)} } }); ConvertTo-Json -InputObject $items -Compress";
        var result = PowerShellNetworkAccess.Run(script, 10000);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return new();
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            if (result.Output.TrimStart().StartsWith("[", StringComparison.Ordinal))
                return JsonSerializer.Deserialize<List<AdapterState>>(result.Output, options) ?? new();
            var one = JsonSerializer.Deserialize<AdapterState>(result.Output, options);
            return one is null ? new() : [one];
        }
        catch { return new(); }
    }

    private void SavePreviousIfMissing(List<AdapterState> states) { try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, JsonSerializer.Serialize(states)); } catch { } }
    private List<AdapterState> ReadPrevious() { try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<List<AdapterState>>(File.ReadAllText(_restoreFile)) ?? new() : new(); } catch { return new(); } }
    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }
}
