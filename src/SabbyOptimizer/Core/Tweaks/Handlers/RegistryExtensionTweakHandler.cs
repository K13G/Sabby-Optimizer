using System.Text.Json;
using Microsoft.Win32;
using PCTweaker.Core.Services;
using PCTweaker.Models;
using PCTweaker.Models.Tweaks;

namespace PCTweaker.Core.Tweaks.Handlers;

public sealed class RegistryExtensionTweakHandler : ITweakHandler
{
    private sealed class RestoreState
    {
        public bool Existed { get; set; }
        public string? ValueType { get; set; }
        public string? Value { get; set; }
    }

    private readonly TweakRuleExtensionManifest _extension;
    private readonly TweakRuleExtensionRule _rule;
    private readonly string _restoreFile;

    public RegistryExtensionTweakHandler(TweakRuleExtensionManifest extension, TweakRuleExtensionRule rule, IAppPaths paths)
    {
        _extension = extension;
        _rule = rule;
        var dir = Path.Combine(paths.UserDataDirectory, "TweakState", "Extensions");
        Directory.CreateDirectory(dir);
        _restoreFile = Path.Combine(dir, SanitizeFileName($"{extension.Id}.{rule.Id}.json"));
        Definition = BuildDefinition(extension, rule);
    }

    public TweakDefinition Definition { get; }

    public Task<TweakDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var (hive, writable) = OpenHive(_rule.RegistryHive, false);
            using (hive)
            using (var key = hive.OpenSubKey(_rule.RegistryPath, writable))
            {
                var current = key?.GetValue(_rule.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var matches = ValueMatches(current, _rule.ValueType, _rule.ApplyValue);
                var canUndo = File.Exists(_restoreFile);
                if (matches)
                    return Task.FromResult(new TweakDetectionResult(TweakStateKind.Applied, "Extension rule active", $"{_extension.Name}: {_rule.ValueName} matches the extension target value.", false, canUndo));

                return Task.FromResult(new TweakDetectionResult(TweakStateKind.NotApplied, "Not applied", $"{_extension.Name}: the registry value does not match the extension target.", true, canUndo));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(TweakDetectionResult.Error($"Extension rule read-back failed: {ex.Message}"));
        }
    }

    public Task<TweakOperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            CaptureOriginalIfMissing();
            var (hive, _) = OpenHive(_rule.RegistryHive, true);
            using (hive)
            using (var key = hive.CreateSubKey(_rule.RegistryPath, writable: true) ?? throw new InvalidOperationException("Registry key could not be opened."))
            {
                var (value, kind) = ParseApplyValue(_rule.ValueType, _rule.ApplyValue);
                key.SetValue(_rule.ValueName, value, kind);
            }

            return Task.FromResult(TweakOperationResult.Completed(
                $"Extension rule '{_rule.Name}' was applied.",
                new TweakDetectionResult(TweakStateKind.Applied, "Extension rule active", "Registry value written by the declarative extension rule.", false, true)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(TweakOperationResult.Failed($"Extension rule failed safely: {ex.Message}"));
        }
    }

    public Task<TweakOperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!File.Exists(_restoreFile))
                return Task.FromResult(TweakOperationResult.Failed("No saved pre-extension value exists, so Sabby did not guess."));

            var restore = JsonSerializer.Deserialize<RestoreState>(File.ReadAllText(_restoreFile)) ?? throw new InvalidDataException("Saved extension restore state is invalid.");
            var (hive, _) = OpenHive(_rule.RegistryHive, true);
            using (hive)
            using (var key = hive.CreateSubKey(_rule.RegistryPath, writable: true) ?? throw new InvalidOperationException("Registry key could not be opened."))
            {
                if (!restore.Existed)
                {
                    key.DeleteValue(_rule.ValueName, throwOnMissingValue: false);
                }
                else
                {
                    var (value, kind) = ParseApplyValue(restore.ValueType ?? "String", restore.Value ?? string.Empty);
                    key.SetValue(_rule.ValueName, value, kind);
                }
            }

            try { File.Delete(_restoreFile); } catch { }
            return Task.FromResult(TweakOperationResult.Completed(
                $"Extension rule '{_rule.Name}' was restored to the saved pre-extension state.",
                new TweakDetectionResult(TweakStateKind.Custom, "Restored", "The exact saved registry value was restored.", true, false)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(TweakOperationResult.Failed($"Extension restore failed safely: {ex.Message}"));
        }
    }

    private void CaptureOriginalIfMissing()
    {
        if (File.Exists(_restoreFile)) return;
        var (hive, _) = OpenHive(_rule.RegistryHive, false);
        using (hive)
        using (var key = hive.OpenSubKey(_rule.RegistryPath, writable: false))
        {
            var names = key?.GetValueNames() ?? Array.Empty<string>();
            var existed = names.Contains(_rule.ValueName, StringComparer.OrdinalIgnoreCase);
            object? value = existed ? key?.GetValue(_rule.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null;
            RegistryValueKind? kind = existed ? key?.GetValueKind(_rule.ValueName) : null;
            var state = new RestoreState
            {
                Existed = existed,
                ValueType = kind switch
                {
                    RegistryValueKind.DWord => "DWord",
                    RegistryValueKind.QWord => "QWord",
                    RegistryValueKind.String or RegistryValueKind.ExpandString => "String",
                    _ => "String"
                },
                Value = value?.ToString()
            };
            File.WriteAllText(_restoreFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static TweakDefinition BuildDefinition(TweakRuleExtensionManifest extension, TweakRuleExtensionRule rule)
    {
        var category = Enum.TryParse<TweakCategory>(rule.Category, true, out var parsedCategory) ? parsedCategory : TweakCategory.Advanced;
        var safety = Enum.TryParse<TweakSafetyLevel>(rule.Safety, true, out var parsedSafety) ? parsedSafety : TweakSafetyLevel.Moderate;
        var admin = rule.RegistryHive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) || rule.RegistryHive.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);
        return new TweakDefinition(
            $"ext.{extension.Id}.{rule.Id}",
            rule.Name,
            string.IsNullOrWhiteSpace(rule.Description) ? $"Rule supplied by {extension.Name}." : rule.Description,
            category,
            safety,
            $"Declarative tweak-rule extension '{extension.Name}' v{extension.Version} by {extension.Author}. Sabby only allows this extension format to write one declared registry value; arbitrary scripts are not executed.",
            $"{rule.RegistryHive}\\{rule.RegistryPath}\\{rule.ValueName} = {rule.ApplyValue} ({rule.ValueType}).",
            "Undo restores the exact registry value captured before the extension rule was applied.",
            admin,
            false,
            true,
            true);
    }

    private static (RegistryKey Hive, bool Writable) OpenHive(string hiveName, bool writable)
    {
        if (hiveName.Equals("HKCU", StringComparison.OrdinalIgnoreCase) || hiveName.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, writable);
        if (hiveName.Equals("HKLM", StringComparison.OrdinalIgnoreCase) || hiveName.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, writable);
        throw new InvalidOperationException("Only HKCU and HKLM registry hives are allowed in tweak-rule extensions.");
    }

    private static (object Value, RegistryValueKind Kind) ParseApplyValue(string type, string raw) => type.Trim().ToLowerInvariant() switch
    {
        "dword" => (ParseInteger32(raw), RegistryValueKind.DWord),
        "qword" => (ParseInteger64(raw), RegistryValueKind.QWord),
        "string" => (raw, RegistryValueKind.String),
        _ => throw new InvalidOperationException("Extension ValueType must be DWord, QWord, or String.")
    };

    private static int ParseInteger32(string raw) => raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? Convert.ToInt32(raw[2..], 16)
        : int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);

    private static long ParseInteger64(string raw) => raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? Convert.ToInt64(raw[2..], 16)
        : long.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);

    private static bool ValueMatches(object? current, string type, string raw)
    {
        if (current is null) return false;
        try
        {
            var (target, kind) = ParseApplyValue(type, raw);
            return kind switch
            {
                RegistryValueKind.DWord => Convert.ToInt32(current) == Convert.ToInt32(target),
                RegistryValueKind.QWord => Convert.ToInt64(current) == Convert.ToInt64(target),
                _ => string.Equals(Convert.ToString(current), Convert.ToString(target), StringComparison.Ordinal)
            };
        }
        catch { return false; }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value;
    }
}
