using System.Collections.ObjectModel;
using System.Windows.Input;
using PCTweaker.Core.Mvvm;
using PCTweaker.Models.Backups;

namespace PCTweaker.ViewModels;

public sealed class BackupSnapshotViewModel : ViewModelBase
{
    private readonly Func<Guid, Task<BackupRestoreSummary>> _restoreSnapshot;
    private readonly Func<Guid, string, Task<BackupRestoreSummary>> _restoreEntry;
    private readonly Func<Guid, Task> _deleteSnapshot;
    private bool _isExpanded;
    private string _status = string.Empty;

    public Guid Id { get; }
    public string Name { get; }
    public string Kind { get; }
    public DateTime CreatedAtLocal { get; }
    public string CreatedText => CreatedAtLocal.ToString("MMM d, yyyy • h:mm tt");
    public int EntryCount { get; }
    public int RestorableCount { get; }
    public string EntryCountText => $"{EntryCount} tracked • {RestorableCount} restorable";
    public ObservableCollection<BackupEntryViewModel> Entries { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
                OnPropertyChanged(nameof(ExpandButtonText));
        }
    }

    public string ExpandButtonText => IsExpanded ? "Hide details" : "View details";

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public ICommand ToggleExpandedCommand { get; }
    public ICommand RestoreSnapshotCommand { get; }
    public ICommand DeleteSnapshotCommand { get; }

    public BackupSnapshotViewModel(
        BackupSnapshot snapshot,
        Func<Guid, Task<BackupRestoreSummary>> restoreSnapshot,
        Func<Guid, string, Task<BackupRestoreSummary>> restoreEntry,
        Func<Guid, Task> deleteSnapshot)
    {
        Id = snapshot.Id;
        Name = snapshot.Name;
        Kind = snapshot.Kind;
        CreatedAtLocal = snapshot.CreatedAtUtc.ToLocalTime();
        EntryCount = snapshot.Entries.Count;
        RestorableCount = snapshot.Entries.Count(entry => entry.Restorable);
        _restoreSnapshot = restoreSnapshot;
        _restoreEntry = restoreEntry;
        _deleteSnapshot = deleteSnapshot;

        foreach (var entry in snapshot.Entries.OrderBy(entry => entry.TweakName, StringComparer.OrdinalIgnoreCase))
        {
            var capturedEntry = entry;
            Entries.Add(new BackupEntryViewModel(
                entry,
                () => _restoreEntry(Id, capturedEntry.TweakId)));
        }

        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        RestoreSnapshotCommand = new AsyncRelayCommand(RestoreAsync, () => RestorableCount > 0);
        DeleteSnapshotCommand = new AsyncRelayCommand(DeleteAsync);
    }

    private async Task RestoreAsync()
    {
        var summary = await _restoreSnapshot(Id);
        Status = summary.Message;
    }

    private async Task DeleteAsync()
    {
        await _deleteSnapshot(Id);
    }
}
