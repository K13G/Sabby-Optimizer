using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class BenchmarkService
{
    private readonly IAppPaths _paths;
    private readonly HardwareInfo _hardware;
    private readonly PingOptimizationService _ping;
    private readonly IAppLogger _logger;
    private readonly string _directory;
    private readonly string _beforeFile;
    private readonly string _afterFile;

    public BenchmarkService(IAppPaths paths, HardwareInfo hardware, PingOptimizationService ping, IAppLogger logger)
    {
        _paths = paths;
        _hardware = hardware;
        _ping = ping;
        _logger = logger;
        _directory = Path.Combine(paths.UserDataDirectory, "Benchmarks");
        _beforeFile = Path.Combine(_directory, "before.json");
        _afterFile = Path.Combine(_directory, "after.json");
        Directory.CreateDirectory(_directory);
    }

    public string ReportDirectory => _directory;

    public async Task<BenchmarkSnapshot?> LoadBeforeAsync(CancellationToken cancellationToken = default) =>
        await LoadAsync(_beforeFile, cancellationToken).ConfigureAwait(false);

    public async Task<BenchmarkSnapshot?> LoadAfterAsync(CancellationToken cancellationToken = default) =>
        await LoadAsync(_afterFile, cancellationToken).ConfigureAwait(false);

    public async Task<BenchmarkSnapshot> RunAsync(
        string stage,
        IProgress<BenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var metrics = new List<BenchmarkMetric>();
        progress?.Report(new BenchmarkProgress(2, "Warming up CPU benchmark…"));

        var cpuHash = await Task.Run(() => MeasureMedian(3, cancellationToken, MeasureCpuHashThroughput), cancellationToken).ConfigureAwait(false);
        metrics.Add(new BenchmarkMetric("cpu.sha256", "CPU hash throughput", "CPU", cpuHash, "MB/s", true, 3));
        progress?.Report(new BenchmarkProgress(18, "Measuring CPU compute throughput…"));

        var cpuMath = await Task.Run(() => MeasureMedian(3, cancellationToken, MeasureCpuMathThroughput), cancellationToken).ConfigureAwait(false);
        metrics.Add(new BenchmarkMetric("cpu.math", "CPU compute throughput", "CPU", cpuMath, "M ops/s", true, 3));
        progress?.Report(new BenchmarkProgress(32, "Measuring memory copy throughput…"));

        var memory = await Task.Run(() => MeasureMedian(3, cancellationToken, MeasureMemoryThroughput), cancellationToken).ConfigureAwait(false);
        metrics.Add(new BenchmarkMetric("memory.copy", "Memory copy throughput", "Memory", memory, "GB/s", true, 5));
        progress?.Report(new BenchmarkProgress(45, "Measuring scheduler latency…"));

        var scheduler = await MeasureSchedulerP95Async(cancellationToken).ConfigureAwait(false);
        metrics.Add(new BenchmarkMetric("scheduler.p95", "Scheduler delay p95", "Latency", scheduler, "ms", false, 8));
        progress?.Report(new BenchmarkProgress(56, "Measuring storage write/read throughput…"));

        var (write, read) = await Task.Run(() => MeasureStorage(cancellationToken), cancellationToken).ConfigureAwait(false);
        metrics.Add(new BenchmarkMetric("disk.write", "Storage sequential write", "Storage", write, "MB/s", true, 8));
        metrics.Add(new BenchmarkMetric("disk.read", "Storage sequential read", "Storage", read, "MB/s", true, 8));
        progress?.Report(new BenchmarkProgress(72, "Measuring local gateway latency…"));

        var route = _ping.GetLocalRoute();
        if (!string.IsNullOrWhiteSpace(route.Gateway))
        {
            var gateway = await _ping.TestAsync("Local gateway", route.Gateway, 15, null, cancellationToken).ConfigureAwait(false);
            metrics.Add(new BenchmarkMetric("net.gateway.avg", "Gateway average latency", "Network", gateway.AverageMs, "ms", false, 10));
            metrics.Add(new BenchmarkMetric("net.gateway.jitter", "Gateway jitter", "Network", gateway.JitterMs, "ms", false, 15));
            metrics.Add(new BenchmarkMetric("net.gateway.loss", "Gateway packet loss", "Network", gateway.LossPercent, "%", false, 1));
        }
        progress?.Report(new BenchmarkProgress(84, "Measuring public Internet latency…"));

        var cloudflare = await _ping.TestAsync("Cloudflare", "1.1.1.1", 15, null, cancellationToken).ConfigureAwait(false);
        metrics.Add(new BenchmarkMetric("net.public.avg", "Public average latency", "Network", cloudflare.AverageMs, "ms", false, 10));
        metrics.Add(new BenchmarkMetric("net.public.jitter", "Public jitter", "Network", cloudflare.JitterMs, "ms", false, 15));
        metrics.Add(new BenchmarkMetric("net.public.loss", "Public packet loss", "Network", cloudflare.LossPercent, "%", false, 1));

        var snapshot = new BenchmarkSnapshot(
            stage,
            DateTimeOffset.Now,
            $"{_hardware.Processor} • {_hardware.Graphics} • {_hardware.Memory}",
            metrics);

        progress?.Report(new BenchmarkProgress(96, "Saving benchmark snapshot…"));
        var target = stage.Equals("Before", StringComparison.OrdinalIgnoreCase) ? _beforeFile : _afterFile;
        await SaveAsync(target, snapshot, cancellationToken).ConfigureAwait(false);
        progress?.Report(new BenchmarkProgress(100, $"{stage} benchmark complete."));
        return snapshot;
    }

    public void Clear()
    {
        TryDelete(_beforeFile);
        TryDelete(_afterFile);
    }

    private static double MeasureMedian(int count, CancellationToken cancellationToken, Func<CancellationToken, double> measure)
    {
        var values = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            values.Add(measure(cancellationToken));
        }
        values.Sort();
        return values[values.Count / 2];
    }

    private static double MeasureCpuHashThroughput(CancellationToken cancellationToken)
    {
        const int size = 8 * 1024 * 1024;
        const int passes = 12;
        var data = new byte[size];
        Random.Shared.NextBytes(data);
        _ = SHA256.HashData(data); // warm-up
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < passes; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = SHA256.HashData(data);
        }
        sw.Stop();
        var mb = (size * (double)passes) / (1024 * 1024);
        return mb / Math.Max(0.001, sw.Elapsed.TotalSeconds);
    }

    private static double MeasureCpuMathThroughput(CancellationToken cancellationToken)
    {
        const int iterations = 4_000_000;
        double accumulator = 0.25;
        var sw = Stopwatch.StartNew();
        for (var i = 1; i <= iterations; i++)
        {
            if ((i & 0x3FFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            accumulator += Math.Sqrt((i % 1000) + accumulator * 0.000001) * 0.00001;
        }
        sw.Stop();
        GC.KeepAlive(accumulator);
        return (iterations / 1_000_000d) / Math.Max(0.001, sw.Elapsed.TotalSeconds);
    }

    private static double MeasureMemoryThroughput(CancellationToken cancellationToken)
    {
        const int size = 64 * 1024 * 1024;
        const int passes = 6;
        var source = GC.AllocateUninitializedArray<byte>(size);
        var destination = GC.AllocateUninitializedArray<byte>(size);
        source[0] = 1;
        Buffer.BlockCopy(source, 0, destination, 0, size); // warm-up/commit
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < passes; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Buffer.BlockCopy(source, 0, destination, 0, size);
        }
        sw.Stop();
        GC.KeepAlive(destination);
        var gib = (size * (double)passes) / (1024d * 1024d * 1024d);
        return gib / Math.Max(0.001, sw.Elapsed.TotalSeconds);
    }

    private async Task<double> MeasureSchedulerP95Async(CancellationToken cancellationToken)
    {
        const int count = 80;
        var values = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            values.Add(Math.Max(0, sw.Elapsed.TotalMilliseconds - 1));
        }
        values.Sort();
        var index = (int)Math.Clamp(Math.Ceiling(values.Count * 0.95) - 1, 0, values.Count - 1);
        return values[index];
    }

    private (double Write, double Read) MeasureStorage(CancellationToken cancellationToken)
    {
        const int size = 48 * 1024 * 1024;
        const int block = 1024 * 1024;
        var path = Path.Combine(_directory, $"bench-{Guid.NewGuid():N}.tmp");
        var buffer = GC.AllocateUninitializedArray<byte>(block);
        Random.Shared.NextBytes(buffer);
        try
        {
            var writeSw = Stopwatch.StartNew();
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, block,
                       FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                for (var written = 0; written < size; written += block)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    stream.Write(buffer, 0, block);
                }
                stream.Flush(flushToDisk: true);
            }
            writeSw.Stop();

            var readSw = Stopwatch.StartNew();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, block, FileOptions.SequentialScan))
            {
                while (stream.Read(buffer, 0, buffer.Length) > 0)
                    cancellationToken.ThrowIfCancellationRequested();
            }
            readSw.Stop();
            var mb = size / (1024d * 1024d);
            return (mb / Math.Max(0.001, writeSw.Elapsed.TotalSeconds), mb / Math.Max(0.001, readSw.Elapsed.TotalSeconds));
        }
        finally
        {
            TryDelete(path);
        }
    }

    private async Task SaveAsync(string path, BenchmarkSnapshot snapshot, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.Error("Benchmark snapshot save failed.", ex);
            throw;
        }
    }

    private async Task<BenchmarkSnapshot?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<BenchmarkSnapshot>(json);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not load benchmark snapshot {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
