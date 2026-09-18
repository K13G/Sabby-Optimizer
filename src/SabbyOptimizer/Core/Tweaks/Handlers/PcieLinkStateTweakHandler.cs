using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class PcieLinkStateTweakHandler : ITweakHandler
{
    private static readonly Guid PcieSubgroup = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    private static readonly Guid AspmSetting = new("ee12f906-d277-404b-b6da-e5fa1a576df5");
    private readonly string _restoreFile;

    public PcieLinkStateTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "pcie-aspm.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() && WindowsPowerSettingAccess.IsAcSettingSupported(PcieSubgroup, AspmSetting);

    public TweakDefinition Definition { get; } = new(
        "power.pcie-aspm",
        "PCIe Link Power Saving",
        "Disable PCI Express link-state power saving on AC power for a latency/consistency-first desktop profile.",
        TweakCategory.CpuPower,
        TweakSafetyLevel.Advanced,
        "Windows exposes PCIe Link State Power Management through the active power plan. Setting AC ASPM to None avoids L0/L1 link power-saving transitions, but can increase platform power draw and is not automatically better for every PC.",
        "The active power scheme's PCI Express / Link State Power Management AC value is set to 0 (None).",
        "Undo restores the exact previous AC ASPM value recorded from the active plan.",
        true,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsPowerSettingAccess.TryReadAcValue(PcieSubgroup, AspmSetting, out var value))
            return Task.FromResult(TweakDetectionResult.Unavailable("Windows did not expose PCIe Link State Power Management for the active scheme."));
        return Task.FromResult(value == 0
            ? new TweakDetectionResult(TweakStateKind.Applied, "ASPM off", "PCIe Link State Power Management is None on AC power.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, value == 1 ? "Moderate savings" : "Maximum savings", $"PCIe Link State Power Management AC index is {value}.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsPowerSettingAccess.TryReadAcValue(PcieSubgroup, AspmSetting, out var before))
            return Task.FromResult(TweakOperationResult.Failed("Could not read the active PCIe ASPM value."));
        SavePreviousIfMissing(before);
        if (!WindowsPowerSettingAccess.TryWriteAcValue(PcieSubgroup, AspmSetting, 0, out var error))
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the PCIe ASPM change (error {error})."));
        return Task.FromResult(TweakOperationResult.Completed("PCIe Link State Power Management was set to None for AC power.", new TweakDetectionResult(TweakStateKind.Applied, "ASPM off", "PCIe link-state power saving is disabled on AC power.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPrevious();
        if (previous is null) return Task.FromResult(TweakOperationResult.Failed("No previous PCIe ASPM value was recorded."));
        if (!WindowsPowerSettingAccess.TryWriteAcValue(PcieSubgroup, AspmSetting, previous.Value, out var error))
            return Task.FromResult(TweakOperationResult.Failed($"Windows rejected the PCIe ASPM restore (error {error})."));
        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed("The previous PCIe Link State Power Management value was restored.", new TweakDetectionResult(TweakStateKind.Custom, "Restored", $"PCIe ASPM AC index restored to {previous}.", true, false)));
    }

    private void SavePreviousIfMissing(uint value) { try { if (!File.Exists(_restoreFile)) File.WriteAllText(_restoreFile, JsonSerializer.Serialize(value)); } catch { } }
    private uint? ReadPrevious() { try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<uint>(File.ReadAllText(_restoreFile)) : null; } catch { return null; } }
    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }
}
