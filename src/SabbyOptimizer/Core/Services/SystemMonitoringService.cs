using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class SystemMonitoringService : IAsyncDisposable
{
    private readonly HardwareInfo _hardware;
    private readonly IAppLogger _logger;
    private readonly object _stateGate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _intervalMs = 2000;

    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private DateTimeOffset _previousNetworkAt;
    private long _previousBytesReceived;
    private long _previousBytesSent;
    private string? _nvidiaSmiPath;
    private DateTimeOffset _nextGpuProbe = DateTimeOffset.MinValue;
    private GpuSample _lastGpu = GpuSample.Empty;

    public event EventHandler<SystemMonitorSnapshot>? SampleUpdated;
    public event EventHandler<bool>? RunningStateChanged;
    public bool IsRunning => _loop is { IsCompleted: false };
    public SystemMonitorSnapshot? Latest { get; private set; }

    public SystemMonitoringService(HardwareInfo hardware, IAppLogger logger)
    {
        _hardware = hardware;
        _logger = logger;
        _nvidiaSmiPath = FindNvidiaSmi();
    }

    public void Start(int intervalMs = 2000)
    {
        intervalMs = Math.Clamp(intervalMs, 750, 10000);
        lock (_stateGate)
        {
            _intervalMs = intervalMs;
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }
        RunningStateChanged?.Invoke(this, true);
        _logger.Info($"Phase 20 monitoring started at {_intervalMs} ms refresh.");
    }

    public async Task RestartAsync(int intervalMs, CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        Start(intervalMs);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_stateGate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        if (cts is null)
        {
            RunningStateChanged?.Invoke(this, false);
            return;
        }
        cts.Cancel();
        try
        {
            if (loop is not null)
                await loop.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
        finally { cts.Dispose(); }
        RunningStateChanged?.Invoke(this, false);
        _logger.Info("Phase 20 monitoring stopped.");
    }

    public async Task<SystemMonitorSnapshot> SampleNowAsync(CancellationToken cancellationToken = default)
    {
        var cpuUsage = ReadCpuUsagePercent();
        var (currentMhz, maxMhz) = ReadCpuClock();
        var (memoryPercent, usedGb, totalGb) = ReadMemory();
        var (downMbps, upMbps) = ReadNetworkRates();

        if (DateTimeOffset.UtcNow >= _nextGpuProbe)
        {
            _lastGpu = await ReadGpuAsync(cancellationToken).ConfigureAwait(false);
            _nextGpuProbe = DateTimeOffset.UtcNow.AddSeconds(2);
        }

        var snapshot = new SystemMonitorSnapshot(
            DateTimeOffset.Now,
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            cpuUsage,
            currentMhz,
            maxMhz,
            memoryPercent,
            usedGb,
            totalGb,
            downMbps,
            upMbps,
            _hardware.Graphics,
            _lastGpu.Utilization,
            _lastGpu.Temperature,
            _lastGpu.GraphicsClock,
            _lastGpu.MemoryClock,
            _lastGpu.PowerWatts,
            _lastGpu.Source);

        Latest = snapshot;
        return snapshot;
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var sample = await SampleNowAsync(cancellationToken).ConfigureAwait(false);
                SampleUpdated?.Invoke(this, sample);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.Warning($"Monitoring sample failed: {ex.Message}"); }

            try { await Task.Delay(_intervalMs, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private double ReadCpuUsagePercent()
    {
        if (!OperatingSystem.IsWindows() || !GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var idleNow = ToUInt64(idle);
        var kernelNow = ToUInt64(kernel);
        var userNow = ToUInt64(user);

        if (_previousKernel == 0 && _previousUser == 0)
        {
            _previousIdle = idleNow;
            _previousKernel = kernelNow;
            _previousUser = userNow;
            return 0;
        }

        var idleDelta = idleNow - _previousIdle;
        var kernelDelta = kernelNow - _previousKernel;
        var userDelta = userNow - _previousUser;
        _previousIdle = idleNow;
        _previousKernel = kernelNow;
        _previousUser = userNow;
        var total = kernelDelta + userDelta;
        if (total == 0) return 0;
        return Math.Clamp((total - idleDelta) * 100d / total, 0, 100);
    }

    private static (double CurrentMhz, double MaxMhz) ReadCpuClock()
    {
        if (!OperatingSystem.IsWindows()) return (0, 0);
        var count = Math.Max(1, Environment.ProcessorCount);
        var size = Marshal.SizeOf<ProcessorPowerInformation>();
        var buffer = Marshal.AllocHGlobal(size * count);
        try
        {
            var status = CallNtPowerInformation(11, IntPtr.Zero, 0, buffer, (uint)(size * count));
            if (status != 0) return (0, 0);
            double current = 0, max = 0;
            var valid = 0;
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<ProcessorPowerInformation>(buffer + i * size);
                if (info.MaxMhz == 0) continue;
                current += info.CurrentMhz;
                max += info.MaxMhz;
                valid++;
            }
            return valid == 0 ? (0, 0) : (current / valid, max / valid);
        }
        catch { return (0, 0); }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static (double Percent, double UsedGb, double TotalGb) ReadMemory()
    {
        if (!OperatingSystem.IsWindows()) return (0, 0, 0);
        var state = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref state)) return (0, 0, 0);
        var total = state.ullTotalPhys / 1073741824d;
        var available = state.ullAvailPhys / 1073741824d;
        var used = Math.Max(0, total - available);
        return (state.dwMemoryLoad, used, total);
    }

    private (double DownMbps, double UpMbps) ReadNetworkRates()
    {
        long received = 0, sent = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var stats = nic.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
        }
        catch { return (0, 0); }

        var now = DateTimeOffset.UtcNow;
        if (_previousNetworkAt == default)
        {
            _previousNetworkAt = now;
            _previousBytesReceived = received;
            _previousBytesSent = sent;
            return (0, 0);
        }
        var seconds = Math.Max(0.001, (now - _previousNetworkAt).TotalSeconds);
        var down = Math.Max(0, received - _previousBytesReceived) * 8d / 1_000_000d / seconds;
        var up = Math.Max(0, sent - _previousBytesSent) * 8d / 1_000_000d / seconds;
        _previousNetworkAt = now;
        _previousBytesReceived = received;
        _previousBytesSent = sent;
        return (down, up);
    }

    private async Task<GpuSample> ReadGpuAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_nvidiaSmiPath) || !File.Exists(_nvidiaSmiPath))
            return new GpuSample(null, null, null, null, null, "No supported vendor sensor source detected");

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = _nvidiaSmiPath,
                Arguments = "--query-gpu=utilization.gpu,temperature.gpu,clocks.current.graphics,clocks.current.memory,power.draw --format=csv,noheader,nounits",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(start);
            if (process is null) return GpuSample.Empty;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(1500);
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return _lastGpu with { Source = "NVIDIA sensor timed out" };
            }
            var line = (await outputTask.ConfigureAwait(false)).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(line)) return GpuSample.Empty;
            var parts = line.Split(',').Select(x => x.Trim()).ToArray();
            if (parts.Length < 5) return GpuSample.Empty;
            return new GpuSample(Parse(parts[0]), Parse(parts[1]), Parse(parts[2]), Parse(parts[3]), Parse(parts[4]), "NVIDIA NVSMI");
        }
        catch { return GpuSample.Empty; }
    }

    private static double? Parse(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static string? FindNvidiaSmi()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe")
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;
        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), "nvidia-smi.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { }
        return null;
    }

    private static ulong ToUInt64(FILETIME value) => ((ulong)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private sealed record GpuSample(double? Utilization, double? Temperature, double? GraphicsClock, double? MemoryClock, double? PowerWatts, string Source)
    {
        public static readonly GpuSample Empty = new(null, null, null, null, null, "Sensor unavailable");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, uint inputBufferLength, IntPtr outputBuffer, uint outputBufferLength);
}
