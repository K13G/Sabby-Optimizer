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
        _service = service;
        Model = model;
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => !IsBusy && CanRemove);
    }

    public async Task<bool> RemoveAsync()
    {
        if (!CanRemove)
        {
            Status = "Windows marks this package as protected/non-removable. Sabby will not force-remove it.";
            return false;
        }

        IsBusy = true;
        RemoveCommand.RaiseCanExecuteChanged();
        Status = "Removing and verifying…";
        try
        {
            var result = await _service.RemoveAsync(Model);
            Status = result.Success ? "✓ Removed & verified" : $"✕ {result.Message}";
            if (result.Success) Removed?.Invoke(this, EventArgs.Empty);
            return result.Success;
        }
        finally
        {
            IsBusy = false;
            RemoveCommand.RaiseCanExecuteChanged();
        }
    }
}

public sealed class DebloatViewModel : ViewModelBase
{
    private const int PageSize = 32;
    private readonly IDebloatService _service;
    private readonly List<DebloatAppViewModel> _all = new();
    private bool _isBusy;
    private string _searchText = string.Empty;
    private string _status = "Scanning optional Windows apps…";
    private double _progress;
    private int _currentPage;
    private int _filteredCount;

    public ObservableCollection<DebloatAppViewModel> Apps { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty)) return;
            _currentPage = 0;
            RebuildPage();
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            ScanCommand.RaiseCanExecuteChanged();
            RemoveSafeCommand.RaiseCanExecuteChanged();
            PreviousPageCommand.RaiseCanExecuteChanged();
            NextPageCommand.RaiseCanExecuteChanged();
        }
    }

    public string PageText
    {
        get
        {
            if (_filteredCount == 0) return "No matching apps";
            var first = (_currentPage * PageSize) + 1;
            var last = Math.Min(_filteredCount, first + Apps.Count - 1);
            return $"{first}–{last} of {_filteredCount}";
        }
    }

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand RemoveSafeCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }

    public DebloatViewModel(IDebloatService service)
    {
        _service = service;
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        RemoveSafeCommand = new AsyncRelayCommand(RemoveSafeAsync, () => !IsBusy);
        PreviousPageCommand = new RelayCommand(PreviousPage, () => !IsBusy && _currentPage > 0);
        NextPageCommand = new RelayCommand(NextPage, () => !IsBusy && ((_currentPage + 1) * PageSize) < _filteredCount);
        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        Progress = 15;
        Status = "Scanning installed Appx packages…";
        try
        {
            var rows = await _service.ScanAsync();
            Progress = 85;
            _all.Clear();

            foreach (var row in rows)
            {
                var vm = new DebloatAppViewModel(_service, row);
                vm.Removed += (_, _) => OnRemoved(vm);
                _all.Add(vm);
            }

            _currentPage = 0;
            RebuildPage();
            Progress = 100;
            Status = $"{_all.Count} removable/optional user apps found • {_all.Count(x => x.SafeForBulk)} marked SAFE ONLY.";
        }
        catch (Exception ex)
        {
            Status = $"Debloat scan failed safely: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveSafeAsync()
    {
        var targets = _all.Where(x => x.SafeForBulk && x.CanRemove).ToArray();
        if (targets.Length == 0)
        {
            Status = "No SAFE ONLY debloat suggestions are installed.";
            return;
        }

        IsBusy = true;
        var done = 0;
        var failed = 0;
        Progress = 0;
        try
        {
            foreach (var target in targets)
            {
                if (await target.RemoveAsync()) done++; else failed++;
                Progress = 100d * (done + failed) / targets.Length;
            }
            Status = $"Debloat finished • {done} removed & verified • {failed} unchanged.";
        }
        finally
        {
            IsBusy = false;
            RaisePagingState();
        }
    }

    private void OnRemoved(DebloatAppViewModel item)
    {
        _all.Remove(item);

        // Never Clear+repopulate the bound collection for a button removal. Doing so caused the
        // outer workspace ScrollViewer to jump down after every successful uninstall.
        Apps.Remove(item);

        var filtered = GetFiltered().ToList();
        _filteredCount = filtered.Count;
        var maxPage = Math.Max(0, (_filteredCount - 1) / PageSize);
        _currentPage = Math.Min(_currentPage, maxPage);

        var desired = filtered.Skip(_currentPage * PageSize).Take(PageSize).ToList();
        foreach (var candidate in desired)
            if (!Apps.Contains(candidate))
                Apps.Add(candidate);

        OnPropertyChanged(nameof(PageText));
        RaisePagingState();
    }

    private IEnumerable<DebloatAppViewModel> GetFiltered()
    {
        var term = SearchText.Trim();
        if (string.IsNullOrWhiteSpace(term))
            return _all;

        return _all.Where(x =>
            x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.PackageIdText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.WhatItIs.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.RemovalEffect.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.Recommendation.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildPage()
    {
        var filtered = GetFiltered().ToList();
        _filteredCount = filtered.Count;
        var maxPage = Math.Max(0, (_filteredCount - 1) / PageSize);
        _currentPage = Math.Clamp(_currentPage, 0, maxPage);

        Apps.Clear();
        foreach (var item in filtered.Skip(_currentPage * PageSize).Take(PageSize))
            Apps.Add(item);

        OnPropertyChanged(nameof(PageText));
        RaisePagingState();
    }

    private void PreviousPage()
    {
        if (_currentPage <= 0) return;
        _currentPage--;
        RebuildPage();
    }

    private void NextPage()
    {
        if (((_currentPage + 1) * PageSize) >= _filteredCount) return;
        _currentPage++;
        RebuildPage();
    }

    private void RaisePagingState()
    {
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
    }
}
