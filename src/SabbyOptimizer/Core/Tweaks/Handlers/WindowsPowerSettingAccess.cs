using System.Runtime.InteropServices;

namespace PCTweaker.Core.Tweaks.Handlers;

internal static class WindowsPowerSettingAccess
{
    internal static readonly Guid ProcessorSubgroup = new("54533251-82be-4824-96c1-47b60b740d00");
    internal static readonly Guid ProcessorBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
    internal static readonly Guid ProcessorMinimumState = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    internal static readonly Guid ProcessorEnergyPerformancePreference = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    internal static readonly Guid CoreParkingMinimumCores = new("0cc5b647-c1df-4637-891a-dec35c318583");
    internal static readonly Guid ProcessorPerformanceIncreasePolicy = new("465e1f50-b610-473a-ab58-00d1077dc418");
    internal static readonly Guid ProcessorPerformanceBoostPolicy = new("45bcc044-d885-43e2-8605-ee0ec6e96b59");

    public static bool IsAcSettingSupported(Guid settingGuid) => TryReadAcValue(ProcessorSubgroup, settingGuid, out _);

    public static bool IsAcSettingSupported(Guid subgroupGuid, Guid settingGuid) => TryReadAcValue(subgroupGuid, settingGuid, out _);

    public static bool TryReadAcValue(Guid settingGuid, out uint value) => TryReadAcValue(ProcessorSubgroup, settingGuid, out value);

    public static bool TryReadAcValue(Guid subgroupGuid, Guid settingGuid, out uint value)
    {
        value = 0;
        if (!TryGetActiveScheme(out var scheme))
            return false;

        var subgroup = subgroupGuid;
        var setting = settingGuid;
        return PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value) == 0;
    }

    public static bool TryWriteAcValue(Guid settingGuid, uint value, out uint error) => TryWriteAcValue(ProcessorSubgroup, settingGuid, value, out error);

    public static bool TryWriteAcValue(Guid subgroupGuid, Guid settingGuid, uint value, out uint error)
    {
        error = 0;
        if (!TryGetActiveScheme(out var scheme))
        {
            error = 1;
            return false;
        }

        var subgroup = subgroupGuid;
        var setting = settingGuid;
        error = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value);
        if (error != 0)
            return false;

        // Microsoft documents that power-setting writes to the active scheme do not take
        // effect until the active scheme is re-applied.
        error = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        return error == 0;
    }

    public static bool TryGetActiveScheme(out Guid scheme)
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

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        out uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subgroupOfPowerSettingsGuid,
        ref Guid powerSettingGuid,
        uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
