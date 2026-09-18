using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class ProcessorBoostModeTweakHandler : ITweakHandler
{
    private const uint Aggressive = 2;
    private readonly string _restoreFile;

    public ProcessorBoostModeTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "processor-boost-mode-ac.txt");
    }

    public static bool IsSupported() =>
        WindowsPowerSettingAccess.IsAcSettingSupported(WindowsPowerSettingAccess.ProcessorBoostMode);

    public TweakDefinition Definition { get; } = new(
        "cpu.boost-mode",
        "CPU Boost Response",
        "Use Windows' Aggressive processor boost mode while plugged in for a more performance-biased boost response.",
        TweakCategory.CpuPower,
        TweakSafetyLevel.Moderate,
        "Windows exposes Processor Performance Boost Mode as a documented processor power setting. Aggressive asks Windows for a more performance-oriented boost response when the processor and platform permit boosting. It can increase power use and temperature, so Sabby only exposes it when Windows reports that this setting exists on the current PC.",
        "The active power scheme's AC Processor Performance Boost Mode (PERFBOOSTMODE). Sabby sets value 2, Aggressive.",
        "Undo restores the exact AC boost-mode value recorded before Sabby changed it.",
        false,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsPowerSettingAccess.TryReadAcValue(WindowsPowerSettingAccess.ProcessorBoostMode, out var value))
            return Task.FromResult(TweakDetectionResult.Unavailable("This PC does not expose the Windows processor boost-mode setting."));

        return Task.FromResult(value == Aggressive
            ? new TweakDetectionResult(TweakStateKind.Applied, "Aggressive", "Processor boost mode is set to Aggressive for AC power.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, $"Mode {value}", $"Processor boost mode is currently value {value}; Sabby's performance state is Aggressive (2).", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsPowerSettingAccess.TryReadAcValue(WindowsPowerSettingAccess.ProcessorBoostMode, out var current))
            return Task.FromResult(TweakOperationResult.Failed("Windows did not expose the processor boost-mode setting on this PC."));

        SavePreviousIfMissing(current);
        if (!WindowsPowerSettingAccess.TryWriteAcValue(WindowsPowerSettingAccess.ProcessorBoostMode, Aggressive, out var error))
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the processor boost-mode change (error {error})."));

        return Task.FromResult(TweakOperationResult.Completed(
            "CPU boost response was set to Aggressive for AC power.",
            new TweakDetectionResult(TweakStateKind.Applied, "Aggressive", "Processor boost mode is set to Aggressive for AC power.", false, File.Exists(_restoreFile))));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var restore = ReadPrevious() ?? 1u;
        if (!WindowsPowerSettingAccess.TryWriteAcValue(WindowsPowerSettingAccess.ProcessorBoostMode, restore, out var error))
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the processor boost-mode restore (error {error})."));

        TryDeleteRestoreFile();
        return Task.FromResult(TweakOperationResult.Completed(
            $"CPU boost response was restored to value {restore}.",
            new TweakDetectionResult(restore == Aggressive ? TweakStateKind.Applied : TweakStateKind.NotApplied, restore == Aggressive ? "Aggressive" : $"Mode {restore}", $"Processor boost mode is value {restore} for AC power.", restore != Aggressive, restore == Aggressive)));
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
