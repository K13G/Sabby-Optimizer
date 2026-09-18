using System.Text.Json;
using Microsoft.Win32;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

/// <summary>
/// Small, declarative Windows policy/registry handler used for documented DWORD policies.
/// It captures every original value before the first write and restores the exact prior state.
/// </summary>
public sealed class RegistryPolicyBundleTweakHandler : ITweakHandler
{
    public enum PolicyHive
    {
        CurrentUser,
        LocalMachine
    }

    public sealed record DwordTarget(PolicyHive Hive, string Path, string Name, int AppliedValue);

    private sealed class SavedValue
    {
        public PolicyHive Hive { get; set; }
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Existed { get; set; }
        public string Kind { get; set; } = nameof(RegistryValueKind.DWord);
        public string? Value { get; set; }
    }

    private readonly IReadOnlyList<DwordTarget> _targets;
    private readonly string _restoreFile;
    private readonly string _appliedLabel;
    private readonly string _notAppliedLabel;

    public RegistryPolicyBundleTweakHandler(
        IAppPaths paths,
        TweakDefinition definition,
        string restoreFileName,
        string appliedLabel,
        string notAppliedLabel,
        params DwordTarget[] targets)
    {
        if (targets.Length == 0) throw new ArgumentException("At least one policy target is required.", nameof(targets));
        Definition = definition;
        _targets = targets;
        _appliedLabel = appliedLabel;
        _notAppliedLabel = notAppliedLabel;
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState", "PolicyBundles");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, restoreFileName);
    }

    public TweakDefinition Definition { get; }

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var allMatch = _targets.All(TargetMatches);
            return Task.FromResult(allMatch
                ? new TweakDetectionResult(TweakStateKind.Applied, _appliedLabel, "All documented policy values match Sabby's requested state.", false, File.Exists(_restoreFile))
                : new TweakDetectionResult(TweakStateKind.NotApplied, _notAppliedLabel, "One or more policy values do not match Sabby's requested state.", true, false));
        }
        catch (Exception ex)
        {
            return Task.FromResult(TweakDetectionResult.Error($"Policy read-back failed: {ex.Message}"));
        }
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hadRestoreState = File.Exists(_restoreFile);
        try
        {
            CaptureOriginalIfMissing();
            foreach (var target in _targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = OpenHive(target.Hive);
                using var key = root.CreateSubKey(target.Path, writable: true)
                    ?? throw new InvalidOperationException($"Could not open registry path '{target.Path}'.");
                key.SetValue(target.Name, target.AppliedValue, RegistryValueKind.DWord);
            }

            if (!_targets.All(TargetMatches))
            {
                RestoreSavedState(deleteOnSuccess: !hadRestoreState);
                return Task.FromResult(TweakOperationResult.Failed("Windows accepted the policy write, but read-back verification failed. The saved state was restored where possible."));
            }

            return Task.FromResult(TweakOperationResult.Completed(
                $"{Definition.Name} was applied and verified.",
                new TweakDetectionResult(TweakStateKind.Applied, _appliedLabel, "All policy values were written and verified.", false, true),
                Definition.RequiresRestart));
        }
        catch (Exception ex)
        {
            try { RestoreSavedState(deleteOnSuccess: !hadRestoreState); } catch { }
            return Task.FromResult(TweakOperationResult.Failed($"The policy change failed safely: {ex.Message}"));
        }
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_restoreFile))
            return Task.FromResult(TweakOperationResult.Failed("No saved pre-change policy state exists, so Sabby did not guess a default."));

        try
        {
            RestoreSavedState(deleteOnSuccess: true);
            return Task.FromResult(TweakOperationResult.Completed(
                $"{Definition.Name} was restored to its exact saved pre-change state.",
                new TweakDetectionResult(TweakStateKind.Custom, "Restored", "The exact registry state captured before activation was restored.", true, false),
                Definition.RequiresRestart));
        }
        catch (Exception ex)
        {
            return Task.FromResult(TweakOperationResult.Failed($"Policy restore failed: {ex.Message}"));
        }
    }

    private bool TargetMatches(DwordTarget target)
    {
        var root = OpenHive(target.Hive);
        using var key = root.OpenSubKey(target.Path, writable: false);
        var value = key?.GetValue(target.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value is not null && Convert.ToInt64(value) == target.AppliedValue;
    }

    private void CaptureOriginalIfMissing()
    {
        if (File.Exists(_restoreFile))
        {
            _ = LoadSavedState(); // Never overwrite a corrupt or mismatched rollback record.
            return;
        }

        var saved = new List<SavedValue>();
        foreach (var target in _targets)
        {
            var root = OpenHive(target.Hive);
            using var key = root.OpenSubKey(target.Path, writable: false);
            var exists = key?.GetValueNames().Contains(target.Name, StringComparer.OrdinalIgnoreCase) == true;
            var state = new SavedValue
            {
                Hive = target.Hive,
                Path = target.Path,
                Name = target.Name,
                Existed = exists
            };

            if (exists && key is not null)
            {
                var kind = key.GetValueKind(target.Name);
                var value = key.GetValue(target.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                state.Kind = kind.ToString();
                state.Value = kind switch
                {
                    RegistryValueKind.DWord => Convert.ToInt32(value).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    RegistryValueKind.QWord => Convert.ToInt64(value).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    RegistryValueKind.String or RegistryValueKind.ExpandString => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                    _ => throw new InvalidOperationException($"Existing value {target.Name} uses unsupported registry type {kind}; Sabby refused to overwrite it.")
                };
            }

            saved.Add(state);
        }

        var json = JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_restoreFile, json);
    }

    private void RestoreSavedState(bool deleteOnSuccess)
    {
        if (!File.Exists(_restoreFile)) return;
        var saved = LoadSavedState();

        foreach (var state in saved)
        {
            var root = OpenHive(state.Hive);
            using var key = root.CreateSubKey(state.Path, writable: true)
                ?? throw new InvalidOperationException($"Could not open registry path '{state.Path}' for restore.");

            if (!state.Existed)
            {
                key.DeleteValue(state.Name, throwOnMissingValue: false);
                continue;
            }

            if (!Enum.TryParse<RegistryValueKind>(state.Kind, out var kind))
                throw new InvalidOperationException($"Saved registry type {state.Kind} is invalid.");
            key.SetValue(state.Name, ParseSavedValue(state, kind), kind);
        }

        var failed = saved.Where(state => !SavedValueMatches(state)).ToList();
        if (failed.Count > 0)
            throw new InvalidOperationException($"Rollback read-back failed for {failed.Count} saved registry value{(failed.Count == 1 ? string.Empty : "s")}; the restore record was retained.");

        if (deleteOnSuccess)
        {
            try { File.Delete(_restoreFile); } catch { }
        }
    }

    private List<SavedValue> LoadSavedState()
    {
        List<SavedValue> saved;
        try
        {
            saved = JsonSerializer.Deserialize<List<SavedValue>>(File.ReadAllText(_restoreFile))
                    ?? throw new InvalidDataException("Saved policy restore state is empty or invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Saved policy restore state is corrupt; Sabby refused to overwrite it.", ex);
        }

        if (saved.Count != _targets.Count)
            throw new InvalidDataException("Saved policy restore state does not match this tweak's target count.");

        foreach (var target in _targets)
        {
            var matches = saved.Count(state => state.Hive == target.Hive &&
                                               state.Path.Equals(target.Path, StringComparison.OrdinalIgnoreCase) &&
                                               state.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase));
            if (matches != 1)
                throw new InvalidDataException($"Saved policy restore state does not uniquely match {target.Name}.");
        }

        foreach (var state in saved.Where(state => state.Existed))
        {
            if (!Enum.TryParse<RegistryValueKind>(state.Kind, out var kind) ||
                kind is not (RegistryValueKind.DWord or RegistryValueKind.QWord or RegistryValueKind.String or RegistryValueKind.ExpandString))
                throw new InvalidDataException($"Saved registry type '{state.Kind}' is not supported for safe restore.");

            _ = ParseSavedValue(state, kind);
        }

        return saved;
    }

    private bool SavedValueMatches(SavedValue state)
    {
        var root = OpenHive(state.Hive);
        using var key = root.OpenSubKey(state.Path, writable: false);
        var exists = key?.GetValueNames().Contains(state.Name, StringComparer.OrdinalIgnoreCase) == true;
        if (!state.Existed) return !exists;
        if (!exists || key is null) return false;

        if (!Enum.TryParse<RegistryValueKind>(state.Kind, out var kind) || key.GetValueKind(state.Name) != kind)
            return false;

        var actual = key.GetValue(state.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var expected = ParseSavedValue(state, kind);
        return kind switch
        {
            RegistryValueKind.DWord => Convert.ToInt32(actual) == Convert.ToInt32(expected),
            RegistryValueKind.QWord => Convert.ToInt64(actual) == Convert.ToInt64(expected),
            RegistryValueKind.String or RegistryValueKind.ExpandString => string.Equals(Convert.ToString(actual), Convert.ToString(expected), StringComparison.Ordinal),
            _ => false
        };
    }

    private static object ParseSavedValue(SavedValue state, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => int.Parse(state.Value ?? "0", System.Globalization.CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(state.Value ?? "0", System.Globalization.CultureInfo.InvariantCulture),
        RegistryValueKind.String or RegistryValueKind.ExpandString => state.Value ?? string.Empty,
        _ => throw new InvalidOperationException($"Saved registry type {state.Kind} is not supported for restore.")
    };

    private static RegistryKey OpenHive(PolicyHive hive) => hive switch
    {
        PolicyHive.CurrentUser => Registry.CurrentUser,
        PolicyHive.LocalMachine => Registry.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(hive))
    };
}
