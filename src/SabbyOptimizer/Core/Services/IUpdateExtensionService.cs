using PCTweaker.Core.Tweaks;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public interface IUpdateExtensionService
{
    string ExtensionsDirectory { get; }
    string StagedUpdatesDirectory { get; }
    IReadOnlyList<ITweakHandler> LoadEnabledHandlers();
    Task<IReadOnlyList<InstalledTweakExtensionInfo>> GetExtensionsAsync(CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> ImportExtensionAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> SetExtensionEnabledAsync(string extensionId, bool enabled, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> RemoveExtensionAsync(string extensionId, CancellationToken cancellationToken = default);
    Task<string> CreateExampleExtensionAsync(CancellationToken cancellationToken = default);
    Task<SabbyReleaseInfo> CheckSabbyUpdateAsync(SabbyUpdateChannel channel, string? feedUrl, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message, string? FilePath)> DownloadAndStageUpdateAsync(SabbyReleaseInfo release, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
