namespace PCTweaker.Models.GameProfiles;

public sealed record GameScanLibraryInfo(bool HasCompletedScan, DateTime? LastScanAtUtc, int SavedProfileCount);
