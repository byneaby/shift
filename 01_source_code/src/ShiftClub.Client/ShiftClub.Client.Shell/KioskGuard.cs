using System.IO;

namespace ShiftClub.Client.Shell;

/// <summary>
/// Heartbeat for the elevated watchdog. No global hotkey blocks (Win/Alt+Tab/Alt+F4 allowed).
/// Process kill protection is ProcessAccessGuard (Senet-style Access Denied) + Client.Service restart.
/// </summary>
public sealed class KioskGuard : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _watchTimer;
    private bool _disposed;

    /// <summary>When true, Shell window may close (staff exit / update).</summary>
    public bool AllowStaffExit { get; set; }

    /// <summary>Unused — kept for call-site compatibility.</summary>
    public bool PauseForGame { get; set; }

    // Kept for MainWindow subscription compatibility (security alerts unused without hotkey blocks).
#pragma warning disable CS0067
    public event Action<string>? BypassAttempt;
#pragma warning restore CS0067


    public KioskGuard()
    {
        _watchTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _watchTimer.Tick += (_, _) => WriteHeartbeat();
    }

    public void Start() => _watchTimer.Start();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watchTimer.Stop();
    }

    private void WriteHeartbeat()
    {
        try
        {
            var hb = Path.Combine(AppContext.BaseDirectory, "kiosk.heartbeat");
            File.WriteAllText(hb, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
            /* ignore */
        }
    }
}
