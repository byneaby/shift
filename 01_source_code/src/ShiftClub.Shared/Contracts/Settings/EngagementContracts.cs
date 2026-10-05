namespace ShiftClub.Shared.Contracts.Settings;

public sealed record EngagementStreakTierDto(
    int Days,
    decimal BonusAmount,
    int BarRewards);

public sealed record EngagementDepositBonusTierDto(
    decimal MinAmount,
    decimal BonusAmount);

public sealed record EngagementSettingsDto(
    bool Enabled,
    IReadOnlyList<EngagementStreakTierDto> StreakTiers,
    decimal BirthdayBonusAmount,
    int BirthdayTimeBankMinutes,
    int TelegramChangeCooldownDays,
    string? PublicWebAppBaseUrl,
    bool DepositBonusEnabled,
    bool DepositBonusStackWithLoyaltyPercent,
    IReadOnlyList<EngagementDepositBonusTierDto> DepositBonusTiers);

public sealed record UpdateEngagementSettingsRequest(
    bool Enabled,
    IReadOnlyList<EngagementStreakTierDto> StreakTiers,
    decimal BirthdayBonusAmount,
    int BirthdayTimeBankMinutes,
    int TelegramChangeCooldownDays,
    string? PublicWebAppBaseUrl,
    bool DepositBonusEnabled,
    bool DepositBonusStackWithLoyaltyPercent,
    IReadOnlyList<EngagementDepositBonusTierDto>? DepositBonusTiers);

/// <summary>Stored JSON: app_settings key engagement.settings</summary>
public sealed class EngagementStoredSettings
{
    public bool Enabled { get; set; } = true;
    public List<EngagementStreakTier> StreakTiers { get; set; } =
    [
        new() { Days = 3, BonusAmount = 200, BarRewards = 0 },
        new() { Days = 5, BonusAmount = 500, BarRewards = 1 },
        new() { Days = 7, BonusAmount = 1000, BarRewards = 1 }
    ];
    public decimal BirthdayBonusAmount { get; set; } = 500;
    public int BirthdayTimeBankMinutes { get; set; } = 60;
    public int TelegramChangeCooldownDays { get; set; } = 30;
    /// <summary>HTTPS base for Telegram Mini App QR scanner, e.g. https://club.example.com</summary>
    public string? PublicWebAppBaseUrl { get; set; }

    /// <summary>Фиксированный бонус на BonusBalance при пополнении от суммы.</summary>
    public bool DepositBonusEnabled { get; set; } = true;
    /// <summary>Суммировать с % бонусом уровня лояльности.</summary>
    public bool DepositBonusStackWithLoyaltyPercent { get; set; } = true;
    public List<EngagementDepositBonusTier> DepositBonusTiers { get; set; } =
    [
        new() { MinAmount = 3000, BonusAmount = 300 },
        new() { MinAmount = 5000, BonusAmount = 500 },
        new() { MinAmount = 10000, BonusAmount = 1500 }
    ];
}

public sealed class EngagementStreakTier
{
    public int Days { get; set; }
    public decimal BonusAmount { get; set; }
    public int BarRewards { get; set; }
}

public sealed class EngagementDepositBonusTier
{
    public decimal MinAmount { get; set; }
    public decimal BonusAmount { get; set; }
}
