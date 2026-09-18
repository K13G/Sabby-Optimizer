using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

/// <summary>
/// Reversible AC processor-power policy control backed by the native Windows power APIs.
/// It stores the exact pre-change value and never invents a rollback default.
/// </summary>
public sealed class ProcessorPolicyTweakHandler : ITweakHandler
{
    private readonly Guid _settingGuid;
    private readonly uint _targetValue;
    private readonly string _restoreFile;
    private readonly string _appliedLabel;
    private readonly string _unitSuffix;

    public ProcessorPolicyTweakHandler(
        IAppPaths paths,
        TweakDefinition definition,
        Guid settingGuid,
        uint targetValue,
        string restoreFileName,
        string appliedLabel,
        string unitSuffix = "")
    {
        Definition = definition;
        _settingGuid = settingGuid;
        _targetValue = targetValue;
        _appliedLabel = appliedLabel;
        _unitSuffix = unitSuffix;
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState", "Processor");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, restoreFileName);
    }

    public TweakDefinition Definition { get; }

    public static bool IsSupported(Guid settingGuid) =>
        OperatingSystem.IsWindows() && WindowsPowerSettingAccess.IsAcSettingSupported(settingGuid);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsPowerSettingAccess.TryReadAcValue(_settingGuid, out var value))
            return Task.FromResult(TweakDetectionResult.Unavailable("Windows does not expose this processor power-policy setting on the active AC power plan."));

        var detail = $"Current AC policy value: {value}{_unitSuffix}.";
        return Task.FromResult(value == _targetValue
            ? new TweakDetectionResult(TweakStateKind.Applied, _appliedLabel, detail, false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, $"{value}{_unitSuffix}", detail, true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsPowerSettingAccess.TryReadAcValue(_settingGuid, out var current))
            return Task.FromResult(TweakOperationResult.Failed("Windows did not expose this processor policy on the active AC power plan."));

        try
        {
            SavePreviousIfMissing(current);
        }
        catch (Exception ex)
        {
            return Task.FromResult(TweakOperationResult.Failed($"Sabby could not save the current processor policy, so no change was made. {ex.Message}"));
        }

        if (!WindowsPowerSettingAccess.TryWriteAcValue(_settingGuid, _targetValue, out var error))
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the processor policy change (error {error})."));

        if (!WindowsPowerSettingAccess.TryReadAcValue(_settingGuid, out var verified) || verified != _targetValue)
        {
            if (TryReadPrevious(out var previous))
                _ = WindowsPowerSettingAccess.TryWriteAcValue(_settingGuid, previous, out _);
            return Task.FromResult(TweakOperationResult.Failed("The processor policy did not pass Windows read-back verification; Sabby attempted to restore the previous value."));
        }

        return Task.FromResult(TweakOperationResult.Completed(
            $"{Definition.Name} was applied and verified.",
            new TweakDetectionResult(TweakStateKind.Applied, _appliedLabel, $"Windows read-back returned {_targetValue}{_unitSuffix}.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadPrevious(out var previous))
            return Task.FromResult(TweakOperationResult.Failed("No valid pre-change processor policy was recorded, so Sabby will not guess a rollback value."));

        if (!WindowsPowerSettingAccess.TryWriteAcValue(_settingGuid, previous, out var error))
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the processor policy restore (error {error})."));

        if (!WindowsPowerSettingAccess.TryReadAcValue(_settingGuid, out var verified) || verified != previous)
            return Task.FromResult(TweakOperationResult.Failed("Windows accepted the restore request, but read-back did not match the saved value. The rollback record was kept."));

        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed(
            $"{Definition.Name} was restored to the exact saved value ({previous}{_unitSuffix}).",
            new TweakDetectionResult(previous == _targetValue ? TweakStateKind.Applied : TweakStateKind.NotApplied,
                previous == _targetValue ? _appliedLabel : $"{previous}{_unitSuffix}",
                $"Restored AC policy value: {previous}{_unitSuffix}.", previous != _targetValue, previous == _targetValue)));
    }

    private void SavePreviousIfMissing(uint value)
    {
        if (File.Exists(_restoreFile))
        {
            if (!uint.TryParse(File.ReadAllText(_restoreFile).Trim(), out _))
                throw new InvalidDataException("The existing rollback record is invalid.");
            return;
        }
        File.WriteAllText(_restoreFile, value.ToString());
    }

    private bool TryReadPrevious(out uint value)
    {
        value = 0;
        try
        {
            return File.Exists(_restoreFile) && uint.TryParse(File.ReadAllText(_restoreFile).Trim(), out value);
        }
        catch { return false; }
    }

    private void TryDelete()
    {
        try { File.Delete(_restoreFile); } catch { }
    }
}
