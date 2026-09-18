using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using PCTweaker.Core.GameProfiles;
using PCTweaker.Models;
using PCTweaker.Models.GameProfiles;

namespace PCTweaker.Core.Services;

public sealed class GpuDriverIntegrationService
{
    private const string GpuPreferenceKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private sealed record PrefBackup(string Executable, string? PreviousValue, bool Existed);
    private readonly HardwareInfo _hardware;
    private readonly IGameProfileService _profiles;
    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly string _backupFile;
    private readonly string _nvapiBackupFile;

    public GpuDriverIntegrationService(HardwareInfo hardware, IGameProfileService profiles, IAppPaths paths, IAppLogger logger)
    {
        _hardware=hardware; _profiles=profiles; _paths=paths; _logger=logger;
        var dir=Path.Combine(paths.UserDataDirectory,"DriverState"); Directory.CreateDirectory(dir);
        _backupFile=Path.Combine(dir,"gpu-quality-safe-windows-profile.json");
        _nvapiBackupFile=Path.Combine(dir,"gpu-quality-safe-nvidia-nvapi.json");
    }

    public async Task<GpuDriverIntegrationStatus> DetectAsync(CancellationToken cancellationToken=default)
    {
        var vendor=DetectVendor(_hardware.Graphics);
        var driver=_hardware.GraphicsDetails;
        var app=FindVendorApp(vendor);
        var startAppName = string.Empty;
        if (app is null && vendor is "NVIDIA" or "Intel")
        {
            try
            {
                var pattern = vendor == "NVIDIA" ? "NVIDIA Control Panel|NVIDIA App" : "Intel.*Graphics|Graphics Command Center";
                var probe = await PowerShellUtility.RunAsync($"$a=Get-StartApps | Where-Object {{$_.Name -match '{pattern}'}} | Select-Object -First 1; if($a){{$a.Name}}", 6000, cancellationToken).ConfigureAwait(false);
                if (probe.Success && !string.IsNullOrWhiteSpace(probe.Output)) startAppName = probe.Output.Trim();
            }
            catch { }
        }
        var api=vendor switch
        {
            "NVIDIA" => CanLoad("nvapi64.dll") ? "Official NVIDIA NVAPI runtime detected. Quality-safe DRS settings can be applied and read back." : "NVIDIA GPU detected; NVAPI runtime was not loadable.",
            "AMD" => CanLoad("amdadlx64.dll") ? "AMD ADLX runtime detected. Sabby uses vendor-supported entry points only." : "AMD GPU detected; ADLX runtime was not loadable, so Sabby will use the official AMD Software UI.",
            "Intel" => "Intel GPU detected. Sabby uses Windows per-app GPU preferences and the official Intel graphics UI when available.",
            _ => "No supported NVIDIA/AMD/Intel vendor integration was detected."
        };
        if(vendor=="NVIDIA")
        {
            try
            {
                var smi=FindExecutable("nvidia-smi.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation"), Environment.SystemDirectory);
                if(smi is not null)
                {
                    var psi=new ProcessStartInfo(smi,"--query-gpu=name,driver_version --format=csv,noheader") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
                    using var p=Process.Start(psi); if(p is not null){var text=await p.StandardOutput.ReadToEndAsync(cancellationToken); await p.WaitForExitAsync(cancellationToken); if(!string.IsNullOrWhiteSpace(text))driver=text.Trim();}
                }
            } catch { }
        }
        var summary=vendor switch
        {
            "NVIDIA" => "Quality-safe NVAPI target: Prefer maximum performance, Texture filtering Quality = Quality, texture-filter optimizations off, NVIDIA Image Scaling off, and highest available refresh. Windows High-performance GPU preference is also applied to saved games.",
            "AMD" => "No-quality-loss target: Radeon Boost off, application-controlled image quality, Anti-Lag only where supported, no forced resolution reduction. Driver-only controls stay in AMD Software/ADLX-supported paths.",
            "Intel" => "No-quality-loss target: native rendering and high-performance per-app GPU selection; proprietary graphics options remain in Intel's official graphics application.",
            _ => "Only documented Windows graphics controls are available."
        };
        var available = app is not null || !string.IsNullOrWhiteSpace(startAppName);
        var appName = app?.DisplayName ?? (!string.IsNullOrWhiteSpace(startAppName) ? startAppName : "Vendor app");
        return new(vendor,_hardware.Graphics,driver,api,available,appName,summary);
    }

    public async Task<(bool Success,string Message)> ApplyQualitySafeAsync(IProgress<double>? progress=null,CancellationToken cancellationToken=default)
    {
        var profiles=(await _profiles.GetAllAsync(cancellationToken)).Where(x=>x.Enabled && File.Exists(x.ExecutablePath)).ToArray();
        if(profiles.Length==0) return (false,"No enabled saved game executables were available to optimize.");

        // Capture and persist the Windows state BEFORE mutating anything. If a vendor integration
        // fails later, Sabby can roll these values back immediately instead of leaving a half-applied batch.
        var backup=new List<PrefBackup>();
        using(var readKey=Registry.CurrentUser.CreateSubKey(GpuPreferenceKeyPath,writable:true))
        {
            if(readKey is null)return(false,"Windows per-app graphics preferences could not be opened for writing.");
            foreach(var profile in profiles)
            {
                var old=readKey.GetValue(profile.ExecutablePath)?.ToString();
                backup.Add(new(profile.ExecutablePath,old,old is not null));
            }
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_backupFile)!);
            await File.WriteAllTextAsync(_backupFile,JsonSerializer.Serialize(backup,new JsonSerializerOptions{WriteIndented=true}),cancellationToken).ConfigureAwait(false);
        }
        catch(Exception ex)
        {
            _logger.Warning($"GPU profile backup write failed: {ex.Message}");
            return(false,$"Sabby cancelled the GPU profile because it could not save rollback data: {ex.Message}");
        }

        var verified=0;
        try
        {
            using var key=Registry.CurrentUser.CreateSubKey(GpuPreferenceKeyPath,writable:true);
            if(key is null)return(false,"Windows per-app graphics preferences could not be opened for writing.");
            for(int i=0;i<profiles.Length;i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var exe=profiles[i].ExecutablePath;
                key.SetValue(exe,"GpuPreference=2;",RegistryValueKind.String);
                var read=key.GetValue(exe)?.ToString();
                if(read?.Contains("GpuPreference=2",StringComparison.OrdinalIgnoreCase)==true)verified++;
                progress?.Report(55d*(i+1)/profiles.Length);
            }
        }
        catch(Exception ex)
        {
            var rollback=await RestoreWindowsPreferencesOnlyAsync(backup,cancellationToken).ConfigureAwait(false);
            return(false,$"Windows GPU preference apply failed: {ex.Message}. {rollback}");
        }

        if(verified!=profiles.Length)
        {
            var rollback=await RestoreWindowsPreferencesOnlyAsync(backup,cancellationToken).ConfigureAwait(false);
            return(false,$"Only {verified}/{profiles.Length} Windows GPU preferences passed read-back verification. {rollback}");
        }

        var vendor=DetectVendor(_hardware.Graphics);
        var vendorApplied=true;
        var vendorMessage="No vendor-specific driver setting was required for this GPU vendor.";
        if(vendor=="NVIDIA")
        {
            progress?.Report(62);
            var nv=await RunNvApiHelperAsync("apply",cancellationToken).ConfigureAwait(false);
            vendorApplied=nv.Success; vendorMessage=nv.Message;
            progress?.Report(100);
            if(!vendorApplied)
            {
                var rollback=await RestoreWindowsPreferencesOnlyAsync(backup,cancellationToken).ConfigureAwait(false);
                return(false,$"NVIDIA NVAPI validation failed, so Sabby rolled back the Windows portion. {vendorMessage} {rollback}");
            }
        }
        else if(vendor=="AMD")
        {
            vendorMessage=CanLoad("amdadlx64.dll")
                ? "AMD ADLX is available; verified Windows per-game High performance preference was applied while undocumented Radeon image-quality flags were left untouched."
                : "AMD ADLX was not loadable; verified Windows per-game High performance preference was applied without undocumented Radeon profile writes.";
            progress?.Report(100);
        }
        else if(vendor=="Intel")
        {
            vendorMessage="Verified Windows per-game High performance preference was applied. Intel vendor-only image controls remain application-controlled through supported Intel/Windows interfaces.";
            progress?.Report(100);
        }

        return (true,$"Windows: {verified}/{profiles.Length} saved games verified as High performance. {vendorMessage}");
    }

    private async Task<string> RestoreWindowsPreferencesOnlyAsync(IReadOnlyList<PrefBackup> backup,CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var key=Registry.CurrentUser.CreateSubKey(GpuPreferenceKeyPath,writable:true);
            if(key is null)return "Automatic Windows rollback could not open the graphics-preference key.";
            foreach(var item in backup)
            {
                if(item.Existed && item.PreviousValue is not null)key.SetValue(item.Executable,item.PreviousValue,RegistryValueKind.String);
                else key.DeleteValue(item.Executable,false);
            }
            try{File.Delete(_backupFile);}catch{}
            await Task.CompletedTask;
            return "Automatic Windows rollback completed.";
        }
        catch(Exception ex){return $"Automatic Windows rollback could not be verified: {ex.Message}";}
    }

    public async Task<(bool Success,string Message)> RestoreAsync(CancellationToken cancellationToken=default)
    {
        var messages=new List<string>();
        var success=true;
        if(File.Exists(_backupFile))
        {
            List<PrefBackup>? backup;
            try{backup=JsonSerializer.Deserialize<List<PrefBackup>>(await File.ReadAllTextAsync(_backupFile,cancellationToken));}
            catch(Exception ex){return(false,$"Could not read the Windows GPU backup: {ex.Message}");}
            if(backup is not null)
            {
                using var key=Registry.CurrentUser.CreateSubKey(GpuPreferenceKeyPath,writable:true);
                if(key is null)return(false,"Windows graphics preferences could not be opened.");
                foreach(var item in backup)
                {
                    if(item.Existed && item.PreviousValue is not null)key.SetValue(item.Executable,item.PreviousValue,RegistryValueKind.String);
                    else key.DeleteValue(item.Executable,false);
                }
                try{File.Delete(_backupFile);}catch{}
                messages.Add($"restored Windows GPU preferences for {backup.Count} saved games");
            }
        }
        if(DetectVendor(_hardware.Graphics)=="NVIDIA" && File.Exists(_nvapiBackupFile))
        {
            var nv=await RunNvApiHelperAsync("restore",cancellationToken).ConfigureAwait(false);
            success &= nv.Success; messages.Add(nv.Message);
        }
        if(messages.Count==0)return(false,"No GPU preference backup exists yet.");
        return(success,string.Join(" • ",messages));
    }

    private async Task<(bool Success,string Message)> RunNvApiHelperAsync(string action,CancellationToken cancellationToken)
    {
        var exe=Environment.ProcessPath;
        if(string.IsNullOrWhiteSpace(exe)||!File.Exists(exe)) return(false,"Could not locate the running Sabby executable for the isolated NVAPI helper.");
        var resultFile=Path.Combine(Path.GetTempPath(),$"sab-nvapi-{Guid.NewGuid():N}.txt");
        try
        {
            var psi=new ProcessStartInfo(exe) { UseShellExecute=false,CreateNoWindow=true };
            psi.ArgumentList.Add(action.Equals("restore",StringComparison.OrdinalIgnoreCase)?"--nvapi-quality-restore":"--nvapi-quality-apply");
            psi.ArgumentList.Add(_nvapiBackupFile); psi.ArgumentList.Add(resultFile);
            using var process=Process.Start(psi);
            if(process is null)return(false,"Could not start the isolated NVIDIA NVAPI helper.");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if(!File.Exists(resultFile))return(false,$"NVIDIA NVAPI helper exited with code {process.ExitCode} without a verification result.");
            var text=(await File.ReadAllTextAsync(resultFile,cancellationToken)).Trim();
            var split=text.IndexOf('|');
            if(split<0)return(false,text);
            return(text[..split]=="1",text[(split+1)..]);
        }
        catch(OperationCanceledException){return(false,"NVIDIA NVAPI helper timed out or was cancelled.");}
        catch(Exception ex){return(false,$"NVIDIA NVAPI helper failed: {ex.Message}");}
        finally{try{File.Delete(resultFile);}catch{}}
    }

    public (bool Success,string Message) OpenVendorApp()
    {
        var vendor=DetectVendor(_hardware.Graphics);
        if (vendor is "Intel" or "NVIDIA")
        {
            try
            {
                var pattern = vendor == "NVIDIA" ? "NVIDIA Control Panel|NVIDIA App" : "Intel.*Graphics|Graphics Command Center";
                var script = $"$a=Get-StartApps | Where-Object {{$_.Name -match '{pattern}'}} | Select-Object -First 1; if($a){{Start-Process explorer.exe ('shell:AppsFolder\\'+$a.AppID); exit 0}}else{{exit 2}}";
                var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encoded}") { UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi); if (p is not null && p.WaitForExit(3000) && p.ExitCode == 0) return (true, $"Opened {vendor} graphics control software.");
            }
            catch { }
        }
        var app=FindVendorApp(vendor);
        if(app is not null)
        {
            try{Process.Start(new ProcessStartInfo(app.Path,app.Arguments){UseShellExecute=true});return(true,$"Opened {app.DisplayName}.");}catch(Exception ex){return(false,ex.Message);}
        }
        // Fallback to Windows graphics page instead of claiming the driver panel exists.
        try{Process.Start(new ProcessStartInfo("ms-settings:display-advancedgraphics"){UseShellExecute=true});return(false,$"{vendor} control software was not found. Opened Windows Graphics settings instead.");}catch(Exception ex){return(false,ex.Message);}
    }

    public static string DetectVendor(string graphics)
    {
        if(graphics.Contains("NVIDIA",StringComparison.OrdinalIgnoreCase)||graphics.Contains("GeForce",StringComparison.OrdinalIgnoreCase))return "NVIDIA";
        if(graphics.Contains("AMD",StringComparison.OrdinalIgnoreCase)||graphics.Contains("Radeon",StringComparison.OrdinalIgnoreCase))return "AMD";
        if(graphics.Contains("Intel",StringComparison.OrdinalIgnoreCase)||graphics.Contains("Arc",StringComparison.OrdinalIgnoreCase))return "Intel";
        return "Unknown";
    }

    private sealed record VendorApp(string DisplayName,string Path,string Arguments="");
    private VendorApp? FindVendorApp(string vendor)
    {
        if(vendor=="NVIDIA")
        {
            var nvcpl=FindAppPath("nvcplui.exe") ?? FindExecutable("nvcplui.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation"),Environment.SystemDirectory);
            if(nvcpl is not null)return new("NVIDIA Control Panel",nvcpl);
            var nva=FindExecutable("NVIDIA app.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation"));
            if(nva is not null)return new("NVIDIA App",nva);
        }
        if(vendor=="AMD")
        {
            var amd=FindExecutable("RadeonSoftware.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"AMD"));
            if(amd is not null)return new("AMD Software",amd);
        }
        if(vendor=="Intel")
        {
            // Appx apps are most reliably opened through the shell. Use Windows graphics as a supported fallback.
            return new("Windows / Intel Graphics", "ms-settings:display-advancedgraphics");
        }
        return null;
    }

    private static bool CanLoad(string dll){try{if(NativeLibrary.TryLoad(dll,out var h)){NativeLibrary.Free(h);return true;}}catch{}return false;}
    private static string? FindAppPath(string exe)
    {
        try{using var k=Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");var v=k?.GetValue(null)?.ToString();if(!string.IsNullOrWhiteSpace(v)&&File.Exists(v))return v;}catch{}
        return null;
    }
    private static string? FindExecutable(string name,params string[] roots)
    {
        foreach(var root in roots.Where(Directory.Exists))
        {
            try
            {
                var direct=Path.Combine(root,name); if(File.Exists(direct))return direct;
                if (!root.Equals(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    foreach(var dir in Directory.EnumerateDirectories(root))
                    {
                        var p=Path.Combine(dir,name); if(File.Exists(p))return p;
                        try{var child=Directory.EnumerateFiles(dir,name,SearchOption.AllDirectories).FirstOrDefault();if(child is not null)return child;}catch{}
                    }
                }
            }catch{}
        }
        return null;
    }
}
