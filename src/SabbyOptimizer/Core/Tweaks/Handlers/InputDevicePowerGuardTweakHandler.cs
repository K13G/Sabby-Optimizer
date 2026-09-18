using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class InputDevicePowerGuardTweakHandler : ITweakHandler, ITweakCompatibilityProvider
{
    private sealed record DeviceState(string InstanceName, string FriendlyName, bool Enable);
    private readonly string _restoreFile;

    public InputDevicePowerGuardTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "input-device-power.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows();

    public TweakDefinition Definition { get; } = new(
        "input.power-guard",
        "Keyboard & Mouse Power Guard",
        "Prevent Windows device-level power management from idling supported keyboard and mouse devices while Sabby's performance profile is active.",
        TweakCategory.Advanced,
        TweakSafetyLevel.Moderate,
        "Some HID keyboard and mouse drivers expose the Windows power-management control represented by GUID_POWER_DEVICE_ENABLE. This tweak disables that per-device power-management flag only for detected keyboard/mouse devices that expose it. It does not globally disable USB selective suspend, which Microsoft recommends leaving enabled on modern systems.",
        "Only supported present Keyboard/Mouse/HID devices that expose the MSPower_DeviceEnable WMI control. Other USB devices, controllers, storage, audio, and hubs are untouched.",
        "Undo restores the exact per-device power-management Boolean that Sabby recorded before the change.",
        true,
        false,
        true,
        true);

    public Task<TweakCompatibilityResult> CheckCompatibilityAsync(bool applying, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakCompatibilityResult.Blocked("No present keyboard or mouse device exposes the Windows per-device power-management control."));
        return Task.FromResult(TweakCompatibilityResult.Compatible(
            $"{states.Count} compatible input-device power control{(states.Count == 1 ? string.Empty : "s")} detected.",
            applying ? "This can increase standby/battery power use, so Sabby keeps global USB selective suspend unchanged." : null));
    }

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakDetectionResult.Unavailable("No supported keyboard/mouse power-management controls were detected."));
        var anyEnabled = states.Any(x => x.Enable);
        return Task.FromResult(anyEnabled
            ? new TweakDetectionResult(TweakStateKind.NotApplied, "Device power saving on", $"{states.Count(x => x.Enable)} supported keyboard/mouse power control(s) still allow device power management.", true, false)
            : new TweakDetectionResult(TweakStateKind.Applied, "Input power guard on", "Supported keyboard/mouse device power-management flags are disabled.", false, File.Exists(_restoreFile)));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No compatible keyboard/mouse power controls were found."));
        SavePreviousIfMissing(states);
        foreach (var state in states.Where(x => x.Enable))
        {
            var instance = PowerShellNetworkAccess.Escape(state.InstanceName);
            var result = PowerShellNetworkAccess.Run($"Get-CimInstance -Namespace root\\wmi -ClassName MSPower_DeviceEnable | Where-Object {{$_.InstanceName -eq '{instance}'}} | Set-CimInstance -Property @{{Enable=$false}} -ErrorAction Stop");
            if (!result.Success)
                return Task.FromResult(TweakOperationResult.Failed($"Could not change power management for {state.FriendlyName}. {result.Error}".Trim()));
        }
        return Task.FromResult(TweakOperationResult.Completed(
            "Supported keyboard/mouse device power-management flags were disabled.",
            new TweakDetectionResult(TweakStateKind.Applied, "Input power guard on", "Supported input-device power controls are disabled.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No previous keyboard/mouse power state was recorded."));
        foreach (var state in previous)
        {
            var instance = PowerShellNetworkAccess.Escape(state.InstanceName);
            var value = state.Enable ? "$true" : "$false";
            var result = PowerShellNetworkAccess.Run($"Get-CimInstance -Namespace root\\wmi -ClassName MSPower_DeviceEnable | Where-Object {{$_.InstanceName -eq '{instance}'}} | Set-CimInstance -Property @{{Enable={value}}} -ErrorAction Stop");
            if (!result.Success)
                return Task.FromResult(TweakOperationResult.Failed($"Could not restore power management for {state.FriendlyName}. {result.Error}".Trim()));
        }
        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed(
            "Keyboard/mouse power-management flags were restored to their previous values.",
            new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous input-device power states restored.", true, false)));
    }

    private static List<DeviceState> ReadStates()
    {
        const string script = @"
$power = @(Get-CimInstance -Namespace root\wmi -ClassName MSPower_DeviceEnable -ErrorAction SilentlyContinue)
$devices = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.Class -in @('Keyboard','Mouse') -or ($_.Class -eq 'HIDClass' -and $_.FriendlyName -match '(?i)keyboard|mouse') })
$items = @()
foreach($d in $devices) {
  $p = $power | Where-Object { $_.InstanceName -like ($d.InstanceId + '*') } | Select-Object -First 1
  if($null -ne $p) { $items += [pscustomobject]@{InstanceName=[string]$p.InstanceName;FriendlyName=[string]$d.FriendlyName;Enable=[bool]$p.Enable} }
}
ConvertTo-Json -InputObject $items -Compress";
        var result = PowerShellNetworkAccess.Run(script, 10000);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return new();
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            if (result.Output.TrimStart().StartsWith("[", StringComparison.Ordinal))
                return JsonSerializer.Deserialize<List<DeviceState>>(result.Output, options) ?? new();
            var one = JsonSerializer.Deserialize<DeviceState>(result.Output, options);
            return one is null ? new() : [one];
        }
        catch { return new(); }
    }

    private void SavePreviousIfMissing(List<DeviceState> states) { try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, JsonSerializer.Serialize(states)); } catch { } }
    private List<DeviceState> ReadPrevious() { try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<List<DeviceState>>(File.ReadAllText(_restoreFile)) ?? new() : new(); } catch { return new(); } }
    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }
}
