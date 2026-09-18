using PCTweaker.Core.Services;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks;

public sealed record TweakEngineSelfCheckResult(bool Passed, string Message);

public static class TweakEngineSelfCheck
{
    public static async Task<TweakEngineSelfCheckResult> RunAsync(ITweakEngine engine, IAppLogger logger)
    {
        const string testId = "internal.engine-self-test";

        try
        {
            var initial = await engine.DetectAsync(testId);
            if (initial.State != TweakStateKind.NotApplied)
                return Fail("Initial self-test state was not clean.", logger);

            var apply = await engine.ApplyAsync(testId);
            if (!apply.Success || apply.VerifiedState?.State != TweakStateKind.Applied)
                return Fail("Apply/verification pipeline did not complete.", logger);

            var undo = await engine.UndoAsync(testId);
            if (!undo.Success || undo.VerifiedState?.State != TweakStateKind.NotApplied)
                return Fail("Undo/verification pipeline did not complete.", logger);

            logger.Info("Tweak engine self-check passed.");
            return new TweakEngineSelfCheckResult(true, "Engine self-check passed");
        }
        catch (Exception ex)
        {
            logger.Error("Tweak engine self-check failed.", ex);
            return new TweakEngineSelfCheckResult(false, "Engine self-check failed — check logs");
        }
    }

    private static TweakEngineSelfCheckResult Fail(string message, IAppLogger logger)
    {
        logger.Warning($"Tweak engine self-check failed: {message}");
        return new TweakEngineSelfCheckResult(false, message);
    }
}
