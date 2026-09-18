using System.Collections.ObjectModel;
using System.Diagnostics;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class BenchmarkComparisonRowViewModel
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Before { get; init; } = string.Empty;
    public string After { get; init; } = string.Empty;
    public string Change { get; init; } = string.Empty;
    public string Verdict { get; init; } = string.Empty;
    public bool Improved => Verdict == "IMPROVED";
    public bool Regressed => Verdict == "REGRESSED";
}

public sealed class BenchmarkViewModel : ViewModelBase
{
    private readonly BenchmarkService _service;
    private readonly SystemMonitoringService _monitoring;
    private readonly ISettingsService _settings;
    private BenchmarkSnapshot? _before;
    private BenchmarkSnapshot? _after;
    private bool _isBusy;
    private double _progress;
    private string _status = "Run a BEFORE benchmark, make your changes, then run AFTER to validate the difference.";
    private string _beforeSummary = "No BEFORE benchmark yet.";
    private string _afterSummary = "No AFTER benchmark yet.";
    private string _overallVerdict = "Waiting for a matched BEFORE / AFTER pair.";

    public ObservableCollection<BenchmarkComparisonRowViewModel> Comparisons { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RunBeforeCommand.RaiseCanExecuteChanged();
            RunAfterCommand.RaiseCanExecuteChanged();
            ClearCommand.RaiseCanExecuteChanged();
        }
    }

    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string BeforeSummary { get => _beforeSummary; private set => SetProperty(ref _beforeSummary, value); }
    public string AfterSummary { get => _afterSummary; private set => SetProperty(ref _afterSummary, value); }
    public string OverallVerdict { get => _overallVerdict; private set => SetProperty(ref _overallVerdict, value); }

    public AsyncRelayCommand RunBeforeCommand { get; }
    public AsyncRelayCommand RunAfterCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public BenchmarkViewModel(BenchmarkService service, SystemMonitoringService monitoring, ISettingsService settings)
    {
        _service = service;
        _monitoring = monitoring;
        _settings = settings;
        RunBeforeCommand = new AsyncRelayCommand(() => RunAsync("Before"), () => !IsBusy);
        RunAfterCommand = new AsyncRelayCommand(() => RunAsync("After"), () => !IsBusy);
        ClearCommand = new RelayCommand(Clear, () => !IsBusy);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _before = await _service.LoadBeforeAsync();
            _after = await _service.LoadAfterAsync();
            RefreshSummaries();
            BuildComparison();
        }
        catch (Exception ex)
        {
            Status = $"Could not load saved benchmark data: {ex.Message}";
        }
    }

    private async Task RunAsync(string stage)
    {
        IsBusy = true;
        Progress = 0;
        var resumeMonitoring = _monitoring.IsRunning;
        try
        {
            if (resumeMonitoring)
            {
                Status = "Pausing live monitoring so it does not add noise to the benchmark…";
                await _monitoring.StopAsync();
            }

            if (stage == "After" && _before is null)
            {
                Status = "Run BEFORE first so Sabby has a matched baseline to compare against.";
                return;
            }

            Status = $"Running {stage.ToUpperInvariant()} benchmark. Avoid launching games or heavy apps until it finishes…";
            var progress = new Progress<BenchmarkProgress>(x =>
            {
                Progress = x.Percent;
                Status = x.Status;
            });
            var snapshot = await _service.RunAsync(stage, progress);
            if (stage == "Before")
            {
                _before = snapshot;
                _after = null;
                Comparisons.Clear();
                AfterSummary = "No AFTER benchmark yet.";
                OverallVerdict = "BEFORE captured. Apply or undo tweaks, then run AFTER under similar conditions.";
            }
            else
            {
                _after = snapshot;
            }
            RefreshSummaries();
            BuildComparison();
            Status = stage == "Before"
                ? "✓ BEFORE benchmark saved. Keep background workload similar, make your tuning changes, then run AFTER."
                : "✓ AFTER benchmark saved and compared against the BEFORE baseline.";
        }
        catch (Exception ex)
        {
            Status = $"Benchmark stopped safely: {ex.Message}";
        }
        finally
        {
            if (resumeMonitoring && _settings.Current.MonitoringEnabled)
                _monitoring.Start(_settings.Current.MonitoringRefreshIntervalMs);
            IsBusy = false;
        }
    }

    private void RefreshSummaries()
    {
        BeforeSummary = _before is null
            ? "No BEFORE benchmark yet."
            : $"{_before.CreatedAt.LocalDateTime:g} • {_before.Metrics.Count} metrics • {_before.HardwareSummary}";
        AfterSummary = _after is null
            ? "No AFTER benchmark yet."
            : $"{_after.CreatedAt.LocalDateTime:g} • {_after.Metrics.Count} metrics • {_after.HardwareSummary}";
    }

    private void BuildComparison()
    {
        Comparisons.Clear();
        if (_before is null || _after is null) return;

        var afterById = _after.Metrics.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var improved = 0;
        var unchanged = 0;
        var regressed = 0;

        foreach (var before in _before.Metrics)
        {
            if (!afterById.TryGetValue(before.Id, out var after)) continue;
            var benefit = CalculateBenefitPercent(before, after);
            var threshold = Math.Max(before.NoiseBandPercent, after.NoiseBandPercent);
            string verdict;
            if (benefit > threshold) { verdict = "IMPROVED"; improved++; }
            else if (benefit < -threshold) { verdict = "REGRESSED"; regressed++; }
            else { verdict = "WITHIN NOISE"; unchanged++; }

            Comparisons.Add(new BenchmarkComparisonRowViewModel
            {
                Name = before.Name,
                Category = before.Category,
                Before = FormatMetric(before),
                After = FormatMetric(after),
                Change = FormatChange(before, after, benefit),
                Verdict = verdict
            });
        }

        OverallVerdict = regressed == 0 && improved > 0
            ? $"MEASURABLE IMPROVEMENT • {improved} improved • {unchanged} within expected run-to-run noise • 0 regressed"
            : improved == 0 && regressed == 0
                ? $"NO CLEAR CHANGE • all {unchanged} comparable metrics stayed inside their noise bands"
                : $"MIXED RESULT • {improved} improved • {unchanged} within noise • {regressed} regressed";
    }

    private static double CalculateBenefitPercent(BenchmarkMetric before, BenchmarkMetric after)
    {
        if (Math.Abs(before.Value) < 0.000001)
        {
            if (Math.Abs(after.Value) < 0.000001) return 0;
            return before.HigherIsBetter ? 100 : -100;
        }
        var raw = ((after.Value - before.Value) / Math.Abs(before.Value)) * 100d;
        return before.HigherIsBetter ? raw : -raw;
    }

    private static string FormatMetric(BenchmarkMetric metric)
    {
        var digits = metric.Unit is "ms" or "%" ? 2 : 1;
        return $"{metric.Value.ToString("F" + digits)} {metric.Unit}";
    }

    private static string FormatChange(BenchmarkMetric before, BenchmarkMetric after, double benefit)
    {
        if (Math.Abs(before.Value) < 0.000001 && Math.Abs(after.Value) >= 0.000001)
            return before.HigherIsBetter ? "new measurable value" : "new penalty";
        var direction = benefit >= 0 ? "+" : string.Empty;
        return $"{direction}{benefit:F1}% benefit";
    }

    private void Clear()
    {
        _service.Clear();
        _before = null;
        _after = null;
        Comparisons.Clear();
        Progress = 0;
        BeforeSummary = "No BEFORE benchmark yet.";
        AfterSummary = "No AFTER benchmark yet.";
        OverallVerdict = "Waiting for a matched BEFORE / AFTER pair.";
        Status = "Saved benchmark baseline/results cleared.";
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_service.ReportDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", _service.ReportDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status = $"Could not open the benchmark folder: {ex.Message}";
        }
    }
}
