using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks;

public sealed class UnavailableTweakHandler : ITweakHandler
{
    private readonly string _reason;

    public TweakDefinition Definition { get; }

    public UnavailableTweakHandler(TweakDefinition definition, string reason)
    {
        Definition = definition;
        _reason = reason;
    }

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TweakDetectionResult.Unavailable(_reason));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TweakOperationResult.Failed(_reason));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TweakOperationResult.Failed(_reason));
    }
}
