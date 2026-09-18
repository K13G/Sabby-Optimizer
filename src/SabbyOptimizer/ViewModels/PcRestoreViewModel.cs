using System.Collections.ObjectModel;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class PcRestoreViewModel : ViewModelBase
{
    private readonly ISystemRestoreService _restoreService;
    private string _newRestorePointName = "Sabby manual restore point";
    private string _status = "Loading restore points…";
    private bool _isBusy;

    public ObservableCollection<RestorePointInfo> RestorePoints { get; } = new();

    public string NewRestorePointName
    {
        get => _newRestorePointName;
        set => SetProperty(ref _newRestorePointName, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }
    public RelayCommand OpenSystemRestoreCommand { get; }

    public PcRestoreViewModel(ISystemRestoreService restoreService)
    {
        _restoreService = restoreService;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !_isBusy);
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(NewRestorePointName));
        OpenSystemRestoreCommand = new RelayCommand(_restoreService.OpenSystemRestore);
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _isBusy = true;
        RefreshCommand.RaiseCanExecuteChanged();
        CreateCommand.RaiseCanExecuteChanged();
        try
        {
            var points = await _restoreService.GetRestorePointsAsync();
            RestorePoints.Clear();
            foreach (var point in points.OrderByDescending(x => x.SequenceNumber).Take(30))
                RestorePoints.Add(point);
            Status = RestorePoints.Count == 0
                ? "No restore points were returned by Windows. System Protection may be disabled."
                : $"{RestorePoints.Count} recent restore point{(RestorePoints.Count == 1 ? string.Empty : "s")} loaded.";
        }
        catch (Exception ex)
        {
            Status = $"Could not load restore points: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
            RefreshCommand.RaiseCanExecuteChanged();
            CreateCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task CreateAsync()
    {
        _isBusy = true;
        RefreshCommand.RaiseCanExecuteChanged();
        CreateCommand.RaiseCanExecuteChanged();
        try
        {
            Status = "Creating restore point…";
            var result = await _restoreService.CreateRestorePointAsync(NewRestorePointName.Trim());
            Status = result.Success ? $"✓ {result.Message}" : $"✕ {result.Message}";
            if (result.Success) await RefreshAsync();
        }
        finally
        {
            _isBusy = false;
            RefreshCommand.RaiseCanExecuteChanged();
            CreateCommand.RaiseCanExecuteChanged();
        }
    }
}
