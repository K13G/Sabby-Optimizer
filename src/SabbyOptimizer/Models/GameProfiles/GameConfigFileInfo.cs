namespace PCTweaker.Models.GameProfiles;

public sealed record GameConfigFileInfo(
    string FilePath,
    string DisplayName,
    string LocationLabel,
    string Extension,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    int RelevanceScore)
{
    public string SizeText => SizeBytes switch
    {
        >= 1024 * 1024 => $"{SizeBytes / 1024d / 1024d:0.##} MB",
        >= 1024 => $"{SizeBytes / 1024d:0.#} KB",
        _ => $"{SizeBytes} B"
    };

    public string ModifiedText => LastWriteTimeUtc.ToLocalTime().ToString("g");
    public string Kind => Extension.TrimStart('.').ToUpperInvariant();
}

public sealed record GameConfigDocument(
    GameConfigFileInfo File,
    string Text,
    string EncodingName,
    string ValidationSummary);
