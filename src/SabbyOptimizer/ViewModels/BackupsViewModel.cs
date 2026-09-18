using System.Collections.ObjectModel;
using System.Windows.Input;
using PCTweaker.Core.Backups;
using PCTweaker.Core.Mvvm;
using PCTweaker.Models.Backups;

namespace PCTweaker.ViewModels;

public sealed class BackupsViewModel : ViewModelBase
{
    private readonly IBackupService _backupService;
    private string _statusMessage = "Restore protection is ready.";
    private int _originalProtectedCount;
    private bool _isBusy;

    public ObservableCollection<BackupSnapshotViewModel> Snapshots { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int OriginalProtectedCount
    {
        get => _originalProtectedCount;
        private set
        {
            if (SetProperty(ref _originalProtectedCount, value))
                OnPropertyChanged(nameof(OriginalProtectedCountText));
        }
    }

    public string OriginalProtectedCountText =>
        $"{OriginalProtectedCount} original state{(OriginalProtectedCount == 1 ? string.Empty : "s")} recorded";

    public string SnapshotCountText =>
        $"{Snapshots.Count} snapshot{(Snapshots.Count == 1 ? string.Empty : "s")}";

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public ICommand CreateSnapshotCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand RestoreOriginalsCommand { get; }

    public BackupsViewModel(IBackupService backupService)
    {
        _backupService = backupService;
        CreateSnapshotCommand = new AsyncRelayCommand(CreateSnapshotAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        RestoreOriginalsCommand = new AsyncRelayCommand(RestoreOriginalsAsync, () => !IsBusy && OriginalProtectedCount > 0);
    }

    public async Task InitializeAsync()
    {
        await Task.Yield();
        await RefreshAsync();
    }

    private async Task CreateSnapshotAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            StatusMessage = "Detecting current tweak states and creating snapshot...";
            await Task.Yield();
            var snapshot = await Task.Run(() => _backupService.CreateSnapshotAsync());
            StatusMessage = $"Created '{snapshot.Name}' with {snapshot.Entries.Count} tracked entries.";
            await ReloadCollectionsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Snapshot creation failed safely: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            StatusMessage = "Refreshing restore history...";
            await ReloadCollectionsAsync();
            StatusMessage = "Restore history is up to date.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Restore history could not be refreshed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadCollectionsAsync()
    {
        var originalsTask = _backupService.GetOriginalEntriesAsync();
        var snapshotsTask = _backupService.GetSnapshotsAsync();
        await Task.WhenAll(originalsTask, snapshotsTask);

        var originals = await originalsTask;
        OriginalProtectedCount = originals.Count;

        var snapshots = await snapshotsTask;
        Snapshots.Clear();
        foreach (var snapshot in snapshots)
        {
            Snapshots.Add(new BackupSnapshotViewModel(
                snapshot,
                RestoreSnapshotAsync,
                RestoreSnapshotEntryAsync,
                DeleteSnapshotAsync));
        }

        OnPropertyChanged(nameof(SnapshotCountText));
    }

    private async Task<BackupRestoreSummary> RestoreSnapshotAsync(Guid snapshotId)
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Restoring snapshot through the safe tweak engine...";
            var summary = await _backupService.RestoreSnapshotAsync(snapshotId);
            StatusMessage = summary.Message + (summary.RequiresRestart ? " A restart is required for at least one restored tweak." : string.Empty);
            return summary;
        }
        catch (Exception ex)
        {
            var summary = new BackupRestoreSummary(0, 0, 0, 1, false, $"Restore failed safely: {ex.Message}");
            StatusMessage = summary.Message;
            return summary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<BackupRestoreSummary> RestoreSnapshotEntryAsync(Guid snapshotId, string tweakId)
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Restoring one tracked tweak...";
            var summary = await _backupService.RestoreSnapshotEntryAsync(snapshotId, tweakId);
            StatusMessage = summary.Message + (summary.RequiresRestart ? " A restart is required." : string.Empty);
            return summary;
        }
        catch (Exception ex)
        {
            var summary = new BackupRestoreSummary(0, 0, 0, 1, false, $"Restore failed safely: {ex.Message}");
            StatusMessage = summary.Message;
            return summary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreOriginalsAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            StatusMessage = "Restoring all recorded original tweak states...";
            var summary = await _backupService.RestoreOriginalsAsync();
            StatusMessage = summary.Message + (summary.RequiresRestart ? " A restart is required for at least one restored tweak." : string.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Original-state restore failed safely: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteSnapshotAsync(Guid snapshotId)
    {
        try
        {
            await _backupService.DeleteSnapshotAsync(snapshotId);
            StatusMessage = "Snapshot deleted.";
            await ReloadCollectionsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Snapshot could not be deleted: {ex.Message}";
        }
    }
}
