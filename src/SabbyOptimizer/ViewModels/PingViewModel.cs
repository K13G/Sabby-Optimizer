using System.Collections.ObjectModel;
using System.Diagnostics;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Core.Tweaks;
using PCTweaker.Models;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.ViewModels;

public sealed class PingViewModel : ViewModelBase
{
    private readonly PingOptimizationService _service;
    private readonly ITweakEngine _tweaks;
    private readonly Queue<double> _liveSamples = new();
    private readonly List<string> _lastOptimizationAppliedIds = new();
    private CancellationTokenSource? _monitorCts;
    private bool _isBusy;
    private bool _isMonitoring;
    private double _progress;
    private string _customHost = "1.1.1.1";
    private string _candidateHosts = "US East (Virginia)=dynamodb.us-east-1.amazonaws.com:443; US East (Ohio)=dynamodb.us-east-2.amazonaws.com:443; US West (Oregon)=dynamodb.us-west-2.amazonaws.com:443; Europe (Ireland)=dynamodb.eu-west-1.amazonaws.com:443; Europe (Frankfurt)=dynamodb.eu-central-1.amazonaws.com:443; Asia (Singapore)=dynamodb.ap-southeast-1.amazonaws.com:443";
    private string _monitorTarget = "1.1.1.1";
    private string _status = "Ready. Quick Test measures your real local hop and Internet reference path. Optimize runs in the background so the UI stays responsive.";
    private string _comparison = "No optimization benchmark yet.";
    private string _bestEndpointSummary = "Scan regions to compare real network measurements to regional cloud endpoints. These are route estimates, not guaranteed game-server addresses.";
    private string _livePingText = "—";
    private string _liveJitterText = "—";
    private string _liveLossText = "—";
    private string _liveQuality = "Ready";
    private string _monitorButtonText = "Start live test";
    private string _internetPingText = "—";
    private string _internetP95Text = "—";
    private string _internetJitterText = "—";
    private string _internetLossText = "—";
    private string _localHopText = "—";
    private string _dnsLookupText = "—";
    private string _adapterHealthText = "Not checked yet";
    private string _mtuText = "—";
    private string _tcpConnectionsText = "—";
    private string _lastTestText = "Not tested yet";
    private string _healthStatus = "Run Quick Test";
    private string _routeTraceText = "Trace route has not been run.";
    private string _busyTitle = string.Empty;
    private string _busyDetail = string.Empty;
    private int _liveAttempts;
    private int _liveLosses;

    public ObservableCollection<PingTestResult> Results { get; } = new();
    public ObservableCollection<RouteCandidateResult> RouteCandidates { get; } = new();

    public string AdapterName { get; }
    public string Gateway { get; }
    public string LinkSpeed { get; }
    public string DnsServers { get; }
    public string LocalAddress { get; }

    public string CustomHost { get => _customHost; set => SetProperty(ref _customHost, value ?? string.Empty); }
    public string CandidateHosts { get => _candidateHosts; set => SetProperty(ref _candidateHosts, value ?? string.Empty); }
    public string MonitorTarget { get => _monitorTarget; set => SetProperty(ref _monitorTarget, value ?? string.Empty); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Comparison { get => _comparison; private set => SetProperty(ref _comparison, value); }
    public string BestEndpointSummary { get => _bestEndpointSummary; private set => SetProperty(ref _bestEndpointSummary, value); }
    public string LivePingText { get => _livePingText; private set => SetProperty(ref _livePingText, value); }
    public string LiveJitterText { get => _liveJitterText; private set => SetProperty(ref _liveJitterText, value); }
    public string LiveLossText { get => _liveLossText; private set => SetProperty(ref _liveLossText, value); }
    public string LiveQuality { get => _liveQuality; private set => SetProperty(ref _liveQuality, value); }
    public string MonitorButtonText { get => _monitorButtonText; private set => SetProperty(ref _monitorButtonText, value); }
    public string InternetPingText { get => _internetPingText; private set => SetProperty(ref _internetPingText, value); }
    public string InternetP95Text { get => _internetP95Text; private set => SetProperty(ref _internetP95Text, value); }
    public string InternetJitterText { get => _internetJitterText; private set => SetProperty(ref _internetJitterText, value); }
    public string InternetLossText { get => _internetLossText; private set => SetProperty(ref _internetLossText, value); }
    public string LocalHopText { get => _localHopText; private set => SetProperty(ref _localHopText, value); }
    public string DnsLookupText { get => _dnsLookupText; private set => SetProperty(ref _dnsLookupText, value); }
    public string AdapterHealthText { get => _adapterHealthText; private set => SetProperty(ref _adapterHealthText, value); }
    public string MtuText { get => _mtuText; private set => SetProperty(ref _mtuText, value); }
    public string TcpConnectionsText { get => _tcpConnectionsText; private set => SetProperty(ref _tcpConnectionsText, value); }
    public string LastTestText { get => _lastTestText; private set => SetProperty(ref _lastTestText, value); }
    public string HealthStatus { get => _healthStatus; private set => SetProperty(ref _healthStatus, value); }
    public string RouteTraceText { get => _routeTraceText; private set => SetProperty(ref _routeTraceText, value); }
    public string BusyTitle { get => _busyTitle; private set => SetProperty(ref _busyTitle, value); }
    public string BusyDetail { get => _busyDetail; private set => SetProperty(ref _busyDetail, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public bool IsMonitoring { get => _isMonitoring; private set => SetProperty(ref _isMonitoring, value); }
    public bool HasLastOptimization => _lastOptimizationAppliedIds.Count > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RunTestsCommand.RaiseCanExecuteChanged();
                ApplyLocalProfileCommand.RaiseCanExecuteChanged();
                ApplyBestCommand.RaiseCanExecuteChanged();
                ApplyAdvancedLatencyCommand.RaiseCanExecuteChanged();
                UndoLastOptimizationCommand.RaiseCanExecuteChanged();
                FindBestEndpointCommand.RaiseCanExecuteChanged();
                FlushDnsCommand.RaiseCanExecuteChanged();
                TraceRouteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand RunTestsCommand { get; }
    public AsyncRelayCommand ApplyLocalProfileCommand { get; }
    public AsyncRelayCommand ApplyBestCommand { get; }
    public AsyncRelayCommand ApplyAdvancedLatencyCommand { get; }
    public AsyncRelayCommand UndoLastOptimizationCommand { get; }
    public AsyncRelayCommand FindBestEndpointCommand { get; }
    public AsyncRelayCommand FlushDnsCommand { get; }
    public AsyncRelayCommand ToggleMonitoringCommand { get; }
    public AsyncRelayCommand TraceRouteCommand { get; }
    public RelayCommand OpenNetworkSettingsCommand { get; }

    public PingViewModel(PingOptimizationService service, ITweakEngine tweaks)
    {
        _service = service;
        _tweaks = tweaks;
        var route = service.GetLocalRouteDetails();
        AdapterName = route.Adapter;
        Gateway = route.Gateway;
        LinkSpeed = route.LinkSpeed;
        DnsServers = route.DnsServers;
        LocalAddress = route.LocalAddress;

        RunTestsCommand = new AsyncRelayCommand(RunTestsAsync, () => !IsBusy);
        ApplyLocalProfileCommand = new AsyncRelayCommand(ApplyLocalProfileAsync, () => !IsBusy);
        ApplyBestCommand = new AsyncRelayCommand(ApplyBestAsync, () => !IsBusy);
        ApplyAdvancedLatencyCommand = new AsyncRelayCommand(ApplyAdvancedLatencyAsync, () => !IsBusy);
        UndoLastOptimizationCommand = new AsyncRelayCommand(UndoLastOptimizationAsync, () => !IsBusy && HasLastOptimization);
        FindBestEndpointCommand = new AsyncRelayCommand(FindBestEndpointAsync, () => !IsBusy);
        FlushDnsCommand = new AsyncRelayCommand(FlushDnsAsync, () => !IsBusy);
        ToggleMonitoringCommand = new AsyncRelayCommand(ToggleMonitoringAsync);
        TraceRouteCommand = new AsyncRelayCommand(TraceRouteAsync, () => !IsBusy);
        OpenNetworkSettingsCommand = new RelayCommand(OpenNetworkSettings);

        RefreshAdapterHealth();
    }

    private Task ToggleMonitoringAsync()
    {
        if (IsMonitoring)
        {
            StopMonitoring();
            return Task.CompletedTask;
        }

        _monitorCts?.Dispose();
        _monitorCts = new CancellationTokenSource();
        _liveSamples.Clear();
        _liveAttempts = 0;
        _liveLosses = 0;
        IsMonitoring = true;
        MonitorButtonText = "Stop live test";
        LiveQuality = "Measuring Internet path…";
        _ = MonitorLoopAsync(_monitorCts.Token);
        return Task.CompletedTask;
    }

    private void StopMonitoring()
    {
        _monitorCts?.Cancel();
        IsMonitoring = false;
        MonitorButtonText = "Start live test";
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var host = string.IsNullOrWhiteSpace(MonitorTarget) ? "1.1.1.1" : MonitorTarget.Trim();
                var result = await _service.TestAsync("Live Internet", host, 1, null, cancellationToken);
                _liveAttempts++;

                if (result.LossPercent >= 100)
                {
                    _liveLosses++;
                    LivePingText = "timeout";
                }
                else
                {
                    _liveSamples.Enqueue(result.AverageMs);
                    while (_liveSamples.Count > 30) _liveSamples.Dequeue();
                    LivePingText = FormatMilliseconds(result.AverageMs);
                }

                var samples = _liveSamples.ToArray();
                var jitter = samples.Length < 2 ? 0d : samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).Average();
                var loss = _liveAttempts == 0 ? 0 : 100d * _liveLosses / _liveAttempts;
                LiveJitterText = samples.Length < 2 ? "—" : FormatMilliseconds(jitter);
                LiveLossText = $"{loss:0.#}%";

                var avg = samples.Length == 0 ? double.MaxValue : samples.Average();
                LiveQuality = loss > 2 ? "Packet loss detected"
                    : avg <= 25 && jitter <= 4 ? "Excellent"
                    : avg <= 50 && jitter <= 8 ? "Good"
                    : avg <= 90 ? "Playable"
                    : "High latency";

                await Task.Delay(800, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            LiveQuality = "Live test paused";
        }
        finally
        {
            IsMonitoring = false;
            MonitorButtonText = "Start live test";
        }
    }

    private async Task RunTestsAsync()
    {
        BeginBusy("Testing your connection", "Measuring local gateway, Internet latency, jitter, loss, DNS resolver time, and adapter health.");
        Results.Clear();
        Status = "Running real network measurements…";
        try
        {
            var targets = BuildTargets();
            Progress = 12;
            var testTasks = targets.Select(x => _service.TestAsync(x.Label, x.Host, 6, null)).ToArray();
            var dnsTask = _service.MeasureDnsResolutionAsync("www.cloudflare.com");
            await Task.WhenAll(testTasks.Cast<Task>().Append(dnsTask));

            Progress = 82;
            foreach (var result in testTasks.Select(x => x.Result)) Results.Add(result);
            var dnsMs = await dnsTask;
            DnsLookupText = dnsMs is null ? "Unavailable" : FormatMilliseconds(dnsMs.Value);
            RefreshAdapterHealth();
            UpdateHealthMetrics(Results);
            Progress = 100;
            Status = BuildStatusFromResults(Results);
            LastTestText = $"Measured {DateTime.Now:t}";
        }
        finally { EndBusy(); }
    }

    private async Task FindBestEndpointAsync()
    {
        var hosts = CandidateHosts
            .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        if (hosts.Length == 0)
        {
            BestEndpointSummary = "Enter at least one hostname/IP (optionally host:port).";
            return;
        }

        BeginBusy("Scanning regions", $"Comparing {hosts.Length} regional path{(hosts.Length == 1 ? string.Empty : "s")} with ICMP and TCP reachability measurements.");
        RouteCandidates.Clear();
        BestEndpointSummary = "Testing real network paths. ICMP loss is cross-checked with TCP because many cloud endpoints deprioritize ping replies.";
        try
        {
            Progress = 18;
            var candidates = await _service.FindBestEndpointAsync(hosts, 6);
            Progress = 92;
            foreach (var item in candidates) RouteCandidates.Add(item);

            var best = candidates.FirstOrDefault(x => x.IsRecommended);
            BestEndpointSummary = best is null
                ? "No regional probe responded. Try your own known server hostname in Advanced tools."
                : $"Lowest measured regional path: {best.Host} • {FormatMilliseconds(best.AverageMs)} average • {FormatMilliseconds(best.P95Ms)} p95 • {FormatMilliseconds(best.JitterMs)} jitter • {best.LossPercent:0.#}% loss. This is a regional route estimate, not a guarantee that your game uses that exact server.";
            Progress = 100;
        }
        finally { EndBusy(); }
    }

    private async Task TraceRouteAsync()
    {
        var target = string.IsNullOrWhiteSpace(CustomHost) ? "1.1.1.1" : CustomHost.Trim();
        BeginBusy("Tracing network path", $"Mapping up to 12 IP hops toward {target}. Routers that suppress ICMP may appear as *.");
        try
        {
            Progress = 15;
            var hops = await _service.TraceRouteAsync(target, 12);
            Progress = 100;
            RouteTraceText = hops.Count == 0
                ? "No trace-route replies were returned. Some networks suppress TTL-expired ICMP responses."
                : string.Join(Environment.NewLine, hops.Select(x => x.Display));
            Status = $"Trace route completed with {hops.Count} observed hop{(hops.Count == 1 ? string.Empty : "s")}.";
        }
        finally { EndBusy(); }
    }

    private List<(string Label, string Host)> BuildTargets()
    {
        var targets = new List<(string Label, string Host)>();
        if (!string.IsNullOrWhiteSpace(Gateway)) targets.Add(("Local gateway", Gateway));
        targets.Add(("Internet reference", "1.1.1.1"));
        targets.Add(("Backup reference", "8.8.8.8"));
        if (!string.IsNullOrWhiteSpace(CustomHost) && !targets.Any(x => x.Host.Equals(CustomHost.Trim(), StringComparison.OrdinalIgnoreCase)))
            targets.Add(("Custom target", CustomHost.Trim()));
        return targets;
    }

    private async Task FlushDnsAsync()
    {
        BeginBusy("Flushing DNS cache", "Clearing the Windows resolver cache. This does not change your game route or claim to lower in-game RTT.");
        try
        {
            var result = await _service.FlushDnsAsync();
            Status = result.Success ? "Windows DNS resolver cache flushed." : $"DNS cache flush was not completed: {result.Message}";
            Progress = 100;
        }
        finally { EndBusy(); }
    }

    private void OpenNetworkSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true });
        }
        catch
        {
            Status = "Windows Network settings could not be opened.";
        }
    }

    private async Task ApplyLocalProfileAsync() => await ApplySafeOptimizationAsync(validate: false);

    private async Task ApplyBestAsync() => await ApplySafeOptimizationAsync(validate: true);

    private async Task ApplySafeOptimizationAsync(bool validate)
    {
        StopMonitoring();
        BeginBusy("Optimizing local network path", "Sabby is applying supported reversible Windows/NIC settings on worker threads so the interface stays responsive.");
        _lastOptimizationAppliedIds.Clear();
        OnPropertyChanged(nameof(HasLastOptimization));
        UndoLastOptimizationCommand.RaiseCanExecuteChanged();
        try
        {
            Progress = 3;
            (PingTestResult? Internet, PingTestResult? Gateway)? before = null;
            if (validate)
            {
                before = await MeasurePrimaryPathAsync(5);
                Comparison = $"Before: {FormatPath(before.Value.Internet, before.Value.Gateway)}";
            }

            var safeIds = new[]
            {
                "network.rss",
                "network.tcp-autotuning",
                "network.power-saving",
                "network.checksum-offload",
                "network.task-offload"
            };
            var apply = await ApplyProfileCoreAsync(safeIds, 18, 70);
            _lastOptimizationAppliedIds.AddRange(apply.AppliedIds);
            OnPropertyChanged(nameof(HasLastOptimization));
            UndoLastOptimizationCommand.RaiseCanExecuteChanged();

            if (!validate)
            {
                Progress = 100;
                Status = $"Local network profile complete • {apply.Done} supported settings verified/already correct • {apply.Skipped} skipped.";
                return;
            }

            BusyDetail = "Waiting briefly for the adapter to settle, then re-measuring the same paths.";
            await Task.Delay(700);
            var after = await MeasurePrimaryPathAsync(6);
            Progress = 90;

            var worse = PathMateriallyWorse(before, after);
            if (worse && _lastOptimizationAppliedIds.Count > 0)
            {
                BusyDetail = "The measured path became worse, so Sabby is restoring only settings changed by this optimization.";
                await UndoIdsAsync(_lastOptimizationAppliedIds.ToArray());
                _lastOptimizationAppliedIds.Clear();
                OnPropertyChanged(nameof(HasLastOptimization));
                UndoLastOptimizationCommand.RaiseCanExecuteChanged();
                await Task.Delay(500);
                after = await MeasurePrimaryPathAsync(5);
                Comparison = $"Before: {FormatPath(before?.Internet, before?.Gateway)} • After rollback: {FormatPath(after.Internet, after.Gateway)}";
                Status = "Optimization benchmarked worse on this connection, so newly changed network settings were automatically restored.";
            }
            else
            {
                Comparison = $"Before: {FormatPath(before?.Internet, before?.Gateway)} • After: {FormatPath(after.Internet, after.Gateway)}";
                Status = _lastOptimizationAppliedIds.Count == 0
                    ? $"Optimization check complete • {apply.Done} supported settings were already correct/verified • {apply.Skipped} skipped."
                    : $"Optimization complete • {apply.Done} supported settings verified/already correct • {apply.Skipped} skipped • {_lastOptimizationAppliedIds.Count} newly changed.";
            }

            await RefreshResultsQuickAsync();
            RefreshAdapterHealth();
            UpdateHealthMetrics(Results);
            Progress = 100;
        }
        finally { EndBusy(); }
    }

    private async Task ApplyAdvancedLatencyAsync()
    {
        StopMonitoring();
        BeginBusy("Advanced latency tuning", "Testing and applying situational NIC settings. These can trade throughput/CPU efficiency for lower scheduling latency and are automatically rolled back if the measured path becomes worse.");
        try
        {
            var before = await MeasurePrimaryPathAsync(5);
            var ids = new[]
            {
                "network.interrupt-moderation",
                "network.d0-packet-coalescing",
                "network.rsc-low-latency",
                "network.eee"
            };
            var apply = await ApplyProfileCoreAsync(ids, 20, 68);
            await Task.Delay(900);
            var after = await MeasurePrimaryPathAsync(6);

            if (PathMateriallyWorse(before, after) && apply.AppliedIds.Count > 0)
            {
                BusyDetail = "Advanced mode measured worse; restoring the advanced changes now.";
                await UndoIdsAsync(apply.AppliedIds.ToArray());
                Status = "Advanced latency settings were rolled back because the measured path became worse.";
            }
            else
            {
                foreach (var id in apply.AppliedIds)
                    if (!_lastOptimizationAppliedIds.Contains(id, StringComparer.OrdinalIgnoreCase)) _lastOptimizationAppliedIds.Add(id);
                OnPropertyChanged(nameof(HasLastOptimization));
                UndoLastOptimizationCommand.RaiseCanExecuteChanged();
                Status = $"Advanced latency validation complete • {apply.Done} verified/already correct • {apply.Skipped} skipped.";
            }
            Comparison = $"Advanced test: {FormatPath(before.Internet, before.Gateway)} → {FormatPath(after.Internet, after.Gateway)}";
            Progress = 100;
        }
        finally { EndBusy(); }
    }

    private async Task UndoLastOptimizationAsync()
    {
        if (_lastOptimizationAppliedIds.Count == 0) return;
        BeginBusy("Restoring previous network state", "Undoing only the settings changed by the most recent Sabby network optimization.");
        try
        {
            var ids = _lastOptimizationAppliedIds.ToArray();
            await UndoIdsAsync(ids);
            _lastOptimizationAppliedIds.Clear();
            OnPropertyChanged(nameof(HasLastOptimization));
            UndoLastOptimizationCommand.RaiseCanExecuteChanged();
            Progress = 100;
            Status = "Previous network values were restored where Sabby had a verified rollback record.";
            await RefreshResultsQuickAsync();
            UpdateHealthMetrics(Results);
        }
        finally { EndBusy(); }
    }

    private async Task<(int Done, int Skipped, List<string> AppliedIds)> ApplyProfileCoreAsync(
        IReadOnlyList<string> ids, double progressStart, double progressEnd)
    {
        var done = 0;
        var skipped = 0;
        var appliedIds = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            var def = _tweaks.Definitions.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            BusyDetail = def is null ? $"Checking {id}…" : $"Checking {def.Name}…";
            try
            {
                if (def is null) { skipped++; continue; }

                // Some NIC handlers use Windows PowerShell cmdlets internally. Run the complete
                // detection/apply chain on a worker thread so synchronous driver/PowerShell calls
                // never block WPF's dispatcher and make the app appear frozen.
                var compatibility = await _tweaks.CheckCompatibilityAsync(id, true);
                if (!compatibility.IsCompatible) { skipped++; continue; }

                var state = await _tweaks.DetectAsync(id);
                if (state.State == TweakStateKind.Applied) { done++; continue; }
                if (!state.CanApply) { skipped++; continue; }

                var result = await Task.Run(async () => await _tweaks.ApplyAsync(id).ConfigureAwait(false));
                if (result.Success) { done++; appliedIds.Add(id); }
                else skipped++;
            }
            catch { skipped++; }
            finally
            {
                Progress = progressStart + (progressEnd - progressStart) * (i + 1) / Math.Max(1, ids.Count);
                await Task.Delay(1);
            }
        }
        return (done, skipped, appliedIds);
    }

    private async Task UndoIdsAsync(IReadOnlyList<string> ids)
    {
        for (var i = ids.Count - 1; i >= 0; i--)
        {
            var id = ids[i];
            try { _ = await Task.Run(async () => await _tweaks.UndoAsync(id).ConfigureAwait(false)); } catch { }
            Progress = 72 + (ids.Count - i) * 25d / Math.Max(1, ids.Count);
            await Task.Delay(1);
        }
    }

    private async Task<(PingTestResult? Internet, PingTestResult? Gateway)> MeasurePrimaryPathAsync(int samples)
    {
        var internetTask = _service.TestAsync("Internet reference", "1.1.1.1", samples, null);
        Task<PingTestResult?> gatewayTask = string.IsNullOrWhiteSpace(Gateway)
            ? Task.FromResult<PingTestResult?>(null)
            : MeasureGatewayAsync(samples);
        await Task.WhenAll((Task)internetTask, (Task)gatewayTask);
        return (await internetTask, await gatewayTask);
    }

    private async Task<PingTestResult?> MeasureGatewayAsync(int samples) =>
        string.IsNullOrWhiteSpace(Gateway) ? null : await _service.TestAsync("Local gateway", Gateway, samples, null);

    private static bool PathMateriallyWorse(
        (PingTestResult? Internet, PingTestResult? Gateway)? before,
        (PingTestResult? Internet, PingTestResult? Gateway) after)
    {
        if (before is null) return false;
        var b = before.Value.Internet;
        var a = after.Internet;
        if (b is null || a is null || b.LossPercent >= 100 || a.LossPercent >= 100) return false;
        return a.LossPercent > b.LossPercent + 0.5 ||
               a.AverageMs - b.AverageMs > Math.Max(2.0, b.AverageMs * 0.18) ||
               a.JitterMs - b.JitterMs > Math.Max(1.5, b.JitterMs * 0.45);
    }

    private async Task RefreshResultsQuickAsync()
    {
        Results.Clear();
        var targets = BuildTargets();
        var tasks = targets.Select(x => _service.TestAsync(x.Label, x.Host, 4, null)).ToArray();
        await Task.WhenAll(tasks);
        foreach (var task in tasks) Results.Add(task.Result);
    }

    private void RefreshAdapterHealth()
    {
        var snapshot = _service.GetNetworkHealthSnapshot();
        MtuText = snapshot.Mtu > 0 ? snapshot.Mtu.ToString() : "Unknown";
        TcpConnectionsText = snapshot.ActiveTcpConnections.ToString();
        AdapterHealthText = snapshot.TotalErrors == 0 && snapshot.TotalDiscards == 0
            ? "0 adapter errors/discards"
            : $"{snapshot.TotalErrors} errors • {snapshot.TotalDiscards} discards";
    }

    private void UpdateHealthMetrics(IEnumerable<PingTestResult> results)
    {
        var list = results.ToList();
        var internet = list.FirstOrDefault(x => x.Target == "Internet reference") ?? list.FirstOrDefault(x => x.Target == "Backup reference");
        var gateway = list.FirstOrDefault(x => x.Target == "Local gateway");

        if (internet is not null && internet.LossPercent < 100)
        {
            InternetPingText = FormatMilliseconds(internet.AverageMs);
            InternetP95Text = FormatMilliseconds(internet.P95Ms);
            InternetJitterText = FormatMilliseconds(internet.JitterMs);
            InternetLossText = $"{internet.LossPercent:0.#}%";
            HealthStatus = internet.LossPercent > 1 ? "Packet loss"
                : internet.AverageMs <= 25 && internet.JitterMs <= 4 ? "Excellent"
                : internet.AverageMs <= 50 && internet.JitterMs <= 8 ? "Good"
                : internet.AverageMs <= 90 ? "Playable"
                : "High latency";
        }
        else
        {
            InternetPingText = InternetP95Text = InternetJitterText = "No reply";
            InternetLossText = "100%";
            HealthStatus = "Internet reference unavailable";
        }

        LocalHopText = gateway is null || gateway.LossPercent >= 100 ? "Unavailable" : FormatMilliseconds(gateway.AverageMs);
    }

    private static string BuildStatusFromResults(IEnumerable<PingTestResult> results)
    {
        var list = results.ToList();
        var gateway = list.FirstOrDefault(x => x.Target == "Local gateway");
        var internet = list.FirstOrDefault(x => x.Target == "Internet reference");
        if (gateway is not null && (gateway.LossPercent > 0 || gateway.AverageMs > 5))
            return "The local hop is showing latency/loss. Check Ethernet/Wi-Fi link quality before tuning Windows settings.";
        if (internet is not null && internet.LossPercent > 1)
            return "The Internet reference path is showing packet loss. Local Windows tweaks cannot repair upstream ISP congestion or routing loss.";
        return "Measurements completed. Values shown are live network measurements, not estimated FPS/ping claims.";
    }

    private static string FormatPath(PingTestResult? internet, PingTestResult? gateway)
    {
        var internetText = internet is null || internet.LossPercent >= 100 ? "Internet unavailable" : $"Internet {FormatMilliseconds(internet.AverageMs)} / jitter {FormatMilliseconds(internet.JitterMs)} / loss {internet.LossPercent:0.#}%";
        var gatewayText = gateway is null || gateway.LossPercent >= 100 ? "local hop unavailable" : $"local {FormatMilliseconds(gateway.AverageMs)}";
        return $"{internetText} • {gatewayText}";
    }

    private static string FormatMilliseconds(double value) => value < 1 ? "<1 ms" : $"{value:0.0} ms";

    private void BeginBusy(string title, string detail)
    {
        IsBusy = true;
        Progress = 0;
        BusyTitle = title;
        BusyDetail = detail;
    }

    private void EndBusy()
    {
        Progress = Math.Clamp(Progress, 0, 100);
        IsBusy = false;
        BusyTitle = string.Empty;
        BusyDetail = string.Empty;
    }
}
