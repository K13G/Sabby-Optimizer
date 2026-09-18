using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class NetworkPowerSavingTweakHandler : ITweakHandler
{
    private sealed record AdapterPowerState(string Name, string SelectiveSuspend, string DeviceSleepOnDisconnect);
    private readonly string _restoreFile;

    public NetworkPowerSavingTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "network-power-saving.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapterPowerManagement") &&
        PowerShellNetworkAccess.CommandExists("Disable-NetAdapterPowerManagement") &&
        PowerShellNetworkAccess.CommandExists("Enable-NetAdapterPowerManagement");

    public TweakDefinition Definition { get; } = new(
        "network.power-saving",
        "NIC Sleep & Suspend",
        "Disable supported adapter sleep/suspend features on connected physical NICs for consistency-focused desktop use.",
        TweakCategory.Network,
        TweakSafetyLevel.Moderate,
        "Windows exposes network-adapter power features such as Device Sleep on Disconnect and NDIS Selective Suspend. They exist to reduce power usage. On a plugged-in gaming desktop, disabling the supported sleep/suspend features can favor consistent adapter availability, but it is not a universal ping reduction and can increase power use.",
        "Only DeviceSleepOnDisconnect and SelectiveSuspend on connected physical adapters that report those features as supported. Wake-on-LAN and protocol offloads are left alone.",
        "Undo restores each supported feature to the exact Enabled/Disabled state recorded before Sabby changed it.",
        true,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        var supported = states.Where(HasSupportedFeature).ToList();
        if (supported.Count == 0)
            return Task.FromResult(TweakDetectionResult.Unavailable("No connected physical adapter reported the sleep/suspend power features Sabby manages."));

        var anyEnabled = supported.Any(s => IsEnabled(s.SelectiveSuspend) || IsEnabled(s.DeviceSleepOnDisconnect));
        return Task.FromResult(anyEnabled
            ? new TweakDetectionResult(TweakStateKind.NotApplied, "Power saving on", "At least one supported NIC sleep/suspend feature is enabled.", true, false)
            : new TweakDetectionResult(TweakStateKind.Applied, "Power saving off", "Supported NIC sleep/suspend features are disabled on connected physical adapters.", false, File.Exists(_restoreFile)));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        var supported = states.Where(HasSupportedFeature).ToList();
        if (supported.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No connected physical adapter reported supported sleep/suspend features."));

        SavePreviousIfMissing(states);
        foreach (var state in supported)
        {
            var switches = new List<string>();
            if (!IsUnsupported(state.SelectiveSuspend)) switches.Add("-SelectiveSuspend");
            if (!IsUnsupported(state.DeviceSleepOnDisconnect)) switches.Add("-DeviceSleepOnDisconnect");
            if (switches.Count == 0) continue;

            var name = PowerShellNetworkAccess.Escape(state.Name);
            var result = PowerShellNetworkAccess.Run($"Disable-NetAdapterPowerManagement -Name '{name}' {string.Join(" ", switches)} -NoRestart -Confirm:$false");
            if (!result.Success)
                return Task.FromResult(TweakOperationResult.Failed($"Could not disable supported NIC power-saving features on {state.Name}. {result.Error}".Trim()));
        }

        return Task.FromResult(TweakOperationResult.Completed(
            "Supported NIC sleep/suspend features were disabled without changing wake/offload features.",
            new TweakDetectionResult(TweakStateKind.Applied, "Power saving off", "Supported NIC sleep/suspend features are disabled.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No saved NIC power state exists, so Sabby did not guess."));

        foreach (var state in previous.Where(HasSupportedFeature))
        {
            if (!RestoreFeature(state.Name, "SelectiveSuspend", state.SelectiveSuspend, "-SelectiveSuspend", out var error) ||
                !RestoreFeature(state.Name, "DeviceSleepOnDisconnect", state.DeviceSleepOnDisconnect, "-DeviceSleepOnDisconnect", out error))
                return Task.FromResult(TweakOperationResult.Failed(error));
        }

        TryDeleteRestoreFile();
        return Task.FromResult(TweakOperationResult.Completed(
            "NIC sleep/suspend power features were restored to their previous states.",
            new TweakDetectionResult(TweakStateKind.Custom, "Restored", "NIC power-saving features were restored to their saved states.", true, false)));
    }

    private static bool RestoreFeature(string adapter, string label, string state, string parameter, out string error)
    {
        error = string.Empty;
        if (IsUnsupported(state)) return true;
        var cmd = IsEnabled(state) ? "Enable-NetAdapterPowerManagement" : "Disable-NetAdapterPowerManagement";
        var name = PowerShellNetworkAccess.Escape(adapter);
        var result = PowerShellNetworkAccess.Run($"{cmd} -Name '{name}' {parameter} -NoRestart -Confirm:$false");
        if (result.Success) return true;
        error = $"Could not restore {label} on {adapter}. {result.Error}".Trim();
        return false;
    }

    private static bool IsEnabled(string value) => value.Equals("Enabled", StringComparison.OrdinalIgnoreCase);
    private static bool IsUnsupported(string value) => value.Equals("Unsupported", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value);
    private static bool HasSupportedFeature(AdapterPowerState state) => !IsUnsupported(state.SelectiveSuspend) || !IsUnsupported(state.DeviceSleepOnDisconnect);

    private static List<AdapterPowerState> ReadStates()
    {
        const string script = "$items=@(Get-NetAdapter -Physical | Where-Object {$_.Status -eq 'Up'} | ForEach-Object { try { $p=Get-NetAdapterPowerManagement -Name $_.Name -ErrorAction Stop; [pscustomobject]@{Name=$_.Name;SelectiveSuspend=[string]$p.SelectiveSuspend;DeviceSleepOnDisconnect=[string]$p.DeviceSleepOnDisconnect} } catch {} }); ConvertTo-Json -InputObject $items -Compress";
        var result = PowerShellNetworkAccess.Run(script);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return new();
        try { return JsonSerializer.Deserialize<List<AdapterPowerState>>(result.Output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
        catch { return new(); }
    }

    private void SavePreviousIfMissing(List<AdapterPowerState> states)
    {
        try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, JsonSerializer.Serialize(states)); } catch { }
    }

    private List<AdapterPowerState> ReadPrevious()
    {
        try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<List<AdapterPowerState>>(File.ReadAllText(_restoreFile)) ?? new() : new(); }
        catch { return new(); }
    }

    private void TryDeleteRestoreFile() { try { File.Delete(_restoreFile); } catch { } }
}
