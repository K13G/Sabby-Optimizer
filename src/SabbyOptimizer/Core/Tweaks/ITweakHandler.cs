using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks;

public interface ITweakHandler
{
    TweakDefinition Definition { get; }

    Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default);

    Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default);

    Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default);
}
