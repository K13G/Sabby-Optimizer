using PCTweaker.Models.Presets;

namespace PCTweaker.Core.Presets;

public interface IPresetService
{
    Task<IReadOnlyList<PresetDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PresetDefinition> CreateAsync(string? name, string? description = null, CancellationToken cancellationToken = default);
    Task<PresetDefinition> CaptureCurrentAsync(string? name, string? description = null, CancellationToken cancellationToken = default);
    Task SaveAsync(PresetDefinition preset, CancellationToken cancellationToken = default);
    Task<PresetDefinition> DuplicateAsync(PresetDefinition source, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PresetDefinition> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task ExportAsync(PresetDefinition preset, string destinationPath, CancellationToken cancellationToken = default);
}
