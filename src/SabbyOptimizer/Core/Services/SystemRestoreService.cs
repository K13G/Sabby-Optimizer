using System.Diagnostics;
using System.Text;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public sealed class SystemRestoreService : ISystemRestoreService
{
    public const string InitialRestorePointName = "before Sabby tweaks";

    private readonly IAppLogger _logger;

    public SystemRestoreService(IAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<RestorePointInfo>> GetRestorePointsAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<RestorePointInfo>();

        const string script = @"
$ErrorActionPreference='Stop'
$points = @(Get-ComputerRestorePoint -ErrorAction Stop | Sort-Object SequenceNumber -Descending)
foreach($p in $points) {
  try { $dt = [Management.ManagementDateTimeConverter]::ToDateTime([string]$p.CreationTime) } catch { $dt = Get-Date }
  $desc = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$p.Description))
  Write-Output (('{0}|{1}|{2}|{3}' -f $p.SequenceNumber,$desc,$dt.ToString('o'),$p.RestorePointType))
}";

        var result = await RunPowerShellAsync(script, 20000, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.Warning($"Could not enumerate restore points: {result.Error}");
            return Array.Empty<RestorePointInfo>();
        }

        var list = new List<RestorePointInfo>();
        foreach (var line in result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length != 4 || !int.TryParse(parts[0], out var seq) || !DateTime.TryParse(parts[2], out var created) || !int.TryParse(parts[3], out var type))
                continue;
            try
            {
                var description = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                list.Add(new RestorePointInfo(seq, description, created, type));
            }
            catch { }
        }
        return list;
    }

    public async Task<(bool Success, string Message)> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return (false, "System Restore is only available on Windows.");
        if (string.IsNullOrWhiteSpace(description))
            return (false, "A restore-point name is required.");

        var escaped = description.Replace("'", "''", StringComparison.Ordinal);
        var script = $@"
$ErrorActionPreference='Stop'
$restoreKey='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
$frequencyName='SystemRestorePointCreationFrequency'
$oldExists=$false
$oldValue=$null
try {{
  Enable-ComputerRestore -Drive ($env:SystemDrive + '\') -ErrorAction Stop
  $old = Get-ItemProperty -Path $restoreKey -Name $frequencyName -ErrorAction SilentlyContinue
  if($null -ne $old) {{ $oldExists=$true; $oldValue=$old.$frequencyName }}
  New-ItemProperty -Path $restoreKey -Name $frequencyName -PropertyType DWord -Value 0 -Force | Out-Null
  $result = ([wmiclass]'root\default:SystemRestore').CreateRestorePoint('{escaped}',12,100)
  if([int]$result.ReturnValue -ne 0) {{ throw ('SystemRestore returned ' + $result.ReturnValue) }}
  $found=$false
  for($i=0; $i -lt 20 -and -not $found; $i++) {{
    Start-Sleep -Milliseconds 250
    $found = @(Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Where-Object {{ $_.Description -eq '{escaped}' }}).Count -gt 0
  }}
  if(-not $found) {{ throw 'Windows did not expose the new restore point after creation.' }}
  Write-Output 'OK'
}} finally {{
  if($oldExists) {{ New-ItemProperty -Path $restoreKey -Name $frequencyName -PropertyType DWord -Value ([int]$oldValue) -Force | Out-Null }}
  else {{ Remove-ItemProperty -Path $restoreKey -Name $frequencyName -ErrorAction SilentlyContinue }}
}}";

        var result = await RunPowerShellAsync(script, 45000, cancellationToken).ConfigureAwait(false);
        if (!result.Success || !result.Output.Contains("OK", StringComparison.OrdinalIgnoreCase))
        {
            var message = string.IsNullOrWhiteSpace(result.Error) ? "Windows did not create the restore point." : result.Error;
            _logger.Warning($"Restore point creation failed: {message}");
            return (false, message);
        }

        _logger.Info($"System restore point created: {description}");
        return (true, $"Restore point created: {description}");
    }

    public async Task<(bool Success, string Message)> EnsureInitialRestorePointAsync(CancellationToken cancellationToken = default)
    {
        var existing = await GetRestorePointsAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(x => x.Description.Equals(InitialRestorePointName, StringComparison.OrdinalIgnoreCase)))
            return (true, "Initial Sabby restore point already exists.");
        return await CreateRestorePointAsync(InitialRestorePointName, cancellationToken).ConfigureAwait(false);
    }

    public void OpenSystemRestore()
    {
        if (!OperatingSystem.IsWindows()) return;
        Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });
    }

    private static async Task<(bool Success, string Output, string Error)> RunPowerShellAsync(string script, int timeoutMs, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(encoded);

        using var process = Process.Start(start);
        if (process is null) return (false, string.Empty, "Could not start Windows PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (false, string.Empty, "Windows PowerShell timed out.");
        }

        var output = (await outputTask.ConfigureAwait(false)).Trim();
        var error = (await errorTask.ConfigureAwait(false)).Trim();
        return (process.ExitCode == 0, output, error);
    }
}
