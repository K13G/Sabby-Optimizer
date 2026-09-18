using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class AppearanceService : IAppearanceService
{
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _animationTimer;
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private AppearancePalette _activePalette;
    private bool _hasPalette;
    private double _cyclePosition;
    private double _lastTickSeconds;

    public VisualStyle ActiveStyle { get; private set; } = VisualStyle.SabbyBlue;
    public double Intensity { get; private set; } = 100;
    public double AnimationSpeed { get; private set; } = 100;

    public event Action<double>? AnimationSpeedChanged;
    public event Action? AppearanceChanged;

    public AppearanceService(IAppLogger logger)
    {
        _logger = logger;
        _lastTickSeconds = _animationClock.Elapsed.TotalSeconds;
        // Animated palettes are slow color cycles, not motion-critical UI. Keep them at a low
        // refresh rate and below input/layout priority so global DynamicResource color invalidation
        // cannot compete with scrolling, hover, navigation, or tweak-state detection. Motion
        // animations use compositor-friendly transforms and remain smooth independently.
        _animationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _animationTimer.Tick += (_, _) => RenderAnimatedFrame();
    }

    public void Apply(VisualStyle style, double intensity = 100, double animationSpeed = 100)
    {
        var styleChanged = !_hasPalette || ActiveStyle != style;
        var clampedSpeed = Math.Clamp(animationSpeed, 0, 200);
        var speedChanged = Math.Abs(AnimationSpeed - clampedSpeed) > 0.001;

        if (_hasPalette && _activePalette.Animated)
            AdvancePhase();

        if (styleChanged)
        {
            _animationTimer.Stop();
            ActiveStyle = style;
            _hasPalette = true;
            _cyclePosition = 0;
            _lastTickSeconds = _animationClock.Elapsed.TotalSeconds;
        }

        // Theme switching replaces the merged ResourceDictionary. Reload the requested visual
        // palette every time so the newly-created theme cannot fall back to its default blue.
        _activePalette = GetPalette(style);

        Intensity = Math.Clamp(intensity, 0, 100);
        AnimationSpeed = clampedSpeed;
        SetEventResources(style, Intensity);

        if (_activePalette.Animated && AnimationSpeed > 0)
        {
            _lastTickSeconds = _animationClock.Elapsed.TotalSeconds;
            if (!_animationTimer.IsEnabled)
                _animationTimer.Start();
        }
        else
        {
            _animationTimer.Stop();
            _lastTickSeconds = _animationClock.Elapsed.TotalSeconds;
        }

        RenderFrame(_cyclePosition);

        if (speedChanged)
            AnimationSpeedChanged?.Invoke(AnimationSpeed);

        if (styleChanged)
        {
            _logger.Info($"Visual style applied: {style}{(_activePalette.Animated ? " (animated)" : string.Empty)}, intensity {Intensity:0}%, speed {AnimationSpeed:0}%.");
            AppearanceChanged?.Invoke();
        }
    }

    private void RenderAnimatedFrame()
    {
        if (!_activePalette.Animated || _activePalette.DurationSeconds <= 0 || AnimationSpeed <= 0)
            return;

        // DynamicResource color changes invalidate a large part of the WPF tree. Do not spend
        // dispatcher/composition time animating a window the user cannot currently see/interact with.
        if (!HasInteractiveWindow())
        {
            _lastTickSeconds = _animationClock.Elapsed.TotalSeconds;
            return;
        }

        try
        {
            AdvancePhase();
            RenderFrame(_cyclePosition);
        }
        catch (InvalidOperationException ex)
        {
            // A visual resource update must never take down the UI dispatcher. If Windows/WPF
            // promotes a presentation resource to a frozen/read-only state, stop only the animated
            // color cycle and keep the rest of Sabby fully usable.
            _animationTimer.Stop();
            _logger.Warning($"Animated appearance update was stopped safely: {ex.Message}");
        }
    }

    private static bool HasInteractiveWindow()
    {
        try
        {
            return Application.Current?.Windows
                .OfType<Window>()
                .Any(window => window.IsVisible &&
                               window.WindowState != WindowState.Minimized &&
                               window.IsActive) == true;
        }
        catch
        {
            return true;
        }
    }

    private void AdvancePhase()
    {
        var now = _animationClock.Elapsed.TotalSeconds;
        var elapsed = Math.Max(0, now - _lastTickSeconds);
        _lastTickSeconds = now;

        if (!_hasPalette || !_activePalette.Animated || _activePalette.DurationSeconds <= 0 || AnimationSpeed <= 0)
            return;

        var speedRatio = AnimationSpeed / 100d;
        _cyclePosition = (_cyclePosition + (elapsed * speedRatio / _activePalette.DurationSeconds)) % 1d;
    }

    private void RenderFrame(double position)
    {
        var rawPrimary = _activePalette.Animated
            ? CycleColor(_activePalette.Primary, _activePalette.Secondary, _activePalette.Tertiary, position)
            : _activePalette.Primary;

        var rawSecondary = _activePalette.Animated
            ? CycleColor(_activePalette.Secondary, _activePalette.Tertiary, _activePalette.Primary, position)
            : _activePalette.Secondary;

        var isLight = IsCurrentThemeLight();
        var neutral = GetCurrentSurfaceColor(isLight);
        var coreStrength = 0.64 + ((Intensity / 100d) * 0.36);
        var alphaScale = 0.12 + ((Intensity / 100d) * 0.88);
        var primary = Blend(neutral, rawPrimary, coreStrength);
        var secondary = Blend(neutral, rawSecondary, coreStrength);

        var accentSubtle = Color.FromArgb(
            ScaleAlpha(isLight ? 34 : 50, alphaScale), rawPrimary.R, rawPrimary.G, rawPrimary.B);
        var cardBorder = Color.FromArgb(
            ScaleAlpha(isLight ? 86 : 116, alphaScale), rawPrimary.R, rawPrimary.G, rawPrimary.B);
        var accentGlow = Color.FromArgb(
            ScaleAlpha(isLight ? 48 : 98, alphaScale), rawPrimary.R, rawPrimary.G, rawPrimary.B);
        var gradientStart = WithAlpha(rawPrimary, ScaleAlpha(255, 0.72 + (0.28 * Intensity / 100d)));
        var gradientEnd = WithAlpha(rawSecondary, ScaleAlpha(255, 0.72 + (0.28 * Intensity / 100d)));

        UpdateAppearanceResources(
            _activePalette.Primary,
            primary,
            secondary,
            accentSubtle,
            cardBorder,
            accentGlow,
            gradientStart,
            gradientEnd);
    }

    private static void UpdateAppearanceResources(
        Color brandAccent,
        Color accent,
        Color accentHover,
        Color accentSubtle,
        Color cardBorder,
        Color accentGlow,
        Color gradientStart,
        Color gradientEnd)
    {
        var resources = GetActiveThemeResources();

        resources["BrandAccentColor"] = brandAccent;
        resources["AccentColor"] = accent;
        resources["AccentHoverColor"] = accentHover;
        resources["AccentSubtleColor"] = accentSubtle;
        resources["CardBorderColor"] = cardBorder;
        resources["AccentGlowColor"] = accentGlow;
        resources["AccentGradientStartColor"] = gradientStart;
        resources["AccentGradientEndColor"] = gradientEnd;

        // Theme dictionaries are replaced when Dark/Light/Darkness changes. Explicitly refresh
        // the brush objects too; relying only on DynamicResource inside a frozen/shared Freezable
        // allowed the new theme's default blue to survive until restart.
        SetSolidBrush(resources, "BrandAccentBrush", brandAccent);
        SetSolidBrush(resources, "AccentBrush", accent);
        SetSolidBrush(resources, "AccentHoverBrush", accentHover);
        SetSolidBrush(resources, "AccentSubtleBrush", accentSubtle);
        SetSolidBrush(resources, "CardBorderBrush", cardBorder);
        SetSolidBrush(resources, "AccentGlowBrush", accentGlow);

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        gradient.GradientStops.Add(new GradientStop(gradientStart, 0));
        gradient.GradientStops.Add(new GradientStop(gradientEnd, 1));
        resources["AccentGradientBrush"] = gradient;
    }

    private static void SetSolidBrush(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static ResourceDictionary GetActiveThemeResources()
    {
        var applicationResources = Application.Current.Resources;
        var theme = applicationResources.MergedDictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true);
        return theme ?? applicationResources;
    }

    private static void SetEventResources(VisualStyle style, double intensity)
    {
        var isBloodBath = style == VisualStyle.BloodBath;
        Application.Current.Resources["BloodBathOverlayVisibility"] =
            isBloodBath ? Visibility.Visible : Visibility.Collapsed;
        Application.Current.Resources["BloodBathEffectOpacity"] =
            isBloodBath ? 0.04 + ((intensity / 100d) * 0.20) : 0d;
    }

    private bool IsCurrentThemeLight()
    {
        if (Application.Current.TryFindResource("AppBackgroundBrush") is not SolidColorBrush background)
            return false;

        var color = background.Color;
        var luminance = (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
        return luminance > 150;
    }

    private static Color GetCurrentSurfaceColor(bool isLight)
    {
        if (Application.Current.TryFindResource("SurfaceBrush") is SolidColorBrush surface)
            return surface.Color;
        return isLight ? Colors.White : Color.FromRgb(18, 26, 35);
    }

    private static byte ScaleAlpha(int value, double scale) =>
        (byte)Math.Clamp((int)Math.Round(value * scale), 0, 255);

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            LerpByte(from.R, to.R, amount),
            LerpByte(from.G, to.G, amount),
            LerpByte(from.B, to.B, amount));
    }

    private static byte LerpByte(byte a, byte b, double t) =>
        (byte)Math.Clamp((int)Math.Round(a + ((b - a) * t)), 0, 255);

    private static Color CycleColor(Color first, Color second, Color third, double position)
    {
        position -= Math.Floor(position);
        if (position < 1d / 3d) return Lerp(first, second, position * 3d);
        if (position < 2d / 3d) return Lerp(second, third, (position - (1d / 3d)) * 3d);
        return Lerp(third, first, (position - (2d / 3d)) * 3d);
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            LerpByte(a.R, b.R, t),
            LerpByte(a.G, b.G, t),
            LerpByte(a.B, b.B, t));
    }

    private static AppearancePalette GetPalette(VisualStyle style) => style switch
    {
        VisualStyle.SabbyBlue => new(Color.FromRgb(82, 158, 255), Color.FromRgb(57, 208, 255), Color.FromRgb(82, 158, 255), false, 0),
        VisualStyle.NeonPulse => new(Color.FromRgb(0, 229, 255), Color.FromRgb(173, 74, 255), Color.FromRgb(255, 62, 175), true, 8),
        VisualStyle.AuroraFlow => new(Color.FromRgb(43, 223, 170), Color.FromRgb(56, 189, 248), Color.FromRgb(139, 92, 246), true, 11),
        VisualStyle.PurpleFlux => new(Color.FromRgb(153, 92, 255), Color.FromRgb(211, 83, 255), Color.FromRgb(153, 92, 255), false, 0),
        VisualStyle.EmeraldCircuit => new(Color.FromRgb(24, 204, 137), Color.FromRgb(58, 226, 168), Color.FromRgb(24, 204, 137), false, 0),
        VisualStyle.SunsetDrive => new(Color.FromRgb(255, 111, 97), Color.FromRgb(255, 177, 66), Color.FromRgb(255, 111, 97), false, 0),
        VisualStyle.BloodBath => new(Color.FromRgb(205, 18, 38), Color.FromRgb(150, 8, 25), Color.FromRgb(62, 0, 8), false, 0),
        VisualStyle.Ice => new(Color.FromRgb(88, 205, 255), Color.FromRgb(183, 238, 255), Color.FromRgb(88, 205, 255), false, 0),
        VisualStyle.Inferno => new(Color.FromRgb(255, 70, 32), Color.FromRgb(255, 155, 28), Color.FromRgb(180, 20, 12), true, 7.5),
        VisualStyle.Gold => new(Color.FromRgb(236, 184, 55), Color.FromRgb(255, 221, 115), Color.FromRgb(236, 184, 55), false, 0),
        VisualStyle.Midnight => new(Color.FromRgb(77, 91, 255), Color.FromRgb(128, 72, 255), Color.FromRgb(77, 91, 255), false, 0),
        VisualStyle.Spectrum => new(Color.FromRgb(255, 70, 100), Color.FromRgb(70, 210, 255), Color.FromRgb(150, 80, 255), true, 9),
        VisualStyle.Crimson => new(Color.FromRgb(205, 36, 64), Color.FromRgb(255, 82, 104), Color.FromRgb(205, 36, 64), false, 0),
        VisualStyle.HyperBlue => new(Color.FromRgb(44, 112, 255), Color.FromRgb(31, 223, 255), Color.FromRgb(104, 71, 255), true, 7.5),
        VisualStyle.Plasma => new(Color.FromRgb(118, 58, 255), Color.FromRgb(255, 61, 206), Color.FromRgb(48, 220, 255), true, 8.5),
        VisualStyle.Carbon => new(Color.FromRgb(150, 160, 176), Color.FromRgb(214, 220, 232), Color.FromRgb(150, 160, 176), false, 0),
        VisualStyle.CyberMint => new(Color.FromRgb(44, 225, 176), Color.FromRgb(60, 218, 255), Color.FromRgb(96, 255, 210), true, 10),
        VisualStyle.ObsidianGold => new(Color.FromRgb(199, 154, 52), Color.FromRgb(255, 213, 108), Color.FromRgb(142, 103, 24), false, 0),
        VisualStyle.DeepOcean => new(Color.FromRgb(18, 96, 190), Color.FromRgb(0, 210, 225), Color.FromRgb(20, 68, 150), true, 12),
        VisualStyle.Voltage => new(Color.FromRgb(255, 218, 42), Color.FromRgb(34, 212, 255), Color.FromRgb(55, 100, 255), true, 6.5),
        VisualStyle.LaserRed => new(Color.FromRgb(220, 26, 52), Color.FromRgb(255, 72, 72), Color.FromRgb(128, 5, 22), true, 7),
        VisualStyle.Glacier => new(Color.FromRgb(116, 194, 236), Color.FromRgb(226, 249, 255), Color.FromRgb(84, 147, 201), false, 0),
        VisualStyle.Matrix => new(Color.FromRgb(25, 188, 76), Color.FromRgb(81, 255, 139), Color.FromRgb(7, 110, 49), true, 11),
        VisualStyle.CottonCandy => new(Color.FromRgb(66, 210, 255), Color.FromRgb(255, 105, 204), Color.FromRgb(159, 105, 255), true, 10),
        VisualStyle.Royal => new(Color.FromRgb(72, 92, 230), Color.FromRgb(145, 86, 255), Color.FromRgb(72, 92, 230), false, 0),
        VisualStyle.Tangerine => new(Color.FromRgb(245, 119, 35), Color.FromRgb(255, 181, 54), Color.FromRgb(245, 119, 35), false, 0),
        VisualStyle.Ultraviolet => new(Color.FromRgb(99, 45, 225), Color.FromRgb(191, 60, 255), Color.FromRgb(65, 105, 255), true, 9.5),
        VisualStyle.Monochrome => new(Color.FromRgb(196, 204, 216), Color.FromRgb(244, 247, 250), Color.FromRgb(126, 136, 150), false, 0),
        VisualStyle.SapphirePulse => new(Color.FromRgb(35, 94, 255), Color.FromRgb(31, 214, 255), Color.FromRgb(83, 116, 255), true, 11),
        VisualStyle.CherryNeon => new(Color.FromRgb(235, 39, 74), Color.FromRgb(255, 83, 167), Color.FromRgb(255, 224, 238), true, 10),
        VisualStyle.CosmicWave => new(Color.FromRgb(66, 56, 214), Color.FromRgb(153, 68, 255), Color.FromRgb(44, 164, 255), true, 13),
        VisualStyle.ToxicWave => new(Color.FromRgb(127, 230, 32), Color.FromRgb(22, 197, 94), Color.FromRgb(65, 255, 177), true, 12),
        VisualStyle.EmberGlow => new(Color.FromRgb(191, 42, 28), Color.FromRgb(255, 115, 39), Color.FromRgb(255, 179, 71), true, 14),
        VisualStyle.FrostPulse => new(Color.FromRgb(132, 216, 255), Color.FromRgb(232, 250, 255), Color.FromRgb(62, 173, 255), true, 13),
        VisualStyle.RoseQuartz => new(Color.FromRgb(226, 102, 143), Color.FromRgb(255, 183, 205), Color.FromRgb(226, 102, 143), false, 0),
        VisualStyle.LimeCircuit => new(Color.FromRgb(132, 224, 60), Color.FromRgb(54, 210, 102), Color.FromRgb(132, 224, 60), false, 0),
        VisualStyle.IceFire => new(Color.FromRgb(42, 145, 255), Color.FromRgb(245, 72, 62), Color.FromRgb(225, 240, 255), true, 15),
        VisualStyle.Eclipse => new(Color.FromRgb(106, 88, 173), Color.FromRgb(161, 133, 255), Color.FromRgb(106, 88, 173), false, 0),
        VisualStyle.Vaporwave => new(Color.FromRgb(255, 77, 194), Color.FromRgb(42, 224, 255), Color.FromRgb(143, 86, 255), true, 12),
        VisualStyle.Nova => new(Color.FromRgb(245, 166, 35), Color.FromRgb(255, 229, 129), Color.FromRgb(255, 102, 42), true, 14),
        VisualStyle.OceanNeon => new(Color.FromRgb(0, 111, 184), Color.FromRgb(0, 219, 193), Color.FromRgb(59, 201, 255), true, 14),
        VisualStyle.Sandstorm => new(Color.FromRgb(191, 133, 56), Color.FromRgb(239, 194, 103), Color.FromRgb(191, 133, 56), false, 0),
        _ => new(Color.FromRgb(82, 158, 255), Color.FromRgb(57, 208, 255), Color.FromRgb(82, 158, 255), false, 0)
    };

    private readonly record struct AppearancePalette(
        Color Primary,
        Color Secondary,
        Color Tertiary,
        bool Animated,
        double DurationSeconds);
}
