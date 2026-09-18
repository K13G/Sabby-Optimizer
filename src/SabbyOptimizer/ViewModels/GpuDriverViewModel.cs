using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;

namespace PCTweaker.ViewModels;

public sealed class GpuDriverViewModel : ViewModelBase
{
    private readonly GpuDriverIntegrationService _service;
    private string _vendor="Detecting…",_gpuName="Detecting…",_driverInfo="Detecting…",_integration="Detecting…",_summary="",_status="Ready";
    private bool _vendorAppAvailable,_isBusy; private double _progress;
    public string Vendor{get=>_vendor;private set=>SetProperty(ref _vendor,value);} public string GpuName{get=>_gpuName;private set=>SetProperty(ref _gpuName,value);} public string DriverInfo{get=>_driverInfo;private set=>SetProperty(ref _driverInfo,value);} public string Integration{get=>_integration;private set=>SetProperty(ref _integration,value);} public string Summary{get=>_summary;private set=>SetProperty(ref _summary,value);} public string Status{get=>_status;private set=>SetProperty(ref _status,value);} public bool VendorAppAvailable{get=>_vendorAppAvailable;private set=>SetProperty(ref _vendorAppAvailable,value);} public double Progress{get=>_progress;private set=>SetProperty(ref _progress,value);} public bool IsBusy{get=>_isBusy;private set{if(SetProperty(ref _isBusy,value)){ApplyCommand.RaiseCanExecuteChanged();RestoreCommand.RaiseCanExecuteChanged();RefreshCommand.RaiseCanExecuteChanged();}}}
    public AsyncRelayCommand ApplyCommand{get;} public AsyncRelayCommand RestoreCommand{get;} public AsyncRelayCommand RefreshCommand{get;} public RelayCommand OpenVendorAppCommand{get;}
    public GpuDriverViewModel(GpuDriverIntegrationService service){_service=service;ApplyCommand=new AsyncRelayCommand(ApplyAsync,()=>!IsBusy);RestoreCommand=new AsyncRelayCommand(RestoreAsync,()=>!IsBusy);RefreshCommand=new AsyncRelayCommand(RefreshAsync,()=>!IsBusy);OpenVendorAppCommand=new RelayCommand(Open);_=RefreshAsync();}
    private async Task RefreshAsync(){IsBusy=true;Progress=25;try{var x=await Task.Run(async()=>await _service.DetectAsync());Vendor=x.Vendor;GpuName=x.GpuName;DriverInfo=x.DriverInfo;Integration=x.IntegrationStatus;Summary=x.QualitySafeSummary;VendorAppAvailable=x.VendorAppAvailable;Progress=100;Status=x.VendorAppAvailable?$"{x.VendorAppName} detected.":"Vendor control software was not found; Windows graphics settings remain available.";}finally{IsBusy=false;}}
    private async Task ApplyAsync(){IsBusy=true;Progress=0;Status="Applying supported no-quality-loss game GPU preferences and verifying…";try{var p=new Progress<double>(v=>Progress=v);var r=await Task.Run(async()=>await _service.ApplyQualitySafeAsync(p));Status=(r.Success?"✓ ":"⚠ ")+r.Message;}finally{IsBusy=false;}}
    private async Task RestoreAsync(){IsBusy=true;Progress=20;try{var r=await Task.Run(async()=>await _service.RestoreAsync());Progress=100;Status=(r.Success?"✓ ":"⚠ ")+r.Message;}finally{IsBusy=false;}}
    private void Open(){var r=_service.OpenVendorApp();Status=(r.Success?"✓ ":"⚠ ")+r.Message;}
}
