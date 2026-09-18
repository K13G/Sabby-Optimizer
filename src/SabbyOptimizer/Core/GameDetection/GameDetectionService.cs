using System.Diagnostics;
using PCTweaker.Core.GameProfiles;
using PCTweaker.Core.Presets;
using PCTweaker.Core.Services;
using PCTweaker.Core.Tweaks;
using PCTweaker.Models.GameDetection;
using PCTweaker.Models.GameProfiles;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.GameDetection;

public sealed class GameDetectionService : IGameDetectionService
{
    private readonly IGameProfileService _profiles;
    private readonly IPresetService _presets;
    private readonly ITweakEngine _engine;
    private readonly IHardwareInfoService _hardware;
    private readonly ISettingsService _settings;
    private readonly IAppLogger _logger;
    private readonly Dictionary<Guid, ActiveSession> _active = new();
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private IReadOnlyList<GameProfileDefinition> _cachedProfiles = Array.Empty<GameProfileDefinition>();
    private DateTime _profilesLoadedAtUtc = DateTime.MinValue;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;

    public event EventHandler<GameRuntimeState>? StateChanged;
    public bool IsMonitoring => _monitorTask is { IsCompleted: false };

    public GameDetectionService(
        IGameProfileService profiles,
        IPresetService presets,
        ITweakEngine engine,
        IHardwareInfoService hardware,
        ISettingsService settings,
        IAppLogger logger)
    {
        _profiles = profiles;
        _presets = presets;
        _engine = engine;
        _hardware = hardware;
        _settings = settings;
        _logger = logger;
    }

    public void Start()
    {
        if (IsMonitoring) return;
        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        RaiseState(new GameRuntimeState(Guid.Empty, false, "Monitoring", "Launch detection is active. Waiting for an enabled saved game to start."));
        _logger.Info("Phase 7 game detection monitor started.");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var cts = _cts;
        var task = _monitorTask;
        _cts = null;
        _monitorTask = null;

        if (cts is not null)
        {
            cts.Cancel();
            if (task is not null)
            {
                try { await task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
            }
            cts.Dispose();
        }

        await RestoreAllSessionsAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info("Phase 7 game detection monitor stopped.");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(2500));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_settings.Current.GameDetectionEnabled)
                {
                    await RestoreAllSessionsAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await PollAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Game detection poll failed: {ex.Message}");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) break;
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var profiles = await GetProfilesAsync(cancellationToken).ConfigureAwait(false);
        var enabled = profiles.Where(profile => profile.Enabled && File.Exists(profile.ExecutablePath)).ToArray();
        var running = BuildRunningMap(enabled);

        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var profile in enabled)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isRunning = running.Contains(profile.Id);
                if (isRunning && !_active.ContainsKey(profile.Id))
                    await BeginSessionAsync(profile, cancellationToken).ConfigureAwait(false);
                else if (!isRunning && _active.TryGetValue(profile.Id, out var session))
                    await EndSessionAsync(session, cancellationToken).ConfigureAwait(false);
            }

            var validIds = enabled.Select(profile => profile.Id).ToHashSet();
            foreach (var stale in _active.Values.Where(session => !validIds.Contains(session.Profile.Id)).ToArray())
                await EndSessionAsync(stale, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task<IReadOnlyList<GameProfileDefinition>> GetProfilesAsync(CancellationToken cancellationToken)
    {
        if (_cachedProfiles.Count > 0 && DateTime.UtcNow - _profilesLoadedAtUtc < TimeSpan.FromSeconds(10))
            return _cachedProfiles;

        _cachedProfiles = await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(false);
        _profilesLoadedAtUtc = DateTime.UtcNow;
        return _cachedProfiles;
    }

    private static HashSet<Guid> BuildRunningMap(IReadOnlyList<GameProfileDefinition> profiles)
    {
        var result = new HashSet<Guid>();
        var byProcessName = profiles
            .GroupBy(profile => Path.GetFileNameWithoutExtension(profile.ExecutablePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var (processName, candidates) in byProcessName)
        {
            if (string.IsNullOrWhiteSpace(processName)) continue;
            Process[] processes;
            try { processes = Process.GetProcessesByName(processName); }
            catch { continue; }

            foreach (var process in processes)
            {
                using (process)
                {
                    string? actualPath = null;
                    try { actualPath = process.MainModule?.FileName; }
                    catch { }

                    foreach (var profile in candidates)
                    {
                        if (actualPath is null || PathsEqual(actualPath, profile.ExecutablePath))
                            result.Add(profile.Id);
                    }
                }
            }
        }

        return result;
    }

    private async Task BeginSessionAsync(GameProfileDefinition profile, CancellationToken cancellationToken)
    {
        var desired = new Dictionary<string, TweakStateKind>(StringComparer.OrdinalIgnoreCase);
        string? presetName = null;
        var smartUsed = false;

        var fallbackPresetUsed = false;
        var requestedPresetId = profile.LinkedPresetId;
        if (requestedPresetId is null && _settings.Current.AutomationFallbackPresetId is Guid fallbackId)
        {
            requestedPresetId = fallbackId;
            fallbackPresetUsed = true;
        }

        if (requestedPresetId is Guid presetId)
        {
            var preset = (await _presets.GetAllAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.Id == presetId);
            if (preset is not null)
            {
                presetName = fallbackPresetUsed ? $"{preset.Name} (fallback)" : preset.Name;
                foreach (var entry in preset.Entries)
                    desired[entry.TweakId] = entry.DesiredState;
            }
        }

        SmartGameTuningPlan? smartPlan = null;
        if (_settings.Current.SmartGameTuningTest)
        {
            smartPlan = SmartGameTuningAdvisor.Build(_hardware.GetHardwareInfo());
            smartUsed = true;
            foreach (var pair in smartPlan.DesiredStates)
                desired.TryAdd(pair.Key, pair.Value); // explicitly linked preset wins
        }

        var previous = new Dictionary<string, TweakStateKind>(StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        var skipped = 0;

        foreach (var pair in desired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TweakDetectionResult before;
            try { before = await _engine.DetectAsync(pair.Key, cancellationToken).ConfigureAwait(false); }
            catch { skipped++; continue; }

            if (before.State is not (TweakStateKind.Applied or TweakStateKind.NotApplied))
            {
                skipped++;
                continue;
            }

            previous[pair.Key] = before.State;
            if (before.State == pair.Value) continue;

            var result = pair.Value == TweakStateKind.Applied
                ? await _engine.ApplyAsync(pair.Key, cancellationToken).ConfigureAwait(false)
                : await _engine.UndoAsync(pair.Key, cancellationToken).ConfigureAwait(false);

            if (result.Success) changed++;
            else skipped++;
        }

        var session = new ActiveSession(profile, previous, presetName, smartUsed, DateTime.UtcNow);
        _active[profile.Id] = session;

        var priorityDetail = TryApplyProcessPriority(profile);

        var detail = desired.Count == 0
            ? "Game detected. No linked preset is assigned."
            : changed > 0
                ? $"Applied {changed} supported setting{(changed == 1 ? string.Empty : "s")}; {skipped} unsupported or unavailable."
                : $"No supported changes were needed yet; {skipped} requested setting{(skipped == 1 ? string.Empty : "s")} are unavailable in the current phase.";

        if (smartPlan is not null)
            detail += $" Smart tuning test classified this PC as {smartPlan.Tier}.";
        if (!string.IsNullOrWhiteSpace(priorityDetail))
            detail += $" {priorityDetail}";

        RaiseState(new GameRuntimeState(profile.Id, true, "Running", detail, presetName, smartUsed, session.StartedAtUtc));
        _logger.Info($"Game detected: {profile.Name}. Linked preset='{presetName ?? "none"}', smart={smartUsed}, changed={changed}, skipped={skipped}.");
    }

    private static string TryApplyProcessPriority(GameProfileDefinition profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ProcessPriority) ||
            profile.ProcessPriority.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var desired = profile.ProcessPriority.Equals("High", StringComparison.OrdinalIgnoreCase)
            ? ProcessPriorityClass.High
            : ProcessPriorityClass.AboveNormal;

        var processName = Path.GetFileNameWithoutExtension(profile.ExecutablePath);
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;

        try
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var actual = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(actual) && !PathsEqual(actual, profile.ExecutablePath))
                            continue;
                        process.PriorityClass = desired;
                        var readBack = process.PriorityClass;
                        return readBack == desired
                            ? $"Process priority verified as {profile.ProcessPriority}."
                            : $"Process priority requested as {profile.ProcessPriority}, but Windows reported {readBack}.";
                    }
                    catch { }
                }
            }
        }
        catch { }

        return $"Process priority {profile.ProcessPriority} could not be verified.";
    }

    private async Task EndSessionAsync(ActiveSession session, CancellationToken cancellationToken)
    {
        var restored = 0;
        var failed = 0;
        foreach (var pair in session.PreviousStates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var current = await _engine.DetectAsync(pair.Key, cancellationToken).ConfigureAwait(false);
                if (current.State == pair.Value) continue;
                var result = pair.Value == TweakStateKind.Applied
                    ? await _engine.ApplyAsync(pair.Key, cancellationToken).ConfigureAwait(false)
                    : await _engine.UndoAsync(pair.Key, cancellationToken).ConfigureAwait(false);
                if (result.Success) restored++; else failed++;
            }
            catch { failed++; }
        }

        _active.Remove(session.Profile.Id);
        RaiseState(new GameRuntimeState(
            session.Profile.Id,
            false,
            "Ready",
            restored > 0 ? $"Game closed. Restored {restored} previous setting{(restored == 1 ? string.Empty : "s")}." : "Game closed. Previous settings are restored.",
            session.PresetName,
            session.SmartTuningUsed,
            session.StartedAtUtc));
        _logger.Info($"Game exited: {session.Profile.Name}. Restored={restored}, failed={failed}.");
    }

    private async Task RestoreAllSessionsAsync(CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var session in _active.Values.ToArray())
                await EndSessionAsync(session, cancellationToken).ConfigureAwait(false);
        }
        finally { _sessionGate.Release(); }
    }

    private void RaiseState(GameRuntimeState state)
    {
        try { StateChanged?.Invoke(this, state); }
        catch (Exception ex) { _logger.Warning($"Game detection state listener failed: {ex.Message}"); }
    }

    private static bool PathsEqual(string left, string right)
    {
        try { return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return left.Equals(right, StringComparison.OrdinalIgnoreCase); }
    }

    private sealed record ActiveSession(
        GameProfileDefinition Profile,
        IReadOnlyDictionary<string, TweakStateKind> PreviousStates,
        string? PresetName,
        bool SmartTuningUsed,
        DateTime StartedAtUtc);
}
