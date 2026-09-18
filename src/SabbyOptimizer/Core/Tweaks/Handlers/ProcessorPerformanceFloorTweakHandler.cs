using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class ProcessorPerformanceFloorTweakHandler : ITweakHandler
{
    private const uint PerformanceFloor = 100;
    private readonly string _restoreFile;

    public ProcessorPerformanceFloorTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "processor-min-state-ac.txt");
    }

    public static bool IsSupported() =>
        WindowsPowerSettingAccess.IsAcSettingSupported(WindowsPowerSettingAccess.ProcessorMinimumState);

    public TweakDefinition Definition { get; } = new(
        "cpu.performance-floor",
        "CPU Performance Floor",
        "Keep the minimum processor performance state at 100% while plugged in for latency-sensitive testing or gaming.",
        TweakCategory.CpuPower,
        TweakSafetyLevel.Advanced,
        "Windows documents the minimum processor performance state as a 0-100% power-policy setting. Microsoft specifically notes that 100% can be useful for ultra-low-latency or invariant-frequency scenarios, but it increases idle power use and heat. Sabby therefore marks this as Advanced and only exposes it when the current PC's active power scheme supports the setting.",
        "The active power scheme's AC Minimum Processor Performance State (PROCTHROTTLEMIN). Sabby's performance state is 100%.",
        "Undo restores the exact minimum processor state recorded before Sabby changed it.",
        false,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsPowerSettingAccess.TryReadAcValue(WindowsPowerSettingAccess.ProcessorMinimumState, out var value))
            return Task.FromResult(TweakDetectionResult.Unavailable("This PC does not expose the Windows minimum processor state setting."));

        return Task.FromResult(value == PerformanceFloor
            ? new TweakDetectionResult(TweakStateKind.Applied, "100% minimum", "The AC minimum processor performance state is 100%.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, $"{value}% minimum", $"The AC minimum processor performance state is {value}%.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsPowerSettingAccess.TryReadAcValue(WindowsPowerSettingAccess.ProcessorMinimumState, out var current))
            return Task.FromResult(TweakOperationResult.Failed("Windows did not expose the minimum processor state on this PC."));

        SavePreviousIfMissing(current);
        if (!WindowsPowerSettingAccess.TryWriteAcValue(WindowsPowerSettingAccess.ProcessorMinimumState, PerformanceFloor, out var error))
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the minimum processor state change (error {error})."));

        return Task.FromResult(TweakOperationResult.Completed(
            "The AC minimum processor performance state was set to 100%.",
            new TweakDetectionResult(TweakStateKind.Applied, "100% minimum", "The AC minimum processor performance state is 100%.", false, File.Exists(_restoreFile))));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var restore = ReadPrevious() ?? 5u;
        if (!WindowsPowerSettingAccess.TryWriteAcValue(WindowsPowerSettingAccess.ProcessorMinimumState, restore, out var error))
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the minimum processor state restore (error {error})."));

        TryDeleteRestoreFile();
        return Task.FromResult(TweakOperationResult.Completed(
            $"The AC minimum processor performance state was restored to {restore}%.",
            new TweakDetectionResult(restore == PerformanceFloor ? TweakStateKind.Applied : TweakStateKind.NotApplied, $"{restore}% minimum", $"The AC minimum processor performance state is {restore}%.", restore != PerformanceFloor, restore == PerformanceFloor)));
    }

    private void SavePreviousIfMissing(uint value)
    {
        try
        {
            if (!File.Exists(_restoreFile))
                File.WriteAllText(_restoreFile, value.ToString());
        }
        catch { }
    }

    private uint? ReadPrevious()
    {
        try
        {
            return File.Exists(_restoreFile) && uint.TryParse(File.ReadAllText(_restoreFile).Trim(), out var value) ? value : null;
        }
        catch { return null; }
    }

    private void TryDeleteRestoreFile()
    {
        try { File.Delete(_restoreFile); } catch { }
    }
}
