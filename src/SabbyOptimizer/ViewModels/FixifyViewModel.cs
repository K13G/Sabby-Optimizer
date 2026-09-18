using System.Collections.ObjectModel;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class FixifyToolItemViewModel : ViewModelBase
{
    private readonly FixifyService _service;
    private bool _isBusy;
    private string _status = "Ready";
    private string _details = "Run this tool only when the described problem applies.";
    private bool _lastRunSucceeded;
    private double _progressPercent;
    private bool _showProgress;
    private string _progressText = string.Empty;

    public FixifyToolDefinition Definition { get; }
    public string Name => Definition.Name;
    public string Category => Definition.Category;
    public string Description => Definition.Description;
    public string WhatItDoes => Definition.WhatItDoes;
    public bool RequiresRestart => Definition.RequiresRestart;
    public bool LongRunning => Definition.LongRunning;
    public string RunText => IsBusy ? "Running…" : (Definition.Id.EndsWith("-guide", StringComparison.OrdinalIgnoreCase) ? "Open" : "Run");

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(RunText));
            OnPropertyChanged(nameof(IsProgressVisible));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(ProgressDisplayText));
            RunCommand.RaiseCanExecuteChanged();
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string Details
    {
        get => _details;
        private set => SetProperty(ref _details, value);
    }

    public bool LastRunSucceeded
    {
        get => _lastRunSucceeded;
        private set => SetProperty(ref _lastRunSucceeded, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set
        {
            if (!SetProperty(ref _progressPercent, Math.Clamp(value, 0, 100))) return;
            ProgressText = _progressPercent <= 0 ? "Starting…" : $"{_progressPercent:0}%";
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(ProgressDisplayText));
        }
    }

    public bool ShowProgress
    {
        get => _showProgress;
        private set
        {
            if (!SetProperty(ref _showProgress, value)) return;
            OnPropertyChanged(nameof(IsProgressVisible));
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(ProgressDisplayText));
        }
    }

    public string ProgressText
    {
        get => _progressText;
        private set
        {
            if (!SetProperty(ref _progressText, value)) return;
            OnPropertyChanged(nameof(ProgressDisplayText));
        }
    }

    public bool IsProgressVisible => ShowProgress && IsBusy;
    public bool IsProgressIndeterminate => IsProgressVisible && ProgressPercent <= 0;
    public string ProgressDisplayText => IsProgressVisible ? ProgressText : string.Empty;

    public AsyncRelayCommand RunCommand { get; }

    public FixifyToolItemViewModel(FixifyService service, FixifyToolDefinition definition)
    {
        _service = service;
        Definition = definition;
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy);
    }

    private async Task RunAsync()
    {
        IsBusy = true;
        LastRunSucceeded = false;
        ShowProgress = true;
        ProgressPercent = 0;
        ProgressText = LongRunning ? "Starting…" : "Working…";
        Status = "Running and verifying…";
        Details = LongRunning ? "Windows is working. Sabby will show command progress when Windows reports it." : "Waiting for Windows…";
        try
        {
            var progress = LongRunning
                ? new Progress<double>(value =>
                {
                    ProgressPercent = value;
                    ProgressText = $"{value:0}%";
                })
                : null;

            var result = await _service.RunAsync(Definition.Id, progress);
            LastRunSucceeded = result.Success;
            if (result.Success && LongRunning)
            {
                ProgressPercent = 100;
                ProgressText = "100%";
            }
            Status = result.Success
                ? (result.RequiresRestart || RequiresRestart ? "✓ Completed • restart required" : "✓ Completed and verified")
                : "✕ Repair did not complete";
            Details = string.IsNullOrWhiteSpace(result.Details) ? result.Summary : $"{result.Summary}\n{result.Details}";
            UiNotificationHub.Publish(Name, result.Success ? Status : Details.Split('\n')[0], result.Success ? UiNotificationKind.Success : UiNotificationKind.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class FixifyViewModel : ViewModelBase
{
    private readonly List<FixifyToolItemViewModel> _allTools;
    private string _searchText = string.Empty;
    private string _selectedCategory = "All";

    public ObservableCollection<FixifyToolItemViewModel> Tools { get; } = new();
    public ObservableCollection<string> Categories { get; } = new() { "All", "Windows", "Diagnostics", "Storage", "Network", "Windows Update", "Security", "GPU", "Cleanup" };

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) Rebuild();
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value)) Rebuild();
        }
    }

    public FixifyViewModel(FixifyService service)
    {
        _allTools = service.Tools.Select(tool => new FixifyToolItemViewModel(service, tool)).ToList();
        Rebuild();
    }

    private void Rebuild()
    {
        IEnumerable<FixifyToolItemViewModel> query = _allTools;
        if (!SelectedCategory.Equals("All", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(x =>
                x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Category.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        Tools.Clear();
        foreach (var tool in query.OrderBy(x => x.Category).ThenBy(x => x.Name))
            Tools.Add(tool);
    }
}
