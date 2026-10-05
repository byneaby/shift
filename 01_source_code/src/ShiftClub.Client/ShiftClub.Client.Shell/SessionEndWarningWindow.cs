using System.Windows;

namespace ShiftClub.Client.Shell;

/// <summary>
/// Предупреждение о малом остатке времени — только голос, без окон поверх игры.
/// </summary>
internal static class SessionEndWarningWindow
{
    public static void Show(int minutesLeft, IReadOnlyCollection<int>? gamePids = null)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => Show(minutesLeft, gamePids));
            return;
        }

        SessionEndVoiceAnnouncer.AnnounceMinutesLeft(minutesLeft);
    }
}
