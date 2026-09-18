using System.Text.Json;
using Microsoft.Win32;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class TaskOffloadRegistryTweakHandler : ITweakHandler
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
    private const string ValueName = "DisableTaskOffload";
    private sealed record RestoreState(bool Existed, int Value);
    private readonly string _restoreFile;

    public TaskOffloadRegistryTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "tcp-task-offload.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows();

    public TweakDefinition Definition { get; } = new(
        "network.task-offload",
        "TCP/IP Hardware Offloads",
        "Keep Windows TCP/IP task offloading available instead of globally disabling NIC offload features through the registry.",
        TweakCategory.Network,
        TweakSafetyLevel.Safe,
        "Windows documents DisableTaskOffload under the TCP/IP Parameters registry key: 1 disables all TCP/IP task offloads while 0 enables them. This card is primarily a repair/guardrail against old tweak packs that globally disabled hardware offloads.",
        @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\DisableTaskOffload. Sabby's recommended value is 0 (or the normal Windows default behavior when the value does not exist).",
        "Undo restores the exact previous value or removes the value again if it did not exist before Sabby changed it.",
        true,
        true,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: false);
        var raw = key?.GetValue(ValueName);
        var disabled = raw is int value && value == 1;
        var hasRestore = File.Exists(_restoreFile);
        return Task.FromResult(disabled
            ? new TweakDetectionResult(TweakStateKind.NotApplied, "Offloads blocked", "DisableTaskOffload is 1, so Windows TCP/IP task offloads are globally disabled.", true, false)
            : new TweakDetectionResult(TweakStateKind.Applied, "Offloads allowed", raw is null ? "Windows is using the normal default task-offload behavior." : "DisableTaskOffload is 0, so TCP/IP task offloads are allowed.", false, hasRestore));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        if (key is null) return Task.FromResult(TweakOperationResult.Failed("Windows did not allow access to the TCP/IP Parameters registry key."));
        var raw = key.GetValue(ValueName);
        SavePreviousIfMissing(new RestoreState(raw is not null, raw is int i ? i : 0));
        key.SetValue(ValueName, 0, RegistryValueKind.DWord);
        return Task.FromResult(TweakOperationResult.Completed(
            "TCP/IP task offloads were enabled. A restart is recommended before judging the result.",
            new TweakDetectionResult(TweakStateKind.Applied, "Offloads allowed", "DisableTaskOffload is 0.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var restore = ReadPrevious();
        if (restore is null) return Task.FromResult(TweakOperationResult.Failed("No previous task-offload registry state was recorded."));
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        if (key is null) return Task.FromResult(TweakOperationResult.Failed("Windows did not allow access to the TCP/IP Parameters registry key."));
        if (restore.Existed) key.SetValue(ValueName, restore.Value, RegistryValueKind.DWord);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed("The previous task-offload registry state was restored.", new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous DisableTaskOffload state restored.", true, false)));
    }

    private void SavePreviousIfMissing(RestoreState state) { try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, JsonSerializer.Serialize(state)); } catch { } }
    private RestoreState? ReadPrevious() { try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<RestoreState>(File.ReadAllText(_restoreFile)) : null; } catch { return null; } }
    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }
}
