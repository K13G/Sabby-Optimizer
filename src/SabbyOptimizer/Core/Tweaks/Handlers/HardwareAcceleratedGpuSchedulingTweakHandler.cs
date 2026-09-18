using System.Text.RegularExpressions;
using Microsoft.Win32;
using PCTweaker.Core.Services;
using PCTweaker.Models;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class HardwareAcceleratedGpuSchedulingTweakHandler : ITweakHandler
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string ValueName = "HwSchMode";
    private readonly string _restoreFile;

    public HardwareAcceleratedGpuSchedulingTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "hags-previous.txt");
    }

    public static bool IsSupported(HardwareInfo hardware)
    {
        if (!TryReadWindowsBuild(hardware.Windows, out var build) || build < 19041)
            return false;

        var gpu = hardware.Graphics ?? string.Empty;
        if (gpu.Contains("Unavailable", StringComparison.OrdinalIgnoreCase))
            return false;

        // HAGS ultimately depends on the display driver/WDDM implementation. These families
        // are a conservative eligibility gate; the handler still detects the Windows state
        // and requires an explicit restart after changing it.
        return gpu.Contains("GeForce RTX", StringComparison.OrdinalIgnoreCase) ||
               gpu.Contains("GeForce GTX 10", StringComparison.OrdinalIgnoreCase) ||
               gpu.Contains("GeForce GTX 16", StringComparison.OrdinalIgnoreCase) ||
               gpu.Contains("Radeon RX 5", StringComparison.OrdinalIgnoreCase) ||
               gpu.Contains("Radeon RX 6", StringComparison.OrdinalIgnoreCase) ||
               gpu.Contains("Radeon RX 7", StringComparison.OrdinalIgnoreCase) ||
               gpu.Contains("Radeon RX 8", StringComparison.OrdinalIgnoreCase) ||
               gpu.Contains("Radeon RX 9", StringComparison.OrdinalIgnoreCase) ||
               gpu.Contains("Intel Arc", StringComparison.OrdinalIgnoreCase);
    }

    public TweakDefinition Definition { get; } = new(
        "graphics.hags",
        "Hardware GPU Scheduling",
        "Enable Windows Hardware-accelerated GPU scheduling on supported modern GPUs.",
        TweakCategory.Graphics,
        TweakSafetyLevel.Moderate,
        "Hardware-accelerated GPU scheduling moves more GPU scheduling work into supported graphics hardware/driver paths. Availability depends on Windows plus the display driver and GPU. Sabby only adds this option when the detected Windows build and GPU family are eligible, and it marks the change as restart-required.",
        "HKLM\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers\\HwSchMode (2 = enabled, 1 = disabled).",
        "Undo restores the previous registry state exactly, including deleting the value if Windows was previously using its default.",
        true,
        true,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: false);
        var raw = key?.GetValue(ValueName);

        if (raw is null)
        {
            return Task.FromResult(new TweakDetectionResult(
                TweakStateKind.Custom,
                "System default",
                "Windows is using its default GPU scheduling preference because HwSchMode is not explicitly set.",
                true,
                File.Exists(_restoreFile)));
        }

        var value = Convert.ToInt32(raw);
        return Task.FromResult(value switch
        {
            2 => new TweakDetectionResult(TweakStateKind.Applied, "Enabled", "Hardware-accelerated GPU scheduling is explicitly enabled. A restart is required after changing this setting.", false, File.Exists(_restoreFile)),
            1 => new TweakDetectionResult(TweakStateKind.NotApplied, "Disabled", "Hardware-accelerated GPU scheduling is explicitly disabled.", true, false),
            _ => new TweakDetectionResult(TweakStateKind.Custom, $"Value {value}", "The GPU scheduling registry value is not one of Sabby's recognized explicit states.", true, File.Exists(_restoreFile))
        });
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        SavePreviousIfMissing(key.GetValue(ValueName));
        key.SetValue(ValueName, 2, RegistryValueKind.DWord);

        return Task.FromResult(TweakOperationResult.Completed(
            "Hardware GPU scheduling was enabled. Restart Windows for the driver scheduling mode to reload.",
            new TweakDetectionResult(TweakStateKind.Applied, "Enabled", "Hardware-accelerated GPU scheduling is explicitly enabled.", false, true),
            requiresRestart: true));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        var previous = ReadPrevious();
        if (previous.Exists)
            key.SetValue(ValueName, previous.Value, RegistryValueKind.DWord);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);

        TryDeleteRestoreFile();
        return Task.FromResult(TweakOperationResult.Completed(
            "Hardware GPU scheduling was restored to its previous Windows state. Restart Windows to fully reload the graphics scheduling mode.",
            previous.Exists && previous.Value == 2
                ? new TweakDetectionResult(TweakStateKind.Applied, "Enabled", "Hardware-accelerated GPU scheduling is explicitly enabled.", false, true)
                : previous.Exists && previous.Value == 1
                    ? new TweakDetectionResult(TweakStateKind.NotApplied, "Disabled", "Hardware-accelerated GPU scheduling is explicitly disabled.", true, false)
                    : new TweakDetectionResult(TweakStateKind.Custom, "System default", "Windows is using its default GPU scheduling preference.", true, true),
            requiresRestart: true));
    }

    private void SavePreviousIfMissing(object? value)
    {
        try
        {
            if (File.Exists(_restoreFile))
                return;
            File.WriteAllText(_restoreFile, value is null ? "missing" : Convert.ToInt32(value).ToString());
        }
        catch { }
    }

    private (bool Exists, int Value) ReadPrevious()
    {
        try
        {
            if (!File.Exists(_restoreFile))
                return (false, 0);
            var text = File.ReadAllText(_restoreFile).Trim();
            return int.TryParse(text, out var value) ? (true, value) : (false, 0);
        }
        catch { return (false, 0); }
    }

    private void TryDeleteRestoreFile()
    {
        try { File.Delete(_restoreFile); } catch { }
    }

    private static bool TryReadWindowsBuild(string? windows, out int build)
    {
        build = 0;
        if (string.IsNullOrWhiteSpace(windows))
            return false;
        var match = Regex.Match(windows, @"Build\s+(?<build>\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["build"].Value, out build);
    }
}
