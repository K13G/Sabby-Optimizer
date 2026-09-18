using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class DefenderPuaProtectionTweakHandler : ITweakHandler, ITweakCompatibilityProvider
{
    private sealed record RestoreState(int PuaProtection);
    private readonly string _restoreFile;

    public DefenderPuaProtectionTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState", "Security");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "defender-pua-protection.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() &&
        PowerShellNetworkAccess.CommandExists("Get-MpPreference") &&
        PowerShellNetworkAccess.CommandExists("Set-MpPreference");

    public TweakDefinition Definition { get; } = new(
        "security.defender-pua",
        "Defender PUA Protection",
        "Enable Microsoft Defender potentially unwanted application blocking and verify the resulting Defender preference.",
        TweakCategory.PrivacySafety,
        TweakSafetyLevel.Moderate,
        "Microsoft Defender PUA protection can block adware, bundlers, reputation-poor installers, and other potentially unwanted applications. Microsoft recommends keeping the protection enabled, but it can intentionally block tools you still choose to run, so Sabby never auto-applies it as a performance tweak.",
        "Microsoft Defender Antivirus PUAProtection is set to Enabled (value 1). No Defender exclusions are added and real-time protection is not weakened.",
        "Undo restores the exact PUAProtection mode captured before activation: Disabled, Enabled, or Audit mode.",
        true,
        false,
        true,
        true);

    public Task<TweakCompatibilityResult> CheckCompatibilityAsync(bool applying, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = ReadCurrent();
        return Task.FromResult(current.HasValue
            ? TweakCompatibilityResult.Compatible("Microsoft Defender exposes the PUAProtection preference on this PC.")
            : TweakCompatibilityResult.Blocked("Microsoft Defender PUAProtection could not be queried. A third-party antivirus or policy may be managing Defender."));
    }

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = ReadCurrent();
        if (!current.HasValue)
            return Task.FromResult(TweakDetectionResult.Unavailable("Microsoft Defender PUAProtection could not be queried."));

        return Task.FromResult(current.Value == 1
            ? new TweakDetectionResult(TweakStateKind.Applied, "PUA blocking on", "Microsoft Defender reports PUAProtection = Enabled.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, current.Value == 2 ? "Audit only" : "PUA blocking off", "Potentially unwanted applications are not currently set to Block mode.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = ReadCurrent();
        if (!current.HasValue) return Task.FromResult(TweakOperationResult.Failed("Defender PUAProtection could not be queried."));
        var hadRestoreState = File.Exists(_restoreFile);
        try
        {
            SavePreviousIfMissing(current.Value);
        }
        catch (Exception ex)
        {
            return Task.FromResult(TweakOperationResult.Failed($"Sabby could not save the current Defender PUA mode, so no Defender setting was changed. {ex.Message}"));
        }
        var result = PowerShellNetworkAccess.Run("Set-MpPreference -PUAProtection Enabled");
        if (!result.Success)
        {
            if (!hadRestoreState) TryDelete();
            return Task.FromResult(TweakOperationResult.Failed($"Defender rejected the PUA protection change. {result.Error}".Trim()));
        }
        var verified = ReadCurrent();
        if (verified != 1)
        {
            var rollbackMode = ModeName(current.Value);
            var rollback = PowerShellNetworkAccess.Run($"Set-MpPreference -PUAProtection {rollbackMode}");
            if (!hadRestoreState && rollback.Success) TryDelete();
            return Task.FromResult(TweakOperationResult.Failed(rollback.Success
                ? "Defender did not report PUAProtection = Enabled after the change; the previous mode was restored."
                : $"Defender read-back failed and the rollback also failed. {rollback.Error}".Trim()));
        }
        return Task.FromResult(TweakOperationResult.Completed(
            "Microsoft Defender PUA blocking was enabled and verified.",
            new TweakDetectionResult(TweakStateKind.Applied, "PUA blocking on", "Defender read-back reports PUAProtection = Enabled.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_restoreFile)) return Task.FromResult(TweakOperationResult.Failed("No previous Defender PUA mode was recorded."));
        RestoreState previous;
        try { previous = ReadPreviousStrict(); }
        catch (Exception ex) { return Task.FromResult(TweakOperationResult.Failed($"The saved Defender restore state is invalid, so Sabby made no change. {ex.Message}")); }
        var mode = ModeName(previous.PuaProtection);
        var result = PowerShellNetworkAccess.Run($"Set-MpPreference -PUAProtection {mode}");
        if (!result.Success) return Task.FromResult(TweakOperationResult.Failed($"Could not restore Defender PUA protection. {result.Error}".Trim()));
        var verified = ReadCurrent();
        if (verified != previous.PuaProtection)
            return Task.FromResult(TweakOperationResult.Failed($"Defender accepted the restore command but read-back returned {verified?.ToString() ?? "unavailable"}; the saved restore state was retained."));
        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed(
            "The previous Microsoft Defender PUA mode was restored.",
            new TweakDetectionResult(TweakStateKind.Custom, "Restored", $"Defender PUAProtection restored to {mode}.", true, false)));
    }

    private static string ModeName(int value) => value switch { 1 => "Enabled", 2 => "AuditMode", _ => "Disabled" };

    private static int? ReadCurrent()
    {
        var result = PowerShellNetworkAccess.Run("[Console]::Out.Write([int](Get-MpPreference).PUAProtection)", 8000);
        return result.Success && int.TryParse(result.Output, out var value) ? value : null;
    }

    private void SavePreviousIfMissing(int value)
    {
        if (File.Exists(_restoreFile))
        {
            _ = ReadPreviousStrict(); // Do not overwrite a corrupt rollback record.
            return;
        }

        if (value is < 0 or > 2)
            throw new InvalidDataException($"Unexpected Defender PUAProtection value {value}.");
        File.WriteAllText(_restoreFile, JsonSerializer.Serialize(new RestoreState(value)));
    }

    private RestoreState ReadPreviousStrict()
    {
        RestoreState saved;
        try
        {
            saved = JsonSerializer.Deserialize<RestoreState>(File.ReadAllText(_restoreFile))
                    ?? throw new InvalidDataException("Saved Defender restore state is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Saved Defender restore state is corrupt.", ex);
        }

        if (saved.PuaProtection is < 0 or > 2)
            throw new InvalidDataException($"Saved Defender PUAProtection value {saved.PuaProtection} is invalid.");
        return saved;
    }

    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }
}
