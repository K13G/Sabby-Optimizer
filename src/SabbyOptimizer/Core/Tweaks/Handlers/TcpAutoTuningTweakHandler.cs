using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class TcpAutoTuningTweakHandler : ITweakHandler
{
    private readonly string _restoreFile;

    public TcpAutoTuningTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "tcp-autotuning.txt");
    }

    public static bool IsSupported() =>
        OperatingSystem.IsWindows() &&
        PowerShellNetworkAccess.CommandExists("Get-NetTCPSetting") &&
        PowerShellNetworkAccess.CommandExists("Set-NetTCPSetting");

    public TweakDefinition Definition { get; } = new(
        "network.tcp-autotuning",
        "TCP Receive Auto-Tuning",
        "Keep Windows TCP receive-window auto-tuning at Normal for modern broadband throughput.",
        TweakCategory.Network,
        TweakSafetyLevel.Safe,
        "Windows TCP receive-window auto-tuning lets the receive window grow with network conditions. Microsoft specifically recommends the Normal level when auto-tuning has been disabled and throughput is lower than expected.",
        "The Internet TCP profile's AutoTuningLevelLocal setting. Sabby uses Windows' Normal mode.",
        "Undo restores the exact auto-tuning level recorded before Sabby changed it.",
        true,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = PowerShellNetworkAccess.Run("$s=Get-NetTCPSetting -SettingName Internet; [Console]::Out.Write([string]$s.AutoTuningLevelLocal)");
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return Task.FromResult(TweakDetectionResult.Unavailable("Windows did not expose the Internet TCP auto-tuning profile."));

        var current = result.Output.Trim();
        var applied = current.Equals("Normal", StringComparison.OrdinalIgnoreCase);
        var previous = ReadPrevious();
        var canUndo = File.Exists(_restoreFile) && !string.IsNullOrWhiteSpace(previous) && !previous.Equals("Normal", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(applied
            ? new TweakDetectionResult(TweakStateKind.Applied, "Normal", canUndo
                ? $"TCP receive auto-tuning is Normal. Sabby can restore the saved pre-change value ({previous})."
                : "TCP receive auto-tuning is already Normal. Sabby did not create a fake Deactivate action because no different pre-change value is saved.", false, canUndo)
            : new TweakDetectionResult(TweakStateKind.NotApplied, current, $"TCP receive auto-tuning is currently {current}; Sabby's recommended state is Normal.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var detect = ReadCurrent();
        if (detect is null)
            return Task.FromResult(TweakOperationResult.Failed("Could not read the current TCP auto-tuning level."));

        SavePreviousIfMissing(detect);
        var result = PowerShellNetworkAccess.Run("Set-NetTCPSetting -SettingName Internet -AutoTuningLevelLocal Normal; [Console]::Out.Write('OK')");
        if (!result.Success)
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the TCP auto-tuning change. {result.Error}".Trim()));

        return Task.FromResult(TweakOperationResult.Completed(
            "TCP receive auto-tuning was set to Normal.",
            new TweakDetectionResult(TweakStateKind.Applied, "Normal", "TCP receive auto-tuning is set to Normal.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (string.IsNullOrWhiteSpace(previous) || previous.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(TweakOperationResult.Failed("No different pre-Sabby TCP auto-tuning value is saved, so there is nothing to restore."));

        var allowed = new[] { "Disabled", "HighlyRestricted", "Restricted", "Normal", "Experimental" };
        if (!allowed.Contains(previous, StringComparer.OrdinalIgnoreCase))
            return Task.FromResult(TweakOperationResult.Failed("The saved TCP auto-tuning value was not recognized, so Sabby did not guess."));

        var value = allowed.First(x => x.Equals(previous, StringComparison.OrdinalIgnoreCase));
        var result = PowerShellNetworkAccess.Run($"Set-NetTCPSetting -SettingName Internet -AutoTuningLevelLocal {value}; [Console]::Out.Write('OK')");
        if (!result.Success)
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the TCP auto-tuning restore. {result.Error}".Trim()));

        TryDeleteRestoreFile();
        var applied = value.Equals("Normal", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(TweakOperationResult.Completed(
            $"TCP receive auto-tuning was restored to {value}.",
            new TweakDetectionResult(applied ? TweakStateKind.Applied : TweakStateKind.NotApplied, value, $"TCP receive auto-tuning is {value}.", !applied, applied)));
    }

    private string? ReadCurrent()
    {
        var result = PowerShellNetworkAccess.Run("$s=Get-NetTCPSetting -SettingName Internet; [Console]::Out.Write([string]$s.AutoTuningLevelLocal)");
        return result.Success ? result.Output.Trim() : null;
    }

    private void SavePreviousIfMissing(string value)
    {
        try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, value); } catch { }
    }

    private string? ReadPrevious()
    {
        try { return File.Exists(_restoreFile) ? File.ReadAllText(_restoreFile).Trim() : null; } catch { return null; }
    }

    private void TryDeleteRestoreFile() { try { File.Delete(_restoreFile); } catch { } }
}
