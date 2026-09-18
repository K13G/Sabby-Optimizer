using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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

        UpdateOverlay? overlay = null;
        try
        {
            if (args.Length < 4)
                throw new InvalidDataException("Update helper arguments were incomplete.");

            var downloadUrl = Decode(args[1]);
            var expectedSha = Decode(args[2]).Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
            var version = SanitizeFilePart(Decode(args[3]));

            overlay = new UpdateOverlay(version);
            overlay.Show();
            overlay.BeginFadeIn();
            await overlay.RenderAsync();

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var stagedDirectory = Path.Combine(localAppData, "SabbyOptimizer", "UserData", "Updates", "Staged");
            Directory.CreateDirectory(stagedDirectory);

            var destination = Path.Combine(stagedDirectory, $"SabbyOptimizer-{version}-Setup.exe");
            var partial = destination + ".part";
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }

            overlay.SetStage("Downloading update…", "Preparing the new Sabby Optimizer build.", 0, false);

            using (var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, true);

                var buffer = new byte[256 * 1024];
                long received = 0;
                var lastDisplayed = -1;

                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
                    if (read <= 0) break;

                    await output.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    received += read;

                    if (total is > 0)
                    {
                        var percent = (int)Math.Clamp(received * 100L / total.Value, 0, 100);
                        if (percent != lastDisplayed)
                        {
                            lastDisplayed = percent;
                            overlay.SetStage(
                                "Downloading update…",
                                $"{percent}% complete • Sabby will restart automatically.",
                                percent,
                                false);
                        }
                    }
                    else
                    {
                        overlay.SetStage(
                            "Downloading update…",
                            "Downloading the verified installer • Sabby will restart automatically.",
                            0,
                            true);
                    }
                }
            }

            overlay.SetStage("Verifying update…", "Checking the installer before anything is changed.", 100, true);

            if (!string.IsNullOrWhiteSpace(expectedSha))
            {
                await using var stream = File.OpenRead(partial);
                var actualSha = Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(false));
                if (!actualSha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded installer failed SHA-256 verification.");
            }

            File.Move(partial, destination, true);

            overlay.SetStage(
                "Installing update…",
                "The download is verified. Approve the Windows prompt if it appears. Sabby will reopen automatically.",
                100,
                true);
            await overlay.RenderAsync();
            await Task.Delay(420).ConfigureAwait(false);

            Process.Start(new ProcessStartInfo
            {
                FileName = destination,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART"
            });

            // Release the running Sabby executable immediately so Setup can replace it.
            overlay.CloseSafe();
        }
        catch (Exception ex)
        {
            WriteFailure(ex);

            if (overlay is not null)
            {
                overlay.SetError(
                    "Update couldn't finish",
                    "Nothing unsafe was applied. Sabby will reopen so you can try the update again.");
                await overlay.RenderAsync();
                await Task.Delay(2600).ConfigureAwait(false);
                overlay.CloseSafe();
            }

            TryReopenSabby();
        }

        return true;
    }

    private static void TryReopenSabby()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private static void WriteFailure(Exception ex)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SabbyOptimizer",
                "Logs");
            Directory.CreateDirectory(root);
            File.AppendAllText(
                Path.Combine(root, "update-helper.log"),
                $"[{DateTime.Now:O}] {ex}\r\n");
        }
        catch { }
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

    private sealed class UpdateOverlay
    {
        private readonly Window _window;
        private readonly TextBlock _title;
        private readonly TextBlock _detail;
        private readonly TextBlock _version;
        private readonly ProgressBar _progress;

        public UpdateOverlay(string version)
        {
            var accent = new SolidColorBrush(Color.FromRgb(190, 18, 36));
            var panel = new SolidColorBrush(Color.FromRgb(16, 18, 22));
            var border = new SolidColorBrush(Color.FromRgb(55, 58, 65));
            var secondary = new SolidColorBrush(Color.FromRgb(174, 181, 191));

            _title = new TextBlock
            {
                Text = "Preparing update…",
                FontSize = 27,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            _detail = new TextBlock
            {
                Text = "Sabby Optimizer is getting the newest verified build.",
                FontSize = 12.5,
                Foreground = secondary,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520
            };

            _version = new TextBlock
            {
                Text = $"Updating to {version}",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = accent,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _progress = new ProgressBar
            {
                Height = 6,
                Width = 430,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Foreground = accent,
                Background = new SolidColorBrush(Color.FromRgb(38, 41, 47)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 26, 0, 0)
            };

            var brand = new TextBlock
            {
                Text = "SABBY OPTIMIZER",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(brand);
            stack.Children.Add(_title);
            stack.Children.Add(_detail);
            stack.Children.Add(_version);
            stack.Children.Add(_progress);

            var card = new Border
            {
                Width = 610,
                Padding = new Thickness(38, 34, 38, 32),
                CornerRadius = new CornerRadius(18),
                Background = panel,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = stack
            };

            var root = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(218, 0, 0, 0))
            };
            root.Children.Add(card);

            _window = new Window
            {
                Title = "Sabby Optimizer Update",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = true,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Content = root,
                Opacity = 0
            };
        }

        public void Show() => _window.Show();

        public void BeginFadeIn()
        {
            _window.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }

        public void SetStage(string title, string detail, double progress, bool indeterminate)
        {
            _ = _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _title.Text = title;
                _detail.Text = detail;
                _progress.IsIndeterminate = indeterminate;
                if (!indeterminate)
                    _progress.Value = Math.Clamp(progress, 0, 100);
            }), DispatcherPriority.Background);
        }

        public void SetError(string title, string detail)
        {
            _ = _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _title.Text = title;
                _detail.Text = detail;
                _version.Text = "Update stopped safely";
                _progress.IsIndeterminate = false;
                _progress.Value = 0;
            }), DispatcherPriority.Send);
        }

        public async Task RenderAsync()
        {
            await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        }

        public void CloseSafe()
        {
            if (_window.Dispatcher.CheckAccess())
                _window.Close();
            else
                _window.Dispatcher.Invoke(_window.Close);
        }
    }
}
