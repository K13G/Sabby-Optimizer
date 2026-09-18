using System.IO;
using System.Text.Json;
using PCTweaker.Core.Services;
using PCTweaker.Core.Tweaks;
using PCTweaker.Models.Presets;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Presets;

public sealed class PresetService : IPresetService
{
    private readonly IAppPaths _paths;
    private readonly ITweakEngine _engine;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public PresetService(IAppPaths paths, ITweakEngine engine, IAppLogger logger)
    {
        _paths = paths;
        _engine = engine;
        _logger = logger;
        Directory.CreateDirectory(_paths.PresetsDirectory);
    }

    public async Task<IReadOnlyList<PresetDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_paths.PresetsDirectory))
                return Array.Empty<PresetDefinition>();

            var presets = new List<PresetDefinition>();
            foreach (var path in Directory.EnumerateFiles(_paths.PresetsDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var preset = await ReadPresetAsync(path, cancellationToken).ConfigureAwait(false);
                    if (preset is not null)
                        presets.Add(NormalizePreset(preset));
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Skipped unreadable preset '{Path.GetFileName(path)}': {ex.Message}");
                }
            }

            return presets
                .OrderByDescending(item => item.ModifiedAtUtc)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task<PresetDefinition> CreateAsync(
        string? name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var preset = new PresetDefinition
        {
            Name = NormalizeName(name, "New Preset"),
            Description = (description ?? string.Empty).Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow
        };

        await SaveAsync(preset, cancellationToken).ConfigureAwait(false);
        return preset;
    }

    public async Task<PresetDefinition> CaptureCurrentAsync(
        string? name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var detected = await _engine.DetectAllAsync(cancellationToken).ConfigureAwait(false);
        var preset = new PresetDefinition
        {
            Name = NormalizeName(name, $"Captured setup {DateTime.Now:MMM d, h:mm tt}"),
            Description = string.IsNullOrWhiteSpace(description)
                ? "Captured from the supported tweak states Sabby Optimizer could verify on this PC."
                : description.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow
        };

        foreach (var definition in _engine.Definitions)
        {
            if (!detected.TryGetValue(definition.Id, out var state))
                continue;

            if (state.State is not (TweakStateKind.Applied or TweakStateKind.NotApplied))
                continue;

            if (state.State == TweakStateKind.NotApplied && !definition.IsReversible)
                continue;

            preset.Entries.Add(new PresetEntry
            {
                TweakId = definition.Id,
                TweakName = definition.Name,
                DesiredState = state.State
            });
        }

        await SaveAsync(preset, cancellationToken).ConfigureAwait(false);
        _logger.Info($"Preset captured: {preset.Name} ({preset.Entries.Count} supported states).");
        return preset;
    }

    public async Task SaveAsync(PresetDefinition preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var normalized = NormalizePreset(preset);
        normalized.ModifiedAtUtc = DateTime.UtcNow;

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_paths.PresetsDirectory);
            await WritePresetAtomicUnsafeAsync(GetPresetPath(normalized.Id), normalized, cancellationToken).ConfigureAwait(false);
            _logger.Info($"Preset saved: {normalized.Id} ({normalized.Name}), {normalized.Entries.Count} entries.");
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task<PresetDefinition> DuplicateAsync(PresetDefinition source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var duplicate = new PresetDefinition
        {
            Id = Guid.NewGuid(),
            Name = $"{NormalizeName(source.Name, "Preset")} - Copy",
            Description = source.Description,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
            Entries = source.Entries
                .Select(entry => new PresetEntry
                {
                    TweakId = entry.TweakId,
                    TweakName = entry.TweakName,
                    DesiredState = entry.DesiredState
                })
                .ToList()
        };

        await SaveAsync(duplicate, cancellationToken).ConfigureAwait(false);
        return duplicate;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetPresetPath(id);
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.Info($"Preset deleted: {id}.");
            }
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task<PresetDefinition> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("The preset file could not be found.", sourcePath);

        PresetDefinition imported;
        await using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, useAsync: true))
        {
            imported = await JsonSerializer.DeserializeAsync<PresetDefinition>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The selected file is not a valid Sabby Optimizer preset.");
        }

        imported = NormalizePreset(imported);
        imported.Id = Guid.NewGuid();
        imported.Name = NormalizeName(imported.Name, "Imported Preset");
        imported.CreatedAtUtc = DateTime.UtcNow;
        imported.ModifiedAtUtc = DateTime.UtcNow;

        await SaveAsync(imported, cancellationToken).ConfigureAwait(false);
        _logger.Info($"Preset imported from '{sourcePath}' as {imported.Id}.");
        return imported;
    }

    public async Task ExportAsync(PresetDefinition preset, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("An export destination is required.", nameof(destinationPath));

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var export = NormalizePreset(preset);
        await WritePresetAtomicUnsafeAsync(destinationPath, export, cancellationToken).ConfigureAwait(false);
        _logger.Info($"Preset exported: {preset.Id} -> '{destinationPath}'.");
    }

    private string GetPresetPath(Guid id) => Path.Combine(_paths.PresetsDirectory, $"{id:N}.json");

    private async Task<PresetDefinition?> ReadPresetAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, useAsync: true);
        return await JsonSerializer.DeserializeAsync<PresetDefinition>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task WritePresetAtomicUnsafeAsync(string path, PresetDefinition preset, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 32 * 1024, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, preset, _jsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, path, true);
    }

    private static PresetDefinition NormalizePreset(PresetDefinition preset)
    {
        preset.SchemaVersion = 1;
        if (preset.Id == Guid.Empty)
            preset.Id = Guid.NewGuid();
        preset.Name = NormalizeName(preset.Name, "Preset");
        preset.Description = (preset.Description ?? string.Empty).Trim();
        preset.Entries ??= new List<PresetEntry>();

        preset.Entries = preset.Entries
            .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.TweakId))
            .Where(entry => entry.DesiredState is TweakStateKind.Applied or TweakStateKind.NotApplied)
            .GroupBy(entry => entry.TweakId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .Select(entry => new PresetEntry
            {
                TweakId = entry.TweakId.Trim(),
                TweakName = string.IsNullOrWhiteSpace(entry.TweakName) ? entry.TweakId.Trim() : entry.TweakName.Trim(),
                DesiredState = entry.DesiredState
            })
            .OrderBy(entry => entry.TweakName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (preset.CreatedAtUtc == default)
            preset.CreatedAtUtc = DateTime.UtcNow;
        if (preset.ModifiedAtUtc == default)
            preset.ModifiedAtUtc = preset.CreatedAtUtc;

        return preset;
    }

    private static string NormalizeName(string? value, string fallback)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }
}
