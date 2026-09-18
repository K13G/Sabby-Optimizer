using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks;

public interface ITweakEngine
{
    IReadOnlyList<TweakDefinition> Definitions { get; }

    TweakExplanation Explain(string tweakId);

    Task<TweakDetectionResult> DetectAsync(string tweakId, CancellationToken cancellationToken = default);

    Task<TweakCompatibilityResult> CheckCompatibilityAsync(string tweakId, bool applying = true, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, TweakDetectionResult>> DetectAllAsync(CancellationToken cancellationToken = default);

    Task<TweakOperationResult> ApplyAsync(string tweakId, CancellationToken cancellationToken = default);

    Task<TweakOperationResult> UndoAsync(string tweakId, CancellationToken cancellationToken = default);
}
