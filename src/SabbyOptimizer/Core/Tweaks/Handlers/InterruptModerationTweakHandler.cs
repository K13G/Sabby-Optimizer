using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class InterruptModerationTweakHandler : ITweakHandler
{
    private sealed record AdapterState(string Name, string[] RegistryValue);
    private readonly string _restoreFile;

    public InterruptModerationTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "interrupt-moderation.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapterAdvancedProperty") &&
        PowerShellNetworkAccess.CommandExists("Set-NetAdapterAdvancedProperty");

    public TweakDefinition Definition { get; } = new(
        "network.interrupt-moderation",
        "Low-Latency Interrupt Mode",
        "Disable NIC interrupt moderation on supported active adapters for lower packet response latency at the cost of more CPU interrupts.",
        TweakCategory.Network,
        TweakSafetyLevel.Advanced,
        "Microsoft documents the trade-off clearly: interrupt moderation reduces CPU interrupt overhead and can improve throughput under heavy load, but moderation can increase response time. This option is therefore situational, not automatically better. Sabby only exposes it when the adapter advertises the standardized InterruptModeration property.",
        "The standardized *InterruptModeration advanced property on supported connected hardware adapters.",
        "Undo restores the exact registry value reported for each adapter before Sabby disabled moderation.",
        true,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0) return Task.FromResult(TweakDetectionResult.Unavailable("No connected physical adapter exposes interrupt moderation."));
        var disabled = states.All(x => x.RegistryValue.Any(v => v == "0"));
        return Task.FromResult(disabled
            ? new TweakDetectionResult(TweakStateKind.Applied, "Low latency", "Interrupt moderation is disabled on all supported connected adapters.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, "Moderated", "At least one supported adapter is using interrupt moderation. This normally reduces CPU overhead but can add response latency.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0) return Task.FromResult(TweakOperationResult.Failed("No adapter with interrupt moderation support was found."));
        SavePreviousIfMissing(states);
        foreach (var state in states)
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterAdvancedProperty -Name '{name}' -RegistryKeyword '*InterruptModeration' -RegistryValue 0 -NoRestart");
            if (!result.Success) return Task.FromResult(TweakOperationResult.Failed($"Could not disable interrupt moderation on {state.Name}. {result.Error}".Trim()));
        }
        return Task.FromResult(TweakOperationResult.Completed("Interrupt moderation was disabled on supported connected adapters. Reconnecting/restarting the adapter may be required by some drivers.", new TweakDetectionResult(TweakStateKind.Applied, "Low latency", "Interrupt moderation is disabled on supported adapters.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous.Count == 0) return Task.FromResult(TweakOperationResult.Failed("No previous interrupt-moderation state was recorded."));
        foreach (var state in previous)
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var values = string.Join(",", state.RegistryValue.Select(v => $"'{PowerShellNetworkAccess.Escape(v)}'"));
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterAdvancedProperty -Name '{name}' -RegistryKeyword '*InterruptModeration' -RegistryValue @({values}) -NoRestart");
            if (!result.Success) return Task.FromResult(TweakOperationResult.Failed($"Could not restore interrupt moderation on {state.Name}. {result.Error}".Trim()));
        }
        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed("Interrupt moderation was restored to the previous per-adapter state.", new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous interrupt-moderation values restored.", true, false)));
    }

    private static List<AdapterState> ReadStates()
    {
        const string script = "$items=@(Get-NetAdapter -IncludeHidden | Where-Object {$_.Status -eq 'Up' -and $_.HardwareInterface -eq $true} | ForEach-Object { $a=$_; $p=Get-NetAdapterAdvancedProperty -Name $a.Name -RegistryKeyword '*InterruptModeration' -ErrorAction SilentlyContinue; if($p){ [pscustomobject]@{Name=$a.Name;RegistryValue=@($p.RegistryValue | ForEach-Object {[string]$_})} } }); ConvertTo-Json -InputObject $items -Compress -Depth 4";
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
