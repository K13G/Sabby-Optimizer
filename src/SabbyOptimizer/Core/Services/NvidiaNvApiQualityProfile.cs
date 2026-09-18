using System.Runtime.InteropServices;
using System.Text.Json;

namespace PCTweaker.Core.Services;

/// <summary>
/// Minimal NVIDIA NVAPI DRS integration using only public interface IDs and public driver-setting IDs.
/// It intentionally touches a small quality-safe global set and stores every pre-existing DWORD so
/// the exact state can be restored. The helper is executed in a child Sabby process so a broken or
/// incompatible vendor runtime cannot take the main WPF process down.
/// </summary>
public static class NvidiaNvApiQualityProfile
{
    private const uint NvApiInitialize = 0x0150E828;
    private const uint DrsCreateSession = 0x0694D52E;
    private const uint DrsDestroySession = 0xDAD9CFF8;
    private const uint DrsLoadSettings = 0x375DBD6B;
    private const uint DrsSaveSettings = 0xFCBC7E14;
    private const uint DrsGetBaseProfile = 0xDA8466A0;
    private const uint DrsSetSetting = 0x577DD202;
    private const uint DrsGetSetting = 0x73BF8338;
    private const uint DrsDeleteProfileSetting = 0xE4A26362;

    // Public IDs from NVIDIA NvApiDriverSettings.h.
    private const uint PreferredPstateId = 0x1057EB71;
    private const uint QualityEnhancementsId = 0x00CE2691;
    private const uint AnisoSampleOptimizationId = 0x00E73211;
    private const uint AnisoFilterOptimizationId = 0x0084CD70;
    private const uint TrilinearOptimizationId = 0x002ECAF2;
    private const uint QualityUpscalingId = 0x10444444;
    private const uint RefreshRateOverrideId = 0x0064B541;

    private static readonly (uint Id, uint Value, string Name)[] QualitySafeSettings =
    [
        (PreferredPstateId, 1, "Power management mode = Prefer maximum performance"),
        (QualityEnhancementsId, 0, "Texture filtering quality = Quality"),
        (AnisoSampleOptimizationId, 0, "Anisotropic sample optimization = Off"),
        (AnisoFilterOptimizationId, 0, "Anisotropic filter optimization = Off"),
        (TrilinearOptimizationId, 0, "Trilinear optimization = Off"),
        (QualityUpscalingId, 0, "NVIDIA Image Scaling = Off"),
        (RefreshRateOverrideId, 1, "Preferred refresh rate = Highest available")
    ];

    internal sealed record SettingBackup(uint Id, uint PreviousValue, bool Existed, string Name);

    [StructLayout(LayoutKind.Explicit, Size = 0x3020)]
    private struct NvDrsSetting
    {
        [FieldOffset(0x0000)] public uint Version;
        [FieldOffset(0x1004)] public uint SettingId;
        [FieldOffset(0x1008)] public uint SettingType;
        [FieldOffset(0x100C)] public uint SettingLocation;
        [FieldOffset(0x1010)] public uint IsCurrentPredefined;
        [FieldOffset(0x1014)] public uint IsPredefinedValid;
        [FieldOffset(0x1018)] public uint PredefinedValue;
        [FieldOffset(0x201C)] public uint CurrentValue;
    }

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitializeDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateSessionDelegate(out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionDelegate(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetBaseProfileDelegate(IntPtr session, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSettingDelegate(IntPtr session, IntPtr profile, uint settingId, ref NvDrsSetting setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSettingDelegate(IntPtr session, IntPtr profile, ref NvDrsSetting setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteSettingDelegate(IntPtr session, IntPtr profile, uint settingId);

    public static (bool Success, string Message) Apply(string backupPath)
    {
        if (!OperatingSystem.IsWindows()) return (false, "NVAPI is only available on Windows.");
        IntPtr session = IntPtr.Zero;
        IntPtr profile = IntPtr.Zero;
        Api? api = null;
        var backup = new List<SettingBackup>();
        try
        {
            api = LoadApi();
            Check("NvAPI_Initialize", api.Initialize());
            Check("NvAPI_DRS_CreateSession", api.CreateSession(out session));
            Check("NvAPI_DRS_LoadSettings", api.LoadSettings(session));
            Check("NvAPI_DRS_GetBaseProfile", api.GetBaseProfile(session, out profile));

            // Read every setting first, then persist rollback data BEFORE changing the driver profile.
            foreach (var target in QualitySafeSettings)
            {
                var read = CreateSetting(target.Id, 0);
                var readStatus = api.GetSetting(session, profile, target.Id, ref read);
                backup.Add(new SettingBackup(target.Id, read.CurrentValue, readStatus == 0, target.Name));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.WriteAllText(backupPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));

            foreach (var target in QualitySafeSettings)
            {
                var setting = CreateSetting(target.Id, target.Value);
                Check($"NvAPI_DRS_SetSetting 0x{target.Id:X8}", api.SetSetting(session, profile, ref setting));
            }

            Check("NvAPI_DRS_SaveSettings", api.SaveSettings(session));

            // Independent read-back after the driver database has been saved.
            foreach (var target in QualitySafeSettings)
            {
                var verify = CreateSetting(target.Id, 0);
                Check($"NvAPI_DRS_GetSetting 0x{target.Id:X8}", api.GetSetting(session, profile, target.Id, ref verify));
                if (verify.CurrentValue != target.Value)
                    throw new InvalidOperationException($"NVIDIA read-back mismatch for {target.Name}: expected {target.Value}, got {verify.CurrentValue}.");
            }

            return (true, $"NVAPI verified {QualitySafeSettings.Length} NVIDIA driver settings with no texture-quality reduction.");
        }
        catch (Exception ex)
        {
            var rollbackText = "";
            if (api is not null && session != IntPtr.Zero && profile != IntPtr.Zero && backup.Count > 0)
            {
                try
                {
                    foreach (var saved in backup)
                    {
                        if (saved.Existed)
                        {
                            var setting = CreateSetting(saved.Id, saved.PreviousValue);
                            Check($"NVAPI rollback 0x{saved.Id:X8}", api.SetSetting(session, profile, ref setting));
                        }
                        else
                        {
                            _ = api.DeleteSetting(session, profile, saved.Id);
                        }
                    }
                    Check("NvAPI_DRS_SaveSettings rollback", api.SaveSettings(session));
                    try { File.Delete(backupPath); } catch { }
                    rollbackText = " Automatic NVAPI rollback completed.";
                }
                catch (Exception rollbackEx)
                {
                    rollbackText = $" Automatic NVAPI rollback could not be fully verified: {rollbackEx.Message}. The saved rollback file was kept.";
                }
            }
            return (false, $"NVIDIA NVAPI profile was not changed successfully: {ex.Message}.{rollbackText}");
        }
        finally
        {
            if (session != IntPtr.Zero)
            {
                try { var destroy = GetDelegate<SessionDelegate>(DrsDestroySession); destroy(session); } catch { }
            }
        }
    }

    public static (bool Success, string Message) Restore(string backupPath)
    {
        if (!File.Exists(backupPath)) return (false, "No NVIDIA NVAPI backup exists yet.");
        IntPtr session = IntPtr.Zero;
        try
        {
            var backup = JsonSerializer.Deserialize<List<SettingBackup>>(File.ReadAllText(backupPath)) ?? [];
            var api = LoadApi();
            Check("NvAPI_Initialize", api.Initialize());
            Check("NvAPI_DRS_CreateSession", api.CreateSession(out session));
            Check("NvAPI_DRS_LoadSettings", api.LoadSettings(session));
            Check("NvAPI_DRS_GetBaseProfile", api.GetBaseProfile(session, out var profile));

            foreach (var saved in backup)
            {
                if (saved.Existed)
                {
                    var setting = CreateSetting(saved.Id, saved.PreviousValue);
                    Check($"NvAPI restore 0x{saved.Id:X8}", api.SetSetting(session, profile, ref setting));
                }
                else
                {
                    // Setting did not exist as an override before Sabby, so remove Sabby's override.
                    _ = api.DeleteSetting(session, profile, saved.Id);
                }
            }
            Check("NvAPI_DRS_SaveSettings", api.SaveSettings(session));
            File.Delete(backupPath);
            return (true, $"Restored {backup.Count} NVIDIA driver setting(s) to their pre-Sabby state.");
        }
        catch (Exception ex) { return (false, $"NVIDIA restore failed: {ex.Message}"); }
        finally
        {
            if (session != IntPtr.Zero)
            {
                try { var destroy = GetDelegate<SessionDelegate>(DrsDestroySession); destroy(session); } catch { }
            }
        }
    }

    private static NvDrsSetting CreateSetting(uint id, uint value) => new()
    {
        Version = 0x00013020, // MAKE_NVAPI_VERSION(sizeof(NVDRS_SETTING=0x3020), 1)
        SettingId = id,
        SettingType = 0, // NVDRS_DWORD_TYPE
        SettingLocation = 0, // NVDRS_CURRENT_PROFILE_LOCATION
        IsCurrentPredefined = 0,
        IsPredefinedValid = 0,
        PredefinedValue = value,
        CurrentValue = value
    };

    private sealed record Api(
        InitializeDelegate Initialize,
        CreateSessionDelegate CreateSession,
        SessionDelegate LoadSettings,
        SessionDelegate SaveSettings,
        GetBaseProfileDelegate GetBaseProfile,
        GetSettingDelegate GetSetting,
        SetSettingDelegate SetSetting,
        DeleteSettingDelegate DeleteSetting);

    private static Api LoadApi()
    {
        if (!NativeLibrary.TryLoad("nvapi64.dll", out var handle) || handle == IntPtr.Zero)
            throw new DllNotFoundException("nvapi64.dll was not loadable.");
        // Keep the driver library loaded for process lifetime. QueryInterface uses its exported entry point.
        return new Api(
            GetDelegate<InitializeDelegate>(NvApiInitialize),
            GetDelegate<CreateSessionDelegate>(DrsCreateSession),
            GetDelegate<SessionDelegate>(DrsLoadSettings),
            GetDelegate<SessionDelegate>(DrsSaveSettings),
            GetDelegate<GetBaseProfileDelegate>(DrsGetBaseProfile),
            GetDelegate<GetSettingDelegate>(DrsGetSetting),
            GetDelegate<SetSettingDelegate>(DrsSetSetting),
            GetDelegate<DeleteSettingDelegate>(DrsDeleteProfileSetting));
    }

    private static T GetDelegate<T>(uint id) where T : Delegate
    {
        var pointer = QueryInterface(id);
        if (pointer == IntPtr.Zero) throw new MissingMethodException($"NVAPI function 0x{id:X8} is unavailable.");
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    private static void Check(string operation, int status)
    {
        if (status != 0) throw new InvalidOperationException($"{operation} returned NVAPI status {status}.");
    }
}
