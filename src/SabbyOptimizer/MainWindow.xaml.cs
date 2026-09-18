using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PCTweaker.Core.Services;
using PCTweaker.ViewModels;

namespace PCTweaker;

public partial class MainWindow : Window
{
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmAppTray = 0x8001;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmNonotify = 0x0080;
    private const int IdiApplication = 32512;
    private const double CollapsedSidebarWidth = 54;
    private const double ExpandedSidebarWidth = 216;

    private readonly ISettingsService _settings;
    private readonly IAppearanceService _appearance;
    private readonly IAppLogger _logger;
    private readonly Dictionary<FrameworkElement, AnimationClock> _dropClocks = new();
    private HwndSource? _hwndSource;
    private IntPtr _windowHandle;
    private IntPtr _trayIconHandle;
    private bool _trayIconHandleOwned;
    private bool _trayIconVisible;
    private bool _allowRealClose;
    private bool _handlingTrayMinimize;
    private bool _sidebarExpanded;
    private int _sidebarAnimationGeneration;
    private WindowState _lastNonMinimizedState = WindowState.Normal;

    public MainWindow(ShellViewModel viewModel, ISettingsService settings, IAppearanceService appearance, IAppLogger logger)
    {
        InitializeComponent();
        DataContext = viewModel;
        _settings = settings;
        _appearance = appearance;
        _logger = logger;

        RestoreWindowState();
        _appearance.AnimationSpeedChanged += OnAnimationSpeedChanged;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        StateChanged += OnWindowStateChanged;
        LocationChanged += OnWindowBoundsChanged;
        SizeChanged += OnWindowBoundsChanged;
        UiNotificationHub.Published += OnUiNotificationPublished;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ChromeCloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }


    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
        }
    }

    private void RestoreWindowState()
    {
        if (_settings.Current.RememberWindowState)
        {
            var saved = _settings.Current.Window;
            Width = Math.Max(MinWidth, saved.Width);
            Height = Math.Max(MinHeight, saved.Height);

            if (saved.Left.HasValue && saved.Top.HasValue && IsPositionVisible(saved.Left.Value, saved.Top.Value, Width, Height))
            {
                Left = saved.Left.Value;
                Top = saved.Top.Value;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }

            if (saved.IsMaximized)
            {
                WindowState = WindowState.Maximized;
                _lastNonMinimizedState = WindowState.Maximized;
            }
        }

        if (_settings.Current.StartMaximized)
        {
            WindowState = WindowState.Maximized;
            _lastNonMinimizedState = WindowState.Maximized;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayoutResources();
        SidebarColumn.Width = new GridLength(CollapsedSidebarWidth);
        SidebarClipGeometry.Rect = new Rect(0, 0, CollapsedSidebarWidth, 10000);
        WorkspaceShift.X = 0;
        CollapsedBrandPanel.Opacity = 1;
        SidebarBrandPanel.Opacity = 0;
        SidebarHost.Tag = "False";

        // Event effects start only after the real window is loaded. This avoids touching animation
        // objects during startup construction and removes the old splash/reveal race entirely.
        Dispatcher.BeginInvoke(new Action(RestartEventAnimations), DispatcherPriority.Background);

        // Reapply the requested state after the HWND has been shown. This avoids startup/reveal
        // timing from leaving a saved maximized window visually restored.
        if (_settings.Current.StartMaximized || (_settings.Current.RememberWindowState && _settings.Current.Window.IsMaximized))
        {
            Dispatcher.BeginInvoke(() =>
            {
                WindowState = WindowState.Maximized;
                _lastNonMinimizedState = WindowState.Maximized;
                ResetRevealScale();
            }, DispatcherPriority.Loaded);
        }
    }

    private static bool IsPositionVisible(double left, double top, double width, double height)
    {
        const double minimumVisible = 80;
        var right = left + width;
        var bottom = top + height;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        return right >= virtualLeft + minimumVisible &&
               left <= virtualRight - minimumVisible &&
               bottom >= virtualTop + minimumVisible &&
               top <= virtualBottom - minimumVisible;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);
        ApplyModernWindowChrome();
    }

    private void ApplyModernWindowChrome()
    {
        if (_windowHandle == IntPtr.Zero) return;
        try
        {
            var dark = 1;
            _ = DwmSetWindowAttribute(_windowHandle, 20, ref dark, sizeof(int));
            var corner = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(_windowHandle, 33, ref corner, sizeof(int));
        }
        catch (Exception ex)
        {
            _logger.Warning($"Modern window chrome could not be fully enabled: {ex.Message}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            ApplyMonitorWorkArea(hwnd, lParam);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg != WmAppTray)
            return IntPtr.Zero;

        var mouseMessage = unchecked((uint)lParam.ToInt64());
        switch (mouseMessage)
        {
            case WmLButtonUp:
            case WmLButtonDblClk:
                RestoreFromTray();
                handled = true;
                break;
            case WmRButtonUp:
                ShowTrayMenu();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        PersistWindowState();

        if (!_allowRealClose && _settings.Current.CloseToTray)
        {
            // Create the tray icon BEFORE cancelling the close. If Windows refuses the icon,
            // allow the normal close instead of leaving the window in a cancelled/frozen state.
            if (EnsureTrayIcon())
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(() =>
                {
                    ShowInTaskbar = false;
                    Hide();
                    ShowTrayNotification("Sabby Optimizer", "Still running in the notification area.");
                    _ = SaveSettingsAsyncSafe();
                    _logger.Info("Main window hidden to notification area from Close.");
                }, DispatcherPriority.ApplicationIdle);
                return;
            }

            _logger.Warning("Close-to-tray was requested, but the notification icon could not be created. Closing normally instead.");
        }

        SaveSettingsBlockingSafe();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            _lastNonMinimizedState = WindowState.Maximized;
            ResetRevealScale();
            Dispatcher.BeginInvoke(new Action(UpdateResponsiveLayoutResources), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(RestartEventAnimations), DispatcherPriority.Background);
        }
        else if (WindowState == WindowState.Normal)
        {
            _lastNonMinimizedState = WindowState.Normal;
            Dispatcher.BeginInvoke(new Action(UpdateResponsiveLayoutResources), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(RestartEventAnimations), DispatcherPriority.Background);
        }
        else if (WindowState == WindowState.Minimized && _settings.Current.MinimizeToTray && !_handlingTrayMinimize)
        {
            _handlingTrayMinimize = true;
            Dispatcher.BeginInvoke(() =>
            {
                if (!HideToTray("Main window hidden to notification area from Minimize."))
                    WindowState = _lastNonMinimizedState;
                _handlingTrayMinimize = false;
            }, DispatcherPriority.Background);
            return;
        }

        PersistWindowState();
        _ = SaveSettingsAsyncSafe();
    }

    private void OnWindowBoundsChanged(object? sender, EventArgs e)
    {
        UpdateResponsiveLayoutResources();

        // Keep the in-memory restore bounds current. Disk persistence still happens on
        // state changes/close so dragging the window does not cause constant file writes.
        if (WindowState == WindowState.Normal)
            PersistWindowState();
    }

    private void UpdateResponsiveLayoutResources()
    {
        // CardColumns is an explicit user preference. Do not silently change 4 to 3 because
        // of DPI scaling or maximized-window device-independent width.
        var requested = Math.Clamp(_settings.Current.CardColumns, 1, 4);
        Application.Current.Resources["CardColumns"] = requested;
    }

    private void PersistWindowState()
    {
        if (!_settings.Current.RememberWindowState)
            return;

        try
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            var window = _settings.Current.Window;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                window.Width = bounds.Width;
                window.Height = bounds.Height;
                window.Left = bounds.Left;
                window.Top = bounds.Top;
            }
            window.IsMaximized = WindowState == WindowState.Maximized ||
                                 (WindowState == WindowState.Minimized && _lastNonMinimizedState == WindowState.Maximized);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to capture window state.", ex);
        }
    }

    private async Task SaveSettingsAsyncSafe()
    {
        try
        {
            await _settings.SaveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to persist settings.", ex);
        }
    }

    private void SaveSettingsBlockingSafe()
    {
        try
        {
            Task.Run(async () => await _settings.SaveAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to persist settings during shutdown.", ex);
        }
    }

    private bool HideToTray(string logMessage)
    {
        PersistWindowState();
        if (!EnsureTrayIcon())
            return false;

        ShowInTaskbar = false;
        Hide();
        ShowTrayNotification("Sabby Optimizer", "Still running in the notification area.");
        _ = SaveSettingsAsyncSafe();
        _logger.Info(logMessage);
        return true;
    }

    private bool EnsureTrayIcon()
    {
        if (_trayIconVisible)
            return true;
        if (_windowHandle == IntPtr.Zero)
            return false;

        var data = CreateNotifyIconData();
        _trayIconVisible = Shell_NotifyIcon(NimAdd, ref data);
        return _trayIconVisible;
    }

    private NotifyIconData CreateNotifyIconData()
    {
        if (_trayIconHandle == IntPtr.Zero)
        {
            try
            {
                var executable = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(executable))
                {
                    _trayIconHandle = ExtractIcon(IntPtr.Zero, executable, 0);
                    _trayIconHandleOwned = _trayIconHandle != IntPtr.Zero;
                }
            }
            catch { }

            if (_trayIconHandle == IntPtr.Zero)
                _trayIconHandle = LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication));
        }

        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _windowHandle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmAppTray,
            hIcon = _trayIconHandle,
            szTip = "Sabby Optimizer",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private void ShowTrayNotification(string title, string message)
    {
        if (!_trayIconVisible || _windowHandle == IntPtr.Zero)
            return;

        try
        {
            var data = CreateNotifyIconData();
            data.uFlags |= NifInfo;
            data.szInfoTitle = title;
            data.szInfo = message;
            data.dwInfoFlags = NiifInfo;
            data.uTimeoutOrVersion = 2500;
            Shell_NotifyIcon(0x00000001, ref data); // NIM_MODIFY
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not show tray notification: {ex.Message}");
        }
    }

    private void RemoveTrayIcon()
    {
        if (!_trayIconVisible || _windowHandle == IntPtr.Zero)
            return;

        var data = CreateNotifyIconData();
        Shell_NotifyIcon(NimDelete, ref data);
        _trayIconVisible = false;
    }

    public void EnsureVisibleAndActivated()
    {
        // Startup safety: never allow a healthy main process to remain invisible because of a
        // stale tray/window-state value, an off-screen saved position, or a half-finished reveal.
        ShowInTaskbar = true;
        if (!IsVisible)
            Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        ResetRevealScale();

        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        if (double.IsNaN(Left) || double.IsNaN(Top) || !IsPositionVisible(Left, Top, width, height))
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + Math.Max(0, (area.Width - width) / 2);
            Top = area.Top + Math.Max(0, (area.Height - height) / 2);
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void RestoreFromTray()
    {
        RemoveTrayIcon();
        ShowInTaskbar = true;
        Show();
        WindowState = _settings.Current.StartMaximized ||
                      (_settings.Current.RememberWindowState && _settings.Current.Window.IsMaximized)
            ? WindowState.Maximized
            : _lastNonMinimizedState;
        ResetRevealScale();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ShowTrayMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            AppendMenu(menu, MfString, new IntPtr(1), "Open Sabby Optimizer");
            AppendMenu(menu, MfSeparator, IntPtr.Zero, string.Empty);
            AppendMenu(menu, MfString, new IntPtr(2), "Exit");

            GetCursorPos(out var point);
            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenu(menu, TpmReturnCmd | TpmNonotify, point.X, point.Y, 0, _windowHandle, IntPtr.Zero);
            if (command == 1) RestoreFromTray();
            else if (command == 2) ExitFromTray();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void ExitFromTray()
    {
        _allowRealClose = true;
        RemoveTrayIcon();
        ShowInTaskbar = true;
        Show();
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _appearance.AnimationSpeedChanged -= OnAnimationSpeedChanged;
        LocationChanged -= OnWindowBoundsChanged;
        SizeChanged -= OnWindowBoundsChanged;
        UiNotificationHub.Published -= OnUiNotificationPublished;
        RemoveTrayIcon();
        if (_trayIconHandleOwned && _trayIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_trayIconHandle);
            _trayIconHandle = IntPtr.Zero;
            _trayIconHandleOwned = false;
        }
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
        foreach (var clock in _dropClocks.Values)
            clock.Controller?.Remove();
        _dropClocks.Clear();
    }

    public void PrepareStartupReveal()
    {
        // First paint must be immediate and pixel-stable. Do not animate the entire WPF tree:
        // full-window opacity/scale reveals delay perceived startup and can soften text.
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
    }

    public void PlayStartupReveal()
    {
        // Intentionally no-op: the real interactive shell is shown immediately.
        Opacity = 1;
    }

    private void ResetRevealScale()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
    }

    public void RestartEventAnimations()
    {
        foreach (var clock in _dropClocks.Values)
            clock.Controller?.Remove();
        _dropClocks.Clear();

        // Do not keep ten forever-running animation clocks alive for themes where the
        // Blood Bath overlay is collapsed. This was measurable dispatcher/composition work
        // with zero visible benefit and contributed to the "30 FPS" feeling.
        if (Application.Current.TryFindResource("BloodBathOverlayVisibility") is not Visibility overlay ||
            overlay != Visibility.Visible)
            return;

        // Start well above the clipped viewport, then travel past the full current screen height.
        // This avoids the old pop-in-at-top and disappearing-halfway behavior.
        var startY = -520d;
        // Travel beyond the actual application viewport on every loop. The prior fixed distance
        // could make droplets appear to vanish around the middle on tall/maximized windows.
        var viewportHeight = Math.Max(ActualHeight, SystemParameters.WorkArea.Height);
        var endY = viewportHeight + 720d;

        // Sidebar droplets are intentionally not animated: that canvas is collapsed and
        // running invisible animation clocks wastes composition/dispatcher time. Four subtle
        // content drops are enough to communicate the style without making the UI feel busy.
        StartDropAnimation(MainDrop1, startY, endY, 11.2, 0.5);
        StartDropAnimation(MainDrop2, startY, endY, 12.6, 4.0);
        UpdateEventAnimationSpeed(_appearance.AnimationSpeed);
    }

    private void StartDropAnimation(FrameworkElement element, double from, double to, double seconds, double delaySeconds)
    {
        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            BeginTime = TimeSpan.FromSeconds(delaySeconds),
            RepeatBehavior = RepeatBehavior.Forever,
            FillBehavior = FillBehavior.HoldEnd
        };
        var clock = animation.CreateClock();
        transform.ApplyAnimationClock(TranslateTransform.YProperty, clock, HandoffBehavior.SnapshotAndReplace);
        _dropClocks[element] = clock;
    }

    private void OnAnimationSpeedChanged(double speed)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateEventAnimationSpeed(speed));
            return;
        }
        UpdateEventAnimationSpeed(speed);
    }

    private void UpdateEventAnimationSpeed(double speed)
    {
        var ratio = Math.Clamp(speed, 0, 200) / 100d;
        foreach (var clock in _dropClocks.Values)
        {
            var controller = clock.Controller;
            if (controller is null) continue;
            if (ratio <= 0)
            {
                controller.Pause();
            }
            else
            {
                controller.SpeedRatio = ratio;
                controller.Resume();
            }
        }
    }


    private void MainScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Kept for binary/XAML compatibility with older local builds. Current XAML no longer
        // hooks this event; RequestBringIntoView suppression alone prevents toggle scroll jumps
        // without scheduling extra dispatcher work on every click.
    }

    private void MainScrollViewer_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // Pointer-triggered buttons inside dense tweak cards should never auto-scroll the outer
        // workspace. Keyboard navigation and non-button content keep normal BringIntoView behavior.
        if (FindVisualAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            e.Handled = true;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnUiNotificationPublished(UiNotification notification)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnUiNotificationPublished(notification));
            return;
        }

        ToastTitleText.Text = notification.Title;
        ToastMessageText.Text = notification.Message;
        ToastAccent.Background = notification.Kind switch
        {
            UiNotificationKind.Success => new SolidColorBrush(Color.FromRgb(49, 184, 102)),
            UiNotificationKind.Warning => new SolidColorBrush(Color.FromRgb(219, 155, 52)),
            UiNotificationKind.Error => new SolidColorBrush(Color.FromRgb(211, 59, 73)),
            _ => Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue
        };

        ToastHost.Visibility = Visibility.Visible;
        ToastHost.BeginAnimation(OpacityProperty, null);
        if (ToastHost.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            ToastHost.RenderTransform = transform;
        }

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.7))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.05)), new CubicEase { EasingMode = EasingMode.EaseIn }));
        fade.Completed += (_, _) => ToastHost.Visibility = Visibility.Collapsed;

        var slide = new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        ToastHost.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
    }

    private void SidebarHost_MouseEnter(object sender, MouseEventArgs e) => SetSidebarExpanded(true);

    private void SidebarHost_MouseLeave(object sender, MouseEventArgs e) => SetSidebarExpanded(false);

    private void SetSidebarExpanded(bool expanded)
    {
        var targetWidth = expanded ? ExpandedSidebarWidth : CollapsedSidebarWidth;
        var currentClipWidth = SidebarClipGeometry.Rect.Width;
        if (_sidebarExpanded == expanded && Math.Abs(currentClipWidth - targetWidth) < 0.5)
            return;

        _sidebarExpanded = expanded;
        SidebarHost.Tag = expanded ? "True" : "False";
        AnimateSidebarBranding(expanded);

        // Overlay the expanded rail instead of resizing the whole window layout. This keeps dense
        // pages perfectly still and removes the expensive measure/arrange pass on every hover.
        SidebarColumn.Width = new GridLength(CollapsedSidebarWidth);
        WorkspaceShift.BeginAnimation(TranslateTransform.XProperty, null);
        WorkspaceShift.X = 0;
        SidebarClipGeometry.BeginAnimation(RectangleGeometry.RectProperty, null);

        var distance = Math.Abs(targetWidth - currentClipWidth);
        var durationMs = Math.Clamp(75d + (distance / (ExpandedSidebarWidth - CollapsedSidebarWidth) * 45d), 75d, 120d);
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var generation = ++_sidebarAnimationGeneration;

        var clip = new RectAnimation(
            new Rect(0, 0, currentClipWidth, 10000),
            new Rect(0, 0, targetWidth, 10000),
            duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        clip.Completed += (_, _) =>
        {
            if (generation != _sidebarAnimationGeneration) return;
            SidebarClipGeometry.BeginAnimation(RectangleGeometry.RectProperty, null);
            SidebarClipGeometry.Rect = new Rect(0, 0, targetWidth, 10000);
        };

        SidebarClipGeometry.BeginAnimation(RectangleGeometry.RectProperty, clip, HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateSidebarBranding(bool expanded)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(expanded ? 145 : 105);
        AnimateOpacity(CollapsedBrandPanel, expanded ? 0 : 1, duration, ease);
        AnimateOpacity(SidebarBrandPanel, expanded ? 1 : 0, duration, ease);
    }

    private static void AnimateOpacity(UIElement element, double target, TimeSpan duration, IEasingFunction ease)
    {
        var animation = new DoubleAnimation(element.Opacity, target, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = target;
        };
        element.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void MainContentHost_TargetUpdated(object sender, DataTransferEventArgs e)
    {
        // Kept for compatibility with older cached XAML. Current navigation swaps content
        // without animating the entire page tree, which avoids tab-click stutter/crashes.
        MainContentHost.BeginAnimation(OpacityProperty, null);
        MainContentHost.Opacity = 1;
    }

    private static void ApplyMonitorWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return;

        try
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return;

            var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo)) return;

            var work = monitorInfo.rcWork;
            var monitorRect = monitorInfo.rcMonitor;
            info.ptMaxPosition.X = work.Left - monitorRect.Left;
            info.ptMaxPosition.Y = work.Top - monitorRect.Top;
            info.ptMaxSize.X = work.Right - work.Left;
            info.ptMaxSize.Y = work.Bottom - work.Top;
            info.ptMaxTrackSize = info.ptMaxSize;
            Marshal.StructureToPtr(info, lParam, false);
        }
        catch
        {
            // Windows will fall back to its normal maximize behavior if monitor metrics are unavailable.
        }
    }
    public void RecoverPresentationAnimations()
    {
        try
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            MainContentHost.BeginAnimation(OpacityProperty, null);
            MainContentHost.Opacity = 1;

            if (MainContentHost.RenderTransform is TranslateTransform pageTransform)
            {
                if (pageTransform.IsFrozen)
                {
                    pageTransform = pageTransform.CloneCurrentValue();
                    MainContentHost.RenderTransform = pageTransform;
                }
                pageTransform.BeginAnimation(TranslateTransform.YProperty, null);
                pageTransform.Y = 0;
            }

        }
        catch (Exception ex)
        {
            _logger.Warning($"Presentation recovery could not fully reset animations: {ex.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint ptReserved;
        public NativePoint ptMaxSize;
        public NativePoint ptMaxPosition;
        public NativePoint ptMinTrackSize;
        public NativePoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    private const uint MonitorDefaultToNearest = 0x00000002;

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ExtractIcon(IntPtr hInst, string pszExeFileName, uint nIconIndex);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint lpPoint);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
}
