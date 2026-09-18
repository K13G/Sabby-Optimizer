using Microsoft.Win32;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class GameModeTweakHandler : ITweakHandler
{
    private const string KeyPath = @"Software\Microsoft\GameBar";
    private const string ValueName = "AutoGameModeEnabled";
    private object? _previousValue;
    private bool _previousCaptured;

    public TweakDefinition Definition { get; } = new(
        "gaming.game-mode",
        "Game Mode",
        "Keep Windows Game Mode enabled while gaming.",
        TweakCategory.Gaming,
        TweakSafetyLevel.Safe,
        "Windows exposes Game Mode as a normal Gaming setting. Microsoft documents AutoGameModeEnabled under HKCU\\Software\\Microsoft\\GameBar as the value associated with the Game Mode toggle. Sabby changes only that documented per-user setting.",
        "HKCU\\Software\\Microsoft\\GameBar\\AutoGameModeEnabled (1 = enabled, 0 = disabled).",
        "Undo restores the value seen immediately before Sabby changed it when available; otherwise it disables Game Mode.",
        false,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        var raw = key?.GetValue(ValueName);
        if (raw is null)
        {
            return Task.FromResult(new TweakDetectionResult(
                TweakStateKind.Custom,
                "Windows default",
                "Game Mode is not explicitly configured in this user registry hive. Windows is using its current default behavior.",
                true,
                true));
        }

        var value = Convert.ToInt32(raw);
        return Task.FromResult(value != 0
            ? new TweakDetectionResult(TweakStateKind.Applied, "Enabled", "Windows Game Mode is enabled for this user.", false, _previousCaptured)
            : new TweakDetectionResult(TweakStateKind.NotApplied, "Disabled", "Windows Game Mode is disabled for this user.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        CapturePrevious(key);
        key.SetValue(ValueName, 1, RegistryValueKind.DWord);
        return Task.FromResult(TweakOperationResult.Completed(
            "Game Mode was enabled.",
            new TweakDetectionResult(TweakStateKind.Applied, "Enabled", "Windows Game Mode is enabled for this user.", false, _previousCaptured)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (_previousCaptured)
        {
            if (_previousValue is null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            else
                key.SetValue(ValueName, Convert.ToInt32(_previousValue), RegistryValueKind.DWord);
        }
        else
        {
            key.SetValue(ValueName, 0, RegistryValueKind.DWord);
        }

        return Task.FromResult(TweakOperationResult.Completed(
            "Game Mode was restored/disabled.",
            new TweakDetectionResult(TweakStateKind.NotApplied, "Disabled", "Game Mode no longer has Sabby's enabled state applied.", true, false)));
    }

    private void CapturePrevious(RegistryKey key)
    {
        if (_previousCaptured) return;
        _previousValue = key.GetValue(ValueName);
        _previousCaptured = true;
    }
}
