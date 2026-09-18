using PCTweaker.ViewModels;

namespace PCTweaker.Core.Navigation;

public interface INavigationService
{
    AppPage CurrentPage { get; }
    ViewModelBase? CurrentViewModel { get; }
    event EventHandler? Navigated;

    void Register(AppPage page, Func<ViewModelBase> factory);
    void NavigateTo(AppPage page);
}
