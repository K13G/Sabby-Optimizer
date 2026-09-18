using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Presets;
using PCTweaker.Core.Tweaks;
using PCTweaker.Models.Presets;

namespace PCTweaker.ViewModels;

public sealed class PresetsViewModel : ViewModelBase
{
    private readonly IPresetService _presetService;
    private readonly ITweakEngine _tweakEngine;
    private string _newPresetName = string.Empty;
    private string _newPresetDescription = string.Empty;
    private string _searchText = string.Empty;
    private string _statusMessage = "Preset library ready.";
    private bool _isBusy;

    public ObservableCollection<PresetCardViewModel> Presets { get; } = new();

    public string NewPresetName
    {
        get => _newPresetName;
        set => SetProperty(ref _newPresetName, value ?? string.Empty);
    }

    public string NewPresetDescription
    {
        get => _newPresetDescription;
        set => SetProperty(ref _newPresetDescription, value ?? string.Empty);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty))
                return;
            OnPropertyChanged(nameof(FilteredPresets));
            OnPropertyChanged(nameof(PresetCountText));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public IReadOnlyList<PresetCardViewModel> FilteredPresets
    {
        get
        {
            var search = SearchText.Trim();
            IEnumerable<PresetCardViewModel> query = Presets;
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(card =>
                    card.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    card.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    card.Model.Entries.Any(entry => entry.TweakName.Contains(search, StringComparison.OrdinalIgnoreCase)));
            }

            return query
                .OrderByDescending(card => card.Model.ModifiedAtUtc)
                .ThenBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public string PresetCountText =>
        $"{FilteredPresets.Count} preset{(FilteredPresets.Count == 1 ? string.Empty : "s")}";

    public ICommand CreatePresetCommand { get; }
    public ICommand CaptureCurrentCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand RefreshCommand { get; }

    public PresetsViewModel(IPresetService presetService, ITweakEngine tweakEngine)
    {
        _presetService = presetService;
        _tweakEngine = tweakEngine;

        CreatePresetCommand = new AsyncRelayCommand(CreatePresetAsync, () => !IsBusy);
        CaptureCurrentCommand = new AsyncRelayCommand(CaptureCurrentAsync, () => !IsBusy);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
    }

    public Task InitializeAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var presets = await _presetService.GetAllAsync();
            Presets.Clear();
            foreach (var preset in presets)
                Presets.Add(CreateCard(preset));

            NotifyListChanged();
            StatusMessage = Presets.Count == 0
                ? "No presets yet. Create one, capture supported current states, or import a Sabby preset."
                : $"Loaded {Presets.Count} preset{(Presets.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load presets: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreatePresetAsync()
    {
        IsBusy = true;
        try
        {
            var preset = await _presetService.CreateAsync(NewPresetName, NewPresetDescription);
            var card = CreateCard(preset);
            Presets.Insert(0, card);
            NewPresetName = string.Empty;
            NewPresetDescription = string.Empty;
            NotifyListChanged();
            StatusMessage = $"Created '{preset.Name}'. Use Edit to rename it or choose desired tweak states.";
            card.EditCommand.Execute(null);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not create preset: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CaptureCurrentAsync()
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Detecting supported tweak states...";
            var preset = await _presetService.CaptureCurrentAsync(NewPresetName, NewPresetDescription);
            Presets.Insert(0, CreateCard(preset));
            NewPresetName = string.Empty;
            NewPresetDescription = string.Empty;
            NotifyListChanged();
            StatusMessage = preset.Entries.Count == 0
                ? $"Captured '{preset.Name}', but no production tweak handlers currently expose a restorable Applied/Not applied state. The preset is still saved and editable."
                : $"Captured '{preset.Name}' with {preset.Entries.Count} verified tweak states.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Capture failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Sabby Optimizer preset",
            Filter = "Sabby preset (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        try
        {
            var preset = await _presetService.ImportAsync(dialog.FileName);
            Presets.Insert(0, CreateCard(preset));
            NotifyListChanged();
            StatusMessage = $"Imported '{preset.Name}' safely. Imported files contain desired tweak states, not executable scripts.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private PresetCardViewModel CreateCard(PresetDefinition preset) =>
        new(
            preset,
            _tweakEngine.Definitions,
            SaveCardAsync,
            DuplicateCardAsync,
            DeleteCardAsync,
            ExportCardAsync);

    private async Task SaveCardAsync(PresetCardViewModel card)
    {
        try
        {
            await _presetService.SaveAsync(card.Model);
            card.RefreshFromModel("Saved.");
            NotifyListChanged();
            StatusMessage = $"Saved '{card.Model.Name}'.";
        }
        catch (Exception ex)
        {
            card.RefreshFromModel($"Save failed: {ex.Message}");
            StatusMessage = $"Could not save preset: {ex.Message}";
        }
    }

    private async Task DuplicateCardAsync(PresetCardViewModel card)
    {
        try
        {
            var duplicate = await _presetService.DuplicateAsync(card.Model);
            Presets.Insert(0, CreateCard(duplicate));
            NotifyListChanged();
            StatusMessage = $"Duplicated '{card.Model.Name}' as '{duplicate.Name}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Duplicate failed: {ex.Message}";
        }
    }

    private async Task DeleteCardAsync(PresetCardViewModel card)
    {
        try
        {
            await _presetService.DeleteAsync(card.Model.Id);
            Presets.Remove(card);
            NotifyListChanged();
            StatusMessage = $"Deleted '{card.Model.Name}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete failed: {ex.Message}";
        }
    }

    private async Task ExportCardAsync(PresetCardViewModel card)
    {
        var safeName = string.Join("_", card.Model.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "Sabby-Preset";

        var dialog = new SaveFileDialog
        {
            Title = "Export Sabby Optimizer preset",
            Filter = "Sabby preset (*.json)|*.json",
            FileName = $"{safeName}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _presetService.ExportAsync(card.Model, dialog.FileName);
            StatusMessage = $"Exported '{card.Model.Name}' to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private void NotifyListChanged()
    {
        OnPropertyChanged(nameof(FilteredPresets));
        OnPropertyChanged(nameof(PresetCountText));
    }
}
