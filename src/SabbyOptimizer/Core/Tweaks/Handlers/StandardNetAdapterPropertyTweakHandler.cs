using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

/// <summary>
/// Capability-gated handler for standardized NDIS advanced-property registry keywords.
/// Only connected physical adapters exposing the requested keyword are modified.
/// </summary>
public sealed class StandardNetAdapterPropertyTweakHandler : ITweakHandler, ITweakCompatibilityProvider
{
    private sealed record AdapterState(string Name, string[] RegistryValue);
    private sealed record SnapshotItem(string Name, string RegistryKeyword, string[] RegistryValue);

    private static readonly object SnapshotLock = new();
    private static IReadOnlyList<SnapshotItem> _snapshot = Array.Empty<SnapshotItem>();
    private static DateTime _snapshotUtc = DateTime.MinValue;
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(2);

    private readonly string _registryKeyword;
    private readonly string _targetValue;
    private readonly string _restoreFile;
    private readonly string _appliedLabel;
    private readonly string _notAppliedLabel;

    public StandardNetAdapterPropertyTweakHandler(
        IAppPaths paths,
        TweakDefinition definition,
        string registryKeyword,
        string targetValue,
        string restoreFileName,
        string appliedLabel,
        string notAppliedLabel)
    {
        Definition = definition;
        _registryKeyword = registryKeyword;
        _targetValue = targetValue;
        _appliedLabel = appliedLabel;
        _notAppliedLabel = notAppliedLabel;
        var directory = Path.Combine(paths.UserDataDirectory, "TweakState", "NetworkAdvanced");
        Directory.CreateDirectory(directory);
        _restoreFile = Path.Combine(directory, restoreFileName);
    }

    public TweakDefinition Definition { get; }

    public static bool IsSupported(string registryKeyword) =>
        OperatingSystem.IsWindows() &&
        !string.IsNullOrWhiteSpace(registryKeyword) &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapter") &&
        PowerShellNetworkAccess.CommandExists("Get-NetAdapterAdvancedProperty") &&
        PowerShellNetworkAccess.CommandExists("Set-NetAdapterAdvancedProperty");

    public Task<TweakCompatibilityResult> CheckCompatibilityAsync(bool applying, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        return Task.FromResult(states.Count == 0
            ? TweakCompatibilityResult.Blocked($"No connected physical network adapter exposes {_registryKeyword}.")
            : TweakCompatibilityResult.Compatible(
                $"{states.Count} compatible adapter{(states.Count == 1 ? string.Empty : "s")} detected.",
                applying ? "Changing an advanced NIC property can briefly interrupt the network link on some drivers." : null));
    }

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates();
        if (states.Count == 0)
            return Task.FromResult(TweakDetectionResult.Unavailable($"No connected physical adapter exposes {_registryKeyword}."));

        var allMatch = states.All(state => state.RegistryValue.Any(v => string.Equals(v, _targetValue, StringComparison.OrdinalIgnoreCase)));
        return Task.FromResult(allMatch
            ? new TweakDetectionResult(TweakStateKind.Applied, _appliedLabel, $"{_registryKeyword} matches the requested value on all supported connected adapters.", false, File.Exists(_restoreFile))
            : new TweakDetectionResult(TweakStateKind.NotApplied, _notAppliedLabel, $"At least one supported adapter has a different {_registryKeyword} value.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = ReadStates(forceRefresh: true);
        if (states.Count == 0)
            return Task.FromResult(TweakOperationResult.Failed($"No connected adapter exposes {_registryKeyword}."));

        var hadRestoreState = File.Exists(_restoreFile);
        try
        {
            SavePrevious(states);
        }
        catch (Exception ex)
        {
            return Task.FromResult(TweakOperationResult.Failed($"Sabby could not save the pre-change adapter state, so no NIC setting was changed. {ex.Message}"));
        }

        foreach (var state in states)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var keyword = PowerShellNetworkAccess.Escape(_registryKeyword);
            var value = PowerShellNetworkAccess.Escape(_targetValue);
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterAdvancedProperty -Name '{name}' -RegistryKeyword '{keyword}' -RegistryValue '{value}' -NoRestart -Confirm:$false");
            if (!result.Success)
            {
                var restoreFailure = Restore(states);
                if (!hadRestoreState && string.IsNullOrWhiteSpace(restoreFailure)) TryDelete();
                var suffix = string.IsNullOrWhiteSpace(restoreFailure) ? "Previous values were restored." : restoreFailure;
                return Task.FromResult(TweakOperationResult.Failed($"Could not change {Definition.Name} on {state.Name}. {suffix} {result.Error}".Trim()));
            }
        }

        InvalidateSnapshot();
        var verifiedStates = ReadStates(forceRefresh: true);
        var verified = verifiedStates.Count == states.Count &&
                       verifiedStates.All(state => state.RegistryValue.Any(v => string.Equals(v, _targetValue, StringComparison.OrdinalIgnoreCase)));
        if (!verified)
        {
            var restoreFailure = Restore(states);
            if (!hadRestoreState && string.IsNullOrWhiteSpace(restoreFailure)) TryDelete();
            var suffix = string.IsNullOrWhiteSpace(restoreFailure) ? "Previous values were restored." : restoreFailure;
            return Task.FromResult(TweakOperationResult.Failed($"{Definition.Name} did not pass adapter read-back verification. {suffix}"));
        }

        return Task.FromResult(TweakOperationResult.Completed(
            $"{Definition.Name} was applied and verified on all supported connected adapters.",
            new TweakDetectionResult(TweakStateKind.Applied, _appliedLabel, "The requested standardized NDIS property passed per-adapter read-back verification.", false, true)));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_restoreFile))
            return Task.FromResult(TweakOperationResult.Failed("No previous per-adapter state was recorded."));

        List<AdapterState> previous;
        try { previous = ReadPreviousStrict(); }
        catch (Exception ex) { return Task.FromResult(TweakOperationResult.Failed($"The saved NIC restore state is invalid, so Sabby made no adapter change. {ex.Message}")); }

        var failure = Restore(previous);
        if (!string.IsNullOrWhiteSpace(failure))
            return Task.FromResult(TweakOperationResult.Failed(failure));

        TryDelete();
        return Task.FromResult(TweakOperationResult.Completed(
            $"{Definition.Name} was restored to the exact saved per-adapter state.",
            new TweakDetectionResult(TweakStateKind.Custom, "Restored", "Previous advanced-property values were restored.", true, false)));
    }

    private List<AdapterState> ReadStates(bool forceRefresh = false) => GetSnapshot(forceRefresh)
        .Where(item => item.RegistryKeyword.Equals(_registryKeyword, StringComparison.OrdinalIgnoreCase))
        .Select(item => new AdapterState(item.Name, item.RegistryValue))
        .ToList();

    private static IReadOnlyList<SnapshotItem> GetSnapshot(bool forceRefresh)
    {
        lock (SnapshotLock)
        {
            if (!forceRefresh && DateTime.UtcNow - _snapshotUtc <= SnapshotLifetime)
                return _snapshot;

            const string script = "$items=@(Get-NetAdapter -IncludeHidden | Where-Object {$_.Status -eq 'Up' -and $_.HardwareInterface -eq $true} | ForEach-Object { $a=$_; Get-NetAdapterAdvancedProperty -Name $a.Name -AllProperties -ErrorAction SilentlyContinue | Where-Object {$_.RegistryKeyword} | ForEach-Object { [pscustomobject]@{Name=$a.Name;RegistryKeyword=[string]$_.RegistryKeyword;RegistryValue=@($_.RegistryValue | ForEach-Object {[string]$_})} } }); ConvertTo-Json -InputObject $items -Compress -Depth 4";
            var result = PowerShellNetworkAccess.Run(script, 12000);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            {
                _snapshot = Array.Empty<SnapshotItem>();
                _snapshotUtc = DateTime.UtcNow;
                return _snapshot;
            }

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                IReadOnlyList<SnapshotItem> parsed;
                if (result.Output.TrimStart().StartsWith("[", StringComparison.Ordinal))
                    parsed = JsonSerializer.Deserialize<List<SnapshotItem>>(result.Output, options) ?? new List<SnapshotItem>();
                else
                {
                    var one = JsonSerializer.Deserialize<SnapshotItem>(result.Output, options);
                    parsed = one is null ? Array.Empty<SnapshotItem>() : new[] { one };
                }

                _snapshot = parsed;
                _snapshotUtc = DateTime.UtcNow;
                return _snapshot;
            }
            catch
            {
                _snapshot = Array.Empty<SnapshotItem>();
                _snapshotUtc = DateTime.UtcNow;
                return _snapshot;
            }
        }
    }

    private static void InvalidateSnapshot()
    {
        lock (SnapshotLock)
            _snapshotUtc = DateTime.MinValue;
    }

    private string? Restore(IEnumerable<AdapterState> states)
    {
        var restoreStates = states.ToList();
        foreach (var state in restoreStates)
        {
            var name = PowerShellNetworkAccess.Escape(state.Name);
            var keyword = PowerShellNetworkAccess.Escape(_registryKeyword);
            var values = string.Join(",", state.RegistryValue.Select(v => $"'{PowerShellNetworkAccess.Escape(v)}'"));
            var result = PowerShellNetworkAccess.Run($"Set-NetAdapterAdvancedProperty -Name '{name}' -RegistryKeyword '{keyword}' -RegistryValue @({values}) -NoRestart -Confirm:$false");
            if (!result.Success)
                return $"Could not restore {Definition.Name} on {state.Name}. {result.Error}".Trim();
        }

        InvalidateSnapshot();
        foreach (var state in restoreStates)
        {
            var current = ReadAdapterProperty(state.Name);
            if (current is null || !RegistryValuesEqual(current, state.RegistryValue))
                return $"{Definition.Name} restore did not pass exact read-back verification on {state.Name}; the saved restore state was retained.";
        }
        return null;
    }

    private void SavePrevious(List<AdapterState> states)
    {
        var saved = File.Exists(_restoreFile) ? ReadPreviousStrict() : new List<AdapterState>();
        foreach (var state in states)
        {
            if (saved.Any(existing => existing.Name.Equals(state.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            saved.Add(state);
        }

        if (saved.Count == 0)
            throw new InvalidOperationException("No adapter state was available to save.");
        File.WriteAllText(_restoreFile, JsonSerializer.Serialize(saved));
    }

    private List<AdapterState> ReadPreviousStrict()
    {
        List<AdapterState> saved;
        try
        {
            saved = JsonSerializer.Deserialize<List<AdapterState>>(File.ReadAllText(_restoreFile))
                    ?? throw new InvalidDataException("Saved NIC restore state is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Saved NIC restore state is corrupt.", ex);
        }

        if (saved.Count == 0 || saved.Any(state => string.IsNullOrWhiteSpace(state.Name) || state.RegistryValue is null || state.RegistryValue.Length == 0))
            throw new InvalidDataException("Saved NIC restore state contains an invalid adapter record.");
        if (saved.GroupBy(state => state.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("Saved NIC restore state contains duplicate adapter records.");
        return saved;
    }

    private string[]? ReadAdapterProperty(string adapterName)
    {
        var name = PowerShellNetworkAccess.Escape(adapterName);
        var keyword = PowerShellNetworkAccess.Escape(_registryKeyword);
        var script = $"$p=Get-NetAdapterAdvancedProperty -Name '{name}' -RegistryKeyword '{keyword}' -AllProperties -ErrorAction Stop | Select-Object -First 1; if($null -eq $p){{throw 'Advanced property not found'}}; $values=@($p.RegistryValue | ForEach-Object {{[string]$_}}); ConvertTo-Json -InputObject $values -Compress";
        var result = PowerShellNetworkAccess.Run(script, 8000);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return null;

        try
        {
            using var document = JsonDocument.Parse(result.Output);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray().Select(element => element.ToString()).ToArray(),
                JsonValueKind.String => new[] { document.RootElement.GetString() ?? string.Empty },
                _ => null
            };
        }
        catch { return null; }
    }

    private static bool RegistryValuesEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == right.Count && left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);

    private void TryDelete() { try { File.Delete(_restoreFile); } catch { } }
}
