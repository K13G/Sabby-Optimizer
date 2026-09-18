using System.IO;

namespace PCTweaker.Core.Services;

public sealed class AppPaths : IAppPaths
{
    public string AppDataDirectory { get; }
    public string UserDataDirectory { get; }
    public string SettingsFile => Path.Combine(UserDataDirectory, "settings.json");
    public string LogsDirectory => Path.Combine(AppDataDirectory, "Logs");
    public string BackupsDirectory => Path.Combine(UserDataDirectory, "Backups");
    public string BackupSnapshotsDirectory => Path.Combine(BackupsDirectory, "Snapshots");
    public string OriginalStateFile => Path.Combine(BackupsDirectory, "original-state.json");
    public string PresetsDirectory => Path.Combine(UserDataDirectory, "Presets");
    public string GameProfilesDirectory => Path.Combine(UserDataDirectory, "GameProfiles");
    public string GameProfilesFile => Path.Combine(GameProfilesDirectory, "profiles.json");

    public AppPaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AppDataDirectory = Path.Combine(localAppData, "SabbyOptimizer");
        UserDataDirectory = Path.Combine(AppDataDirectory, "UserData");

        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(UserDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(BackupSnapshotsDirectory);
        Directory.CreateDirectory(PresetsDirectory);
        Directory.CreateDirectory(GameProfilesDirectory);

        MigrateLegacyUserData();
    }

    private void MigrateLegacyUserData()
    {
        try
        {
            var oldSettings = Path.Combine(AppDataDirectory, "settings.json");
            if (!File.Exists(SettingsFile) && File.Exists(oldSettings))
                File.Copy(oldSettings, SettingsFile, overwrite: false);

            CopyDirectoryIfNeeded(Path.Combine(AppDataDirectory, "Backups"), BackupsDirectory);
            CopyDirectoryIfNeeded(Path.Combine(AppDataDirectory, "Presets"), PresetsDirectory);
        }
        catch
        {
            // Migration is best-effort. Existing user data is never deleted or overwritten.
        }
    }

    private static void CopyDirectoryIfNeeded(string source, string destination)
    {
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            var destinationFile = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(destinationFile))
                File.Copy(file, destinationFile, overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
        {
            var destinationDirectory = Path.Combine(destination, Path.GetFileName(directory));
            CopyDirectoryIfNeeded(directory, destinationDirectory);
        }
    }
}
