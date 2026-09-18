using System.Diagnostics;
using System.Text;

namespace PCTweaker.Core.Tweaks.Handlers;

internal static class PowerShellNetworkAccess
{
    private static readonly object CommandCacheLock = new();
    private static readonly Dictionary<string, bool> CommandCache = new(StringComparer.OrdinalIgnoreCase);

    internal sealed record Result(bool Success, string Output, string Error, int ExitCode);

    public static Result Run(string script, int timeoutMilliseconds = 12000)
    {
        if (!OperatingSystem.IsWindows())
            return new Result(false, string.Empty, "Windows PowerShell is only available on Windows.", -1);

        try
        {
            var wrapped = "$ErrorActionPreference='Stop';" + script;
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(encoded);

            using var process = Process.Start(startInfo);
            if (process is not null)
            {
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            }
            if (process is null)
                return new Result(false, string.Empty, "Could not start Windows PowerShell.", -1);

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new Result(false, string.Empty, "Windows PowerShell timed out.", -1);
            }

            Task.WaitAll(outputTask, errorTask);
            var output = outputTask.Result.Trim();
            var error = errorTask.Result.Trim();
            return new Result(process.ExitCode == 0, output, error, process.ExitCode);
        }
        catch (Exception ex)
        {
            return new Result(false, string.Empty, ex.Message, -1);
        }
    }


    public static void PrimeCommandCache(IEnumerable<string> commands)
    {
        if (!OperatingSystem.IsWindows()) return;
        var requested = commands
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requested.Length == 0) return;

        lock (CommandCacheLock)
        {
            requested = requested.Where(x => !CommandCache.ContainsKey(x)).ToArray();
        }
        if (requested.Length == 0) return;

        var literals = string.Join(",", requested.Select(x => $"'{Escape(x)}'"));
        var script = $"$names=@({literals}); foreach($n in $names){{ if(Get-Command $n -ErrorAction SilentlyContinue){{[Console]::Out.WriteLine($n+'|1')}}else{{[Console]::Out.WriteLine($n+'|0')}} }}";
        var result = Run(script, 6500);
        if (!result.Success) return;

        var parsed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var sep = line.LastIndexOf('|');
            if (sep <= 0) continue;
            parsed[line[..sep]] = line[(sep + 1)..].Trim() == "1";
        }

        lock (CommandCacheLock)
        {
            foreach (var name in requested)
                if (parsed.TryGetValue(name, out var exists)) CommandCache[name] = exists;
        }
    }

    public static bool CommandExists(string command)
    {
        lock (CommandCacheLock)
        {
            if (CommandCache.TryGetValue(command, out var cached))
                return cached;
        }

        var safe = Escape(command);
        var result = Run($"if (Get-Command '{safe}' -ErrorAction SilentlyContinue) {{ 'YES' }} else {{ 'NO' }}", 5000);
        var exists = result.Success && result.Output.Equals("YES", StringComparison.OrdinalIgnoreCase);
        lock (CommandCacheLock)
            CommandCache[command] = exists;
        return exists;
    }

    public static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
