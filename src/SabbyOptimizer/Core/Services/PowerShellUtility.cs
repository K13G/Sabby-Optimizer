using System.Diagnostics;
using System.Text;

namespace PCTweaker.Core.Services;

internal static class PowerShellUtility
{
    public sealed record Result(bool Success, string Output, string Error, int ExitCode);

    public static async Task<Result> RunAsync(string script, int timeoutMs = 30000, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return new(false, string.Empty, "Windows only.", -1);
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
        if (process is null) return new(false, string.Empty, "Could not start Windows PowerShell.", -1);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new(false, string.Empty, "The Windows command timed out.", -1);
        }
        var output = (await stdout.ConfigureAwait(false)).Trim();
        var error = (await stderr.ConfigureAwait(false)).Trim();
        return new(process.ExitCode == 0, output, error, process.ExitCode);
    }
}
