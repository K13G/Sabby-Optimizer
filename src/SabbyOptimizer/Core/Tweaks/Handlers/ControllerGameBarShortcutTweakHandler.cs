using System.Text.Json;
using Microsoft.Win32;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class ControllerGameBarShortcutTweakHandler : ITweakHandler
{
    private const string KeyPath = @"Software\Microsoft\GameBar";
    private const string ValueName = "UseNexusForGameBarEnabled";
    private sealed record RestoreState(bool Existed, int Value);
    private readonly string _restoreFile;

    public ControllerGameBarShortcutTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "controller-gamebar-shortcut.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public TweakDefinition Definition { get; } = new(
        "gaming.controller-gamebar-shortcut",
        "Controller Game Bar Shortcut",
        "Stop the Xbox/controller Home button from opening Game Bar when you do not use that shortcut.",
        TweakCategory.Gaming,
        TweakSafetyLevel.Safe,
        "Windows documents UseNexusForGameBarEnabled as the setting behind 'Allow your controller to open Game Bar'. This is a convenience tweak, not an FPS boost.",
        @"HKCU\Software\Microsoft\GameBar\UseNexusForGameBarEnabled is set to DWORD 0.",
        "Undo restores the exact previous value or removes it if Windows had no explicit value.",
        false,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        var raw = key?.GetValue(ValueName);
        var enabled = raw is null || (raw is int i && i != 0);
        return Task.FromResult(!enabled
            ? new TweakDetectionResult(TweakStateKind.Applied, "Shortcut off", "Controller Home button is not allowed to open Game Bar.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, "Shortcut on", "Controller Home button is allowed to open Game Bar.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (key is null) return Task.FromResult(TweakOperationResult.Failed("Windows did not allow access to the Game Bar user setting."));
        var raw = key.GetValue(ValueName);
        SavePreviousIfMissing(new RestoreState(raw is not null, raw is int i ? i : 1));
        key.SetValue(ValueName, 0, RegistryValueKind.DWord);
        return Task.FromResult(TweakOperationResult.Completed("The controller shortcut for opening Game Bar was disabled.", new TweakDetectionResult(TweakStateKind.Applied, "Shortcut off", "UseNexusForGameBarEnabled is 0.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous is null) return Task.FromResult(TweakOperationResult.Failed("No previous controller Game Bar shortcut state was recorded."));
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (key is null) return Task.FromResult(TweakOperationResult.Failed("Windows did not allow access to the Game Bar user setting."));
        if (previous.Existed) key.SetValue(ValueName, previous.Value, RegistryValueKind.DWord);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed("The previous controller Game Bar shortcut setting was restored.", new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous Game Bar controller shortcut state restored.", true, false)));
    }

    private void SavePreviousIfMissing(RestoreState state) { try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, JsonSerializer.Serialize(state)); } catch { } }
    private RestoreState? ReadPrevious() { try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<RestoreState>(File.ReadAllText(_restoreFile)) : null; } catch { return null; } }
    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }
}
