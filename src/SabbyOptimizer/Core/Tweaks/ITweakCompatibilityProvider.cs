using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks;

public interface ITweakCompatibilityProvider
{
    Task<TweakCompatibilityResult> CheckCompatibilityAsync(bool applying, CancellationToken cancellationToken = default);
}
