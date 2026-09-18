using System.Runtime.InteropServices;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class WindowsAnimationsTweakHandler : ITweakHandler
{
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const uint SpiSetClientAreaAnimation = 0x1043;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;
    private bool? _previousEnabled;

    public TweakDefinition Definition { get; } = new(
        "windows.visual-effects",
        "Windows Animations",
        "Disable client-area animations without applying a blanket visual-effects registry pack.",
        TweakCategory.Windows,
        TweakSafetyLevel.Safe,
        "Windows provides a documented SystemParametersInfo setting for client-area animations. Sabby changes only that one system preference instead of forcing an opaque bundle of visual-effect registry values.",
        "The documented SPI_SETCLIENTAREAANIMATION user preference.",
        "Undo restores the animation state captured before Sabby changed it when available; otherwise animations are enabled.",
        false,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SystemParametersInfoGet(SpiGetClientAreaAnimation, 0, out var enabled, 0))
            return Task.FromResult(TweakDetectionResult.Error("Windows did not return the client-area animation setting."));

        return Task.FromResult(!enabled
            ? new TweakDetectionResult(TweakStateKind.Applied, "Animations off", "Windows client-area animations are disabled.", false, true)
            : new TweakDetectionResult(TweakStateKind.NotApplied, "Animations on", "Windows client-area animations are enabled.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_previousEnabled.HasValue && SystemParametersInfoGet(SpiGetClientAreaAnimation, 0, out var current, 0))
            _previousEnabled = current;

        if (!SetAnimationState(false))
            return Task.FromResult(TweakOperationResult.Failed("Windows rejected the animation preference change."));

        return Task.FromResult(TweakOperationResult.Completed(
            "Windows client-area animations were disabled.",
            new TweakDetectionResult(TweakStateKind.Applied, "Animations off", "Windows client-area animations are disabled.", false, _previousEnabled.HasValue)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var desired = _previousEnabled ?? true;
        if (!SetAnimationState(desired))
            return Task.FromResult(TweakOperationResult.Failed("Windows rejected the animation preference restore."));

        var state = desired ? TweakStateKind.NotApplied : TweakStateKind.Applied;
        return Task.FromResult(TweakOperationResult.Completed(
            "Windows client-area animation preference was restored.",
            new TweakDetectionResult(state, desired ? "Animations on" : "Animations off", desired ? "Windows client-area animations are enabled." : "Windows client-area animations are disabled.", !desired, desired)));
    }

    private static bool SetAnimationState(bool enabled)
    {
        var value = enabled;
        return SystemParametersInfoSet(SpiSetClientAreaAnimation, 0, ref value, SpifUpdateIniFile | SpifSendChange);
    }

    // C# does not allow overloads that differ only by ref/out. Give each native signature
    // a distinct managed name while pointing both at the same Win32 entry point.
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoGet(uint uiAction, uint uiParam, out bool pvParam, uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoSet(uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);
}
