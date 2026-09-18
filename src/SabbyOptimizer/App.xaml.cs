using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PCTweaker.Core.Backups;
using PCTweaker.Core.GameDetection;
using PCTweaker.Core.GameProfiles;
using PCTweaker.Core.Navigation;
using PCTweaker.Core.Presets;
using PCTweaker.Core.Services;
using PCTweaker.Core.Tweaks;
using PCTweaker.Models;
using PCTweaker.ViewModels;

namespace PCTweaker;

public partial class App : Application
{
    private IAppLogger? _logger;
    private Mutex? _singleInstanceMutex;
    private IGameDetectionService? _gameDetectionService;
    private SystemMonitoringService? _monitoringService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (await FastUpdateHelper.TryRunHelperAsync(e.Args))
        {
            Shutdown(0);
            return;
        }

        if (TryRunNvApiHelperCommand(e.Args))
            return;

        if (await TryRunElevatedTweakCommandAsync(e.Args))
            return;

        if (!TryAcquireSingleInstance())
        {
            MessageBox.Show(
                "Sabby Optimizer is already running. Check the taskbar or notification area.",
                "Sabby Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        var startupClock = Stopwatch.StartNew();
        MainWindow? mainWindow = null;

        try
        {
            var paths = new AppPaths();
            _logger = new FileLogger(paths);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            var settings = new JsonSettingsService(paths, _logger);
            await settings.LoadAsync();
            var stableFeed = SabbyUpdateDefaults.NormalizeStableFeed(settings.Current.StableUpdateFeedUrl);
            var updateSettingsChanged =
                settings.Current.SabbyUpdateChannel != SabbyUpdateChannel.Stable ||
                !settings.Current.AutoCheckSabbyUpdates ||
                !string.Equals(settings.Current.StableUpdateFeedUrl, stableFeed, StringComparison.Ordinal);

            settings.Current.SabbyUpdateChannel = SabbyUpdateChannel.Stable;
            settings.Current.StableUpdateFeedUrl = stableFeed;
            settings.Current.AutoCheckSabbyUpdates = true;
            if (updateSettingsChanged)
            {
                await settings.SaveAsync();
                _logger.Info("Sabby update settings migrated to the permanent K13G stable channel with automatic checks enabled.");
            }
            Current.Resources["CardColumns"] = settings.Current.CardColumns;

            var theme = new ThemeService(_logger);
            theme.Apply(settings.Current.Theme);
            var appearance = new AppearanceService(_logger);
            appearance.Apply(settings.Current.VisualStyle, settings.Current.StyleIntensity, settings.Current.AnimationSpeed);

            // Build only the cheap, dependency-light shell before first paint. Expensive CIM,
            // PowerShell and driver capability probes are never awaited by navigation again.
            var updateExtensions = new UpdateExtensionService(paths, _logger);
            var startupService = new AppStartupService(_logger);
            var hardwareInfo = new HardwareInfoService(_logger);
            var hardwareTask = Task.Run(hardwareInfo.GetHardwareInfo);

            var quickHardware = new HardwareInfo
            {
                DeviceName = Environment.MachineName,
                Processor = "Loading…",
                ProcessorDetails = "Hardware scan running in parallel",
                Graphics = "Loading…",
                GraphicsDetails = "Hardware scan running in parallel",
                Memory = "Loading…",
                MemoryDetails = "Hardware scan running in parallel",
                Motherboard = "Loading…",
                MotherboardDetails = "Hardware scan running in parallel",
                Windows = "Windows",
                WindowsDetails = "Hardware scan running in parallel",
                SystemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:",
                Network = "Loading…",
                NetworkDetails = "Hardware scan running in parallel",
                SupportedFeatures = "Core controls ready"
            };

            var backupRepository = new BackupRepository(paths, _logger);
            var quickEngine = new TweakEngine(TweakCatalog.CreateFastStartupCatalog(paths), _logger, backupRepository);
            var quickSelfCheck = new TweakEngineSelfCheckResult(true, "Core controls ready • hardware-aware controls are loading quietly");
            var quickBackupService = new BackupService(quickEngine, backupRepository, _logger);
            var quickPresetService = new PresetService(paths, quickEngine, _logger);
            var gameProfileService = new GameProfileService(paths, _logger);
            var gameScanService = new GameScanService(_logger);
            var quickGameDetection = new GameDetectionService(gameProfileService, quickPresetService, quickEngine, hardwareInfo, settings, _logger);
            var maintenanceService = new MaintenanceService(paths, _logger);
            var quickGpuDriverService = new GpuDriverIntegrationService(quickHardware, gameProfileService, paths, _logger);
            var debloatService = new DebloatService(_logger);
            var pingService = new PingOptimizationService();
            var systemRestoreService = new SystemRestoreService(_logger);
            var updateService = new UpdateCenterService(_logger);

            var dashboardViewModel = new DashboardViewModel(paths, quickHardware);
            var navigation = new NavigationService();
            navigation.Register(AppPage.Dashboard, () => dashboardViewModel);
            navigation.Register(AppPage.Tweaks, () => new TweaksViewModel(quickEngine, quickSelfCheck, quickHardware, quickBackupService));
            navigation.Register(AppPage.Maintenance, () => new SystemToolsViewModel(maintenanceService));
            navigation.Register(AppPage.Fixify, () => new FixifyViewModel(new FixifyService(maintenanceService, quickGpuDriverService, _logger)));
            navigation.Register(AppPage.Debloat, () => new DebloatViewModel(debloatService));
            navigation.Register(AppPage.Ping, () => new PingViewModel(pingService, quickEngine));
            navigation.Register(AppPage.GpuDriver, () => new GpuDriverViewModel(quickGpuDriverService));
            navigation.Register(AppPage.Updates, () => new UpdateCenterViewModel(updateService, settings));
            navigation.Register(AppPage.GameProfiles, () =>
            {
                var vm = new GameProfilesViewModel(gameScanService, gameProfileService, quickPresetService, quickGameDetection);
                _ = vm.InitializeAsync();
                return vm;
            });
            var quickMonitoring = new SystemMonitoringService(quickHardware, _logger);
            navigation.Register(AppPage.Benchmark, () => new BenchmarkViewModel(new BenchmarkService(paths, quickHardware, pingService, _logger), quickMonitoring, settings));
            navigation.Register(AppPage.Backups, () =>
            {
                var vm = new BackupsViewModel(quickBackupService);
                _ = vm.InitializeAsync();
                return vm;
            });
            navigation.Register(AppPage.PcRestore, () => new PcRestoreViewModel(systemRestoreService));
            navigation.Register(AppPage.Settings, () => new SettingsViewModel(settings, theme, appearance, startupService, updateExtensions));
            navigation.Register(AppPage.Credits, () => new CreditsViewModel());

            var shell = new ShellViewModel(navigation, settings);
            mainWindow = new MainWindow(shell, settings, appearance, _logger);
            MainWindow = mainWindow;
            mainWindow.PrepareStartupReveal();
            mainWindow.Show();
            mainWindow.PlayStartupReveal();
            mainWindow.EnsureVisibleAndActivated();
            await Dispatcher.Yield(DispatcherPriority.Loaded);

            // Restore the user's last page only after the first interactive Dashboard frame is on
            // screen. This keeps cold launch responsive even when the saved page has a large XAML tree.
            _ = Dispatcher.BeginInvoke(new Action(shell.RestoreRequestedStartupPage), DispatcherPriority.ContextIdle);

            // The update check and the full PC capability scan are independent background tasks.
            // Neither owns the page host, so clicking navigation always changes real page content.
            _ = RunStartupUpdateCheckAsync(mainWindow, updateExtensions, settings, paths);
            _ = RunReleaseMigrationAsync(paths, settings);
            _ = UpgradeHardwareAwarePagesAsync(
                navigation, dashboardViewModel, hardwareInfo, hardwareTask, settings, paths,
                backupRepository, gameProfileService, gameScanService, maintenanceService,
                pingService);

            try
            {
                if (startupService.IsEnabled() != settings.Current.StartWithWindows)
                    startupService.SetEnabled(settings.Current.StartWithWindows);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Startup registration check failed safely: {ex.Message}");
            }

            _logger.Info($"Interactive workspace became visible in {startupClock.ElapsedMilliseconds} ms. Full capability discovery is background-only.");
        }
        catch (Exception ex)
        {
            _logger?.Critical("Fatal startup failure.", ex);
            if (mainWindow is not null)
            {
                mainWindow.EnsureVisibleAndActivated();
                MessageBox.Show(mainWindow,
                    $"Sabby Optimizer hit a startup error but kept the workspace open.\n\n{ex.Message}",
                    "Sabby Optimizer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show(
                $"Sabby Optimizer could not start.\n\n{ex.Message}",
                "Sabby Optimizer - Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static bool IsLegacyLocalUpdateFeed(string? feed)
    {
        if (string.IsNullOrWhiteSpace(feed)) return false;
        if (feed.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            feed.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return Uri.TryCreate(feed, UriKind.Absolute, out var uri) && uri.IsLoopback;
    }
    private async Task RunReleaseMigrationAsync(IAppPaths paths, ISettingsService settings)
    {
        try
        {
            var migration = new ReleaseMigrationService(paths, _logger!);
            await migration.RunAsync(settings);
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Release migration did not complete: {ex.Message}");
        }
    }

    private async Task RunStartupUpdateCheckAsync(
        Window owner,
        UpdateExtensionService updateExtensions,
        ISettingsService settings,
        IAppPaths paths)
    {
        try
        {
            var coordinator = new StartupUpdateCoordinator(updateExtensions, settings, _logger!);
            await coordinator.CheckAndEnforceAsync(owner);
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Startup update check failed without blocking Sabby: {ex.Message}");
        }
    }

    private static async Task WaitForCapabilityDemandAsync(NavigationService navigation, TimeSpan idleTimeout)
    {
        static bool NeedsFullCapabilities(AppPage page) => page is
            AppPage.GpuDriver or AppPage.GameProfiles;

        if (NeedsFullCapabilities(navigation.CurrentPage))
            return;

        var requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (NeedsFullCapabilities(navigation.CurrentPage))
                requested.TrySetResult(true);
        };

        navigation.Navigated += handler;
        try
        {
            await Task.WhenAny(requested.Task, Task.Delay(idleTimeout));
        }
        finally
        {
            navigation.Navigated -= handler;
        }
    }

    private async Task UpgradeHardwareAwarePagesAsync(
        NavigationService navigation,
        DashboardViewModel dashboard,
        HardwareInfoService hardwareInfo,
        Task<HardwareInfo> hardwareTask,
        ISettingsService settings,
        IAppPaths paths,
        BackupRepository backupRepository,
        GameProfileService gameProfileService,
        GameScanService gameScanService,
        MaintenanceService maintenanceService,
        PingOptimizationService pingService)
    {
        try
        {
            var hardware = await hardwareTask;
            dashboard.UpdateHardware(hardware);

            // Keep first interaction clear of full catalog/self-check work. Core controls are already usable.
            await Task.Delay(700);
            var fullCatalog = await Task.Run(() => TweakCatalog.CreateCatalog(paths, hardware));

            var fullEngine = new TweakEngine(fullCatalog, _logger!, backupRepository);
            var selfCheck = await Task.Run(async () => await TweakEngineSelfCheck.RunAsync(fullEngine, _logger!));
            var backupService = new BackupService(fullEngine, backupRepository, _logger!);
            var presetService = new PresetService(paths, fullEngine, _logger!);
            var gameDetection = new GameDetectionService(gameProfileService, presetService, fullEngine, hardwareInfo, settings, _logger!);
            var gpuDriverService = new GpuDriverIntegrationService(hardware, gameProfileService, paths, _logger!);
            var monitoringService = new SystemMonitoringService(hardware, _logger!);

            _gameDetectionService = gameDetection;
            _monitoringService = monitoringService;

            // Replace only factories/cached pages that depend on the full hardware/tweak graph.
            // If the user is currently on one of them it refreshes in place; every other page is untouched.
            navigation.Replace(AppPage.Tweaks, () => new TweaksViewModel(fullEngine, selfCheck, hardware, backupService), refreshIfCurrent: false);
            navigation.Replace(AppPage.Ping, () => new PingViewModel(pingService, fullEngine), refreshIfCurrent: false);
            navigation.Replace(AppPage.GpuDriver, () => new GpuDriverViewModel(gpuDriverService), refreshIfCurrent: false);
            navigation.Replace(AppPage.Fixify, () => new FixifyViewModel(new FixifyService(maintenanceService, gpuDriverService, _logger!)), refreshIfCurrent: false);
            navigation.Replace(AppPage.GameProfiles, () =>
            {
                var vm = new GameProfilesViewModel(gameScanService, gameProfileService, presetService, gameDetection);
                _ = vm.InitializeAsync();
                return vm;
            }, refreshIfCurrent: false);
            navigation.Replace(AppPage.Benchmark, () => new BenchmarkViewModel(new BenchmarkService(paths, hardware, pingService, _logger!), monitoringService, settings), refreshIfCurrent: false);
            navigation.Replace(AppPage.Backups, () =>
            {
                var vm = new BackupsViewModel(backupService);
                _ = vm.InitializeAsync();
                return vm;
            }, refreshIfCurrent: false);

            if (settings.Current.GameDetectionEnabled)
                gameDetection.Start();

            // No all-tweaks scan runs automatically. Detection is lazy/per-card and Refresh All is
            // explicit, preventing dozens of PowerShell/registry probes from turning startup into a
            // 30 FPS workload.
            _logger?.Info($"Background capability discovery complete: {fullEngine.Definitions.Count} visible controls.");
        }
        catch (Exception ex)
        {
            _logger?.Error("Hardware-aware background initialization failed safely; core pages remain usable.", ex);
            UiNotificationHub.Publish(
                "Hardware-aware controls delayed",
                "Core Sabby pages are still usable. Open the log if hardware-specific controls do not appear.",
                UiNotificationKind.Warning);
        }
    }

    private bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\SabbyOptimizer.MainInstance", out var createdNew);
            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Single-instance protection could not be initialized: {ex.Message}");
            return true;
        }
    }

    private bool TryRunNvApiHelperCommand(string[] args)
    {
        if (args.Length < 3 || !args[0].StartsWith("--nvapi-quality-", StringComparison.OrdinalIgnoreCase))
            return false;

        var action = args[0];
        var backupPath = args[1];
        var resultPath = args[2];
        try
        {
            var result = action.Equals("--nvapi-quality-restore", StringComparison.OrdinalIgnoreCase)
                ? NvidiaNvApiQualityProfile.Restore(backupPath)
                : NvidiaNvApiQualityProfile.Apply(backupPath);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            File.WriteAllText(resultPath, $"{(result.Success ? 1 : 0)}|{result.Message}");
            Shutdown(result.Success ? 0 : 2);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(resultPath, $"0|NVAPI helper failed: {ex.Message}"); } catch { }
            Shutdown(3);
        }
        return true;
    }

    private async Task<bool> TryRunElevatedTweakCommandAsync(string[] args)
    {
        if (args.Length < 3 || !args[0].Equals("--elevated-tweak", StringComparison.OrdinalIgnoreCase))
            return false;

        var action = args[1];
        var tweakId = args[2];
        try
        {
            var paths = new AppPaths();
            _logger = new FileLogger(paths);
            var hardwareService = new HardwareInfoService(_logger);
            var hardware = hardwareService.GetHardwareInfo();
            var backupRepository = new BackupRepository(paths, _logger);
            var extensionService = new UpdateExtensionService(paths, _logger);
            var elevatedCatalog = TweakCatalog.CreateCatalog(paths, hardware).Concat(extensionService.LoadEnabledHandlers()).ToArray();
            var engine = new TweakEngine(elevatedCatalog, _logger, backupRepository);

            var result = action.Equals("undo", StringComparison.OrdinalIgnoreCase)
                ? await engine.UndoAsync(tweakId)
                : await engine.ApplyAsync(tweakId);

            _logger.Info($"Elevated tweak command {action} {tweakId}: {result.Success} • {result.Message}");
            Shutdown(result.Success ? 0 : 2);
        }
        catch (Exception ex)
        {
            _logger?.Error("Elevated tweak helper failed.", ex);
            Shutdown(3);
        }

        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _gameDetectionService?.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Game detection shutdown cleanup failed: {ex.Message}");
        }

        try
        {
            _monitoringService?.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Monitoring shutdown cleanup failed: {ex.Message}");
        }

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch { }
        finally
        {
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Critical("Unhandled UI exception.", e.Exception);

        // WPF Freezable/animation objects can occasionally become immutable after resource/template
        // promotion. These are presentation-only failures: recover the visual state and keep the app
        // usable instead of interrupting the user with a modal error dialog.
        var message = e.Exception.Message ?? string.Empty;
        // Some WPF binding failures are wrapped in different exception types depending on the
        // control/template path. Do not require InvalidOperationException for the legacy
        // NetworkControlContext binding signature: current Sabby templates do not consume that
        // compatibility value, so this is safe to treat as a non-fatal presentation fault.
        // A WPF read-only binding mistake is presentation-only. Treat the whole class of
        // TwoWay/OneWayToSource read-only binding failures as recoverable instead of showing
        // a modal error or destabilizing navigation.
        var readOnlyBindingFault =
            message.Contains("TwoWay or OneWayToSource binding cannot work", StringComparison.OrdinalIgnoreCase);

        var recoverablePresentationError = readOnlyBindingFault ||
            (e.Exception is InvalidOperationException &&
             (message.Contains("read-only state", StringComparison.OrdinalIgnoreCase) ||
              message.Contains("frozen", StringComparison.OrdinalIgnoreCase) ||
              message.Contains("Freezable", StringComparison.OrdinalIgnoreCase)));

        if (recoverablePresentationError)
        {
            if (MainWindow is MainWindow window)
                window.RecoverPresentationAnimations();

            _logger?.Warning($"Recovered from a non-fatal WPF presentation error: {message}");
            e.Handled = true;
            return;
        }

        var logPath = Path.Combine(new AppPaths().LogsDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");
        MessageBox.Show(
            $"Sabby Optimizer hit a UI error but will stay open.\n\n{message}\n\nLog: {logPath}",
            "Sabby Optimizer Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.Error("Unobserved background task exception.", e.Exception);
        e.SetObserved();
    }
}
