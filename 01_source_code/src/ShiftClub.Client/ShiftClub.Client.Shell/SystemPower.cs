using System.Diagnostics;
using System.Windows;

namespace ShiftClub.Client.Shell;

internal static class SystemPower
{
    public static bool ConfirmAndRestart(Window? owner = null) =>
        ConfirmAndRun(owner, "Перезагрузка", "Перезагрузить этот ПК сейчас?", "/r /t 0");

    public static bool ConfirmAndShutdown(Window? owner = null) =>
        ConfirmAndRun(owner, "Выключение", "Выключить этот ПК сейчас?", "/s /t 0");

    private static bool ConfirmAndRun(Window? owner, string title, string message, string args)
    {
        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
            return false;

        try
        {
            Process.Start(new ProcessStartInfo("shutdown", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
