using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class HardwareInfoService : IHardwareInfoService
{
    private sealed class ProbeResult
    {
        public string CpuName { get; set; } = string.Empty;
        public int CpuCores { get; set; }
        public int CpuThreads { get; set; }
        public int CpuMaxMHz { get; set; }
        public int RamModules { get; set; }
        public int RamSpeed { get; set; }
        public string BoardManufacturer { get; set; } = string.Empty;
        public string BoardProduct { get; set; } = string.Empty;
        public string BiosVersion { get; set; } = string.Empty;
        public string BiosDate { get; set; } = string.Empty;
        public int RssCapable { get; set; }
        public List<ProbeNetworkAdapter> Network { get; set; } = new();
    }

    private sealed class ProbeNetworkAdapter
    {
        public string Name { get; set; } = string.Empty;
        public string InterfaceDescription { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string LinkSpeed { get; set; } = string.Empty;
        public string DriverInformation { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public int InterfaceIndex { get; set; }
    }

    private readonly IAppLogger _logger;
    private readonly object _cacheLock = new();
    private HardwareInfo? _cached;

    public HardwareInfoService(IAppLogger logger)
    {
        _logger = logger;
    }

    public HardwareInfo GetHardwareInfo()
    {
        lock (_cacheLock)
        {
            if (_cached is not null)
                return _cached;

            var probe = SafeProbe();
            var processor = !string.IsNullOrWhiteSpace(probe?.CpuName)
                ? probe.CpuName.Trim()
                : SafeRead(GetProcessor, "Unavailable");
            var graphics = SafeRead(GetGraphics, "Unavailable");
            var gpuDetails = SafeRead(() => GetGraphicsDetails(graphics), "Driver/VRAM details unavailable");
            var memory = SafeRead(GetMemory, "Unavailable");
            var motherboard = !string.IsNullOrWhiteSpace(probe?.BoardProduct)
                ? string.Join(" ", new[] { probe!.BoardManufacturer, probe.BoardProduct }.Where(x => !string.IsNullOrWhiteSpace(x)))
                : SafeRead(GetMotherboard, "Unavailable");
            var windows = SafeRead(GetWindows, "Unavailable");
            var network = BuildNetworkSummary(probe);
            var networkDetails = BuildNetworkDetails(probe);

            _cached = new HardwareInfo
            {
                Processor = processor,
                ProcessorDetails = BuildProcessorDetails(probe),
                Graphics = graphics,
                GraphicsDetails = gpuDetails,
                Memory = memory,
                MemoryDetails = BuildMemoryDetails(probe),
                Motherboard = string.IsNullOrWhiteSpace(motherboard) ? "Unavailable" : motherboard,
                MotherboardDetails = BuildMotherboardDetails(probe),
                Windows = windows,
                WindowsDetails = BuildWindowsDetails(),
                SystemDrive = SafeRead(GetSystemDrive, "Unavailable"),
                Network = network,
                NetworkDetails = networkDetails,
                SupportedFeatures = BuildSupportedFeatures(graphics, windows, probe),
                DeviceName = Environment.MachineName
            };
            return _cached;
        }
    }

    private ProbeResult? SafeProbe()
    {
        try
        {
            return RunCimProbe();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Extended hardware probe failed: {ex.Message}");
            return null;
        }
    }

    private ProbeResult? RunCimProbe()
    {
        if (!OperatingSystem.IsWindows()) return null;

        const string script = """
$ErrorActionPreference='SilentlyContinue'
$cpu=Get-CimInstance Win32_Processor | Select-Object -First 1
$mem=@(Get-CimInstance Win32_PhysicalMemory)
$board=Get-CimInstance Win32_BaseBoard | Select-Object -First 1
$bios=Get-CimInstance Win32_BIOS | Select-Object -First 1
$net=@(Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Select-Object Name,InterfaceDescription,Status,@{N='LinkSpeed';E={$_.LinkSpeed.ToString()}},DriverInformation,MacAddress,InterfaceIndex)
$rss=0
foreach($n in $net){ if($n.Status -eq 'Up'){ try { $r=Get-NetAdapterRss -Name $n.Name -ErrorAction Stop; if($null -ne $r){$rss++} } catch{} } }
$speeds=@($mem | Where-Object {$_.Speed -gt 0} | Select-Object -ExpandProperty Speed)
[pscustomobject]@{
 CpuName=[string]$cpu.Name
 CpuCores=[int]$cpu.NumberOfCores
 CpuThreads=[int]$cpu.NumberOfLogicalProcessors
 CpuMaxMHz=[int]$cpu.MaxClockSpeed
 RamModules=[int]$mem.Count
 RamSpeed=if($speeds.Count -gt 0){[int](($speeds | Measure-Object -Maximum).Maximum)}else{0}
 BoardManufacturer=[string]$board.Manufacturer
 BoardProduct=[string]$board.Product
 BiosVersion=[string]$bios.SMBIOSBIOSVersion
 BiosDate=if($bios.ReleaseDate){([datetime]$bios.ReleaseDate).ToString('yyyy-MM-dd')}else{''}
 RssCapable=[int]$rss
 Network=[object[]]$net
} | ConvertTo-Json -Depth 5 -Compress
""";

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var info = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(encoded);

        using var process = Process.Start(info);
        if (process is not null)
        {
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        }
        if (process is null) return null;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(3500))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return null;
        }
        Task.WaitAll(outputTask, errorTask);
        var output = outputTask.Result;
        var error = errorTask.Result;
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            if (!string.IsNullOrWhiteSpace(error)) _logger.Warning($"Hardware PowerShell probe: {error.Trim()}");
            return null;
        }

        return JsonSerializer.Deserialize<ProbeResult>(output.Trim(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private string SafeRead(Func<string> reader, string fallback)
    {
        try
        {
            var value = reader();
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Hardware detection failed in {reader.Method.Name}: {ex.Message}");
            return fallback;
        }
    }

    private static string BuildProcessorDetails(ProbeResult? probe)
    {
        if (probe is null || probe.CpuThreads <= 0)
            return $"{Environment.ProcessorCount} logical processors";

        var clock = probe.CpuMaxMHz > 0 ? $" • up to {probe.CpuMaxMHz / 1000d:0.00} GHz" : string.Empty;
        return $"{probe.CpuCores} cores / {probe.CpuThreads} threads{clock}";
    }

    private static string BuildMemoryDetails(ProbeResult? probe)
    {
        if (probe is null) return "Module details unavailable";
        var modules = probe.RamModules > 0 ? $"{probe.RamModules} module{(probe.RamModules == 1 ? string.Empty : "s")}" : "Modules unavailable";
        var speed = probe.RamSpeed > 0 ? $" • {probe.RamSpeed} MT/s reported" : string.Empty;
        return modules + speed;
    }

    private static string BuildMotherboardDetails(ProbeResult? probe)
    {
        if (probe is null || string.IsNullOrWhiteSpace(probe.BiosVersion)) return "BIOS details unavailable";
        return string.IsNullOrWhiteSpace(probe.BiosDate)
            ? $"BIOS {probe.BiosVersion}"
            : $"BIOS {probe.BiosVersion} • {probe.BiosDate}";
    }

    private static string BuildWindowsDetails()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var installation = key?.GetValue("InstallationType")?.ToString();
        var architecture = RuntimeInformation.OSArchitecture.ToString();
        return string.Join(" • ", new[] { architecture, installation }.Where(x => !string.IsNullOrWhiteSpace(x))!);
    }

    private static string BuildNetworkSummary(ProbeResult? probe)
    {
        if (probe?.Network is null || probe.Network.Count == 0) return "No physical adapters detected";
        var up = probe.Network
            .Where(x => x.Status.Equals("Up", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Name.Contains("Ethernet", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault() ?? probe.Network[0];
        return string.IsNullOrWhiteSpace(up.LinkSpeed) ? up.Name : $"{up.Name} • {up.LinkSpeed}";
    }

    private static string BuildNetworkDetails(ProbeResult? probe)
    {
        if (probe?.Network is null || probe.Network.Count == 0) return "No physical network adapter details available";
        return string.Join(" | ", probe.Network.Select(adapter =>
        {
            var speed = string.IsNullOrWhiteSpace(adapter.LinkSpeed) ? string.Empty : $" • {adapter.LinkSpeed}";
            return $"{adapter.Name}: {adapter.Status}{speed}";
        }));
    }

    private static string BuildSupportedFeatures(string graphics, string windows, ProbeResult? probe)
    {
        var features = new List<string>();
        if (Environment.Is64BitOperatingSystem) features.Add("x64 Windows");
        if (probe?.RssCapable > 0) features.Add($"RSS ×{probe.RssCapable}");
        if (!graphics.Contains("Unavailable", StringComparison.OrdinalIgnoreCase) && windows.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
            features.Add("HAGS check");
        if (OperatingSystem.IsWindows()) features.Add("CPU power controls");
        if (probe?.Network.Count > 0) features.Add("NIC tuning");
        return features.Count == 0 ? "No optional feature capabilities detected" : string.Join(" • ", features);
    }

    private static string GetProcessor()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unavailable";
    }

    private static string GetGraphics()
    {
        var liveDisplayDevices = new List<string>();
        AddDisplayDevices(liveDisplayDevices);
        var detected = SelectBestAdapter(liveDisplayDevices);
        if (detected is not null) return detected;

        var directXAdapters = new List<string>();
        AddDirectXRegistryAdapters(directXAdapters);
        detected = SelectBestAdapter(directXAdapters);
        if (detected is not null) return detected;

        var displayClassAdapters = new List<string>();
        AddDisplayClassAdapters(displayClassAdapters);
        detected = SelectBestAdapter(displayClassAdapters);
        if (detected is not null) return detected;

        var pciAdapters = new List<string>();
        AddPciDisplayAdapters(pciAdapters);
        return SelectBestAdapter(pciAdapters) ?? "Unavailable";
    }

    private static string GetGraphicsDetails(string selectedGraphics)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var directX = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
                if (directX is null) continue;
                foreach (var subName in directX.GetSubKeyNames())
                {
                    using var adapter = directX.OpenSubKey(subName);
                    var description = adapter?.GetValue("Description")?.ToString();
                    if (string.IsNullOrWhiteSpace(description) ||
                        (!selectedGraphics.Contains(description, StringComparison.OrdinalIgnoreCase) && !description.Contains(selectedGraphics, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var driver = adapter?.GetValue("DriverVersion")?.ToString();
                    string? vram = null;
                    try
                    {
                        var raw = adapter?.GetValue("DedicatedVideoMemory");
                        if (raw is not null)
                        {
                            var bytes = Convert.ToInt64(raw);
                            if (bytes > 0) vram = $"{bytes / 1024d / 1024d / 1024d:0.#} GB dedicated VRAM";
                        }
                    }
                    catch { }
                    return string.Join(" • ", new[] { vram, string.IsNullOrWhiteSpace(driver) ? null : $"Driver {driver}" }.Where(x => !string.IsNullOrWhiteSpace(x))!);
                }
            }
            catch { }
        }
        return "Driver/VRAM details unavailable";
    }

    private static string? SelectBestAdapter(IEnumerable<string> adapters) => adapters
        .Select(CleanAdapterName)
        .Where(IsUsableAdapterName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(AdapterPriority)
        .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();

    private static void AddDisplayDevices(List<string> adapters)
    {
        for (uint index = 0; index < 16; index++)
        {
            var device = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 0)) break;
            if ((device.StateFlags & DisplayDeviceMirroringDriver) != 0 || (device.StateFlags & DisplayDeviceRemote) != 0) continue;
            if (!string.IsNullOrWhiteSpace(device.DeviceString)) adapters.Add(device.DeviceString);
        }
    }

    private static void AddDirectXRegistryAdapters(List<string> adapters)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var directX = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
                if (directX is null) continue;
                foreach (var subName in directX.GetSubKeyNames())
                {
                    using var adapter = directX.OpenSubKey(subName);
                    var description = adapter?.GetValue("Description")?.ToString();
                    if (!string.IsNullOrWhiteSpace(description)) adapters.Add(description);
                }
            }
            catch { }
        }
    }

    private static void AddDisplayClassAdapters(List<string> adapters)
    {
        const string displayClassPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        using var classKey = Registry.LocalMachine.OpenSubKey(displayClassPath);
        if (classKey is null) return;
        foreach (var subKeyName in classKey.GetSubKeyNames())
        {
            using var adapterKey = classKey.OpenSubKey(subKeyName);
            var description = adapterKey?.GetValue("DriverDesc")?.ToString();
            if (!string.IsNullOrWhiteSpace(description)) adapters.Add(description);
        }
    }

    private static void AddPciDisplayAdapters(List<string> adapters)
    {
        using var pci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");
        if (pci is null) return;
        foreach (var deviceKeyName in pci.GetSubKeyNames())
        {
            using var deviceKey = pci.OpenSubKey(deviceKeyName);
            if (deviceKey is null) continue;
            foreach (var instanceName in deviceKey.GetSubKeyNames())
            {
                using var instance = deviceKey.OpenSubKey(instanceName);
                if (instance is null) continue;
                var className = instance.GetValue("Class")?.ToString();
                var classGuid = instance.GetValue("ClassGUID")?.ToString();
                var isDisplay = string.Equals(className, "Display", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(classGuid, "{4d36e968-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase);
                if (!isDisplay) continue;
                var friendly = instance.GetValue("FriendlyName")?.ToString();
                var description = instance.GetValue("DeviceDesc")?.ToString();
                if (!string.IsNullOrWhiteSpace(friendly)) adapters.Add(friendly);
                if (!string.IsNullOrWhiteSpace(description)) adapters.Add(description);
            }
        }
    }

    private static string CleanAdapterName(string name)
    {
        var value = name.Trim();
        var semicolon = value.LastIndexOf(';');
        if (semicolon >= 0 && semicolon < value.Length - 1) value = value[(semicolon + 1)..].Trim();
        if (value.StartsWith("@", StringComparison.Ordinal) && value.Contains('%'))
        {
            var lastPercent = value.LastIndexOf('%');
            if (lastPercent >= 0 && lastPercent < value.Length - 1) value = value[(lastPercent + 1)..].TrimStart(';', ' ');
        }
        return value;
    }

    private static bool IsUsableAdapterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string[] excluded = ["Microsoft Basic Display", "Microsoft Remote Display", "Remote Display Adapter", "Indirect Display", "Mirror Driver"];
        return !excluded.Any(item => name.Contains(item, StringComparison.OrdinalIgnoreCase));
    }

    private static int AdapterPriority(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static string GetMemory()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(ref status)) return "Unavailable";
        var gib = status.TotalPhysical / 1024d / 1024d / 1024d;
        return $"{Math.Round(gib):0} GB RAM";
    }

    private static string GetMotherboard()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        var manufacturer = key?.GetValue("BaseBoardManufacturer")?.ToString()?.Trim();
        var product = key?.GetValue("BaseBoardProduct")?.ToString()?.Trim();
        return string.Join(" ", new[] { manufacturer, product }.Where(x => !string.IsNullOrWhiteSpace(x))!);
    }

    private static string GetWindows()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName")?.ToString()?.Trim() ?? "Windows";
        var displayVersion = key?.GetValue("DisplayVersion")?.ToString()?.Trim();
        var build = key?.GetValue("CurrentBuildNumber")?.ToString()?.Trim();
        var ubr = key?.GetValue("UBR")?.ToString()?.Trim();
        if (int.TryParse(build, out var buildNumber) && buildNumber >= 22000 && product.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
            product = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        var buildText = string.IsNullOrWhiteSpace(build) ? null : string.IsNullOrWhiteSpace(ubr) ? $"Build {build}" : $"Build {build}.{ubr}";
        return string.Join(" • ", new[] { product, displayVersion, buildText }.Where(x => !string.IsNullOrWhiteSpace(x))!);
    }

    private static string GetSystemDrive()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrWhiteSpace(root)) return "Unavailable";
        var drive = new DriveInfo(root);
        if (!drive.IsReady) return drive.Name;
        var totalGiB = drive.TotalSize / 1024d / 1024d / 1024d;
        var freeGiB = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
        return $"{drive.Name.TrimEnd('\\')} • {totalGiB:0} GB total • {freeGiB:0} GB free";
    }

    private const int DisplayDeviceMirroringDriver = 0x00000008;
    private const int DisplayDeviceRemote = 0x04000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
            MemoryLoad = 0;
            TotalPhysical = 0;
            AvailablePhysical = 0;
            TotalPageFile = 0;
            AvailablePageFile = 0;
            TotalVirtual = 0;
            AvailableVirtual = 0;
            AvailableExtendedVirtual = 0;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
