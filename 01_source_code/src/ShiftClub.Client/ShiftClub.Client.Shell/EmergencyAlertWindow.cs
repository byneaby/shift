using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShiftClub.Client.Shell;

/// <summary>
/// Экстренное сообщение (напр. «ВОЕНКОМАТ»).
/// Exclusive fullscreen игры нельзя перекрыть обычным Topmost — поэтому перед показом
/// сворачиваем чужие fullscreen/игровые окна, затем держим красный экран 3 секунды
/// без ручного закрытия (чтобы случайный клик в игре не сбросил сигнал).
/// </summary>
internal sealed class EmergencyAlertWindow : Window
{
    private const int DisplaySeconds = 3;

    private static EmergencyAlertWindow? _current;

    private readonly DispatcherTimer _assertTopTimer;
    private readonly DispatcherTimer _autoCloseTimer;
    private readonly DispatcherTimer _blinkTimer;
    private readonly IReadOnlyCollection<int> _gamePids;
    private bool _blinkOn = true;
    private bool _closing;

    private EmergencyAlertWindow(string headline, string body, IReadOnlyCollection<int>? gamePids)
    {
        _gamePids = gamePids ?? Array.Empty<int>();

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0xC1, 0x0B, 0x0B));
        Cursor = Cursors.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = true;

        Content = BuildContent(headline, body);

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            Background = new SolidColorBrush(_blinkOn
                ? Color.FromRgb(0xC1, 0x0B, 0x0B)
                : Color.FromRgb(0x8A, 0x03, 0x03));
        };

        // Часто: игры пытаются вернуть exclusive fullscreen.
        _assertTopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _assertTopTimer.Tick += (_, _) =>
        {
            EmergencyDisplayGrab.MinimizeCompetitors(_gamePids);
            ForceTopmost();
        };

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(DisplaySeconds) };
        _autoCloseTimer.Tick += (_, _) => HardClose();

        SourceInitialized += (_, _) => CoverPrimaryScreen();
        Loaded += (_, _) =>
        {
            EmergencyDisplayGrab.MinimizeCompetitors(_gamePids);
            CoverPrimaryScreen();
            ForceTopmost();
            Activate();
            Focus();
            _assertTopTimer.Start();
            _autoCloseTimer.Start();
            _blinkTimer.Start();
            EmergencySound.StartLoop();
        };
        Deactivated += (_, _) =>
        {
            if (!_closing)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    EmergencyDisplayGrab.MinimizeCompetitors(_gamePids);
                    ForceTopmost();
                }, DispatcherPriority.Send);
            }
        };

        // Нельзя закрыть руками — только авто 3 сек.
        PreviewKeyDown += (_, e) => e.Handled = true;
        PreviewMouseDown += (_, e) => e.Handled = true;
        PreviewMouseUp += (_, e) => e.Handled = true;
    }

    /// <summary>Показать/заменить экстренное сообщение (UI-поток).</summary>
    public static void ShowAlert(string? text, IReadOnlyCollection<int>? gamePids = null)
    {
        var (headline, body) = SplitText(text);
        try { _current?.HardClose(); }
        catch { /* ignore */ }

        // Сначала выталкиваем игру из exclusive fullscreen, потом рисуем оверлей.
        EmergencyDisplayGrab.MinimizeCompetitors(gamePids);

        var win = new EmergencyAlertWindow(headline, body, gamePids);
        _current = win;
        win.Show();
        win.Activate();
        win.ForceTopmost();
    }

    private static (string Headline, string Body) SplitText(string? text)
    {
        var clean = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return ("ВНИМАНИЕ", "Сообщение от администратора");

        var nl = clean.IndexOf('\n');
        if (nl > 0)
        {
            var head = clean[..nl].Trim();
            var rest = clean[(nl + 1)..].Trim();
            if (rest.Length > 0)
                return (head, rest);
            return (head, string.Empty);
        }
        return (clean, string.Empty);
    }

    private UIElement BuildContent(string headline, string body)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        stack.Children.Add(new TextBlock
        {
            Text = "⚠",
            Foreground = Brushes.White,
            FontSize = 120,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        });

        stack.Children.Add(new TextBlock
        {
            Text = headline,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 96,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        if (!string.IsNullOrWhiteSpace(body))
        {
            stack.Children.Add(new TextBlock
            {
                Text = body,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 44,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = $"{DisplaySeconds} сек",
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontSize = 28,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 56, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            Margin = new Thickness(64),
            MaxWidth = 1600,
            Child = stack,
        };

        var root = new Grid();
        root.Children.Add(viewbox);
        return root;
    }

    private void CoverPrimaryScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var w = GetSystemMetrics(SM_CXSCREEN);
        var h = GetSystemMetrics(SM_CYSCREEN);
        if (w <= 0 || h <= 0)
            return;
        Left = 0;
        Top = 0;
        Width = w;
        Height = h;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, w, h, SWP_SHOWWINDOW);
    }

    private void ForceTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        Topmost = true;
        CoverPrimaryScreen();
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        EmergencyDisplayGrab.StealForeground(hwnd);
    }

    private void HardClose()
    {
        if (_closing)
            return;
        _closing = true;
        try { _assertTopTimer.Stop(); } catch { }
        try { _autoCloseTimer.Stop(); } catch { }
        try { _blinkTimer.Stop(); } catch { }
        EmergencySound.StopLoop();
        if (ReferenceEquals(_current, this))
            _current = null;
        try { Close(); } catch { }
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}

/// <summary>
/// Выталкивает exclusive fullscreen / игровые окна, чтобы алерт мог занять экран.
/// Обычный Topmost поверх DirectX exclusive не работает — нужна смена режима (minimize).
/// </summary>
internal static class EmergencyDisplayGrab
{
    private const int SwForceMinimize = 11;
    private const int SwMinimize = 6;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    public static void MinimizeCompetitors(IReadOnlyCollection<int>? preferPids)
    {
        try
        {
            var self = (uint)Environment.ProcessId;
            var screenW = GetSystemMetrics(SmCxScreen);
            var screenH = GetSystemMetrics(SmCyScreen);
            var prefer = preferPids is { Count: > 0 }
                ? new HashSet<int>(preferPids)
                : null;

            EnumWindows((hwnd, _) =>
            {
                try
                {
                    if (!IsWindowVisible(hwnd))
                        return true;
                    if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero)
                        return true; // owned popups

                    GetWindowThreadProcessId(hwnd, out var pid);
                    if (pid == 0 || pid == self)
                        return true;

                    var isGamePid = prefer is not null && prefer.Contains((int)pid);
                    var isFs = IsNearFullscreen(hwnd, screenW, screenH);
                    if (!isGamePid && !isFs)
                        return true;

                    // ForceMinimize надёжнее выбивает exclusive DirectX, чем обычный Minimize.
                    if (!ShowWindow(hwnd, SwForceMinimize))
                        ShowWindow(hwnd, SwMinimize);
                }
                catch { /* next window */ }
                return true;
            }, IntPtr.Zero);
        }
        catch { /* best-effort */ }
    }

    public static void StealForeground(IntPtr targetHwnd)
    {
        if (targetHwnd == IntPtr.Zero)
            return;
        try
        {
            var fg = GetForegroundWindow();
            var fgThread = GetWindowThreadProcessId(fg, out _);
            var thisThread = GetCurrentThreadId();
            var attached = false;
            if (fgThread != 0 && fgThread != thisThread)
                attached = AttachThreadInput(thisThread, fgThread, true);
            try
            {
                BringWindowToTop(targetHwnd);
                SetForegroundWindow(targetHwnd);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(thisThread, fgThread, false);
            }
        }
        catch { /* ignore */ }
    }

    private static bool IsNearFullscreen(IntPtr hwnd, int screenW, int screenH)
    {
        if (screenW <= 0 || screenH <= 0)
            return false;
        if (!GetWindowRect(hwnd, out var r))
            return false;
        var w = r.Right - r.Left;
        var h = r.Bottom - r.Top;
        return w >= screenW - 8 && h >= screenH - 8 && r.Left <= 4 && r.Top <= 4;
    }

    private const uint GW_OWNER = 4;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal static class EmergencySound
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_LOOP = 0x0008;
    private const uint SND_ALIAS = 0x00010000;
    private const uint SND_PURGE = 0x0040;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    public static void StartLoop()
    {
        try
        {
            PlaySound("SystemHand", IntPtr.Zero, SND_ASYNC | SND_LOOP | SND_ALIAS);
        }
        catch { /* optional */ }
    }

    public static void StopLoop()
    {
        try
        {
            PlaySound(null, IntPtr.Zero, SND_PURGE);
        }
        catch { /* ignore */ }
    }
}
