using System.Runtime.InteropServices;

namespace ShiftClub.Client.Shell;

/// <summary>Detect exclusive / borderless fullscreen games so the custom taskbar can auto-hide.</summary>
internal static class FullscreenWindowDetect
{
    public static bool IsForeignFullscreenForeground(
        IntPtr excludeHwnd,
        IReadOnlyCollection<int>? onlyThesePids = null)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == excludeHwnd)
            return false;

        if (!GetWindowRect(hwnd, out var rect))
            return false;

        var screenW = GetSystemMetrics(0); // SM_CXSCREEN
        var screenH = GetSystemMetrics(1); // SM_CYSCREEN
        if (screenW <= 0 || screenH <= 0)
            return false;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        // Borderless / exclusive fullscreen typically covers the whole primary monitor.
        if (width < screenW - 4 || height < screenH - 4)
            return false;
        if (rect.Left > 2 || rect.Top > 2)
            return false;

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId)
            return false;

        // Only treat as "game fullscreen" when it matches a Shell-launched process.
        // Avoids hiding the dock for Discord/Chrome/maximized apps.
        if (onlyThesePids is { Count: > 0 })
        {
            if (!onlyThesePids.Contains((int)pid))
                return false;
        }

        return true;
    }

    /// <summary>
    /// True, если на переднем плане рабочий стол Windows (все окна свёрнуты / нажато
    /// «Свернуть все» / Win+D). Тогда оболочку можно показать как фон рабочего стола.
    /// </summary>
    public static bool IsDesktopForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return true; // нет активного окна — считаем, что виден рабочий стол

        var sb = new System.Text.StringBuilder(64);
        if (GetClassName(hwnd, sb, sb.Capacity) == 0)
            return false;
        var cls = sb.ToString();
        // Классы окна рабочего стола Windows.
        return cls is "Progman" or "WorkerW";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
