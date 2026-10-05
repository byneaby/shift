namespace ShiftClub.Shared.Licensing;

/// <summary>
/// Коды фич для лицензии. Одна сборка — разные SKU.
/// Фича, отсутствующая в лицензии, выключается в панели и в рассылках.
/// </summary>
public static class LicenseFeatures
{
    /// <summary>Рулетка SHIFT CASE (ключи, призы, публичная рулетка).</summary>
    public const string ShiftCase = "shift_case";

    /// <summary>Несколько филиалов в одной панели.</summary>
    public const string MultiBranch = "multi_branch";

    /// <summary>Автосообщения Telegram CRM (win-back, промо, напоминания).</summary>
    public const string TelegramCrm = "telegram_crm";

    /// <summary>Приём оплат через Kaspi Smart POS.</summary>
    public const string KaspiPos = "kaspi_pos";

    public static readonly string[] All =
    [
        ShiftCase,
        MultiBranch,
        TelegramCrm,
        KaspiPos
    ];

    public static string Label(string code) => code switch
    {
        ShiftCase => "SHIFT CASE (рулетка)",
        MultiBranch => "Несколько филиалов",
        TelegramCrm => "Telegram CRM",
        KaspiPos => "Kaspi Smart POS",
        _ => code
    };
}
