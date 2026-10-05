using System.Runtime.InteropServices;

namespace ShiftClub.Client.Shell;

/// <summary>
/// Native Windows taskbar. Hidden on the pre-session lock/login screen;
/// shown once the guest is in the shell (logged in / session).
/// </summary>
internal static class WindowsTaskbar
{
    private const int SwHide = 0;
    private const int SwShow = 5;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public static void Hide()
    {
        foreach (var hwnd in EnumerateTaskbarWindows())
        {
            if (hwnd != IntPtr.Zero)
                ShowWindow(hwnd, SwHide);
        }
    }

    public static void Show()
    {
        foreach (var hwnd in EnumerateTaskbarWindows())
        {
            if (hwnd != IntPtr.Zero)
                ShowWindow(hwnd, SwShow);
        }
    }

    public static bool IsHidden()
    {
        var primary = FindWindow("Shell_TrayWnd", null);
        return primary != IntPtr.Zero && !IsWindowVisible(primary);
    }

    private static IEnumerable<IntPtr> EnumerateTaskbarWindows()
    {
        yield return FindWindow("Shell_TrayWnd", null);
        yield return FindWindow("Shell_SecondaryTrayWnd", null);
    }
}
