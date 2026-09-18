namespace PCTweaker.Core.Services;

public interface IAppStartupService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}
