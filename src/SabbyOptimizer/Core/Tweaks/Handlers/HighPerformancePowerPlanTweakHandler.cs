using System.Runtime.InteropServices;
using System.Text;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class HighPerformancePowerPlanTweakHandler : ITweakHandler
{
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    private readonly string _previousSchemeFile;

    public HighPerformancePowerPlanTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _previousSchemeFile = Path.Combine(directory, "power-plan-previous.txt");
    }

    public TweakDefinition Definition { get; } = new(
        "power.active-plan",
        "High / Ultimate Power Plan",
        "Use a performance-oriented Windows power scheme only when the current plan is not already custom or performance-focused.",
        TweakCategory.CpuPower,
        TweakSafetyLevel.Moderate,
        "Windows identifies each power scheme with a GUID and friendly name. Sabby now preserves any active custom scheme instead of assuming the built-in High performance plan is better. Built-in Balanced and Power saver can still be changed to High performance. Ultimate Performance is also preserved.",
        "The active Windows power scheme. Sabby only targets the built-in High performance scheme when the active plan is a known built-in Balanced/Power saver style plan.",
        "Undo returns to the exact scheme Sabby recorded before applying High performance. Sabby does not offer Undo if it did not make the power-plan change.",
        false,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetActiveScheme(out var active))
            return Task.FromResult(TweakDetectionResult.Error("Windows did not return the active power scheme."));

        var hasRestore = File.Exists(_previousSchemeFile);
        var friendlyName = TryGetFriendlyName(active) ?? active.ToString("D");

        if (active == HighPerformance)
        {
            return Task.FromResult(new TweakDetectionResult(
                TweakStateKind.Applied,
                "High performance",
                $"{friendlyName} is active. Windows reports the built-in High performance scheme.",
                false,
                hasRestore));
        }

        if (active == UltimatePerformance)
        {
            return Task.FromResult(new TweakDetectionResult(
                TweakStateKind.Custom,
                "Already optimized",
                $"{friendlyName} is already active. Sabby will not replace Ultimate Performance with the less-specialized High performance plan.",
                false,
                false));
        }

        // The critical safety change: do not overwrite a custom plan merely because one or two
        // processor values differ from Microsoft's built-in High performance defaults. Custom
        // plans can contain many additional settings that Sabby does not own.
        if (active != Balanced && active != PowerSaver)
        {
            var tuning = DescribeProcessorTuning();
            return Task.FromResult(new TweakDetectionResult(
                TweakStateKind.Custom,
                "Already optimized",
                $"Custom power plan '{friendlyName}' is active ({active}). Sabby preserves custom plans instead of replacing them automatically.{tuning}",
                false,
                false));
        }

        var planKind = active == Balanced ? "Balanced" : "Power saver";
        return Task.FromResult(new TweakDetectionResult(
            TweakStateKind.NotApplied,
            planKind,
            $"{friendlyName} is active ({active}). High performance is available as an optional performance-biased built-in plan.",
            true,
            false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetActiveScheme(out var current))
            return Task.FromResult(TweakOperationResult.Failed("Could not read the active Windows power scheme."));

        if (current == UltimatePerformance || (current != Balanced && current != PowerSaver && current != HighPerformance))
        {
            var name = TryGetFriendlyName(current) ?? current.ToString("D");
            return Task.FromResult(TweakOperationResult.Failed($"'{name}' is a custom/Ultimate power plan. Sabby preserved it instead of replacing it."));
        }

        if (current != HighPerformance)
        {
            try { File.WriteAllText(_previousSchemeFile, current.ToString("D")); } catch { }
        }

        var target = HighPerformance;
        var result = PowerSetActiveScheme(IntPtr.Zero, ref target);
        if (result != 0)
            return Task.FromResult(TweakOperationResult.Failed($"Windows could not activate High performance (error {result})."));

        return Task.FromResult(TweakOperationResult.Completed(
            "High performance power plan was activated.",
            new TweakDetectionResult(TweakStateKind.Applied, "High performance", "The built-in High performance power scheme is active.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = ReadPreviousScheme();
        if (previous is null)
            return Task.FromResult(TweakOperationResult.Failed("Sabby did not record a previous power plan, so it did not guess."));

        var target = previous.Value;
        var result = PowerSetActiveScheme(IntPtr.Zero, ref target);
        if (result != 0)
            return Task.FromResult(TweakOperationResult.Failed($"Windows could not restore the previous power plan (error {result})."));

        try { File.Delete(_previousSchemeFile); } catch { }
        var name = TryGetFriendlyName(target) ?? target.ToString("D");
        return Task.FromResult(TweakOperationResult.Completed(
            $"The previous Windows power plan '{name}' was restored.",
            new TweakDetectionResult(TweakStateKind.NotApplied, "Restored", $"Power plan restored to {name} ({target}).", true, false)));
    }

    private static string DescribeProcessorTuning()
    {
        var parts = new List<string>();
        if (WindowsPowerSettingAccess.TryReadAcValue(WindowsPowerSettingAccess.ProcessorBoostMode, out var boost))
            parts.Add($"AC boost mode index {boost}");
        if (WindowsPowerSettingAccess.TryReadAcValue(WindowsPowerSettingAccess.ProcessorMinimumState, out var floor))
            parts.Add($"AC minimum processor state {floor}%");
        return parts.Count == 0 ? string.Empty : $" Current processor settings: {string.Join(", ", parts)}.";
    }

    private Guid? ReadPreviousScheme()
    {
        try
        {
            if (File.Exists(_previousSchemeFile) && Guid.TryParse(File.ReadAllText(_previousSchemeFile).Trim(), out var parsed))
                return parsed;
        }
        catch { }
        return null;
    }

    private static bool TryGetActiveScheme(out Guid scheme)
    {
        scheme = Guid.Empty;
        var result = PowerGetActiveScheme(IntPtr.Zero, out var ptr);
        if (result != 0 || ptr == IntPtr.Zero)
            return false;
        try
        {
            scheme = Marshal.PtrToStructure<Guid>(ptr);
            return true;
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    private static string? TryGetFriendlyName(Guid scheme)
    {
        try
        {
            uint size = 0;
            var copy = scheme;
            _ = PowerReadFriendlyName(IntPtr.Zero, ref copy, IntPtr.Zero, IntPtr.Zero, null, ref size);
            if (size == 0 || size > 16 * 1024) return null;
            var buffer = new byte[size];
            copy = scheme;
            var result = PowerReadFriendlyName(IntPtr.Zero, ref copy, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
            if (result != 0) return null;
            return Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
        }
        catch
        {
            return null;
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        byte[]? buffer,
        ref uint bufferSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
