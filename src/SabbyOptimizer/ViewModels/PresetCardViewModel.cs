using System.Collections.ObjectModel;
using System.Windows.Input;
using PCTweaker.Core.Mvvm;
using PCTweaker.Models.Presets;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.ViewModels;

public sealed class PresetCardViewModel : ViewModelBase
{
    private readonly IReadOnlyList<TweakDefinition> _definitions;
    private readonly Func<PresetCardViewModel, Task> _save;
    private readonly Func<PresetCardViewModel, Task> _duplicate;
    private readonly Func<PresetCardViewModel, Task> _delete;
    private readonly Func<PresetCardViewModel, Task> _export;

    private string _editName;
    private string _editDescription;
    private bool _isEditing;
    private bool _isDeleteConfirmOpen;
    private string _status = string.Empty;

    public PresetDefinition Model { get; }
    public ObservableCollection<PresetTweakOptionViewModel> TweakOptions { get; } = new();

    public string Name => Model.Name;
    public string Description => string.IsNullOrWhiteSpace(Model.Description) ? "No description" : Model.Description;
    public string EntryCountText => $"{Model.Entries.Count} tweak{(Model.Entries.Count == 1 ? string.Empty : "s")}";
    public string ModifiedText => $"Modified {Model.ModifiedAtUtc.ToLocalTime():MMM d, yyyy h:mm tt}";

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value ?? string.Empty);
    }

    public string EditDescription
    {
        get => _editDescription;
        set => SetProperty(ref _editDescription, value ?? string.Empty);
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    public bool IsDeleteConfirmOpen
    {
        get => _isDeleteConfirmOpen;
        private set => SetProperty(ref _isDeleteConfirmOpen, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public ICommand EditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand RequestDeleteCommand { get; }
    public ICommand CancelDeleteCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }

    public PresetCardViewModel(
        PresetDefinition model,
        IReadOnlyList<TweakDefinition> definitions,
        Func<PresetCardViewModel, Task> save,
        Func<PresetCardViewModel, Task> duplicate,
        Func<PresetCardViewModel, Task> delete,
        Func<PresetCardViewModel, Task> export)
    {
        Model = model;
        _definitions = definitions;
        _save = save;
        _duplicate = duplicate;
        _delete = delete;
        _export = export;
        _editName = model.Name;
        _editDescription = model.Description;

        EditCommand = new RelayCommand(BeginEdit);
        CancelEditCommand = new RelayCommand(CancelEdit);
        SaveEditCommand = new AsyncRelayCommand(SaveEditAsync);
        DuplicateCommand = new AsyncRelayCommand(() => _duplicate(this));
        ExportCommand = new AsyncRelayCommand(() => _export(this));
        RequestDeleteCommand = new RelayCommand(() => IsDeleteConfirmOpen = true);
        CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirmOpen = false);
        ConfirmDeleteCommand = new AsyncRelayCommand(() => _delete(this));

        RebuildOptions();
    }

    public void RefreshFromModel(string? status = null)
    {
        _editName = Model.Name;
        _editDescription = Model.Description;
        OnPropertyChanged(nameof(EditName));
        OnPropertyChanged(nameof(EditDescription));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(EntryCountText));
        OnPropertyChanged(nameof(ModifiedText));
        RebuildOptions();
        if (status is not null)
            Status = status;
    }

    private void BeginEdit()
    {
        EditName = Model.Name;
        EditDescription = Model.Description;
        RebuildOptions();
        Status = string.Empty;
        IsEditing = true;
    }

    private void CancelEdit()
    {
        EditName = Model.Name;
        EditDescription = Model.Description;
        RebuildOptions();
        Status = string.Empty;
        IsEditing = false;
    }

    private async Task SaveEditAsync()
    {
        Model.Name = string.IsNullOrWhiteSpace(EditName) ? "Preset" : EditName.Trim();
        Model.Description = (EditDescription ?? string.Empty).Trim();
        Model.Entries = TweakOptions
            .Select(option => option.ToEntry())
            .Where(entry => entry is not null)
            .Cast<PresetEntry>()
            .ToList();

        await _save(this);
        IsEditing = false;
    }

    private void RebuildOptions()
    {
        var existing = Model.Entries.ToDictionary(entry => entry.TweakId, StringComparer.OrdinalIgnoreCase);
        TweakOptions.Clear();
        foreach (var definition in _definitions.OrderBy(definition => definition.Category).ThenBy(definition => definition.Name))
        {
            existing.TryGetValue(definition.Id, out var entry);
            TweakOptions.Add(new PresetTweakOptionViewModel(definition, entry));
        }
    }
}
