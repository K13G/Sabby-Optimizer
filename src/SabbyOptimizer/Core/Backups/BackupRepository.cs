using System.IO;
using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Models.Backups;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Backups;

public sealed class BackupRepository : IBackupRepository
{
    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public BackupRepository(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        Directory.CreateDirectory(_paths.BackupsDirectory);
        Directory.CreateDirectory(_paths.BackupSnapshotsDirectory);
    }

    public async Task CaptureIfMissingAsync(
        TweakDefinition definition,
        TweakDetectionResult state,
        CancellationToken cancellationToken = default)
    {
        if (!definition.IsVisible || !IsStateRestorable(definition, state.State))
            return;

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadOriginalDocumentUnsafeAsync(cancellationToken).ConfigureAwait(false);
            if (document.Entries.ContainsKey(definition.Id))
                return;

            document.Entries[definition.Id] = CreateEntry(definition, state);
            await WriteJsonAtomicUnsafeAsync(_paths.OriginalStateFile, document, cancellationToken).ConfigureAwait(false);
            _logger.Info($"Captured original tweak state: {definition.Id} -> {state.State}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to capture original tweak state: {definition.Id}.", ex);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task<IReadOnlyList<BackupEntrySnapshot>> GetOriginalEntriesAsync(CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadOriginalDocumentUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return document.Entries.Values
                .OrderBy(entry => entry.TweakName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task SaveSnapshotAsync(BackupSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_paths.BackupSnapshotsDirectory);
            var path = GetSnapshotPath(snapshot.Id);
            await WriteJsonAtomicUnsafeAsync(path, snapshot, cancellationToken).ConfigureAwait(false);
            _logger.Info($"Backup snapshot saved: {snapshot.Id} ({snapshot.Name}), {snapshot.Entries.Count} entries.");
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task<IReadOnlyList<BackupSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_paths.BackupSnapshotsDirectory))
                return Array.Empty<BackupSnapshot>();

            var snapshots = new List<BackupSnapshot>();
            foreach (var file in Directory.EnumerateFiles(_paths.BackupSnapshotsDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var snapshot = await ReadJsonAsync<BackupSnapshot>(file, cancellationToken).ConfigureAwait(false);
                    if (snapshot is null)
                        continue;

                    snapshot.Entries ??= new List<BackupEntrySnapshot>();
                    snapshots.Add(snapshot);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Skipped unreadable backup snapshot '{Path.GetFileName(file)}': {ex.Message}");
                }
            }

            return snapshots
                .OrderByDescending(snapshot => snapshot.CreatedAtUtc)
                .ToArray();
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task<BackupSnapshot?> GetSnapshotAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetSnapshotPath(id);
            if (!File.Exists(path))
                return null;

            var snapshot = await ReadJsonAsync<BackupSnapshot>(path, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
                snapshot.Entries ??= new List<BackupEntrySnapshot>();
            return snapshot;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task DeleteSnapshotAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetSnapshotPath(id);
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.Info($"Backup snapshot deleted: {id}.");
            }
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private async Task<OriginalStateDocument> LoadOriginalDocumentUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.OriginalStateFile))
            return new OriginalStateDocument();

        try
        {
            var document = await ReadJsonAsync<OriginalStateDocument>(_paths.OriginalStateFile, cancellationToken).ConfigureAwait(false)
                ?? new OriginalStateDocument();

            document.Entries = new Dictionary<string, BackupEntrySnapshot>(
                document.Entries ?? new Dictionary<string, BackupEntrySnapshot>(),
                StringComparer.OrdinalIgnoreCase);
            return document;
        }
        catch (Exception ex)
        {
            var corruptName = Path.Combine(
                _paths.BackupsDirectory,
                $"original-state.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            try
            {
                File.Move(_paths.OriginalStateFile, corruptName, true);
            }
            catch
            {
                // Best-effort preservation of a damaged file.
            }

            _logger.Error("Original-state backup file was invalid. A new document will be created.", ex);
            return new OriginalStateDocument();
        }
    }

    private static BackupEntrySnapshot CreateEntry(TweakDefinition definition, TweakDetectionResult state) =>
        new()
        {
            TweakId = definition.Id,
            TweakName = definition.Name,
            State = state.State,
            DisplayText = state.DisplayText,
            Detail = state.Detail,
            Restorable = IsStateRestorable(definition, state.State),
            CapturedAtUtc = DateTime.UtcNow
        };

    internal static bool IsStateRestorable(TweakDefinition definition, TweakStateKind state) =>
        state switch
        {
            TweakStateKind.Applied => true,
            TweakStateKind.NotApplied => definition.IsReversible,
            _ => false
        };

    private string GetSnapshotPath(Guid id) =>
        Path.Combine(_paths.BackupSnapshotsDirectory, $"{id:N}.json");

    private async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, useAsync: true);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteJsonAtomicUnsafeAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 32 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, path, true);
    }
}
