using System.Collections.ObjectModel;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Services;
using PCTweaker.Models;

namespace PCTweaker.ViewModels;

public sealed class DebloatAppViewModel : ViewModelBase
{
    private readonly IDebloatService _service;
    private bool _isBusy;
    private string _status = "Installed";
    public DebloatAppInfo Model { get; }
    public string Name => Model.DisplayName;
    public string PackageIdText => Model.PackageIdText;
    public string Publisher => Model.Publisher;
    public string WhatItIs => Model.WhatItIs;
    public string RemovalEffect => Model.RemovalEffect;
    public string Recommendation => Model.Recommendation;
    public string ScoreLabel => Model.ScoreLabel;
    public string SafetyLabel => Model.SafetyLabel;
    public double SafetyGreenWidth => Model.SafetyGreenWidth;
    public double SafetyRedWidth => Model.SafetyRedWidth;
    public bool SafeForBulk => Model.SafeForBulk;
    public bool CanRemove => Model.CanRemove;
    public string RemoveButtonText => Model.CanRemove ? "Remove" : "Protected by Windows";
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public AsyncRelayCommand RemoveCommand { get; }
    public event EventHandler? Removed;

    public DebloatAppViewModel(IDebloatService service, DebloatAppInfo model)
    {
        _service = service; Model = model;
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => !IsBusy && CanRemove);
    }

    public async Task<bool> RemoveAsync()
    {
        if (!CanRemove)
        {
            Status = "Windows marks this package as protected/non-removable. Sabby will not force-remove it.";
            return false;
        }

        IsBusy = true; RemoveCommand.RaiseCanExecuteChanged(); Status = "Removing and verifying…";
        try
        {
            var result = await _service.RemoveAsync(Model);
            Status = result.Success ? "✓ Removed & verified" : $"✕ {result.Message}";
            if (result.Success) Removed?.Invoke(this, EventArgs.Empty);
            return result.Success;
        }
        finally { IsBusy = false; RemoveCommand.RaiseCanExecuteChanged(); }
    }
}

public sealed class DebloatViewModel : ViewModelBase
{
    private readonly IDebloatService _service;
    private readonly List<DebloatAppViewModel> _all = new();
    private bool _isBusy;
    private string _searchText = string.Empty;
    private string _status = "Scan Windows apps to see conservative debloat recommendations.";
    private double _progress;
    public ObservableCollection<DebloatAppViewModel> Apps { get; } = new();
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value ?? string.Empty)) Rebuild(); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { ScanCommand.RaiseCanExecuteChanged(); RemoveSafeCommand.RaiseCanExecuteChanged(); } } }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand RemoveSafeCommand { get; }

    public DebloatViewModel(IDebloatService service)
    {
        _service = service;
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        RemoveSafeCommand = new AsyncRelayCommand(RemoveSafeAsync, () => !IsBusy);
        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        IsBusy = true; Progress = 15; Status = "Scanning installed Appx packages…";
        try
        {
            var rows = await _service.ScanAsync(); Progress = 85;
            _all.Clear();
            foreach (var row in rows)
            {
                var vm = new DebloatAppViewModel(_service, row);
                vm.Removed += (_, _) => { _all.Remove(vm); Rebuild(); };
                _all.Add(vm);
            }
            Rebuild(); Progress = 100; Status = $"{_all.Count} removable/optional user apps found • {_all.Count(x => x.SafeForBulk)} marked SAFE ONLY.";
        }
        finally { IsBusy = false; }
    }

    private async Task RemoveSafeAsync()
    {
        var targets = _all.Where(x => x.SafeForBulk).ToArray();
        if (targets.Length == 0) { Status = "No SAFE ONLY debloat suggestions are installed."; return; }
        IsBusy = true; var done=0; var failed=0; Progress=0;
        try
        {
            foreach (var target in targets)
            {
                if (await target.RemoveAsync()) done++; else failed++;
                Progress = 100d * (done + failed) / targets.Length;
            }
            Status = $"Debloat finished • {done} removed & verified • {failed} unchanged.";
        }
        finally { IsBusy=false; Rebuild(); }
    }

    private void Rebuild()
    {
        var q = _all.AsEnumerable(); var term=SearchText.Trim();
        if (!string.IsNullOrWhiteSpace(term)) q=q.Where(x=>x.Name.Contains(term,StringComparison.OrdinalIgnoreCase)||x.PackageIdText.Contains(term,StringComparison.OrdinalIgnoreCase)||x.WhatItIs.Contains(term,StringComparison.OrdinalIgnoreCase)||x.RemovalEffect.Contains(term,StringComparison.OrdinalIgnoreCase)||x.Recommendation.Contains(term,StringComparison.OrdinalIgnoreCase));
        Apps.Clear(); foreach(var item in q) Apps.Add(item);
    }
}
