using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

/// <summary>
/// Reversible AC-power control for the documented USB selective suspend power setting.
/// Intended as an input-device stability/latency troubleshooting option, not an FPS guarantee.
/// </summary>
public sealed class UsbSelectiveSuspendTweakHandler : ITweakHandler
{
    private static readonly Guid UsbSubgroup = new("2a737441-1930-4402-8d77-b2bebba308a3");
    private static readonly Guid SelectiveSuspend = new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
    private readonly string _restoreFile;

    public UsbSelectiveSuspendTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "usb-selective-suspend.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() &&
        WindowsPowerSettingAccess.IsAcSettingSupported(UsbSubgroup, SelectiveSuspend);

    public TweakDefinition Definition { get; } = new(
        "input.usb-selective-suspend",
        "USB Selective Suspend",
        "Disable USB selective suspend on AC power for a desktop/input-stability profile.",
        TweakCategory.CpuPower,
        TweakSafetyLevel.Moderate,
        "Windows can selectively suspend idle USB ports to save power. Disabling that behavior on AC can help troubleshoot devices that wake slowly or disconnect under aggressive power saving, but it increases power use and is not a universal latency improvement.",
        "The active power plan's USB selective suspend AC index is set to Disabled (0).",
        "Undo restores the exact previous AC value from the active power plan.",
        true,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsPowerSettingAccess.TryReadAcValue(UsbSubgroup, SelectiveSuspend, out var value))
            return Task.FromResult(TweakDetectionResult.Unavailable("Windows did not expose USB selective suspend for the active power plan."));

        return Task.FromResult(value == 0
            ? new TweakDetectionResult(TweakStateKind.Applied, "Suspend off", "USB selective suspend is disabled on AC power.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, "Suspend on", "USB selective suspend is enabled on AC power.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsPowerSettingAccess.TryReadAcValue(UsbSubgroup, SelectiveSuspend, out var before))
            return Task.FromResult(TweakOperationResult.Failed("Could not read USB selective suspend from the active power plan."));

        SavePreviousIfMissing(before);
        if (!WindowsPowerSettingAccess.TryWriteAcValue(UsbSubgroup, SelectiveSuspend, 0, out var error))
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the USB selective suspend change (error {error})."));

        if (!WindowsPowerSettingAccess.TryReadAcValue(UsbSubgroup, SelectiveSuspend, out var verify) || verify != 0)
            return Task.FromResult(TweakOperationResult.Failed("Windows did not verify USB selective suspend as disabled."));

        return Task.FromResult(TweakOperationResult.Completed(
            "USB selective suspend was disabled on AC power and verified.",
            new TweakDetectionResult(TweakStateKind.Applied, "Suspend off", "USB selective suspend is disabled on AC power.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous is null)
            return Task.FromResult(TweakOperationResult.Failed("No previous USB selective suspend value was recorded."));

        if (!WindowsPowerSettingAccess.TryWriteAcValue(UsbSubgroup, SelectiveSuspend, previous.Value, out var error))
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the USB selective suspend restore (error {error})."));

        if (!WindowsPowerSettingAccess.TryReadAcValue(UsbSubgroup, SelectiveSuspend, out var verify) || verify != previous.Value)
            return Task.FromResult(TweakOperationResult.Failed("USB selective suspend restore read-back failed; the rollback record was retained."));

        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed(
            "The previous USB selective suspend AC value was restored.",
            new TweakDetectionResult(previous.Value == 0 ? TweakStateKind.Applied : TweakStateKind.NotApplied,
                previous.Value == 0 ? "Suspend off" : "Suspend on",
                $"USB selective suspend AC index restored to {previous.Value}.", previous.Value != 0, previous.Value == 0)));
    }

    private void SavePreviousIfMissing(uint value)
    {
        if (File.Exists(_restoreFile)) return;
        File.WriteAllText(_restoreFile, JsonSerializer.Serialize(value));
    }

    private uint? ReadPrevious()
    {
        try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<uint>(File.ReadAllText(_restoreFile)) : null; }
        catch { return null; }
    }

    private void TryDelete()
    {
        try { File.Delete(_restoreFile); } catch { }
    }
}
