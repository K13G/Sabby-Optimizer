using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class PingOptimizationService
{
    public (string Adapter, string Gateway) GetLocalRoute()
    {
        var details = GetLocalRouteDetails();
        return (details.Adapter, details.Gateway);
    }

    public (string Adapter, string Gateway, string LinkSpeed, string DnsServers, string LocalAddress) GetLocalRouteDetails()
    {
        var nic = FindPrimaryAdapter();
        if (nic is null)
            return ("Active adapter not resolved", string.Empty, "Unknown", "Unknown", "Unknown");

        try
        {
            var props = nic.GetIPProperties();
            var gateway = props.GatewayAddresses.Select(x => x.Address)
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(x));
            var dns = props.DnsAddresses
                .Where(x => x.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => x.ToString())
                .Distinct()
                .ToArray();
            var local = props.UnicastAddresses
                .Select(x => x.Address)
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "Unknown";
            var speed = nic.Speed > 0 ? $"{nic.Speed / 1_000_000d:0} Mbps" : "Unknown";
            return (nic.Name, gateway?.ToString() ?? string.Empty, speed,
                dns.Length == 0 ? "Automatic / unresolved" : string.Join(", ", dns), local);
        }
        catch
        {
            return (nic.Name, string.Empty, "Unknown", "Unknown", "Unknown");
        }
    }

    public NetworkHealthSnapshot GetNetworkHealthSnapshot()
    {
        var nic = FindPrimaryAdapter();
        if (nic is null)
            return new NetworkHealthSnapshot("Unknown", "Unknown", 0, 0, 0, 0, 0, 0, 0, 0, 0);

        try
        {
            var stats = nic.GetIPv4Statistics();
            var mtu = 0;
            try { mtu = nic.GetIPProperties().GetIPv4Properties()?.Mtu ?? 0; } catch { }
            var tcpConnections = 0;
            try { tcpConnections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length; } catch { }

            return new NetworkHealthSnapshot(
                nic.Name,
                nic.NetworkInterfaceType.ToString(),
                mtu,
                nic.Speed > 0 ? (long)Math.Round(nic.Speed / 1_000_000d) : 0,
                stats.IncomingPacketsWithErrors,
                stats.OutgoingPacketsWithErrors,
                stats.IncomingPacketsDiscarded,
                stats.OutgoingPacketsDiscarded,
                stats.BytesReceived,
                stats.BytesSent,
                tcpConnections);
        }
        catch
        {
            return new NetworkHealthSnapshot(nic.Name, nic.NetworkInterfaceType.ToString(), 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
    }

    public async Task<double?> MeasureDnsResolutionAsync(string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        try
        {
            var sw = Stopwatch.StartNew();
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return addresses.Length == 0 ? null : sw.Elapsed.TotalMilliseconds;
        }
        catch { return null; }
    }

    public async Task<PingTestResult> TestAsync(string label, string host, int count = 10, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var samples = new List<double>();
        var lost = 0;
        string resolved = host;
        try
        {
            resolved = (await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? host;
        }
        catch { }

        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(host, 900).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success) samples.Add(reply.RoundtripTime);
                else lost++;
            }
            catch { lost++; }

            progress?.Report(100d * (i + 1) / count);
            if (i + 1 < count) await Task.Delay(45, cancellationToken).ConfigureAwait(false);
        }

        return BuildResult(label, resolved, samples, lost, count);
    }

    public Task<PingTestResult> TestTcpEndpointAsync(string label, string host, int port = 443, int count = 6, CancellationToken cancellationToken = default) =>
        TestTcpAsync(label, host, port, count, cancellationToken);

    public async Task<IReadOnlyList<RouteCandidateResult>> FindBestEndpointAsync(
        IEnumerable<string> hosts, int samplesPerHost = 8, CancellationToken cancellationToken = default)
    {
        var unique = hosts
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        var gate = new SemaphoreSlim(3, 3);
        var tasks = unique.Select(async entry =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var (label, host, port) = ParseEndpoint(entry);
                var sampleCount = Math.Clamp(samplesPerHost, 4, 8);

                // ICMP is useful when the destination answers it, but many public/cloud/game-adjacent
                // endpoints rate-limit or deprioritize echo replies. Measure TCP as a second, real
                // reachability sample and use it when ICMP loss would otherwise distort the result.
                var icmpTask = TestAsync(label, host, sampleCount, null, cancellationToken);
                var tcpTask = TestTcpAsync(label, host, port, sampleCount, cancellationToken);
                await Task.WhenAll(icmpTask, tcpTask).ConfigureAwait(false);
                var icmp = await icmpTask.ConfigureAwait(false);
                var tcp = await tcpTask.ConfigureAwait(false);

                var useTcp = tcp.LossPercent < 100 &&
                             (icmp.LossPercent >= 100 || icmp.LossPercent > 10 || tcp.LossPercent + 5 < icmp.LossPercent);
                var result = useTcp ? tcp : icmp;
                var probeType = useTcp ? $"TCP:{port}" : "ICMP";
                var score = result.LossPercent >= 100
                    ? double.MaxValue
                    : result.AverageMs + (result.JitterMs * 0.9) + (result.P95Ms * 0.20) + (result.LossPercent * 5.0);

                return new RouteCandidateResult
                {
                    Label = label,
                    Host = label,
                    Address = result.Address,
                    ProbeType = probeType,
                    AverageMs = result.AverageMs,
                    MedianMs = result.MedianMs,
                    P95Ms = result.P95Ms,
                    JitterMs = result.JitterMs,
                    LossPercent = result.LossPercent,
                    Score = score
                };
            }
            finally { gate.Release(); }
        }).ToArray();

        var measured = (await Task.WhenAll(tasks).ConfigureAwait(false)).OrderBy(x => x.Score).ToList();
        var bestIndex = measured.FindIndex(x => x.Score < double.MaxValue);
        return measured.Select((x, index) => new RouteCandidateResult
        {
            Label = x.Label,
            Host = x.Host,
            Address = x.Address,
            ProbeType = x.ProbeType,
            AverageMs = x.AverageMs,
            MedianMs = x.MedianMs,
            P95Ms = x.P95Ms,
            JitterMs = x.JitterMs,
            LossPercent = x.LossPercent,
            Score = x.Score,
            IsRecommended = index == bestIndex && bestIndex >= 0
        }).ToArray();
    }

    public async Task<IReadOnlyList<RouteHopResult>> TraceRouteAsync(string host, int maxHops = 12, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host)) return Array.Empty<RouteHopResult>();

        IPAddress? destination = null;
        try
        {
            destination = (await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
        }
        catch { }
        if (destination is null) return Array.Empty<RouteHopResult>();

        var hops = new List<RouteHopResult>();
        var buffer = new byte[32];
        using var ping = new Ping();
        for (var ttl = 1; ttl <= Math.Clamp(maxHops, 4, 20); ttl++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            PingReply? reply = null;
            try
            {
                reply = await ping.SendPingAsync(destination, 700, buffer, new PingOptions(ttl, true)).ConfigureAwait(false);
            }
            catch { }
            sw.Stop();

            var address = reply?.Address?.ToString() ?? string.Empty;
            var reached = reply?.Status == IPStatus.Success;
            var elapsed = reply is not null && reply.RoundtripTime > 0 ? reply.RoundtripTime : sw.Elapsed.TotalMilliseconds;
            hops.Add(new RouteHopResult(ttl, address, elapsed, reached));
            if (reached) break;
        }
        return hops;
    }

    public async Task<(bool Success, string Message)> FlushDnsAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return (false, "DNS flush is only available on Windows.");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ipconfig.exe",
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process is null) return (false, "Could not start ipconfig.exe.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = (await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
            var error = (await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
            return process.ExitCode == 0
                ? (true, string.IsNullOrWhiteSpace(output) ? "Windows DNS cache flushed." : output)
                : (false, string.IsNullOrWhiteSpace(error) ? "Windows rejected the DNS flush." : error);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private static async Task<PingTestResult> TestTcpAsync(string label, string host, int port, int count, CancellationToken cancellationToken)
    {
        var samples = new List<double>();
        var lost = 0;
        string resolved = host;
        try
        {
            resolved = (await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? host;
        }
        catch { }

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clock = Stopwatch.StartNew();
            try
            {
                using var tcp = new TcpClient(AddressFamily.InterNetwork);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(900);
                await tcp.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
                clock.Stop();
                samples.Add(clock.Elapsed.TotalMilliseconds);
            }
            catch { lost++; }
            if (i + 1 < count) await Task.Delay(55, cancellationToken).ConfigureAwait(false);
        }

        return BuildResult(label, resolved, samples, lost, count);
    }

    private static (string Label, string Host, int Port) ParseEndpoint(string entry)
    {
        var value = entry.Trim();
        var label = value;
        var equals = value.IndexOf('=');
        if (equals > 0 && equals < value.Length - 1)
        {
            label = value[..equals].Trim();
            value = value[(equals + 1)..].Trim();
        }

        var colon = value.LastIndexOf(':');
        if (colon > 0 && colon < value.Length - 1 && int.TryParse(value[(colon + 1)..], out var port) && port is > 0 and <= 65535)
            return (string.IsNullOrWhiteSpace(label) ? value[..colon].Trim() : label, value[..colon].Trim(), port);

        return (string.IsNullOrWhiteSpace(label) ? value : label, value, 443);
    }

    private static PingTestResult BuildResult(string label, string address, List<double> samples, int lost, int count)
    {
        if (samples.Count == 0)
            return new(label, address, 0, 0, 0, 0, 0, 0, 100);

        samples.Sort();
        var avg = samples.Average();
        var median = Percentile(samples, 0.50);
        var p95 = Percentile(samples, 0.95);
        var jitter = samples.Count < 2 ? 0 : samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        return new(label, address, samples.First(), avg, median, p95, samples.Last(), jitter, 100d * lost / count);
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var index = Math.Clamp(percentile, 0, 1) * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        var fraction = index - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
    }

    private static NetworkInterface? FindPrimaryAdapter()
    {
        var candidates = new List<NetworkInterface>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                var hasIpv4Gateway = nic.GetIPProperties().GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(g.Address));
                if (hasIpv4Gateway) candidates.Add(nic);
            }
            catch { }
        }

        return candidates
            .OrderBy(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .ThenByDescending(n => n.Speed)
            .FirstOrDefault();
    }
}
