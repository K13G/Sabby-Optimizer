using PCTweaker.Models.GameDetection;

namespace PCTweaker.Core.GameDetection;

public interface IGameDetectionService : IAsyncDisposable
{
    event EventHandler<GameRuntimeState>? StateChanged;
    bool IsMonitoring { get; }
    void Start();
    Task StopAsync(CancellationToken cancellationToken = default);
}
