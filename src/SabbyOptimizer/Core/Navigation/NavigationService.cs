using PCTweaker.ViewModels;

namespace PCTweaker.Core.Navigation;

public sealed class NavigationService : INavigationService
{
    private const int MaxCachedPages = 4;

    private readonly Dictionary<AppPage, Func<ViewModelBase>> _factories = new();
    private readonly Dictionary<AppPage, ViewModelBase> _cache = new();
    private readonly LinkedList<AppPage> _cacheOrder = new();

    private bool _isNavigating;
    private AppPage? _pendingPage;

    public AppPage CurrentPage { get; private set; } = AppPage.Dashboard;
    public ViewModelBase? CurrentViewModel { get; private set; }

    public event EventHandler? Navigated;

    public void Register(AppPage page, Func<ViewModelBase> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[page] = factory;
    }

    public void Replace(AppPage page, Func<ViewModelBase> factory, bool refreshIfCurrent = false)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[page] = factory;

        if (_cache.Remove(page, out var cached) && !ReferenceEquals(cached, CurrentViewModel))
            DisposeIfSupported(cached);
        RemoveFromCacheOrder(page);

        if (!refreshIfCurrent || CurrentPage != page)
            return;

        var viewModel = factory();
        Cache(page, viewModel);
        CurrentViewModel = viewModel;
        RaiseNavigated();
    }

    public void NavigateTo(AppPage page)
    {
        // A page can trigger another navigation from a command/event while the first navigation
        // is still notifying listeners. Queue the latest request instead of letting the selected
        // sidebar item and displayed page fall out of sync.
        if (_isNavigating)
        {
            _pendingPage = page;
            return;
        }

        var requested = page;
        while (true)
        {
            NavigateCore(requested);

            if (_pendingPage is not AppPage pending || pending == CurrentPage)
            {
                _pendingPage = null;
                break;
            }

            _pendingPage = null;
            requested = pending;
        }
    }

    private void NavigateCore(AppPage page)
    {
        if (CurrentPage == page && CurrentViewModel is not null)
        {
            Touch(page);
            return;
        }

        if (!_factories.TryGetValue(page, out var factory))
            throw new InvalidOperationException($"No page factory is registered for {page}.");

        var previousPage = CurrentPage;
        var previousViewModel = CurrentViewModel;
        _isNavigating = true;
        try
        {
            if (!_cache.TryGetValue(page, out var viewModel))
            {
                viewModel = factory();
                Cache(page, viewModel);
            }
            else
            {
                Touch(page);
            }

            CurrentPage = page;
            CurrentViewModel = viewModel;
            TrimCache(page);
            RaiseNavigated();
        }
        catch
        {
            CurrentPage = previousPage;
            CurrentViewModel = previousViewModel;
            throw;
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private void Cache(AppPage page, ViewModelBase viewModel)
    {
        _cache[page] = viewModel;
        Touch(page);
        TrimCache(page);
    }

    private void Touch(AppPage page)
    {
        RemoveFromCacheOrder(page);
        _cacheOrder.AddLast(page);
    }

    private void TrimCache(AppPage keepPage)
    {
        while (_cache.Count > MaxCachedPages && _cacheOrder.First is not null)
        {
            var candidate = _cacheOrder.First.Value;
            _cacheOrder.RemoveFirst();

            if (candidate == keepPage || candidate == CurrentPage)
            {
                _cacheOrder.AddLast(candidate);
                if (_cacheOrder.Count <= 1)
                    break;
                continue;
            }

            if (_cache.Remove(candidate, out var removed))
                DisposeIfSupported(removed);
        }
    }

    private void RemoveFromCacheOrder(AppPage page)
    {
        var node = _cacheOrder.Find(page);
        if (node is not null)
            _cacheOrder.Remove(node);
    }

    private static void DisposeIfSupported(ViewModelBase viewModel)
    {
        if (viewModel is IDisposable disposable)
        {
            try { disposable.Dispose(); } catch { }
        }
    }

    private void RaiseNavigated()
    {
        var handlers = Navigated?.GetInvocationList();
        if (handlers is null)
            return;

        foreach (EventHandler handler in handlers)
        {
            try { handler(this, EventArgs.Empty); }
            catch { }
        }
    }
}
