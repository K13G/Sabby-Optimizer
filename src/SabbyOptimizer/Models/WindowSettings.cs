namespace PCTweaker.Models;

public sealed class WindowSettings
{
    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 760;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool IsMaximized { get; set; }

    public void Normalize()
    {
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 980, 7680) : 1180;
        Height = double.IsFinite(Height) ? Math.Clamp(Height, 640, 4320) : 760;

        if (Left is { } left && !double.IsFinite(left)) Left = null;
        if (Top is { } top && !double.IsFinite(top)) Top = null;
    }
}
