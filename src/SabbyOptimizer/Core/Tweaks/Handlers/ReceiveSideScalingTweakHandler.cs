using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class ReceiveSideScalingTweakHandler : ITweakHandler
{
    private sealed record AdapterState(string Name, bool Enabled);
    private readonly string _restoreFile;

    public ReceiveSideScalingTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "network-rss.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapterRss") &&
        PowerShellNetworkAccess.CommandExists("Set-NetAdapterRss");

    public TweakDefinition Definition { get; } = new(
        "network.rss",
        "Receive Side Scaling",
        "Enable RSS on connected physical adapters so receive processing can be distributed across CPU cores.",
        TweakCategory.Network,
        TweakSafetyLevel.Safe,
        "Receive Side Scaling is a Windows networking technology that distributes receive processing across multiple processors. Microsoft documents RSS as a way to reduce a single-CPU receive bottleneck and improve network scalability on supported adapters.",
        "RSS enabled state on connected physical network adapters that report RSS support.",
        "Undo restores the RSS enabled state recorded for each adapter before Sabby changed it.",
        true,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakDetectionResult.Unavailable("No connected physical adapter reported RSS support."));

        var disabled = states.Count(x => !x.Enabled);
        if (disabled == 0)
            return Task.FromResult(new TweakDetectionResult(TweakStateKind.Applied, "Enabled", $"RSS is enabled on all {states.Count} supported connected adapter(s).", false, File.Exists(_restoreFile)));

        return Task.FromResult(new TweakDetectionResult(TweakStateKind.NotApplied, $"{disabled} disabled", $"RSS is disabled on {disabled} of {states.Count} supported connected adapter(s).", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No connected physical adapter reported RSS support."));

        SavePreviousIfMissing(states);
        foreach (var state in states.Where(x => !x.Enabled))
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterRss -Name '{name}' -Enabled $true -NoRestart -Confirm:$false");
            if (!result.Success)
                return Task.FromResult(TweakOperationResult.Failed($"Could not enable RSS on {state.Name}. {result.Error}".Trim()));
        }

        return Task.FromResult(TweakOperationResult.Completed(
            "RSS was enabled on supported connected physical adapters.",
            new TweakDetectionResult(TweakStateKind.Applied, "Enabled", "RSS is enabled on supported connected physical adapters.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No saved RSS state exists, so Sabby did not guess."));

        foreach (var state in previous)
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var enabled = state.Enabled ? "$true" : "$false";
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterRss -Name '{name}' -Enabled {enabled} -NoRestart -Confirm:$false");
            if (!result.Success)
                return Task.FromResult(TweakOperationResult.Failed($"Could not restore RSS on {state.Name}. {result.Error}".Trim()));
        }

        TryDeleteRestoreFile();
        return Task.FromResult(TweakOperationResult.Completed(
            "RSS settings were restored to their previous per-adapter states.",
            new TweakDetectionResult(TweakStateKind.Custom, "Restored", "RSS settings were restored to their saved per-adapter states.", true, false)));
    }

    private static List<AdapterState> ReadStates()
    {
        const string script = "$items=@(Get-NetAdapter -Physical | Where-Object {$_.Status -eq 'Up'} | ForEach-Object { try { $r=Get-NetAdapterRss -Name $_.Name -ErrorAction Stop; [pscustomobject]@{Name=$_.Name;Enabled=[bool]$r.Enabled} } catch {} }); ConvertTo-Json -InputObject $items -Compress";
        var result = PowerShellNetworkAccess.Run(script);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return new();
        try { return JsonSerializer.Deserialize<List<AdapterState>>(result.Output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
        catch { return new(); }
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

    private void TryDeleteRestoreFile() { try { File.Delete(_restoreFile); } catch { } }
}
