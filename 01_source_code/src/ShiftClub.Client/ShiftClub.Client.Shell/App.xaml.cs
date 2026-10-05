using System.Threading;
using System.Windows;
using ShiftClub.Shared.Security;

namespace ShiftClub.Client.Shell;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Prevent double Shell (Startup + Keeper + Service racing) — second instance exits immediately.
        _singleInstance = new Mutex(true, @"Global\ShiftClub.Client.Shell.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            try { _singleInstance.Dispose(); } catch { /* ignore */ }
            _singleInstance = null;
            Shutdown();
            return;
        }

        try
        {
            // Steam "registry path not writable" + Epic/Riot/Battle.net (needs admin for HKLM ACL).
            LauncherRegistryFix.Apply();
        }
        catch
        {
            /* ignore */
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
        }
        catch
        {
            /* ignore */
        }

        base.OnExit(e);
    }
}
