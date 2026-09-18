using System.Reflection;
using PCTweaker.Core.Services;
using PCTweaker.Core.Tweaks;
using PCTweaker.Models.Backups;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Backups;

public sealed class BackupService : IBackupService
{
    private readonly ITweakEngine _engine;
    private readonly IBackupRepository _repository;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _restoreGate = new(1, 1);

    public BackupService(ITweakEngine engine, IBackupRepository repository, IAppLogger logger)
    {
        _engine = engine;
        _repository = repository;
        _logger = logger;
    }

    public async Task EnsureOriginalBaselineAsync(CancellationToken cancellationToken = default)
    {
        // Do not re-detect every tweak on every launch. Once an original state has already been
        // captured, it is intentionally immutable and does not need another PowerShell/registry
        // probe. Only newly-added tweak definitions are detected here. This removes a major source
        // of startup CPU/process bursts and the small UI stalls they caused.
        var existing = await _repository.GetOriginalEntriesAsync(cancellationToken).ConfigureAwait(false);
        var protectedIds = existing
            .Select(entry => entry.TweakId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in _engine.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (protectedIds.Contains(definition.Id))
                continue;

            var state = await _engine.DetectAsync(definition.Id, cancellationToken).ConfigureAwait(false);
            await _repository.CaptureIfMissingAsync(definition, state, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<BackupSnapshot> CreateSnapshotAsync(
        string? name = null,
        string kind = "Manual",
        CancellationToken cancellationToken = default)
    {
        var states = await _engine.DetectAllAsync(cancellationToken).ConfigureAwait(false);
        var timestamp = DateTime.Now;
        var snapshot = new BackupSnapshot
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(name) ? $"Snapshot {timestamp:MMM d, h:mm tt}" : name.Trim(),
            Kind = string.IsNullOrWhiteSpace(kind) ? "Manual" : kind.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty
        };

        foreach (var definition in _engine.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!states.TryGetValue(definition.Id, out var state))
                continue;

            snapshot.Entries.Add(new BackupEntrySnapshot
            {
                TweakId = definition.Id,
                TweakName = definition.Name,
                State = state.State,
                DisplayText = state.DisplayText,
                Detail = state.Detail,
                Restorable = BackupRepository.IsStateRestorable(definition, state.State),
                CapturedAtUtc = DateTime.UtcNow
            });
        }

        await _repository.SaveSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public Task<IReadOnlyList<BackupSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default) =>
        _repository.GetSnapshotsAsync(cancellationToken);

    public Task<IReadOnlyList<BackupEntrySnapshot>> GetOriginalEntriesAsync(CancellationToken cancellationToken = default) =>
        _repository.GetOriginalEntriesAsync(cancellationToken);

    public async Task<BackupRestoreSummary> RestoreSnapshotAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _repository.GetSnapshotAsync(snapshotId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return new BackupRestoreSummary(0, 0, 0, 1, false, "Snapshot was not found.");

        return await RestoreEntriesAsync(snapshot.Entries, $"snapshot '{snapshot.Name}'", cancellationToken).ConfigureAwait(false);
    }

    public async Task<BackupRestoreSummary> RestoreSnapshotEntryAsync(Guid snapshotId, string tweakId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _repository.GetSnapshotAsync(snapshotId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return new BackupRestoreSummary(0, 0, 0, 1, false, "Snapshot was not found.");

        var entry = snapshot.Entries.FirstOrDefault(item => item.TweakId.Equals(tweakId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return new BackupRestoreSummary(0, 0, 0, 1, false, "That tweak was not found in the snapshot.");

        return await RestoreEntriesAsync(new[] { entry }, entry.TweakName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BackupRestoreSummary> RestoreOriginalsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _repository.GetOriginalEntriesAsync(cancellationToken).ConfigureAwait(false);
        return await RestoreEntriesAsync(entries, "recorded original states", cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteSnapshotAsync(Guid snapshotId, CancellationToken cancellationToken = default) =>
        _repository.DeleteSnapshotAsync(snapshotId, cancellationToken);

    private async Task<BackupRestoreSummary> RestoreEntriesAsync(
        IEnumerable<BackupEntrySnapshot> entries,
        string scope,
        CancellationToken cancellationToken)
    {
        await _restoreGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var restored = 0;
            var alreadyMatched = 0;
            var skipped = 0;
            var failed = 0;
            var requiresRestart = false;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!entry.Restorable)
                {
                    skipped++;
                    continue;
                }

                TweakDetectionResult current;
                try
                {
                    current = await _engine.DetectAsync(entry.TweakId, cancellationToken).ConfigureAwait(false);
                }
                catch (KeyNotFoundException)
                {
                    skipped++;
                    continue;
                }

                if (current.State == entry.State)
                {
                    alreadyMatched++;
                    continue;
                }

                TweakOperationResult result;
                if (entry.State == TweakStateKind.Applied)
                {
                    result = await _engine.ApplyAsync(entry.TweakId, cancellationToken).ConfigureAwait(false);
                }
                else if (entry.State == TweakStateKind.NotApplied)
                {
                    result = await _engine.UndoAsync(entry.TweakId, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    skipped++;
                    continue;
                }

                if (result.Success)
                {
                    restored++;
                    requiresRestart |= result.RequiresRestart;
                }
                else
                {
                    failed++;
                }
            }

            var message = $"Restore finished for {scope}: {restored} restored, {alreadyMatched} already matched, {skipped} skipped, {failed} failed.";
            _logger.Info(message);
            return new BackupRestoreSummary(restored, alreadyMatched, skipped, failed, requiresRestart, message);
        }
        finally
        {
            _restoreGate.Release();
        }
    }
}
