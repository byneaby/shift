using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Shared.Contracts.Sessions;

public sealed record TariffDto(
    Guid Id,
    Guid BranchId,
    Guid? ZoneId,
    string? ZoneName,
    string Name,
    string Code,
    string? Description,
    TariffKind Kind,
    BillingMode BillingMode,
    TariffDurationMode DurationMode,
    decimal PricePerHour,
    decimal MinCharge,
    int? FixedDurationMinutes,
    decimal? FixedPrice,
    int? MinDurationMinutes,
    int? MaxDurationMinutes,
    int DaysOfWeekMask,
    string? AvailableFrom,
    string? AvailableTo,
    bool AllowPause,
    bool IsActive,
    int SortOrder,
    string? ColorHex,
    bool IsAvailableNow,
    /// <summary>Для TimeWindow при доступности сейчас: локальный конец периода «ЧЧ:ММ».</summary>
    string? WindowEndsAtLocal = null,
    /// <summary>Минуты до конца окна (TimeWindow), иначе null.</summary>
    int? RemainingMinutesInWindow = null,
    /// <summary>Текст для кассира/клиента перед продажей.</summary>
    string? SalePreview = null,
    /// <summary>Активная маркетинговая скидка %, 0 если нет.</summary>
    decimal PromoPercent = 0,
    /// <summary>Бейдж акции, напр. «1 месяц −50%».</summary>
    string? PromoLabel = null);

public sealed record UpsertTariffRequest(
    Guid? BranchId,
    Guid? ZoneId,
    string Name,
    string Code,
    string? Description,
    TariffKind Kind,
    BillingMode BillingMode,
    TariffDurationMode DurationMode,
    decimal PricePerHour,
    decimal MinCharge,
    int? FixedDurationMinutes,
    decimal? FixedPrice,
    int? MinDurationMinutes,
    int? MaxDurationMinutes,
    int DaysOfWeekMask,
    string? AvailableFrom,
    string? AvailableTo,
    bool AllowPause,
    bool IsActive,
    int SortOrder,
    string? ColorHex);

/// <summary>Обратная совместимость со старым CreateTariffRequest.</summary>
public sealed record CreateTariffRequest(
    Guid BranchId,
    Guid? ZoneId,
    string Name,
    string Code,
    TariffKind Kind,
    BillingMode BillingMode,
    decimal PricePerHour,
    decimal MinCharge,
    int? FixedDurationMinutes,
    decimal? FixedPrice);

public sealed record StartGuestSessionRequest(
    Guid ComputerId,
    Guid TariffId,
    int DurationMinutes,
    PaymentMethod PaymentMethod,
    string? GuestName,
    Guid? CustomerId,
    string? IdempotencyKey,
    bool UseTimeBank = false,
    /// <summary>Если задан — старт по брони: проверка ПК, скидка предоплаты, статусы Active.</summary>
    Guid? BookingId = null,
    /// <summary>Части оплаты при PaymentMethod = Mixed (нал + Kaspi и т.п.).</summary>
    IReadOnlyList<PaymentPartDto>? Payments = null);

public sealed record StartGuestSessionsBatchRequest(
    IReadOnlyList<Guid> ComputerIds,
    Guid TariffId,
    int DurationMinutes,
    PaymentMethod PaymentMethod,
    string? GuestName,
    Guid? CustomerId,
    string? IdempotencyKey,
    bool UseTimeBank = false,
    IReadOnlyList<PaymentPartDto>? Payments = null);

public sealed record StartGuestSessionsBatchResult(
    IReadOnlyList<SessionDto> Sessions,
    IReadOnlyList<string> Errors);

public sealed record ExtendSessionRequest(
    int AdditionalMinutes,
    PaymentMethod PaymentMethod,
    /// <summary>Сколько клиент передал наличными/картой. Должно быть ≥ расчётной суммы (сдача = разница).</summary>
    decimal? AmountTendered,
    string? IdempotencyKey,
    Guid? CustomerId = null,
    /// <summary>Купить другой тариф как продление (день/ночь/пакет/час). Null — доплата минут по текущему тарифу.</summary>
    Guid? TariffId = null,
    /// <summary>Части оплаты при PaymentMethod = Mixed.</summary>
    IReadOnlyList<PaymentPartDto>? Payments = null);

public sealed record ExtendSessionQuoteDto(
    int AdditionalMinutes,
    decimal ExpectedAmount,
    decimal PricePerHour,
    decimal PricePerMinute,
    string BillingMode,
    string TariffName,
    decimal ListAmount = 0,
    decimal DiscountAmount = 0,
    Guid? TariffId = null,
    string? DurationMode = null,
    decimal MarketingDiscount = 0,
    decimal LoyaltyDiscount = 0,
    decimal CaseDiscount = 0,
    decimal PrepaidApplied = 0,
    /// <summary>Прайс зоны после маркетинговой акции, до лояльности/кейса/брони.</summary>
    decimal AfterPromoAmount = 0);

public sealed record EndSessionRequest(
    bool ForceUnpaid = false,
    string? Reason = null,
    bool SaveRemainingToTimeBank = false);

public sealed record AttachSessionCustomerRequest(Guid CustomerId);

public sealed record TransferSessionRequest(
    Guid TargetComputerId,
    string? IdempotencyKey = null);

public sealed record SessionDto(
    Guid Id,
    Guid BranchId,
    Guid ComputerId,
    string? ComputerName,
    Guid ZoneId,
    string? ZoneName,
    Guid TariffId,
    string? TariffName,
    Guid? CustomerId,
    string? GuestName,
    PaymentMethod PaymentMethod,
    SessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? PlannedEndsAt,
    DateTimeOffset? ActualEndedAt,
    int DurationMinutes,
    decimal BasePrice,
    decimal DiscountAmount,
    decimal TotalPrice,
    decimal PaidAmount,
    decimal DebtAmount,
    int? RemainingSeconds,
    DateTimeOffset? PausedAt,
    bool AllowPause,
    TariffDurationMode DurationMode = TariffDurationMode.FixedDuration,
    DateTimeOffset? WindowPeriodStartsAt = null,
    DateTimeOffset? WindowPeriodEndsAt = null);

public sealed record SessionStartedEvent(SessionDto Session);
public sealed record SessionUpdatedEvent(SessionDto Session);
public sealed record SessionEndedEvent(SessionDto Session);
public sealed record SessionWarningEvent(Guid SessionId, Guid ComputerId, int MinutesLeft);
