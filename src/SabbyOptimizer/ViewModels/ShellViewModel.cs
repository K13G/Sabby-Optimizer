using System.Collections.ObjectModel;
using System.Windows.Input;
using PCTweaker.Core.Mvvm;
using PCTweaker.Core.Navigation;
using PCTweaker.Core.Services;

namespace PCTweaker.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;
    private NavigationItem? _selectedSidebarItem;
    private readonly AppPage _requestedStartupPage;
    private bool _startupPageRestored;

    public ObservableCollection<NavigationItem> NavigationItems { get; } = new()
    {
        new(AppPage.Dashboard, "Dashboard", "\uE80F"),
        new(AppPage.Tweaks, "Tweaks", "\uE945"),
        new(AppPage.Maintenance, "Maintenance", "\uE90F"),
        new(AppPage.Fixify, "Fixify", "\uE9D9"),
        new(AppPage.Debloat, "Debloat", "\uE74D"),
        new(AppPage.Ping, "Ping", "\uE968"),
        new(AppPage.GpuDriver, "GPU Drivers", "\uE7F4"),
        new(AppPage.Updates, "Updates", "\uE895"),
        new(AppPage.GameProfiles, "Game Profiles", "\uE7FC"),
        new(AppPage.Benchmark, "Benchmarks", "\uE9D2"),
        new(AppPage.Backups, "Backups", "\uE8B7"),
        new(AppPage.PcRestore, "PC Restore", "\uE777")
    };

    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenCreditsCommand { get; }
    public bool IsSettingsSelected => _navigation.CurrentPage == AppPage.Settings;
    public bool IsCreditsSelected => _navigation.CurrentPage == AppPage.Credits;

    public NavigationItem? SelectedSidebarItem
    {
        get => _selectedSidebarItem;
        set
        {
            if (value is null || ReferenceEquals(_selectedSidebarItem, value))
                return;

            var previous = _selectedSidebarItem;
            _selectedSidebarItem = value;
            OnPropertyChanged();
            try
            {
                _navigation.NavigateTo(value.Page);
            }
            catch (Exception ex)
            {
                _selectedSidebarItem = previous;
                OnPropertyChanged();
                UiNotificationHub.Publish("Page could not open", $"{value.Label} stayed closed: {ex.Message}", UiNotificationKind.Warning);
            }
        }
    }

    public ViewModelBase? CurrentViewModel => _navigation.CurrentViewModel;

    public string CurrentPageTitle => _navigation.CurrentPage switch
    {
        AppPage.Dashboard => "Dashboard",
        AppPage.Tweaks => "Tweaks",
        AppPage.Maintenance => "Maintenance",
        AppPage.Fixify => "Fixify",
        AppPage.Debloat => "Debloat",
        AppPage.Ping => "Ping",
        AppPage.GpuDriver => "GPU Drivers",
        AppPage.Updates => "Updates",
        AppPage.GameProfiles => "Game Profiles",
        AppPage.Benchmark => "Benchmarks",
        AppPage.Backups => "Backups",
        AppPage.PcRestore => "PC Restore",
        AppPage.Settings => "Settings",
        AppPage.Credits => "Credits",
        _ => "Dashboard"
    };

    public string CurrentPageSubtitle => _navigation.CurrentPage switch
    {
        AppPage.Dashboard => "Hardware overview and system readiness",
        AppPage.Tweaks => "Verified performance, latency, Windows, and gaming controls",
        AppPage.Maintenance => "Startup, services, cleanup, and maintenance tools",
        AppPage.Fixify => "Windows repair and recovery tools",
        AppPage.Debloat => "Verified app removal with safety checks",
        AppPage.Ping => "Latency diagnostics and local network tuning",
        AppPage.GpuDriver => "GPU driver status and supported integrations",
        AppPage.Updates => "Windows, driver, and application updates",
        AppPage.GameProfiles => "Per-game settings linked to real executables",
        AppPage.Benchmark => "Before/after validation and performance testing",
        AppPage.Backups => "Snapshots, originals, and rollback protection",
        AppPage.PcRestore => "Windows restore points and system rollback",
        AppPage.Settings => "Appearance, behavior, layout, and Sabby updates",
        AppPage.Credits => "People behind Sabby Optimizer",
        _ => string.Empty
    };

    public ShellViewModel(INavigationService navigation, ISettingsService settings)
    {
        _navigation = navigation;
        _settings = settings;
        _navigation.Navigated += OnNavigated;
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        OpenCreditsCommand = new RelayCommand(OpenCredits);

        _requestedStartupPage = settings.Current.RememberLastPage ? settings.Current.LastPage : AppPage.Dashboard;

        // Always paint the lightweight Dashboard first. Restoring a dense page such as Tweaks,
        // Game Profiles or Monitoring before the first frame can add seconds to perceived startup.
        _selectedSidebarItem = NavigationItems[0];
        _navigation.NavigateTo(AppPage.Dashboard);
    }

    public void RestoreRequestedStartupPage()
    {
        if (_startupPageRestored)
            return;
        _startupPageRestored = true;

        if (_requestedStartupPage == AppPage.Dashboard)
            return;

        if (_requestedStartupPage == AppPage.Settings)
        {
            OpenSettings();
            return;
        }

        if (_requestedStartupPage == AppPage.Credits)
        {
            OpenCredits();
            return;
        }

        if (_requestedStartupPage == AppPage.Extensions)
        {
            OpenSettings();
            return;
        }

        SelectedSidebarItem = NavigationItems.FirstOrDefault(x => x.Page == _requestedStartupPage) ?? NavigationItems[0];
    }

    private void OpenSettings() => TryOpenUtilityPage(AppPage.Settings, "Settings");

    private void OpenCredits() => TryOpenUtilityPage(AppPage.Credits, "Credits");

    private void TryOpenUtilityPage(AppPage page, string label)
    {
        var previous = _selectedSidebarItem;
        _selectedSidebarItem = null;
        OnPropertyChanged(nameof(SelectedSidebarItem));
        try
        {
            _navigation.NavigateTo(page);
        }
        catch (Exception ex)
        {
            _selectedSidebarItem = previous;
            OnPropertyChanged(nameof(SelectedSidebarItem));
            UiNotificationHub.Publish("Page could not open", $"{label} stayed closed: {ex.Message}", UiNotificationKind.Warning);
        }
    }

    private void OnNavigated(object? sender, EventArgs e)
    {
        if (_settings.Current.RememberLastPage)
            _settings.Current.LastPage = _navigation.CurrentPage;

        var sidebarMatch = NavigationItems.FirstOrDefault(item => item.Page == _navigation.CurrentPage);
        if (!ReferenceEquals(_selectedSidebarItem, sidebarMatch))
        {
            _selectedSidebarItem = sidebarMatch;
            OnPropertyChanged(nameof(SelectedSidebarItem));
        }

        OnPropertyChanged(nameof(CurrentViewModel));
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageSubtitle));
        OnPropertyChanged(nameof(IsSettingsSelected));
        OnPropertyChanged(nameof(IsCreditsSelected));
    }
}
