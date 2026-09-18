using System.Collections.ObjectModel;
using System.ComponentModel;
using PCTweaker.Core.Backups;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Tweaks;
using PCTweaker.Models;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.ViewModels;

public sealed class TweaksViewModel : ViewModelBase
{
    private static readonly string[] AutoOptimizeIds =
    [
        "gaming.capture",
        "gaming.game-mode",
        "cpu.boost-mode",
        "power.active-plan",
        "graphics.hags",
        "windows.visual-effects",
        "system.power-throttling",
        "network.rss",
        "network.checksum-offload",
        "network.lso",
        "network.task-offload",
        "network.tcp-autotuning",
        "network.power-saving",
        "network.eee"
    ];

    // "FPS" is a curated view, not a claim that every setting increases average FPS.
    // It groups controls that can affect gaming scheduling, background capture, CPU power,
    // GPU scheduling, input responsiveness, or frame-time consistency.
    private static readonly HashSet<string> FpsIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "gaming.capture", "gaming.game-mode", "gaming.controller-gamebar-shortcut",
        "cpu.boost-mode", "cpu.performance-floor", "cpu.energy-performance-preference", "cpu.core-parking-min",
        "cpu.performance-increase-policy", "cpu.boost-policy", "power.active-plan", "power.pcie-aspm",
        "graphics.hags", "system.power-throttling", "windows.visual-effects",
        "input.power-guard", "input.mouse-acceleration", "input.keyboard-repeat", "input.usb-selective-suspend",
        "network.rss", "network.tcp-autotuning", "network.power-saving", "network.checksum-offload",
        "network.lso", "network.rsc-low-latency", "network.interrupt-moderation", "network.task-offload",
        "network.eee", "network.d0-packet-coalescing", "network.flow-control", "network.priority-vlan"
    };

    private static readonly string[] CompetitiveFpsProfileIds =
    [
        "gaming.capture", "gaming.game-mode", "cpu.boost-mode", "cpu.energy-performance-preference",
        "cpu.performance-increase-policy", "power.active-plan", "graphics.hags", "system.power-throttling",
        "input.power-guard", "input.usb-selective-suspend", "input.mouse-acceleration", "input.keyboard-repeat",
        "network.rss", "network.tcp-autotuning", "network.power-saving"
    ];

    private static readonly string[] MaximumFpsProfileIds =
    [
        "gaming.capture", "gaming.game-mode", "cpu.boost-mode", "cpu.energy-performance-preference",
        "cpu.performance-increase-policy", "cpu.boost-policy", "cpu.core-parking-min", "cpu.performance-floor",
        "power.active-plan", "power.pcie-aspm", "graphics.hags", "system.power-throttling", "windows.visual-effects",
        "input.power-guard", "input.usb-selective-suspend", "input.mouse-acceleration", "input.keyboard-repeat",
        "network.rss", "network.tcp-autotuning", "network.power-saving", "network.checksum-offload",
        "network.rsc-low-latency", "network.interrupt-moderation", "network.eee", "network.d0-packet-coalescing"
    ];

    private static readonly string[] LowLatencyProfileIds =
    [
        "network.rss", "network.tcp-autotuning", "network.power-saving",
        "network.checksum-offload", "network.task-offload", "network.eee"
    ];

    private readonly ITweakEngine _engine;
    private readonly HardwareInfo _hardware;
    private readonly IBackupService _backupService;
    private string _refreshStatus = "Ready — controls verify on demand. No long startup state scan.";
    private string _selectedCategory = "All tweaks";
    private string _selectedSort = "Best";
    private string _searchText = string.Empty;
    private string _applyBestStatus = "Profiles only apply supported, reversible Safe/Moderate controls and verify Windows read-back.";
    private bool _isApplyingBest;
    private double _applyBestProgress;
    private object? _networkControlContext;
    private bool _showOnlyActionable;

    public ObservableCollection<TweakCardViewModel> Tweaks { get; }
    private ObservableCollection<TweakCardViewModel> _visibleTweaks = new();
    public ObservableCollection<TweakCardViewModel> VisibleTweaks => _visibleTweaks;

    public ObservableCollection<string> Categories { get; } = new()
    {
        "All tweaks",
        "FPS",
        "Gaming",
        "CPU & Power",
        "Graphics",
        "Network",
        "Input",
        "Windows",
        "Safety & Privacy",
        "Advanced"
    };

    public ObservableCollection<string> SortOptions { get; } = new()
    {
        "Best",
        "Newest",
        "Oldest",
        "A–Z"
    };

    public string EngineStatus { get; }
    public string EngineStatusDetail { get; }
    public string DefinitionCountText => $"{Tweaks.Count} controls";
    public string PrivacyControlCountText => $"{Tweaks.Count(x => x.Definition.Category == TweakCategory.PrivacySafety)} privacy/safety";
    public string NetworkControlCountText => $"{Tweaks.Count(x => x.Definition.Category == TweakCategory.Network)} network";
    public string FpsControlCountText => $"{Tweaks.Count(x => FpsIds.Contains(x.Definition.Id))} FPS/frame-time";
    public string ActiveCountText => $"{Tweaks.Count(x => x.IsActivated)} active";
    public string ReadyCountText => $"{Tweaks.Count(x => x.CanApply || x.IsStatePending)} ready/check";
    public string ProtectedCountText => $"{Tweaks.Count(x => x.IsActivated && !x.CanUndo)} detected-on / protected";

    // Compatibility shim for a previous Tweaks template. Keep writable so stale/cached XAML
    // can never surface a TwoWay-to-read-only binding failure.
    public object? NetworkControlContext
    {
        get => _networkControlContext;
        set => SetProperty(ref _networkControlContext, value);
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
                RebuildVisibleTweaks();
        }
    }

    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value))
                RebuildVisibleTweaks();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RebuildVisibleTweaks();
        }
    }

    public bool ShowOnlyActionable
    {
        get => _showOnlyActionable;
        set
        {
            if (SetProperty(ref _showOnlyActionable, value))
                RebuildVisibleTweaks();
        }
    }

    public string RefreshStatus
    {
        get => _refreshStatus;
        private set => SetProperty(ref _refreshStatus, value);
    }

    public string ApplyBestStatus
    {
        get => _applyBestStatus;
        private set => SetProperty(ref _applyBestStatus, value);
    }

    public bool IsApplyingBest
    {
        get => _isApplyingBest;
        private set => SetProperty(ref _isApplyingBest, value);
    }

    public double ApplyBestProgress
    {
        get => _applyBestProgress;
        private set => SetProperty(ref _applyBestProgress, Math.Clamp(value, 0, 100));
    }

    public AsyncRelayCommand RefreshAllCommand { get; }
    public AsyncRelayCommand ApplyBestForPcCommand { get; }
    public AsyncRelayCommand ApplyCompetitiveFpsCommand { get; }
    public AsyncRelayCommand ApplyMaximumFpsCommand { get; }
    public AsyncRelayCommand ApplyLowLatencyProfileCommand { get; }

    public TweaksViewModel(ITweakEngine engine, TweakEngineSelfCheckResult selfCheck, HardwareInfo hardware, IBackupService backupService)
    {
        _engine = engine;
        _hardware = hardware;
        _backupService = backupService;
        EngineStatus = selfCheck.Passed ? "ENGINE READY" : "ENGINE CHECK FAILED";
        EngineStatusDetail = selfCheck.Message;

        Tweaks = new ObservableCollection<TweakCardViewModel>(
            engine.Definitions.Select(definition => new TweakCardViewModel(engine, definition)));

        foreach (var tweak in Tweaks)
            tweak.PropertyChanged += OnTweakPropertyChanged;

        RefreshAllCommand = new AsyncRelayCommand(() => RefreshAllAsync(), () => !_isApplyingBest);
        ApplyBestForPcCommand = new AsyncRelayCommand(ApplyBestForPcAsync, () => !_isApplyingBest);
        ApplyCompetitiveFpsCommand = new AsyncRelayCommand(
            () => ApplyCuratedProfileAsync("Competitive FPS", CompetitiveFpsProfileIds), () => !_isApplyingBest);
        ApplyMaximumFpsCommand = new AsyncRelayCommand(
            () => ApplyCuratedProfileAsync("Maximum FPS (advanced)", MaximumFpsProfileIds), () => !_isApplyingBest);
        ApplyLowLatencyProfileCommand = new AsyncRelayCommand(
            () => ApplyCuratedProfileAsync("Low-latency local stack", LowLatencyProfileIds), () => !_isApplyingBest);
        RebuildVisibleTweaks();
    }

    public async Task PrewarmAsync()
    {
        // Phase 23.5: do not sweep every tweak in the background. Many hardware-aware cards
        // invoke PowerShell/CIM/driver reads; a delayed full scan still competes with the user
        // several seconds after launch and was a major source of "random" UI stutter. Cards
        // stay Ready and perform their targeted compatibility/read-back only when used.
        await Task.Delay(250);
        RefreshStatus = "Ready — controls verify on demand. No background startup scan is running.";
        RaiseStats();
    }

    private async Task InitializeAsync(int concurrency = 2)
    {
        try
        {
            await RefreshAllAsync(concurrency);
        }
        catch
        {
            RefreshStatus = "Background state discovery finished with some unavailable controls. Refresh any card for details.";
        }
    }

    private async Task RefreshAllAsync(int concurrency = 2)
    {
        RefreshStatus = "Detecting supported tweak states in the background…";
        concurrency = Math.Clamp(concurrency, 1, 3);

        if (concurrency == 1)
        {
            var index = 0;
            foreach (var tweak in Tweaks)
            {
                await tweak.RefreshAsync();
                index++;
                if (index % 2 == 0)
                    await Task.Delay(45);
            }
        }
        else
        {
            using var gate = new SemaphoreSlim(concurrency, concurrency);
            var tasks = Tweaks.Select(async tweak =>
            {
                await gate.WaitAsync();
                try { await tweak.RefreshAsync(); }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks);
        }

        RefreshStatus = $"State detection complete • {Tweaks.Count} controls checked";
        RebuildVisibleTweaks();
        RaiseStats();
    }

    private async Task ApplyBestForPcAsync()
    {
        IsApplyingBest = true;
        ApplyBestProgress = 0;
        RaiseCommandState();
        try
        {
            ApplyBestStatus = $"Checking recommendations for {_hardware.Processor} + {_hardware.Graphics}…";
            await RefreshAllAsync(2);

            try
            {
                ApplyBestStatus = "Creating a rollback snapshot before the Best-for-PC batch…";
                await _backupService.CreateSnapshotAsync($"Before Apply Best {DateTime.Now:MMM d h:mm tt}", "Safety");
            }
            catch (Exception ex)
            {
                ApplyBestStatus = $"Best-for-PC was cancelled because Sabby could not create its rollback snapshot: {ex.Message}";
                return;
            }

            var applied = new List<string>();
            var already = new List<string>();
            var skipped = new List<string>();
            var processed = 0;

            foreach (var id in AutoOptimizeIds)
            {
                try
                {
                    var tweak = Tweaks.FirstOrDefault(x => x.Definition.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (tweak is null) continue;

                    ApplyBestStatus = $"Optimizing {tweak.Name}…";
                    if (tweak.Definition.SafetyLevel == TweakSafetyLevel.Advanced || tweak.IsRecommendationBlocked)
                    {
                        skipped.Add(tweak.Name);
                        continue;
                    }

                    await tweak.RefreshAsync();
                    if (tweak.IsActivated) { already.Add(tweak.Name); continue; }
                    if (!tweak.CanApply) { skipped.Add(tweak.Name); continue; }
                    await tweak.ApplyRecommendedAsync();
                    if (tweak.IsActivated) applied.Add(tweak.Name); else skipped.Add(tweak.Name);
                }
                finally
                {
                    processed++;
                    ApplyBestProgress = processed * 100d / AutoOptimizeIds.Length;
                }
            }

            ApplyBestStatus = $"PC BEST verified: {applied.Count} applied • {already.Count} already set • {skipped.Count} safely skipped. Advanced/situational controls are never forced.";
            RefreshStatus = $"Best-for-PC pass finished • {DateTime.Now:h:mm:ss tt}";
            RebuildVisibleTweaks();
            RaiseStats();
        }
        finally
        {
            ApplyBestProgress = 100;
            IsApplyingBest = false;
            RaiseCommandState();
        }
    }

    private async Task ApplyCuratedProfileAsync(string profileName, IReadOnlyList<string> ids)
    {
        IsApplyingBest = true;
        ApplyBestProgress = 0;
        RaiseCommandState();
        try
        {
            try
            {
                ApplyBestStatus = $"Creating rollback snapshot before {profileName}…";
                await _backupService.CreateSnapshotAsync($"Before {profileName} {DateTime.Now:MMM d h:mm tt}", "Performance");
            }
            catch (Exception ex)
            {
                ApplyBestStatus = $"{profileName} cancelled: rollback snapshot failed: {ex.Message}";
                return;
            }

            var applied = 0;
            var already = 0;
            var skipped = 0;
            for (var i = 0; i < ids.Count; i++)
            {
                var tweak = Tweaks.FirstOrDefault(x => x.Definition.Id.Equals(ids[i], StringComparison.OrdinalIgnoreCase));
                if (tweak is null) { skipped++; continue; }
                ApplyBestStatus = $"{profileName}: checking {tweak.Name}…";
                await tweak.RefreshAsync();

                if (tweak.IsActivated) already++;
                else if (tweak.Definition.SafetyLevel == TweakSafetyLevel.Advanced || !tweak.CanApply) skipped++;
                else
                {
                    await tweak.ApplyRecommendedAsync();
                    if (tweak.IsActivated) applied++; else skipped++;
                }

                ApplyBestProgress = (i + 1) * 100d / ids.Count;
                await Task.Delay(35);
            }

            ApplyBestStatus = $"{profileName} complete • {applied} applied • {already} already correct • {skipped} safely skipped.";
            RebuildVisibleTweaks();
            RaiseStats();
        }
        finally
        {
            ApplyBestProgress = 100;
            IsApplyingBest = false;
            RaiseCommandState();
        }
    }

    private void OnTweakPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TweakCardViewModel.IsActivated) or nameof(TweakCardViewModel.CanApply) or
            nameof(TweakCardViewModel.CanUndo) or nameof(TweakCardViewModel.IsBusy))
        {
            RaiseStats();
            if (ShowOnlyActionable)
                RebuildVisibleTweaks();
        }
    }

    private void RaiseStats()
    {
        OnPropertyChanged(nameof(ActiveCountText));
        OnPropertyChanged(nameof(ReadyCountText));
        OnPropertyChanged(nameof(ProtectedCountText));
    }

    private void RaiseCommandState()
    {
        RefreshAllCommand.RaiseCanExecuteChanged();
        ApplyBestForPcCommand.RaiseCanExecuteChanged();
        ApplyCompetitiveFpsCommand.RaiseCanExecuteChanged();
        ApplyMaximumFpsCommand.RaiseCanExecuteChanged();
        ApplyLowLatencyProfileCommand.RaiseCanExecuteChanged();
    }

    private void RebuildVisibleTweaks()
    {
        IEnumerable<TweakCardViewModel> query = Tweaks;

        if (!SelectedCategory.Equals("All tweaks", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(tweak => SelectedCategory switch
            {
                "FPS" => FpsIds.Contains(tweak.Definition.Id),
                "Input" => tweak.Definition.Id.StartsWith("input.", StringComparison.OrdinalIgnoreCase),
                "CPU & Power" => tweak.CategoryLabel.Equals("CPU & POWER", StringComparison.OrdinalIgnoreCase),
                "Safety & Privacy" => tweak.CategoryLabel.Equals("SAFETY & PRIVACY", StringComparison.OrdinalIgnoreCase),
                _ => tweak.CategoryLabel.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase)
            });
        }

        if (ShowOnlyActionable)
            query = query.Where(x => x.CanApply || x.CanUndo || x.IsBusy);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(tweak =>
                tweak.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                tweak.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                tweak.CategoryLabel.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                tweak.StateDisplay.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                tweak.VerificationGuide.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                tweak.CompatibilityText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        query = SelectedSort switch
        {
            "Newest" => query.OrderByDescending(x => x.IntroducedPhase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "Oldest" => query.OrderBy(x => x.IntroducedPhase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "A–Z" => query.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderByDescending(x => x.EvidenceScore).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        };

        // Replace the filtered collection in one notification instead of firing 50+
        // individual collection-change events. This removes a large source of click/category lag.
        _visibleTweaks = new ObservableCollection<TweakCardViewModel>(query.ToArray());
        OnPropertyChanged(nameof(VisibleTweaks));
    }
}
