using PCTweaker.Core.Services;
using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private HardwareInfo _hardware;
    private string _hardwareStatus = "Detecting hardware quietly in the background…";

    public string AppDataPath { get; }
    public string LogsPath { get; }

    public HardwareInfo Hardware
    {
        get => _hardware;
        private set => SetProperty(ref _hardware, value);
    }

    public string HardwareStatus
    {
        get => _hardwareStatus;
        private set => SetProperty(ref _hardwareStatus, value);
    }

    public DashboardViewModel(IAppPaths paths, HardwareInfo initialHardware)
    {
        AppDataPath = paths.UserDataDirectory;
        LogsPath = paths.LogsDirectory;
        _hardware = initialHardware;
    }

    public DashboardViewModel(IAppPaths paths, IHardwareInfoService hardwareInfoService)
        : this(paths, hardwareInfoService.GetHardwareInfo())
    {
        HardwareStatus = "Hardware detection complete.";
    }

    public void UpdateHardware(HardwareInfo hardware)
    {
        Hardware = hardware;
        HardwareStatus = "Hardware detection complete.";
    }
}
