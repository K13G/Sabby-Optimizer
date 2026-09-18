using System.Text.Json;
using Microsoft.Win32;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class PowerThrottlingRegistryTweakHandler : ITweakHandler
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string ValueName = "PowerThrottlingOff";
    private sealed record RestoreState(bool Existed, int Value);
    private readonly string _restoreFile;

    public PowerThrottlingRegistryTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "power-throttling.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    public TweakDefinition Definition { get; } = new(
        "system.power-throttling",
        "Power Throttling",
        "Turn off Windows Power Throttling on a plugged-in performance PC when you prefer consistent foreground/background performance over energy savings.",
        TweakCategory.Windows,
        TweakSafetyLevel.Moderate,
        "Microsoft documents PowerThrottlingOff as the policy-backed registry value for turning off Windows Power Throttling. This is not a free FPS switch: it can increase background power use and heat, so Sabby labels it as situational rather than universally beneficial.",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling\PowerThrottlingOff. Sabby sets it to 1.",
        "Undo restores the exact previous DWORD or removes it if the value did not exist before Sabby changed it.",
        true,
        true,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: false);
        var raw = key?.GetValue(ValueName);
        var off = raw is int i && i == 1;
        return Task.FromResult(off
            ? new TweakDetectionResult(TweakStateKind.Applied, "Throttling off", "Windows Power Throttling is disabled by the documented policy value.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, "Windows managed", "Windows is allowed to use Power Throttling normally.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        if (key is null) return Task.FromResult(TweakOperationResult.Failed("Windows did not allow access to the PowerThrottling registry key."));
        var raw = key.GetValue(ValueName);
        SavePreviousIfMissing(new RestoreState(raw is not null, raw is int i ? i : 0));
        key.SetValue(ValueName, 1, RegistryValueKind.DWord);
        return Task.FromResult(TweakOperationResult.Completed("Windows Power Throttling was turned off. Restart Windows before comparing behavior.", new TweakDetectionResult(TweakStateKind.Applied, "Throttling off", "PowerThrottlingOff is 1.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var restore = ReadPrevious();
        if (restore is null) return Task.FromResult(TweakOperationResult.Failed("No previous Power Throttling state was recorded."));
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        if (key is null) return Task.FromResult(TweakOperationResult.Failed("Windows did not allow access to the PowerThrottling registry key."));
        if (restore.Existed) key.SetValue(ValueName, restore.Value, RegistryValueKind.DWord);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed("The previous Windows Power Throttling state was restored.", new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous PowerThrottlingOff state restored.", true, false)));
    }

    private void SavePreviousIfMissing(RestoreState state) { try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, JsonSerializer.Serialize(state)); } catch { } }
    private RestoreState? ReadPrevious() { try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<RestoreState>(File.ReadAllText(_restoreFile)) : null; } catch { return null; } }
    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }
}
