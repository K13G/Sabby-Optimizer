using System.ComponentModel;
using System.Diagnostics;

namespace PCTweaker.Core.Tweaks;

internal static class ElevatedTweakRunner
{
    internal sealed record Result(bool Success, string Message, bool Cancelled = false);

    public static async Task<Result> RunAsync(string tweakId, bool apply, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return new Result(false, "Administrator elevation is only available on Windows.");

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            return new Result(false, "Sabby Optimizer could not locate its executable for administrator approval.");

        try
        {
            var action = apply ? "apply" : "undo";
            var info = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--elevated-tweak {action} {tweakId}",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(info);
            if (process is null)
                return new Result(false, "Windows could not start the administrator helper.");

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0
                ? new Result(true, "The change was applied with administrator approval.")
                : new Result(false, "The administrator helper could not complete the requested tweak.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new Result(false, "Administrator approval was cancelled. Nothing was changed.", true);
        }
        catch (Exception ex)
        {
            return new Result(false, $"Administrator approval failed: {ex.Message}");
        }
    }
}
