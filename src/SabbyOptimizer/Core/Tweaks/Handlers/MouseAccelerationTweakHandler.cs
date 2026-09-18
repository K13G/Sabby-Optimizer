using System.Runtime.InteropServices;
using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class MouseAccelerationTweakHandler : ITweakHandler
{
    private const uint SpiGetMouse = 0x0003;
    private const uint SpiSetMouse = 0x0004;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;
    private readonly string _restoreFile;

    private sealed record MouseState(int Threshold1, int Threshold2, int Acceleration);

    public MouseAccelerationTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "mouse-acceleration.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows();

    public TweakDefinition Definition { get; } = new(
        "input.mouse-acceleration",
        "Mouse Acceleration Off",
        "Disable Windows mouse acceleration for a consistent raw-feeling pointer response in games that use Windows pointer input.",
        TweakCategory.Gaming,
        TweakSafetyLevel.Moderate,
        "SystemParametersInfo exposes the two mouse thresholds and acceleration level. Disabling acceleration can make mouse movement more predictable, but it is a preference and does not change the physical mouse polling rate.",
        "Windows mouse acceleration is set to zero while preserving Sabby's saved pre-change thresholds for Undo.",
        "Undo restores the exact three mouse parameters recorded before the tweak.",
        false,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGet(out var state)) return Task.FromResult(TweakDetectionResult.Unavailable("Windows did not expose mouse acceleration parameters."));
        var off = state.Acceleration == 0;
        return Task.FromResult(off
            ? new TweakDetectionResult(TweakStateKind.Applied, "Acceleration off", "Windows mouse acceleration is disabled.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, "Acceleration on", $"Windows mouse acceleration level is {state.Acceleration}.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGet(out var before)) return Task.FromResult(TweakOperationResult.Failed("Could not read the current mouse acceleration state."));
        SavePreviousIfMissing(before);
        var values = new[] { 0, 0, 0 };
        if (!SystemParametersInfo(SpiSetMouse, 0, values, SpifUpdateIniFile | SpifSendChange))
            return Task.FromResult(TweakOperationResult.Failed("Windows rejected the mouse acceleration change."));
        return Task.FromResult(TweakOperationResult.Completed("Windows mouse acceleration was disabled.", new TweakDetectionResult(TweakStateKind.Applied, "Acceleration off", "Windows mouse acceleration is disabled.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous is null) return Task.FromResult(TweakOperationResult.Failed("No previous mouse acceleration state was recorded."));
        var values = new[] { previous.Threshold1, previous.Threshold2, previous.Acceleration };
        if (!SystemParametersInfo(SpiSetMouse, 0, values, SpifUpdateIniFile | SpifSendChange))
            return Task.FromResult(TweakOperationResult.Failed("Windows rejected the mouse acceleration restore."));
        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed("The previous mouse acceleration parameters were restored.", new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous mouse parameters restored.", true, false)));
    }

    private static bool TryGet(out MouseState state)
    {
        var values = new int[3];
        if (!SystemParametersInfo(SpiGetMouse, 0, values, 0)) { state = new MouseState(0, 0, 0); return false; }
        state = new MouseState(values[0], values[1], values[2]);
        return true;
    }

    private void SavePreviousIfMissing(MouseState value) { try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, JsonSerializer.Serialize(value)); } catch { } }
    private MouseState? ReadPrevious() { try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<MouseState>(File.ReadAllText(_restoreFile)) : null; } catch { return null; } }
    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, [In, Out] int[] pvParam, uint fWinIni);
}
