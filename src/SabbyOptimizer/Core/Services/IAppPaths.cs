namespace PCTweaker.Core.Services;

public interface IAppPaths
{
    string AppDataDirectory { get; }
    string UserDataDirectory { get; }
    string SettingsFile { get; }
    string LogsDirectory { get; }
    string BackupsDirectory { get; }
    string BackupSnapshotsDirectory { get; }
    string OriginalStateFile { get; }
    string PresetsDirectory { get; }
    string GameProfilesDirectory { get; }
    string GameProfilesFile { get; }
}
