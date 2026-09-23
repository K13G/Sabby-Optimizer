using System.Text.Json;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public interface IDebloatService
{
    Task<IReadOnlyList<DebloatAppInfo>> ScanAsync(CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> RemoveAsync(DebloatAppInfo app, CancellationToken cancellationToken = default);
}

public sealed class DebloatService : IDebloatService
{
    private sealed record PackageRow(string Name, string PackageFullName, string Publisher, bool NonRemovable);
    private readonly IAppLogger _logger;

    public DebloatService(IAppLogger logger) => _logger = logger;

    public async Task<IReadOnlyList<DebloatAppInfo>> ScanAsync(CancellationToken cancellationToken = default)
    {
        const string script = @"
$items = @(Get-AppxPackage | Where-Object {
  -not $_.IsFramework -and -not $_.IsResourcePackage -and
  $_.Name -notmatch 'Microsoft\.WindowsStore|Microsoft\.StorePurchaseApp|Microsoft\.DesktopAppInstaller|Microsoft\.SecHealthUI|Microsoft\.Windows\.ShellExperienceHost|Microsoft\.Windows\.StartMenuExperienceHost|Microsoft\.WindowsAppRuntime|Microsoft\.AAD\.BrokerPlugin|Microsoft\.AccountsControl|Microsoft\.Windows\.CloudExperienceHost'
} | ForEach-Object { [pscustomobject]@{Name=[string]$_.Name;PackageFullName=[string]$_.PackageFullName;Publisher=[string]$_.Publisher;NonRemovable=[bool]$_.NonRemovable} })
ConvertTo-Json -InputObject $items -Compress -Depth 3";
        var result = await PowerShellUtility.RunAsync(script, 20000, cancellationToken).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            _logger.Warning($"Debloat scan failed: {result.Error}");
            return Array.Empty<DebloatAppInfo>();
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            List<PackageRow> rows;
            if (result.Output.TrimStart().StartsWith("[", StringComparison.Ordinal))
                rows = JsonSerializer.Deserialize<List<PackageRow>>(result.Output, options) ?? new();
            else
            {
                var one = JsonSerializer.Deserialize<PackageRow>(result.Output, options);
                rows = one is null ? new() : [one];
            }

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.PackageFullName))
                .Select(ToInfo)
                .OrderByDescending(x => x.RemoveScore)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Debloat scan parse failed: {ex.Message}");
            return Array.Empty<DebloatAppInfo>();
        }
    }

    public async Task<(bool Success, string Message)> RemoveAsync(DebloatAppInfo app, CancellationToken cancellationToken = default)
    {
        if (!app.CanRemove)
            return (false, "Windows marks this package as protected/non-removable.");

        var escapedFull = app.PackageFullName.Replace("'", "''", StringComparison.Ordinal);
        var escapedName = app.Name.Replace("'", "''", StringComparison.Ordinal);

        var script = $@"
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$target = Get-AppxPackage | Where-Object {{ $_.PackageFullName -eq '{escapedFull}' }} | Select-Object -First 1
if($null -eq $target) {{ Write-Output 'REMOVED'; exit 0 }}
try {{
    Remove-AppxPackage -Package $target.PackageFullName -Confirm:$false -ErrorAction Stop
}} catch {{
    $fallback = Get-AppxPackage -Name '{escapedName}' -ErrorAction SilentlyContinue | Where-Object {{ $_.PackageFullName -eq '{escapedFull}' }} | Select-Object -First 1
    if($null -eq $fallback) {{ Write-Output 'REMOVED'; exit 0 }}
    $fallback | Remove-AppxPackage -Confirm:$false -ErrorAction Stop
}}
for($i=0; $i -lt 8; $i++) {{
    Start-Sleep -Milliseconds 180
    $remaining = Get-AppxPackage | Where-Object {{ $_.PackageFullName -eq '{escapedFull}' }} | Select-Object -First 1
    if($null -eq $remaining) {{ Write-Output 'REMOVED'; exit 0 }}
}}
Write-Error 'Package is still present after Windows completed the removal request.'
exit 4";

        var result = await PowerShellUtility.RunAsync(script, 45000, cancellationToken).ConfigureAwait(false);
        var removed = result.Output.Contains("REMOVED", StringComparison.OrdinalIgnoreCase);
        if (result.Success && removed)
        {
            _logger.Info($"Debloat removal verified for {app.Name}.");
            return (true, $"{app.Name} was removed for the current Windows user and verified.");
        }

        var error = FriendlyRemovalError(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
        _logger.Warning($"Debloat removal rejected for {app.Name}: {error}");
        return (false, error);
    }

    private static DebloatAppInfo ToInfo(PackageRow row)
    {
        var n = row.Name.ToLowerInvariant();

        // Windows 11 increasingly exposes shell-backed Appx packages in Get-AppxPackage even
        // though Remove-AppxPackage refuses to uninstall them. Never advertise those as removable.
        if (row.NonRemovable || n.Contains("peopleexperiencehost", StringComparison.OrdinalIgnoreCase))
        {
            return Make(row,
                FriendlyName(row.Name),
                Describe(row.Name),
                "Windows marks this package as part of the operating system. Sabby will not force-remove protected shell components.",
                "KEEP — Windows-protected component. The Remove button is disabled.",
                5,
                false,
                false);
        }

        // Known consumer/promotional apps Windows or OEM images commonly preload. These are
        // deliberately current-user Appx removals only; Sabby never removes Store, shell,
        // installer, security, runtime, or servicing components from SAFE ONLY.
        if (ContainsAny(n,
            "clipchamp", "bingnews", "bingsports", "bingfinance", "bingfoodanddrink",
            "gethelp", "getstarted", "solitaire", "windowsfeedbackhub",
            "skypeapp", "3dviewer", "3dbuilder", "microsoft.windowscommunicationsapps",
            "windowsalarms", "windowscamera", "microsoft.microsoftsticky",
            "king.com.candycrush", "candycrush", "bubblewitch", "marchofempires"))
        {
            return Make(row,
                FriendlyName(row.Name),
                Describe(row.Name),
                "Removes only this optional current-user Store app. Windows itself, the Microsoft Store, gaming services, and your personal files are not removed.",
                "Strong debloat candidate when you do not use the app. Included in SAFE ONLY.",
                92,
                true);
        }

        if (ContainsAny(n,
            "microsoft.people", "bingweather", "windowsmaps", "mixedreality", "microsoftofficehub", "todos",
            "windowssoundrecorder", "quickassist", "powerautomatedesktop", "549981c3f5f10",
            "communicationsapps", "windowsalarms", "windowscamera", "microsoft.windowscamera",
            "yourphone", "zunemusic", "zunevideo", "msteams", "teams",
            "onenote", "microsoftsticky", "stickynotes", "microsoft.microsoftsolitairecollection"))
        {
            return Make(row,
                FriendlyName(row.Name),
                Describe(row.Name),
                "Removes this optional Windows/Store feature for the current user. You can normally reinstall it later from Microsoft Store if you want it back.",
                "Good debloat candidate if you do not use the feature. Sabby leaves it out of bulk SAFE ONLY when it may still be useful to many PCs.",
                76,
                false);
        }

        if (ContainsAny(n,
            "xbox", "gamingapp", "paint", "photos", "sticky", "onenote", "camera", "calculator", "notepad", "windowsclock"))
        {
            return Make(row,
                FriendlyName(row.Name),
                Describe(row.Name),
                n.Contains("xbox") || n.Contains("gamingapp")
                    ? "Removes this Xbox/gaming-facing app for the current user. This can remove UI/features you may need for Game Pass, Xbox sign-in, captures, or related gaming workflows."
                    : "Removes this normal Windows app for the current user. It does not remove the Windows shell, but you lose the app until you reinstall it.",
                "Optional only. Remove it manually if you know you do not use it; Sabby will not include it in SAFE ONLY.",
                58,
                false);
        }

        // Common third-party promotional Store packages can be clutter, but do not assume they
        // are unwanted. They remain manual-only so Sabby never bulk-removes something the user chose.
        if (ContainsAny(n, "spotify", "netflix", "disney", "tiktok", "instagram", "facebook", "primevideo", "hulu"))
        {
            return Make(row,
                FriendlyName(row.Name),
                "Third-party Store app that may have been installed by you, the PC image, or a promotional recommendation.",
                "Removes the app for the current user only. Account data stored online is not deleted, but local app data may be removed by Windows with the package.",
                "Potential clutter, but Sabby requires manual removal because it may be an app you intentionally use.",
                62,
                false);
        }

        return Make(row,
            FriendlyName(row.Name),
            "Installed current-user Windows Store/Appx package that Sabby does not classify as disposable.",
            "Removing an unknown package can remove an app or supporting feature. Sabby does not bulk-remove unclassified packages.",
            "KEEP by default unless you recognize the package and know you do not need it.",
            20,
            false);
    }

    private static DebloatAppInfo Make(
        PackageRow row,
        string displayName,
        string whatItIs,
        string removalEffect,
        string recommendation,
        int score,
        bool safeForBulk,
        bool canRemove = true) =>
        new(row.Name, displayName, row.PackageFullName, row.Publisher, whatItIs, removalEffect, recommendation, score, safeForBulk, canRemove);

    private static string FriendlyRemovalError(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Windows rejected the package removal.";

        var text = raw.Replace("\r", " ").Replace("\n", " ").Trim();
        if (text.Contains("0x80073CFA", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("part of Windows and cannot be uninstalled", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cannot be uninstalled on a per-user basis", StringComparison.OrdinalIgnoreCase))
            return "Windows protects this package and does not allow it to be removed for the current user.";

        if (text.Contains("deployment failed", StringComparison.OrdinalIgnoreCase))
            return "Windows deployment services rejected the removal. The app was left unchanged.";

        // PowerShell can return serialized CLIXML error payloads. Never dump that implementation
        // detail into a card; keep a compact diagnostic while the full error remains in the log.
        if (text.Contains("<Objs", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("CLIXML", StringComparison.OrdinalIgnoreCase))
            return "Windows rejected the removal. Sabby left the package unchanged; see the app log for technical details.";

        return text.Length <= 220 ? text : text[..220] + "…";
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string FriendlyName(string raw)
    {
        var n = raw.ToLowerInvariant();
        if (n.Contains("clipchamp")) return "Clipchamp";
        if (n.Contains("bingnews")) return "Microsoft News";
        if (n.Contains("bingweather")) return "Microsoft Weather";
        if (n.Contains("gethelp")) return "Get Help";
        if (n.Contains("getstarted")) return "Tips / Get Started";
        if (n.Contains("solitaire")) return "Microsoft Solitaire Collection";
        if (n.Contains("windowsfeedbackhub")) return "Feedback Hub";
        if (n.Contains("peopleexperiencehost")) return "Windows People Experience";
        if (n.Contains("people")) return "Microsoft People";
        if (n.Contains("skypeapp")) return "Skype";
        if (n.Contains("3dviewer")) return "3D Viewer";
        if (n.Contains("3dbuilder")) return "3D Builder";
        if (n.Contains("windowscamera") || n.Contains("microsoft.windowscamera")) return "Camera";
        if (n.Contains("microsoftsticky") || n.Contains("stickynotes")) return "Sticky Notes";
        if (n.Contains("windowscalculator")) return "Calculator";
        if (n.Contains("windowsnotepad")) return "Notepad";
        if (n.Contains("windowsphotos") || n.Contains("microsoft.photos")) return "Photos";
        if (n.Contains("windowsmaps")) return "Windows Maps";
        if (n.Contains("mixedreality")) return "Mixed Reality Portal";
        if (n.Contains("microsoftofficehub")) return "Microsoft 365 / Office Hub";
        if (n.Contains("todos")) return "Microsoft To Do";
        if (n.Contains("windowssoundrecorder")) return "Sound Recorder";
        if (n.Contains("quickassist")) return "Quick Assist";
        if (n.Contains("powerautomatedesktop")) return "Power Automate";
        if (n.Contains("549981c3f5f10")) return "Cortana";
        if (n.Contains("communicationsapps")) return "Mail and Calendar";
        if (n.Contains("windowsalarms") || n.Contains("windowsclock")) return "Clock";
        if (n.Contains("yourphone")) return "Phone Link";
        if (n.Contains("zunemusic")) return "Media Player / Groove Music";
        if (n.Contains("zunevideo")) return "Movies & TV";
        if (n.Contains("msteams") || n.Contains("teams")) return "Microsoft Teams";
        if (n.Contains("gamingapp")) return "Xbox app";
        if (n.Contains("xbox")) return "Xbox component";
        if (n.Contains("paint")) return "Microsoft Paint";
        if (n.Contains("photos")) return "Microsoft Photos";
        if (n.Contains("sticky")) return "Sticky Notes";
        if (n.Contains("king.com") || n.Contains("candycrush")) return "Promotional game";
        return raw;
    }

    private static string Describe(string raw)
    {
        var n = raw.ToLowerInvariant();
        if (n.Contains("clipchamp")) return "Microsoft's consumer video editor.";
        if (n.Contains("bingnews")) return "Microsoft News feed app.";
        if (n.Contains("bingweather")) return "Microsoft Weather app and its local UI.";
        if (n.Contains("gethelp")) return "Windows Get Help support app.";
        if (n.Contains("getstarted")) return "Windows tips/onboarding app.";
        if (n.Contains("solitaire")) return "Microsoft's bundled Solitaire game collection.";
        if (n.Contains("windowsfeedbackhub")) return "Feedback Hub used to send Windows feedback and diagnostics reports.";
        if (n.Contains("peopleexperiencehost")) return "Windows shell component used by the People/contacts experience.";
        if (n.Contains("people")) return "Legacy People/contacts front-end.";
        if (n.Contains("windowsmaps")) return "Windows Maps application and offline-map front-end.";
        if (n.Contains("mixedreality")) return "Windows Mixed Reality Portal front-end.";
        if (n.Contains("microsoftofficehub")) return "Microsoft 365/Office launcher and promotional hub.";
        if (n.Contains("todos")) return "Microsoft To Do task-list app.";
        if (n.Contains("windowssoundrecorder")) return "Windows Sound Recorder app.";
        if (n.Contains("quickassist")) return "Microsoft remote-assistance app.";
        if (n.Contains("powerautomatedesktop")) return "Microsoft Power Automate desktop front-end.";
        if (n.Contains("549981c3f5f10")) return "Legacy Cortana app package.";
        if (n.Contains("communicationsapps")) return "Legacy Mail and Calendar package.";
        if (n.Contains("yourphone")) return "Phone Link for connecting Android/iPhone features to Windows.";
        if (n.Contains("king.com") || n.Contains("candycrush")) return "Consumer/promotional Store game package.";
        return "Optional current-user Store app.";
    }
}
