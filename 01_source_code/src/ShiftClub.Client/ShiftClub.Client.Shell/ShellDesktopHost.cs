using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ShiftClub.Client.Shell;

internal enum ShellLayoutMode
{
    /// <summary>Above the Windows taskbar (session / logged-in shell).</summary>
    WorkArea,
    /// <summary>Full primary screen, covering the taskbar (login / waiting).</summary>
    FullScreen
}

/// <summary>
/// Positions the Shell window: fullscreen lock UI, or work-area session UI.
/// </summary>
internal static class ShellDesktopHost
{
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotopmost = new(-2);

    private const uint SwpNosize = 0x0001;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public static void ApplyBounds(Window window, ShellLayoutMode mode)
    {
        if (window.WindowState != WindowState.Normal)
            window.WindowState = WindowState.Normal;

        if (mode == ShellLayoutMode.FullScreen)
        {
            // Full primary display — covers the taskbar strip.
            window.Left = 0;
            window.Top = 0;
            window.Width = Math.Max(320, SystemParameters.PrimaryScreenWidth);
            window.Height = Math.Max(240, SystemParameters.PrimaryScreenHeight);
            return;
        }

        var wa = SystemParameters.WorkArea;
        window.Left = wa.Left;
        window.Top = wa.Top;
        window.Width = Math.Max(320, wa.Width);
        window.Height = Math.Max(240, wa.Height);
    }

    public static void FitToWorkArea(Window window) => ApplyBounds(window, ShellLayoutMode.WorkArea);

    public static void FitToFullScreen(Window window) => ApplyBounds(window, ShellLayoutMode.FullScreen);

    public static void PushBehind(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        window.Topmost = false;
        SetWindowPos(hwnd, HwndNotopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
        SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
    }

    public static void BringForward(
        Window window,
        bool topmost,
        bool activate = true,
        ShellLayoutMode layout = ShellLayoutMode.WorkArea)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        ApplyBounds(window, layout);
        window.Topmost = topmost;
        uint flags = SwpNomove | SwpNosize | SwpShowwindow;
        if (!activate)
            flags |= SwpNoactivate;
        SetWindowPos(
            hwnd,
            topmost ? HwndTopmost : HwndNotopmost,
            0, 0, 0, 0,
            flags);
        if (activate)
        {
            window.Activate();
            window.Focus();
        }
    }

    public static void EnsureNotMinimized(Window window, ShellLayoutMode layout = ShellLayoutMode.WorkArea)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        ApplyBounds(window, layout);
    }
}
