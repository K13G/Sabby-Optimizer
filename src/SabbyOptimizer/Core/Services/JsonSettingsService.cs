using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class JsonSettingsService : ISettingsService
{
    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Current { get; private set; } = new();

    public JsonSettingsService(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_paths.SettingsFile))
            {
                Current = new AppSettings();
                await SaveCoreAsync(cancellationToken);
                _logger.Info("Created default settings file.");
                return;
            }

            await using var stream = File.OpenRead(_paths.SettingsFile);
            Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _jsonOptions, cancellationToken)
                      ?? new AppSettings();

            Current.Normalize();
            _logger.Info("Settings loaded successfully.");
        }
        catch (JsonException ex)
        {
            BackupCorruptSettings();
            Current = new AppSettings();
            _logger.Error("Settings JSON was invalid. Defaults were loaded and the corrupt file was preserved.", ex);
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Current.Normalize();
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Current = new AppSettings();
            await SaveCoreAsync(cancellationToken);
            _logger.Info("Settings reset to defaults.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsFile)!);
        var tempFile = _paths.SettingsFile + ".tmp";

        await using (var stream = File.Create(tempFile))
            await JsonSerializer.SerializeAsync(stream, Current, _jsonOptions, cancellationToken);

        File.Move(tempFile, _paths.SettingsFile, true);
    }

    private void BackupCorruptSettings()
    {
        try
        {
            if (!File.Exists(_paths.SettingsFile))
                return;

            var settingsDirectory = Path.GetDirectoryName(_paths.SettingsFile)!;
            Directory.CreateDirectory(settingsDirectory);
            var backup = Path.Combine(
                settingsDirectory,
                $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(_paths.SettingsFile, backup, true);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not preserve corrupt settings file: {ex.Message}");
        }
    }
}
