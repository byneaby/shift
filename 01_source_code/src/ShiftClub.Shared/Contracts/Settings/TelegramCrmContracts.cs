namespace ShiftClub.Shared.Contracts.Settings;

public sealed record TelegramCrmSettingsDto(
    bool Enabled,
    int QuietHourFrom,
    int QuietHourTo,
    int MinDaysBetweenMessages,
    int MaxSendsPerTick,
    int MaxSendsPerDay,
    bool WinbackEnabled,
    int WinbackAfterDays,
    int WinbackCooldownDays,
    bool UnusedKeyEnabled,
    int UnusedKeyMinIdleDays,
    int UnusedKeyCooldownDays,
    bool PromoNudgeEnabled,
    int PromoNudgeCooldownDays,
    TelegramCrmStatsDto Stats);

public sealed record TelegramCrmStatsDto(
    int LinkedCustomers,
    int SentToday,
    int WinbackCandidates,
    int UnusedKeyCandidates,
    int PromoCandidates);

public sealed record UpdateTelegramCrmSettingsRequest(
    bool Enabled,
    int QuietHourFrom,
    int QuietHourTo,
    int MinDaysBetweenMessages,
    int MaxSendsPerTick,
    int MaxSendsPerDay,
    bool WinbackEnabled,
    int WinbackAfterDays,
    int WinbackCooldownDays,
    bool UnusedKeyEnabled,
    int UnusedKeyMinIdleDays,
    int UnusedKeyCooldownDays,
    bool PromoNudgeEnabled,
    int PromoNudgeCooldownDays);

/// <summary>Stored JSON: app_settings key telegram.crm</summary>
public sealed class TelegramCrmStoredSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Локальные часы клуба: с какого часа можно слать (включительно).</summary>
    public int QuietHourFrom { get; set; } = 11;

    /// <summary>До какого часа (невключительно). Например 21 = до 20:59.</summary>
    public int QuietHourTo { get; set; } = 21;

    /// <summary>Минимум дней между любыми автосообщениями одному клиенту.</summary>
    public int MinDaysBetweenMessages { get; set; } = 5;

    public int MaxSendsPerTick { get; set; } = 20;
    public int MaxSendsPerDay { get; set; } = 60;

    public bool WinbackEnabled { get; set; } = true;
    public int WinbackAfterDays { get; set; } = 10;
    public int WinbackCooldownDays { get; set; } = 21;

    public bool UnusedKeyEnabled { get; set; } = true;
    public int UnusedKeyMinIdleDays { get; set; } = 3;
    public int UnusedKeyCooldownDays { get; set; } = 14;

    public bool PromoNudgeEnabled { get; set; } = true;
    public int PromoNudgeCooldownDays { get; set; } = 10;
}

public static class TelegramCrmKinds
{
    public const string Winback = "winback";
    public const string UnusedKey = "unused_key";
    public const string Promo = "promo";
}
