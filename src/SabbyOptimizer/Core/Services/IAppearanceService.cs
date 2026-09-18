using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public interface IAppearanceService
{
    VisualStyle ActiveStyle { get; }
    double Intensity { get; }
    double AnimationSpeed { get; }
    event Action<double>? AnimationSpeedChanged;
    event Action? AppearanceChanged;
    void Apply(VisualStyle style, double intensity = 100, double animationSpeed = 100);
}
