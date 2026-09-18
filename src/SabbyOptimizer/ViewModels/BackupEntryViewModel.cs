using System.Windows.Input;
using PCTweaker.Core.Mvvm;
using PCTweaker.Models.Backups;

namespace PCTweaker.ViewModels;

public sealed class BackupEntryViewModel : ViewModelBase
{
    private readonly Func<Task<BackupRestoreSummary>> _restore;
    private string _status = string.Empty;

    public string TweakId { get; }
    public string Name { get; }
    public string CapturedState { get; }
    public string Detail { get; }
    public bool CanRestore { get; }
    public string RestoreAvailability => CanRestore ? "Restorable" : "Recorded only";

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public ICommand RestoreCommand { get; }

    public BackupEntryViewModel(BackupEntrySnapshot entry, Func<Task<BackupRestoreSummary>> restore)
    {
        TweakId = entry.TweakId;
        Name = entry.TweakName;
        CapturedState = entry.DisplayText;
        Detail = entry.Detail;
        CanRestore = entry.Restorable;
        _restore = restore;
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => CanRestore);
    }

    private async Task RestoreAsync()
    {
        var summary = await _restore();
        Status = summary.Message;
    }
}
