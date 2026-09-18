namespace PCTweaker.Models;

public sealed record DetectedGame(
    string Name,
    string Platform,
    string ExecutablePath,
    string InstallDirectory,
    string SourceId = "",
    string? ArtworkPath = null);
