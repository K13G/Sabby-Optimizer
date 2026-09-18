using PCTweaker.ViewModels;

namespace PCTweaker.Core.Navigation;

public sealed class NavigationService : INavigationService
{
    private readonly Dictionary<AppPage, Func<ViewModelBase>> _factories = new();
    private readonly Dictionary<AppPage, ViewModelBase> _cache = new();

    public AppPage CurrentPage { get; private set; } = AppPage.Dashboard;
    public ViewModelBase? CurrentViewModel { get; private set; }

    public event EventHandler? Navigated;

    public void Register(AppPage page, Func<ViewModelBase> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[page] = factory;
    }

    /// <summary>
    /// Replaces a page factory without rebuilding the entire shell. This is used by startup so
    /// Sabby can expose a responsive, usable workspace immediately and silently upgrade only the
    /// hardware-dependent pages after capability discovery finishes in the background.
    /// </summary>
    public void Replace(AppPage page, Func<ViewModelBase> factory, bool refreshIfCurrent = false)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[page] = factory;
        _cache.Remove(page);

        if (!refreshIfCurrent || CurrentPage != page)
            return;

        var viewModel = factory();
        _cache[page] = viewModel;
        CurrentViewModel = viewModel;
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    public void NavigateTo(AppPage page)
    {
        if (!_factories.TryGetValue(page, out var factory))
            throw new InvalidOperationException($"No page factory is registered for {page}.");

        if (!_cache.TryGetValue(page, out var viewModel))
        {
            viewModel = factory();
            _cache[page] = viewModel;
        }

        CurrentPage = page;
        CurrentViewModel = viewModel;
        Navigated?.Invoke(this, EventArgs.Empty);
    }
}
