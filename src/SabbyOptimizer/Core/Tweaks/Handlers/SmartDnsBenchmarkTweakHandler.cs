using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class SmartDnsBenchmarkTweakHandler : ITweakHandler
{
    private sealed record DnsProvider(string Name, string Primary, string Secondary);
    private sealed record DnsState(int InterfaceIndex, string Name, bool Automatic, string[] Servers);
    private sealed record DnsRestoreEnvelope(List<DnsState> Previous, string ProviderName, string[] AppliedServers, double MedianMilliseconds, DateTime AppliedAtUtc, double JitterMilliseconds = 0, double P90Milliseconds = 0, string BenchmarkSummary = "");
    private sealed record ProviderResult(DnsProvider Provider, double MedianMilliseconds, double JitterMilliseconds, double P90Milliseconds, double Score, int Successes, int Attempts);

    private static readonly DnsProvider[] Providers =
    [
        new("Cloudflare", "1.1.1.1", "1.0.0.1"),
        new("Google", "8.8.8.8", "8.8.4.4"),
        new("Quad9 Unfiltered", "9.9.9.10", "149.112.112.10"),
        new("AdGuard Non-filtering", "94.140.14.140", "94.140.14.141"),
        new("Control D Unfiltered", "76.76.2.0", "76.76.10.0"),
        new("DNS.SB", "185.222.222.222", "45.11.45.11")
    ];

    private readonly string _restoreFile;

    public SmartDnsBenchmarkTweakHandler(IAppPaths paths)
    {
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, "dns-smart-benchmark.json");
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() &&
        PowerShellNetworkAccess.CommandExists("Get-DnsClientServerAddress") &&
        PowerShellNetworkAccess.CommandExists("Set-DnsClientServerAddress") &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapter");

    public TweakDefinition Definition { get; } = new(
        "network.smart-dns",
        "Smart DNS Benchmark",
        "Benchmark major public resolvers from this PC, apply the fastest healthy pair, and remember exactly what DNS configuration was replaced.",
        TweakCategory.Network,
        TweakSafetyLevel.Safe,
        "DNS performance depends on the route from your ISP to each resolver, so a globally popular resolver is not automatically the fastest for your connection. Sabby sends real DNS queries directly to a curated set of public anycast resolvers, ranks successful responses by median lookup time, then applies the fastest healthy provider. This can improve name-resolution latency and reliability, but it does not lower an already-established game's server RTT.",
        "IPv4 DNS server addresses on connected physical adapters. Candidates include Cloudflare, Google, Quad9, AdGuard non-filtering, Control D unfiltered, and DNS.SB.",
        "Undo restores each adapter's prior static DNS list or returns it to automatic/DHCP DNS if that was the original state.",
        true,
        false,
        true,
        true);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakDetectionResult.Unavailable("No connected physical IPv4 adapter was found."));

        var saved = ReadEnvelope();
        if (saved is not null && saved.AppliedServers.Length >= 2 && states.All(x => Matches(x.Servers, saved.AppliedServers)))
        {
            return Task.FromResult(new TweakDetectionResult(
                TweakStateKind.Applied,
                $"{saved.ProviderName} • {saved.MedianMilliseconds:0} ms",
                $"Sabby's last DNS benchmark selected {saved.ProviderName} ({string.Join(" / ", saved.AppliedServers)}) • {saved.MedianMilliseconds:0.0} ms median • {saved.JitterMilliseconds:0.0} ms jitter • {saved.P90Milliseconds:0.0} ms p90. {saved.BenchmarkSummary}",
                false,
                true));
        }

        var provider = Providers.FirstOrDefault(p => states.All(x => Matches(x.Servers, new[] { p.Primary, p.Secondary })));
        if (provider is not null)
        {
            return Task.FromResult(new TweakDetectionResult(
                TweakStateKind.NotApplied,
                provider.Name,
                $"{provider.Name} is currently configured, but Sabby has not benchmarked it against the other built-in candidates in this activation session.",
                true,
                false));
        }

        return Task.FromResult(new TweakDetectionResult(
            TweakStateKind.NotApplied,
            "Benchmark available",
            "Activate to benchmark the built-in resolver list from this network and apply the fastest healthy DNS pair.",
            true,
            false));
    }

    public async Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return TweakOperationResult.Failed("No connected physical IPv4 adapter was found.");

        var results = await Task.WhenAll(Providers.Select(provider => Task.Run(() => BenchmarkProvider(provider, cancellationToken), cancellationToken)));
        var healthy = results
            .Where(x => x is not null && x.Successes >= 8)
            .Cast<ProviderResult>()
            .OrderBy(x => x.Score)
            .ThenBy(x => x.MedianMilliseconds)
            .ToArray();
        var best = healthy.FirstOrDefault();

        if (best is null)
            return TweakOperationResult.Failed("DNS benchmark could not get enough valid responses from the built-in public resolvers. Nothing was changed.");

        var previous = ReadEnvelope()?.Previous ?? states;
        var servers = new[] { best.Provider.Primary, best.Provider.Secondary };
        foreach (var state in states)
        {
            var result = PowerShellNetworkAccess.Run(
                $"Set-DnsClientServerAddress -InterfaceIndex {state.InterfaceIndex} -ServerAddresses ('{PowerShellNetworkAccess.Escape(servers[0])}','{PowerShellNetworkAccess.Escape(servers[1])}'); Clear-DnsClientCache",
                12000);
            if (!result.Success)
            {
                // Do not leave a half-applied DNS configuration if one adapter fails.
                RestoreStates(states);
                return TweakOperationResult.Failed($"Could not set DNS on {state.Name}. The pre-benchmark DNS state was restored where possible. {result.Error}".Trim());
            }
        }

        var top = healthy.Take(3).Select((x, i) => $"#{i + 1} {x.Provider.Name} {x.MedianMilliseconds:0.0}ms median/{x.JitterMilliseconds:0.0}ms jitter/{x.Successes}/{x.Attempts} replies").ToArray();
        var summary = string.Join(" • ", top);
        SaveEnvelope(new DnsRestoreEnvelope(previous, best.Provider.Name, servers, best.MedianMilliseconds, DateTime.UtcNow, best.JitterMilliseconds, best.P90Milliseconds, summary));
        return TweakOperationResult.Completed(
            $"{best.Provider.Name} won this local benchmark: {best.MedianMilliseconds:0.0} ms median, {best.JitterMilliseconds:0.0} ms jitter, {best.P90Milliseconds:0.0} ms p90, {best.Successes}/{best.Attempts} replies. Applied {string.Join(" / ", servers)}. Top results: {summary}",
            new TweakDetectionResult(TweakStateKind.Applied, $"{best.Provider.Name} • {best.MedianMilliseconds:0} ms", $"Applied and verified {best.Provider.Name}. {summary}", false, true));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var saved = ReadEnvelope();
        if (saved is null || saved.Previous.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed("No saved DNS state exists, so Sabby did not guess."));

        var restoreFailure = RestoreStates(saved.Previous);
        if (!string.IsNullOrWhiteSpace(restoreFailure))
            return Task.FromResult(TweakOperationResult.Failed(restoreFailure));

        TryDeleteRestoreFile();
        return Task.FromResult(TweakOperationResult.Completed(
            "DNS settings were restored to their previous per-adapter configuration.",
            new TweakDetectionResult(TweakStateKind.NotApplied, "Restored", "Previous DNS configuration restored.", true, false)));
    }


    private static string? RestoreStates(IEnumerable<DnsState> states)
    {
        foreach (var state in states)
        {
            PowerShellNetworkAccess.Result result;
            if (state.Automatic || state.Servers.Length == 0)
            {
                result = PowerShellNetworkAccess.Run($"Set-DnsClientServerAddress -InterfaceIndex {state.InterfaceIndex} -ResetServerAddresses; Clear-DnsClientCache");
            }
            else
            {
                var args = string.Join(",", state.Servers.Select(s => $"'{PowerShellNetworkAccess.Escape(s)}'"));
                result = PowerShellNetworkAccess.Run($"Set-DnsClientServerAddress -InterfaceIndex {state.InterfaceIndex} -ServerAddresses ({args}); Clear-DnsClientCache");
            }

            if (!result.Success)
                return $"Could not restore DNS on {state.Name}. {result.Error}".Trim();
        }

        return null;
    }

    private static ProviderResult? BenchmarkProvider(DnsProvider provider, CancellationToken cancellationToken)
    {
        var samples = new List<double>();
        var domains = new[] { "example.com", "microsoft.com", "cloudflare.com", "github.com", "wikipedia.org", "openai.com" };
        var attempts = new List<(string Server, string Domain)>();
        foreach (var domain in domains)
        {
            attempts.Add((provider.Primary, domain));
            attempts.Add((provider.Secondary, domain));
        }

        // One warm-up query per resolver is discarded so the ranking is less dominated by
        // the first socket/path setup and more representative of repeated real lookups.
        _ = MeasureDnsQuery(provider.Primary, "example.com", 950);
        _ = MeasureDnsQuery(provider.Secondary, "example.com", 950);

        foreach (var attempt in attempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ms = MeasureDnsQuery(attempt.Server, attempt.Domain, 950);
            if (ms.HasValue) samples.Add(ms.Value);
        }
        if (samples.Count == 0) return null;
        samples.Sort();
        var median = Percentile(samples, 0.50);
        var p90 = Percentile(samples, 0.90);
        var jitter = samples.Count < 2 ? 0 : samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        var loss = 1d - (double)samples.Count / attempts.Count;
        // Median matters most; instability, tail latency, and packet loss break ties heavily.
        var score = median + (jitter * 0.55) + (Math.Max(0, p90 - median) * 0.20) + (loss * 180);
        return new ProviderResult(provider, median, jitter, p90, score, samples.Count, attempts.Count);
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return double.PositiveInfinity;
        var pos = (sorted.Count - 1) * percentile;
        var lo = (int)Math.Floor(pos); var hi = (int)Math.Ceiling(pos);
        if (lo == hi) return sorted[lo];
        var weight = pos - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * weight;
    }

    private static double? MeasureDnsQuery(string server, string domain, int timeoutMilliseconds)
    {
        try
        {
            var address = IPAddress.Parse(server);
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveTimeout = timeoutMilliseconds,
                SendTimeout = timeoutMilliseconds
            };
            socket.Connect(new IPEndPoint(address, 53));

            var transactionId = (ushort)Random.Shared.Next(1, ushort.MaxValue);
            var query = BuildQuery(transactionId, domain);
            var response = new byte[2048];
            var stopwatch = Stopwatch.StartNew();
            socket.Send(query);
            var received = socket.Receive(response);
            stopwatch.Stop();

            if (received < 12)
                return null;
            if (response[0] != (byte)(transactionId >> 8) || response[1] != (byte)(transactionId & 0xFF))
                return null;
            if ((response[2] & 0x80) == 0)
                return null;
            var responseCode = response[3] & 0x0F;
            if (responseCode != 0 && responseCode != 3)
                return null;
            return stopwatch.Elapsed.TotalMilliseconds;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BuildQuery(ushort transactionId, string domain)
    {
        using var stream = new MemoryStream();
        void WriteU16(ushort value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value & 0xFF));
        }

        WriteU16(transactionId);
        WriteU16(0x0100); // recursion desired
        WriteU16(1);      // QDCOUNT
        WriteU16(0);
        WriteU16(0);
        WriteU16(0);

        foreach (var label in domain.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63)
                throw new InvalidOperationException("Invalid DNS label.");
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }
        stream.WriteByte(0);
        WriteU16(1); // A
        WriteU16(1); // IN
        return stream.ToArray();
    }

    private static bool Matches(IEnumerable<string> current, IReadOnlyCollection<string> target)
    {
        var actual = current.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        return actual.Length == target.Count && target.All(x => actual.Contains(x, StringComparer.OrdinalIgnoreCase));
    }

    private static List<DnsState> ReadStates()
    {
        const string script = "$items=@(Get-NetAdapter -IncludeHidden | Where-Object {$_.Status -eq 'Up' -and $_.HardwareInterface -eq $true} | ForEach-Object { $a=$_; try { $dnsInfo=Get-DnsClientServerAddress -InterfaceIndex $a.ifIndex -AddressFamily IPv4 -ErrorAction Stop; $dns=@($dnsInfo.ServerAddresses | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | ForEach-Object {[string]$_}); $guid=[string]$a.InterfaceGuid; if(-not $guid.StartsWith('{')){$guid='{'+$guid+'}'}; $path='HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\'+$guid; $ns=(Get-ItemProperty -Path $path -Name NameServer -ErrorAction SilentlyContinue).NameServer; [pscustomobject]@{InterfaceIndex=[int]$a.ifIndex;Name=[string]$a.Name;Automatic=[string]::IsNullOrWhiteSpace([string]$ns);Servers=[string[]]$dns} } catch {} }); ConvertTo-Json -InputObject $items -Compress -Depth 5";
        var result = PowerShellNetworkAccess.Run(script);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return new();
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            if (result.Output.TrimStart().StartsWith("[", StringComparison.Ordinal))
                return JsonSerializer.Deserialize<List<DnsState>>(result.Output, options) ?? new();
            var one = JsonSerializer.Deserialize<DnsState>(result.Output, options);
            return one is null ? new() : [one];
        }
        catch { return new(); }
    }

    private void SaveEnvelope(DnsRestoreEnvelope value)
    {
        try { File.WriteAllText(_restoreFile, JsonSerializer.Serialize(value)); } catch { }
    }

    private DnsRestoreEnvelope? ReadEnvelope()
    {
        try { return File.Exists(_restoreFile) ? JsonSerializer.Deserialize<DnsRestoreEnvelope>(File.ReadAllText(_restoreFile)) : null; }
        catch { return null; }
    }

    private void TryDeleteRestoreFile() { try { File.Delete(_restoreFile); } catch { } }
}
