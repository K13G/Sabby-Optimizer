using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PCTweaker.Core.Services;

public static class GameIconService
{
    private const uint ShgfiSysIconIndex = 0x000004000;
    private const int ShilExtraLarge = 2;
    private const int ShilJumbo = 4;
    private const int IldTransparent = 0x00000001;
    private static readonly Guid IidImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheGate = new();

    public static ImageSource? TryLoadIcon(string executablePath, string? platform = null, string? sourceId = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return null;

        var cacheKey = $"{platform}|{sourceId}|{executablePath}";
        lock (CacheGate)
        {
            if (Cache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var source = TryLoadLauncherArtwork(platform, sourceId)
                     ?? TryLoadShellImage(executablePath, ShilJumbo)
                     ?? TryLoadShellImage(executablePath, ShilExtraLarge)
                     ?? TryLoadLargeFileIcon(executablePath);

        if (source is not null)
        {
            lock (CacheGate)
                Cache[cacheKey] = source;
        }

        return source;
    }

    private static ImageSource? TryLoadLauncherArtwork(string? platform, string? sourceId)
    {
        if (!string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(sourceId) || !sourceId.All(char.IsDigit))
            return null;

        try
        {
            var steamRoot = GetSteamRoot();
            if (string.IsNullOrWhiteSpace(steamRoot))
                return null;

            var cache = Path.Combine(steamRoot, "appcache", "librarycache");
            if (!Directory.Exists(cache))
                return null;

            var exactCandidates = new[]
            {
                Path.Combine(cache, $"{sourceId}_icon.jpg"),
                Path.Combine(cache, $"{sourceId}_icon.png"),
                Path.Combine(cache, $"{sourceId}_logo.png"),
                Path.Combine(cache, $"{sourceId}_header.jpg")
            };

            foreach (var candidate in exactCandidates)
            {
                var image = TryLoadBitmap(candidate);
                if (image is not null) return image;
            }

            var appFolder = Path.Combine(cache, sourceId);
            if (!Directory.Exists(appFolder))
                return null;

            var files = Directory.EnumerateFiles(appFolder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path =>
                {
                    var name = Path.GetFileName(path);
                    if (name.Contains("icon", StringComparison.OrdinalIgnoreCase)) return 4;
                    if (name.Contains("logo", StringComparison.OrdinalIgnoreCase)) return 3;
                    if (name.Contains("header", StringComparison.OrdinalIgnoreCase)) return 2;
                    return 1;
                })
                .ThenBy(path => new FileInfo(path).Length)
                .Take(12);

            foreach (var file in files)
            {
                var image = TryLoadBitmap(file);
                if (image is not null) return image;
            }
        }
        catch
        {
            // Launcher artwork is optional; fall back to the Windows shell icon.
        }

        return null;
    }

    private static ImageSource? TryLoadBitmap(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = 256;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetSteamRoot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string path && Directory.Exists(path))
                return path.Replace('/', Path.DirectorySeparatorChar);
        }
        catch { }

        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static ImageSource? TryLoadShellImage(string executablePath, int imageListSize)
    {
        SHFILEINFO info = default;
        var result = SHGetFileInfo(
            executablePath,
            0,
            out info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            ShgfiSysIconIndex);

        if (result == IntPtr.Zero || info.iIcon < 0)
            return null;

        IImageList? imageList = null;
        IntPtr icon = IntPtr.Zero;
        try
        {
            var iid = IidImageList;
            if (SHGetImageList(imageListSize, ref iid, out imageList) != 0 || imageList is null)
                return null;
            if (imageList.GetIcon(info.iIcon, IldTransparent, out icon) != 0 || icon == IntPtr.Zero)
                return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (icon != IntPtr.Zero) DestroyIcon(icon);
        }
    }

    private static ImageSource? TryLoadLargeFileIcon(string executablePath)
    {
        SHFILEINFO info = default;
        var result = SHGetFileInfo(
            executablePath,
            0,
            out info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            0x000000100 /* SHGFI_ICON */);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, out int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, out int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "SHGetImageList")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IImageList? ppv);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
