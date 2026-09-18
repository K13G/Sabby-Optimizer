namespace PCTweaker.Models;

public sealed record RestorePointInfo(
    int SequenceNumber,
    string Description,
    DateTime CreatedAt,
    int RestorePointType)
{
    public string CreatedText => CreatedAt.ToString("MMM d, yyyy • h:mm tt");
    public string TypeText => RestorePointType switch
    {
        0 => "Application install",
        1 => "Application uninstall",
        10 => "Device driver",
        12 => "Settings change",
        13 => "Cancelled operation",
        _ => $"Type {RestorePointType}"
    };
}
