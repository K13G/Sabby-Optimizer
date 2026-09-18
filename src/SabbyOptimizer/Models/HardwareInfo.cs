namespace PCTweaker.Models;

public sealed class HardwareInfo
{
    public string Processor { get; init; } = "Unavailable";
    public string ProcessorDetails { get; init; } = "Unavailable";
    public string Graphics { get; init; } = "Unavailable";
    public string GraphicsDetails { get; init; } = "Unavailable";
    public string Memory { get; init; } = "Unavailable";
    public string MemoryDetails { get; init; } = "Unavailable";
    public string Motherboard { get; init; } = "Unavailable";
    public string MotherboardDetails { get; init; } = "Unavailable";
    public string Windows { get; init; } = "Unavailable";
    public string WindowsDetails { get; init; } = "Unavailable";
    public string SystemDrive { get; init; } = "Unavailable";
    public string Network { get; init; } = "Unavailable";
    public string NetworkDetails { get; init; } = "Unavailable";
    public string SupportedFeatures { get; init; } = "Unavailable";
    public string DeviceName { get; init; } = Environment.MachineName;
}
