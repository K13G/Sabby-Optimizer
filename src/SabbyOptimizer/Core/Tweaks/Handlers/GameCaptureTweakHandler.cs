using Microsoft.Win32;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class GameCaptureTweakHandler : ITweakHandler
{
    private const string CaptureKeyPath = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string CaptureValueName = "AppCaptureEnabled";
    private const string StoreKeyPath = @"System\GameConfigStore";
    private const string StoreValueName = "GameDVR_Enabled";

    private object? _previousCapture;
    private object? _previousStore;
    private bool _previousCaptured;

    public TweakDefinition Definition { get; } = new(
        "gaming.capture",
        "Background Capture",
        "Disable Windows game recording/capture when you do not use it.",
        TweakCategory.Gaming,
        TweakSafetyLevel.Safe,
        "Windows game capture can record clips and screenshots. Microsoft support documentation identifies AppCaptureEnabled and GameDVR_Enabled as the per-user values controlling this capture path. Sabby's optimized state disables both without uninstalling Game Bar.",
        "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR\\AppCaptureEnabled and HKCU\\System\\GameConfigStore\\GameDVR_Enabled.",
        "Undo restores the exact two values captured before Sabby changed them during the current session; if no prior session value is available, capture is enabled again.",
        false,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var captureKey = Registry.CurrentUser.OpenSubKey(CaptureKeyPath, writable: false);
        using var storeKey = Registry.CurrentUser.OpenSubKey(StoreKeyPath, writable: false);
        var capture = captureKey?.GetValue(CaptureValueName);
        var store = storeKey?.GetValue(StoreValueName);

        if (capture is not null && store is not null)
        {
            var captureValue = Convert.ToInt32(capture);
            var storeValue = Convert.ToInt32(store);
            if (captureValue == 0 && storeValue == 0)
                return Task.FromResult(new TweakDetectionResult(TweakStateKind.Applied, "Capture off", "Windows game capture is disabled for this user.", false, true));
            if (captureValue != 0 && storeValue != 0)
                return Task.FromResult(new TweakDetectionResult(TweakStateKind.NotApplied, "Capture on", "Windows game capture is enabled for this user.", true, false));
        }

        return Task.FromResult(new TweakDetectionResult(
            TweakStateKind.Custom,
            "Mixed/default",
            "The capture values are mixed or one is using the Windows default. Sabby can still set a known optimized state safely.",
            true,
            true));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var captureKey = Registry.CurrentUser.CreateSubKey(CaptureKeyPath, writable: true);
        using var storeKey = Registry.CurrentUser.CreateSubKey(StoreKeyPath, writable: true);
        CapturePrevious(captureKey, storeKey);
        captureKey.SetValue(CaptureValueName, 0, RegistryValueKind.DWord);
        storeKey.SetValue(StoreValueName, 0, RegistryValueKind.DWord);
        return Task.FromResult(TweakOperationResult.Completed(
            "Windows background game capture was disabled.",
            new TweakDetectionResult(TweakStateKind.Applied, "Capture off", "Windows game capture is disabled for this user.", false, _previousCaptured)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var captureKey = Registry.CurrentUser.CreateSubKey(CaptureKeyPath, writable: true);
        using var storeKey = Registry.CurrentUser.CreateSubKey(StoreKeyPath, writable: true);

        RestoreOrEnable(captureKey, CaptureValueName, _previousCaptured ? _previousCapture : 1);
        RestoreOrEnable(storeKey, StoreValueName, _previousCaptured ? _previousStore : 1);

        return Task.FromResult(TweakOperationResult.Completed(
            "Windows game capture was restored/enabled.",
            new TweakDetectionResult(TweakStateKind.NotApplied, "Capture on", "Windows game capture no longer has Sabby's disabled state applied.", true, false)));
    }

    private void CapturePrevious(RegistryKey captureKey, RegistryKey storeKey)
    {
        if (_previousCaptured) return;
        _previousCapture = captureKey.GetValue(CaptureValueName);
        _previousStore = storeKey.GetValue(StoreValueName);
        _previousCaptured = true;
    }

    private static void RestoreOrEnable(RegistryKey key, string name, object? value)
    {
        if (value is null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, Convert.ToInt32(value), RegistryValueKind.DWord);
    }
}
