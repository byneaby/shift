using ShiftClub.Server.Setup;

namespace ShiftClub.Server.Installer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var name = Path.GetFileName(Environment.ProcessPath ?? "");
        var uninstall = name.Equals(ServerUninstallPlan.UninstallExeName, StringComparison.OrdinalIgnoreCase)
            || args.Any(arg => arg.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
        Application.Run(uninstall ? new UninstallForm() : new SetupWizardForm());
    }
}
