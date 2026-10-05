using ShiftClub.Shared.Contracts.Cases;

namespace ShiftClub.Shared.Contracts.Public;

public sealed record PublicClubInfoDto(
    string Name,
    string? City,
    string? Address,
    string? Phone,
    string? BotUsername,
    string? PublicWebAppBaseUrl);

public sealed record PublicFloorPcDto(
    Guid Id,
    string Name,
    string Occupancy,
    string? ZoneName,
    string? ZoneColorHex,
    int? GridCol,
    int? GridRow);

public sealed record PublicFloorElementDto(
    string Kind,
    string? Label,
    int GridCol,
    int GridRow,
    int ColSpan,
    int RowSpan,
    string? ColorHex);

public sealed record PublicFloorCountsDto(int Free, int Busy, int Reserved, int Offline, int Maintenance, int Total);

public sealed record PublicFloorMapDto(
    int GridCols,
    int GridRows,
    string? BackgroundHex,
    PublicFloorCountsDto Counts,
    IReadOnlyList<PublicFloorPcDto> Pcs,
    IReadOnlyList<PublicFloorElementDto> Elements);

public sealed record PublicLoyaltyLevelDto(
    string Name,
    string Code,
    decimal MinSpent,
    decimal BonusPercent,
    decimal TimeDiscountPercent);

public sealed record PublicStreakTierDto(
    int Days,
    decimal BonusAmount,
    int BarRewards);

public sealed record PublicDepositBonusTierDto(
    decimal MinAmount,
    decimal BonusAmount);

public sealed record PublicLoyaltyInfoDto(
    string ClubName,
    bool RewardsEnabled,
    IReadOnlyList<PublicLoyaltyLevelDto> Levels,
    IReadOnlyList<PublicStreakTierDto> StreakTiers,
    decimal BirthdayBonusAmount,
    int BirthdayTimeBankMinutes,
    bool DepositBonusEnabled,
    IReadOnlyList<PublicDepositBonusTierDto> DepositBonusTiers);

/// <summary>Публичный прайс для TV-стойки /price.</summary>
public sealed record PublicPriceBoardDto(
    string Currency,
    IReadOnlyList<PublicPriceZoneDto> Zones,
    PublicPriceExtrasDto? Extras,
    PublicPriceWindowsDto Windows,
    PublicPricePromoDto? Promo = null,
    PublicPriceDepositBonusDto? DepositBonus = null);

public sealed record PublicPriceWindowsDto(
    int DayFromHour,
    int DayToHour,
    int NightFromHour,
    int NightToHour,
    string DayLabel,
    string NightLabel);

public sealed record PublicPricePromoDto(
    bool Active,
    decimal Percent,
    string Label,
    string? Title,
    DateTimeOffset? EndsAt);

public sealed record PublicPriceDepositBonusDto(
    bool Enabled,
    IReadOnlyList<PublicDepositBonusTierDto> Tiers);

/// <summary>Регистрация на странице акции: аккаунт + открытие SHIFT CASE за ключ регистрации.</summary>
public sealed record CampaignRegisterRequest(
    string FirstName,
    string LastName,
    string Phone,
    string Password,
    string? Pin = null);

public sealed record CampaignRegisterResultDto(
    Guid CustomerId,
    string DisplayName,
    string Phone,
    CaseOpenResultDto? CaseOpen,
    string Message);

public sealed record PublicPriceZoneDto(
    string Id,
    string Code,
    string Name,
    string Color,
    IReadOnlyList<PublicPriceRowDto> Rows);

public sealed record PublicPriceRowDto(
    string Key,
    string Label,
    string Note,
    string Icon,
    decimal Price,
    /// <summary>Цена до акции; null если скидки нет.</summary>
    decimal? OriginalPrice = null);

public sealed record PublicPriceExtrasDto(
    string Title,
    IReadOnlyList<PublicPriceExtraItemDto> Items);

public sealed record PublicPriceExtraItemDto(
    string Id,
    string Label,
    string Note,
    decimal Price,
    decimal? OriginalPrice = null);

/// <summary>Публичное меню бара для TV-стойки /price/bar.</summary>
public sealed record PublicBarMenuDto(
    string Currency,
    IReadOnlyList<PublicBarCategoryDto> Categories);

public sealed record PublicBarCategoryDto(
    string Id,
    string Code,
    string Name,
    string Color,
    IReadOnlyList<PublicBarItemDto> Items);

public sealed record PublicBarItemDto(
    string Id,
    string Sku,
    string Name,
    string Note,
    decimal Price,
    string? ImageUrl = null);

public sealed record TelegramBroadcastRequest(
    /// <summary>customers | staff | both</summary>
    string Audience,
    string Message);

public sealed record TelegramBroadcastResultDto(
    int Queued,
    int Customers,
    int Staff,
    string Preview);
