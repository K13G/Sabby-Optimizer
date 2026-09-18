using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks;

internal sealed class InMemorySelfTestTweakHandler : ITweakHandler
{
    private bool _enabled;

    public TweakDefinition Definition { get; } = new(
        "internal.engine-self-test",
        "Engine self-test",
        "Internal-only reversible test used to validate the tweak pipeline.",
        TweakCategory.Internal,
        TweakSafetyLevel.Safe,
        "This hidden tweak changes only an in-memory boolean. It exists so Sabby Optimizer can validate detection, apply, verification, and undo without touching Windows.",
        "An in-memory boolean owned by the running Sabby Optimizer process.",
        "Undo flips the same in-memory boolean back to its original state.",
        false,
        false,
        true,
        false);

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_enabled
            ? new TweakDetectionResult(TweakStateKind.Applied, "Applied", "Internal self-test state is enabled.", false, true)
            : new TweakDetectionResult(TweakStateKind.NotApplied, "Not applied", "Internal self-test state is disabled.", true, false));
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _enabled = true;
        var state = new TweakDetectionResult(TweakStateKind.Applied, "Applied", "Internal self-test state is enabled.", false, true);
        return Task.FromResult(TweakOperationResult.Completed("Internal self-test apply completed.", state));
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _enabled = false;
        var state = new TweakDetectionResult(TweakStateKind.NotApplied, "Not applied", "Internal self-test state is disabled.", true, false);
        return Task.FromResult(TweakOperationResult.Completed("Internal self-test undo completed.", state));
    }
}
