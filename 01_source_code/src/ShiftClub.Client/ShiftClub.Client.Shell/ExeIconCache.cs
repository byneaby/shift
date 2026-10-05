using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShiftClub.Client.Shell;

/// <summary>Кэш иконок exe как в панели задач Windows.</summary>
internal static class ExeIconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        var key = exePath.Trim();
        return Cache.GetOrAdd(key, Load);
    }

    public static ImageSource? GetForProcess(int pid, string? knownExePath = null)
    {
        if (!string.IsNullOrWhiteSpace(knownExePath) && File.Exists(knownExePath))
            return Get(knownExePath);

        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            var path = p.MainModule?.FileName;
            return Get(path);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            // Prefer shell large icon (closer to Explorer / taskbar).
            var fromShell = TryExtractShellIcon(path);
            if (fromShell is not null)
                return fromShell;

            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(48, 48));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryExtractShellIcon(string path)
    {
        var shfi = new SHFILEINFO();
        var hImg = SHGetFileInfo(
            path,
            0,
            ref shfi,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_LARGEICON);
        if (hImg == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(48, 48));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
