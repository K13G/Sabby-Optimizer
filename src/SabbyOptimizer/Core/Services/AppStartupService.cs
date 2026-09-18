using Microsoft.Win32;

namespace PCTweaker.Core.Services;

public sealed class AppStartupService : IAppStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SabbyOptimizer";
    private readonly IAppLogger _logger;

    public AppStartupService(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not read Windows startup registration: {ex.Message}");
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
                ?? throw new InvalidOperationException("Windows startup registry key could not be opened.");

            if (!enabled)
            {
                key.DeleteValue(ValueName, false);
                _logger.Info("Disabled launch with Windows.");
                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
                throw new InvalidOperationException("Sabby Optimizer executable path could not be determined.");

            key.SetValue(ValueName, $"\"{executable}\"");
            _logger.Info("Enabled launch with Windows.");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to update launch-with-Windows setting.", ex);
        }
    }
}
