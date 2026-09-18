using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public interface IThemeService
{
    ThemeMode ActiveTheme { get; }
    void Apply(ThemeMode mode);
}
