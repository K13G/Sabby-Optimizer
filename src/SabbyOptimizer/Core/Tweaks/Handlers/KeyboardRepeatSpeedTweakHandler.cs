using System.Runtime.InteropServices;
using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class KeyboardRepeatSpeedTweakHandler : ITweakHandler
{
    private const uint SpiGetKeyboardSpeed = 0x000A;
    private const uint SpiSetKeyboardSpeed = 0x000B;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;
    private readonly string _restoreFile;

    public KeyboardRepeatSpeedTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "keyboard-repeat-speed.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows();

    public TweakDefinition Definition { get; } = new(
        "input.keyboard-repeat",
        "Fast Keyboard Repeat",
        "Set Windows keyboard repeat speed to its documented maximum for faster key-repeat response in text and hold-to-repeat actions.",
        TweakCategory.Gaming,
        TweakSafetyLevel.Safe,
        "Windows exposes keyboard repeat speed through SystemParametersInfo. This does not reduce USB input latency or increase keyboard polling rate; it only changes how quickly Windows repeats a held key.",
        "The current-user Windows keyboard repeat-speed setting is set to 31, the documented maximum.",
        "Undo restores the exact repeat-speed value recorded before Sabby changed it.",
        false,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGet(out var value)) return Task.FromResult(TweakDetectionResult.Unavailable("Windows did not expose keyboard repeat speed."));
        return Task.FromResult(value >= 31
            ? new TweakDetectionResult(TweakStateKind.Applied, "Maximum repeat", "Keyboard repeat speed is set to 31/31.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, $"{value}/31", $"Keyboard repeat speed is currently {value}/31.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGet(out var before)) return Task.FromResult(TweakOperationResult.Failed("Could not read the current keyboard repeat speed."));
        SavePreviousIfMissing(before);
        if (!SystemParametersInfo(SpiSetKeyboardSpeed, 31, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange))
            return Task.FromResult(TweakOperationResult.Failed("Windows rejected the keyboard repeat-speed change."));
        return Task.FromResult(TweakOperationResult.Completed("Keyboard repeat speed was set to the documented maximum.", new TweakDetectionResult(TweakStateKind.Applied, "Maximum repeat", "Keyboard repeat speed is 31/31.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous is null) return Task.FromResult(TweakOperationResult.Failed("No previous keyboard repeat speed was recorded."));
        if (!SystemParametersInfo(SpiSetKeyboardSpeed, previous.Value, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange))
            return Task.FromResult(TweakOperationResult.Failed("Windows rejected the keyboard repeat-speed restore."));
        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed("The previous keyboard repeat speed was restored.", new TweakDetectionResult(TweakStateKind.Custom, $"Restored {previous}/31", "Previous keyboard repeat speed restored.", true, false)));
    }

    private static bool TryGet(out uint value)
    {
        value = 0;
        return SystemParametersInfo(SpiGetKeyboardSpeed, 0, ref value, 0);
    }

    private void SavePreviousIfMissing(uint value) { try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, JsonSerializer.Serialize(value)); } catch { } }
    private uint? ReadPrevious() { try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<uint>(File.ReadAllText(_restoreFile)) : null; } catch { return null; } }
    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
}
