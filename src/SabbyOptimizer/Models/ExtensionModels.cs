namespace PCTweaker.Models;

public sealed class TweakRuleExtensionManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "Local extension";
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? MinimumSabbyVersion { get; set; }
    public string? UpdateManifestUrl { get; set; }
    public List<TweakRuleExtensionRule> Rules { get; set; } = new();
}

public sealed class TweakRuleExtensionRule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Advanced";
    public string Safety { get; set; } = "Moderate";
    public string RegistryHive { get; set; } = "HKCU";
    public string RegistryPath { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string ValueType { get; set; } = "DWord";
    public string ApplyValue { get; set; } = string.Empty;
}

public sealed record InstalledTweakExtensionInfo(
    string Id,
    string Name,
    string Version,
    string Author,
    string Description,
    bool Enabled,
    int RuleCount,
    string FilePath,
    bool Valid,
    string ValidationMessage,
    string? UpdateManifestUrl);

public sealed record SabbyReleaseInfo(
    bool FeedConfigured,
    bool UpdateAvailable,
    bool Mandatory,
    string CurrentVersion,
    string LatestVersion,
    string Status,
    string? DownloadUrl,
    string? Sha256,
    string? SignerThumbprint,
    string? ReleaseNotes,
    DateTimeOffset? PublishedAt);
