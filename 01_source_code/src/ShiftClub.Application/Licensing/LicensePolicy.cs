using ShiftClub.Shared.Contracts.Licensing;

namespace ShiftClub.Application.Licensing;

/// <summary>
/// Что разрешено в каждом состоянии лицензии.
/// Деградация мягкая: клуб с полным залом в субботу не должен «погаснуть».
/// Текущие сеансы всегда доигрывают, смену всегда можно закрыть.
/// </summary>
public static class LicensePolicy
{
    /// <summary>За сколько дней до конца показывать предупреждение в панели.</summary>
    public const int WarnBeforeDays = 7;

    public static bool CanStartSessions(LicenseState state) => state switch
    {
        LicenseState.Missing => true,
        LicenseState.Active => true,
        _ => false
    };

    public static bool CanRegisterComputers(LicenseState state) => state switch
    {
        LicenseState.Missing => true,
        LicenseState.Active => true,
        _ => false
    };

    /// <summary>Продажи бара и пополнения. В льготном периоде ещё можно — чтобы смена закрылась честно.</summary>
    public static bool CanSell(LicenseState state) => state switch
    {
        LicenseState.Missing => true,
        LicenseState.Active => true,
        LicenseState.Grace => true,
        _ => false
    };

    public static bool IsBlocking(LicenseState state) =>
        state is LicenseState.Grace or LicenseState.Expired or LicenseState.Invalid;

    public static string Summary(LicenseState state, int? daysLeft, string? clubName) => state switch
    {
        LicenseState.Missing => "Лицензия не установлена",
        LicenseState.Active when daysLeft is { } d && d <= WarnBeforeDays =>
            $"Лицензия {clubName} заканчивается через {d} дн.",
        LicenseState.Active => $"Лицензия активна{(clubName is null ? "" : $" — {clubName}")}",
        LicenseState.Grace => "Срок лицензии истёк — льготный период",
        LicenseState.Expired => "Срок лицензии истёк",
        LicenseState.Invalid => "Лицензионный ключ недействителен",
        _ => "Лицензия"
    };

    public static string? Warning(LicenseState state, int? daysLeft, int graceDaysLeft) => state switch
    {
        LicenseState.Missing =>
            "Ключ лицензии не установлен. Вставьте ключ в разделе «Лицензия».",
        LicenseState.Active when daysLeft is { } d && d <= WarnBeforeDays =>
            $"Лицензия заканчивается через {d} дн. Продлите, чтобы сеансы не остановились.",
        LicenseState.Grace =>
            $"Срок истёк. Новые сеансы не запускаются. Текущие доигрывают, кассу можно закрыть. "
            + $"Полная блокировка через {graceDaysLeft} дн.",
        LicenseState.Expired =>
            "Срок истёк. Доступны только просмотр и отчёты. Продлите лицензию.",
        LicenseState.Invalid =>
            "Ключ не прошёл проверку подписи. Запросите новый ключ.",
        _ => null
    };

    /// <summary>Сообщение, которое увидит кассир при попытке действия.</summary>
    public static string BlockMessage(LicenseState state, string action) => state switch
    {
        LicenseState.Grace or LicenseState.Expired =>
            $"{action} недоступно: срок лицензии SHIFT истёк. Обратитесь к владельцу клуба.",
        LicenseState.Invalid =>
            $"{action} недоступно: лицензионный ключ недействителен.",
        LicenseState.Missing =>
            $"{action} недоступно: лицензия SHIFT не установлена.",
        _ => $"{action} недоступно."
    };

    public static string ComputerLimitMessage(int maxComputers) =>
        $"Достигнут лимит лицензии: {maxComputers} ПК. "
        + "Удалите неиспользуемый ПК или расширьте лицензию.";
}
