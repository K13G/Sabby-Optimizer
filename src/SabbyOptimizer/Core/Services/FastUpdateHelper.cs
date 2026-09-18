using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using PCTweaker.Models;

namespace PCTweaker.Core.Services;

public static class FastUpdateHelper
{
    private const string HelperSwitch = "--sab-update-helper";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(4) };

    public static bool TryStart(SabbyReleaseInfo release, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(release.DownloadUrl))
        {
            message = "The update manifest does not contain a download URL.";
            return false;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            message = "Sabby could not locate its installed executable.";
            return false;
        }

        try
        {
            var args = string.Join(" ", HelperSwitch,
                Quote(Encode(release.DownloadUrl)),
                Quote(Encode(release.Sha256 ?? string.Empty)),
                Quote(Encode(release.LatestVersion)));

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (process is null)
            {
                message = "Windows did not start the update helper.";
                return false;
            }

            message = "Updater started.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Updater could not start: {ex.Message}";
            return false;
        }
    }

    public static async Task<bool> TryRunHelperAsync(string[] args)
    {
        if (args.Length == 0 || !args[0].Equals(HelperSwitch, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            if (args.Length < 4)
                throw new InvalidDataException("Update helper arguments were incomplete.");

            var downloadUrl = Decode(args[1]);
            var expectedSha = Decode(args[2]).Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
            var version = SanitizeFilePart(Decode(args[3]));

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var stagedDirectory = Path.Combine(localAppData, "SabbyOptimizer", "UserData", "Updates", "Staged");
            Directory.CreateDirectory(stagedDirectory);

            var destination = Path.Combine(stagedDirectory, $"SabbyOptimizer-{version}-Setup.exe");
            var partial = destination + ".part";
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }

            using (var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, true);
                await input.CopyToAsync(output, 256 * 1024).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(expectedSha))
            {
                await using var stream = File.OpenRead(partial);
                var actualSha = Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(false));
                if (!actualSha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded installer failed SHA-256 verification.");
            }

            File.Move(partial, destination, true);
            await Task.Delay(220).ConfigureAwait(false);

            Process.Start(new ProcessStartInfo
            {
                FileName = destination,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART"
            });
        }
        catch (Exception ex)
        {
            try
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SabbyOptimizer", "Logs");
                Directory.CreateDirectory(root);
                File.AppendAllText(Path.Combine(root, "update-helper.log"), $"[{DateTime.Now:O}] {ex}\r\n");
            }
            catch { }
        }

        return true;
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string SanitizeFilePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "update";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Where(ch => !invalid.Contains(ch)).ToArray());
    }
}
