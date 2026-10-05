namespace ShiftClub.Shared.Contracts.TgWebApp;

public sealed record TgWebAppAuthRequest(string InitData);

public sealed record TgZoneTimeBankDto(Guid ZoneId, string ZoneName, int Minutes);

public sealed record TgWebAppAuthDto(
    Guid CustomerId,
    string FullName,
    string FirstName,
    string LastName,
    string Phone,
    string? Iin,
    decimal Balance,
    decimal BonusBalance,
    string? LoyaltyLevelName,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    int VisitStreakDays,
    int PendingBarRewards,
    int TimeBankMinutes,
    bool ComfortHideBalance,
    bool ComfortSoundEnabled,
    string ComfortLanguage,
    int ComfortBrightness,
    bool NeedsRegistration,
    bool IsStaff,
    Guid? EmployeeId,
    string? EmployeeDisplayName,
    bool HasPassword,
    long TelegramUserId,
    IReadOnlyList<TgZoneTimeBankDto>? TimeBanks = null,
    Guid? CurrentZoneId = null,
    string? CurrentZoneName = null,
    int CurrentZoneTimeBankMinutes = 0,
    int LoyaltyProgress = 0,
    int LoyaltyGoal = 0,
    decimal LoyaltyBonusPercent = 0,
    decimal LoyaltyTimeDiscountPercent = 0,
    int VisitCount = 0,
    decimal TotalSpent = 0);

public sealed record TgWebAppRegisterRequest(
    string InitData,
    string Phone,
    string FirstName,
    string LastName,
    string Password,
    string? Iin);

/// <summary>Привязка Telegram к уже существующему аккаунту клуба (телефон/логин + пароль или PIN).</summary>
public sealed record TgWebAppLinkRequest(
    string InitData,
    string PhoneOrLogin,
    string Password);

/// <summary>Подтверждение QR/кода с экрана ПК из Mini App (вход, привязка TG, сеанс…).</summary>
public sealed record TgWebAppQrConfirmRequest(
    string? InitData,
    string QrPayload);

public sealed record TgWebAppQrConfirmDto(
    string Message,
    string Purpose,
    TgWebAppAuthDto Auth,
    bool NeedsRegistration = false,
    Guid? TicketId = null,
    string? TicketCode = null);

/// <summary>Регистрация / привязка аккаунта после скана Login QR без существующего TG.</summary>
public sealed record TgWebAppQrRegisterRequest(
    string InitData,
    string TicketCode,
    string Phone,
    string FirstName,
    string LastName,
    string Password,
    string? Iin = null,
    bool LinkExisting = false);

public sealed record TgWebAppUpdateProfileRequest(
    string FirstName,
    string LastName,
    string Phone,
    string? Iin,
    string? NewPassword);

public sealed record TgWebAppSessionDto(
    Guid Id,
    string? ComputerName,
    string? ZoneName,
    string? TariffName,
    string Status,
    int MinutesLeft,
    DateTimeOffset StartedAt,
    DateTimeOffset? PlannedEndsAt,
    Guid? ZoneId = null,
    Guid? ComputerId = null);

public sealed record TgWebAppHomeDto(
    TgWebAppAuthDto Profile,
    TgWebAppSessionDto? ActiveSession,
    IReadOnlyList<TgWebAppNewsItemDto> News);

public sealed record TgWebAppNewsItemDto(
    Guid Id,
    string Title,
    string? Body,
    DateTimeOffset CreatedAt);

public sealed record TgWebAppBarProductDto(
    Guid Id,
    string CategoryName,
    string Name,
    decimal SalePrice,
    bool IsAvailable,
    string? ImageUrl);

public sealed record TgWebAppBarCatalogDto(
    IReadOnlyList<TgWebAppBarProductDto> Products);

public sealed record TgWebAppPlaceOrderRequest(
    IReadOnlyList<TgWebAppOrderItemRequest> Items,
    string PaymentMode = "PayAtCashier");

public sealed record TgWebAppOrderItemRequest(Guid ProductId, decimal Quantity);

public sealed record TgWebAppOrderDto(
    Guid Id,
    string Status,
    decimal Total,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Items);

public sealed record TgWebAppComfortRequest(
    bool ComfortHideBalance,
    bool ComfortSoundEnabled,
    string ComfortLanguage,
    int ComfortBrightness);

public sealed record TgEndSessionRequest(bool SaveRemainingToTimeBank = true);

public sealed record TgStaffHomeDto(
    TgWebAppAuthDto Profile,
    TgStaffFloorSummaryDto Floor,
    IReadOnlyList<TgStaffBarOrderDto> OpenBarOrders,
    TgStaffShiftDto? Shift,
    IReadOnlyList<TgStaffBookingDto> Bookings);

public sealed record TgStaffFloorSummaryDto(
    int Free,
    int Busy,
    int Offline,
    int Maintenance,
    IReadOnlyList<TgStaffPcDto> Pcs);

public sealed record TgStaffPcDto(
    Guid Id,
    string Name,
    string Occupancy,
    string? OccupancyDetail,
    int? RemainingSeconds,
    string? GuestName,
    string? ZoneName,
    Guid? SessionId = null,
    string StationKind = "Pc",
    Guid? BookingId = null,
    string? BookingContact = null,
    DateTimeOffset? BookingStartsAt = null,
    DateTimeOffset? BookingEndsAt = null);

public sealed record TgStaffBookingDto(
    Guid Id,
    string Number,
    string ContactName,
    string ContactPhone,
    string Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<string> ComputerNames);

public sealed record TgStaffBarOrderDto(
    Guid Id,
    string Status,
    decimal Total,
    DateTimeOffset CreatedAt,
    string? ComputerName,
    IReadOnlyList<string> Items);

public sealed record TgStaffShiftDto(
    Guid Id,
    string CashRegisterName,
    DateTimeOffset OpenedAt,
    decimal CashSales,
    decimal KaspiSales);

public sealed record TgFloorMapDto(
    Guid BranchId,
    int GridCols,
    int GridRows,
    string? BackgroundHex,
    TgFloorCountsDto Counts,
    IReadOnlyList<TgFloorPcDto> Pcs,
    IReadOnlyList<TgFloorElementDto> Elements);

public sealed record TgFloorCountsDto(int Free, int Busy, int Reserved, int Offline, int Maintenance, int Total);

public sealed record TgFloorPcDto(
    Guid Id,
    string Name,
    string Occupancy,
    string? OccupancyDetail,
    string? ZoneName,
    string? ZoneColorHex,
    int? GridCol,
    int? GridRow,
    int? RemainingMinutes,
    Guid? SessionId = null,
    string StationKind = "Pc",
    string? GuestName = null,
    Guid? BookingId = null,
    string? BookingContact = null,
    DateTimeOffset? BookingStartsAt = null,
    DateTimeOffset? BookingEndsAt = null);

public sealed record TgStaffWakeRequest(Guid ComputerId);

public sealed record TgStaffTransferRequest(Guid TargetComputerId);

public sealed record TgStaffTransferTargetDto(
    Guid Id,
    string Name,
    string Occupancy,
    string? OccupancyDetail,
    string? ZoneName,
    bool IsOffline);

/// <summary>Старт гостевого сеанса сотрудником из Mini App.</summary>
public sealed record TgStaffStartSessionRequest(
    Guid ComputerId,
    Guid TariffId,
    int DurationMinutes,
    string PaymentMethod,
    string? GuestName = null,
    Guid? CustomerId = null,
    bool UseTimeBank = false,
    /// <summary>Если ПК офлайн — сначала Wake-on-LAN, затем старт сеанса.</summary>
    bool WakeIfOffline = true);

public sealed record TgStaffExtendSessionRequest(
    int AdditionalMinutes,
    string PaymentMethod,
    decimal? AmountTendered = null,
    Guid? TariffId = null);

public sealed record TgStaffEndSessionRequest(
    bool SaveRemainingToTimeBank = true,
    string? Reason = null);

public sealed record TgStaffTariffDto(
    Guid Id,
    string Name,
    string Code,
    string Kind,
    string DurationMode,
    decimal PricePerHour,
    int? FixedDurationMinutes,
    decimal? FixedPrice,
    string? ZoneName,
    string? SalePreview,
    bool IsAvailableNow);

public sealed record TgStaffCustomerHitDto(
    Guid Id,
    string FullName,
    string Phone,
    decimal Balance,
    int TimeBankMinutes);

public sealed record TgFloorElementDto(
    Guid Id,
    string Kind,
    string? Label,
    int GridCol,
    int GridRow,
    int ColSpan,
    int RowSpan,
    string? ColorHex);
