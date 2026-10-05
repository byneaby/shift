namespace ShiftClub.Shared.Contracts.ClientLauncher;

public sealed record ClientCustomerLoginRequest(string PhoneOrLogin, string Pin);

/// <summary>Текущая бронь, удерживающая ПК (soft-hold / прибыл / reserved).</summary>
public sealed record ClientBookingHoldDto(
    Guid BookingId,
    string Number,
    string ContactName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsOwner);

public sealed record ClientZoneTimeBankDto(Guid ZoneId, string ZoneName, int Minutes);

public sealed record ClientCustomerAuthDto(
    Guid CustomerId,
    string FullName,
    string Phone,
    decimal Balance,
    decimal BonusBalance,
    string? LoyaltyLevelName,
    string CustomerToken,
    DateTimeOffset ExpiresAt,
    int VisitCount = 0,
    decimal TotalSpent = 0,
    int TimeBankMinutes = 0,
    int LoyaltyProgress = 0,
    int LoyaltyGoal = 500,
    IReadOnlyList<ClientZoneTimeBankDto>? TimeBanks = null,
    Guid? CurrentZoneId = null,
    string? CurrentZoneName = null,
    int CurrentZoneTimeBankMinutes = 0,
    string? Email = null,
    string? Login = null,
    bool HasPin = false,
    bool HasPassword = false,
    string? FirstName = null,
    string? LastName = null,
    /// <summary>% бонуса при пополнении по текущему уровню лояльности.</summary>
    decimal LoyaltyBonusPercent = 0,
    /// <summary>% скидки на игровое время по текущему уровню.</summary>
    decimal LoyaltyTimeDiscountPercent = 0,
    int VisitStreakDays = 0,
    int PendingBarRewards = 0,
    bool ComfortHideBalance = false,
    bool ComfortSoundEnabled = true,
    string ComfortLanguage = "ru",
    int ComfortBrightness = 100,
    DateOnly? BirthDate = null,
    bool TelegramLinked = false,
    string? EngagementMessage = null,
    /// <summary>Если задано — смена Telegram доступна с этой даты (UTC).</summary>
    DateTimeOffset? TelegramChangeAvailableAt = null);

public sealed record ClientComfortSettingsRequest(
    bool ComfortHideBalance,
    bool ComfortSoundEnabled,
    string ComfortLanguage,
    int ComfortBrightness);

public sealed record ClientTelegramTicketRequest(string Purpose, Guid? SessionId = null);

public sealed record ClientTelegramTicketDto(
    Guid TicketId,
    string Code,
    string DeepLink,
    string QrPngBase64,
    DateTimeOffset ExpiresAt,
    string Purpose);

public sealed record ClientTelegramTicketStatusDto(
    string Status,
    ClientCustomerAuthDto? Auth,
    string? Message,
    bool NeedsRegistration = false,
    string? TelegramDisplayName = null);

/// <summary>Завершение регистрации на ПК после скана QR без привязанного Telegram.</summary>
public sealed record ClientTelegramCompleteRegistrationRequest(
    string FirstName,
    string LastName,
    string Phone,
    string Password,
    string? Iin = null,
    bool LinkExisting = false);

/// <summary>Смена ПИН/пароля/логина с Shell (нужен текущий секрет).</summary>
public sealed record ClientChangeCredentialsRequest(
    string CurrentSecret,
    string? NewPin = null,
    string? NewPassword = null,
    string? Login = null);

public sealed record ClientUpdateProfileRequest(
    string FirstName,
    string LastName,
    string? Email);

public sealed record ClientAccountSessionDto(
    Guid Id,
    string? ComputerName,
    string? ZoneName,
    string? TariffName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int DurationMinutes,
    decimal TotalPrice,
    string PaymentMethod = "Free",
    DateTimeOffset? PlannedEndsAt = null);

public sealed record ClientAccountBookingDto(
    Guid Id,
    string Number,
    string Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? ZoneName,
    string? Computers,
    decimal PrepaidAmount);

public sealed record ClientBarProductDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string Sku,
    decimal SalePrice,
    decimal StockQty,
    bool IsAvailable,
    string? ImageUrl = null);

public sealed record ClientBarCategoryDto(Guid Id, string Name, string Code);

public sealed record ClientBarCatalogDto(
    IReadOnlyList<ClientBarCategoryDto> Categories,
    IReadOnlyList<ClientBarProductDto> Products);

public sealed record ClientPlaceBarOrderItemRequest(Guid ProductId, decimal Quantity);

/// <summary>
/// Заказ с клиента. Предпочтительно <see cref="Items"/> (корзина).
/// Поля ProductId/Quantity — для совместимости со старыми клиентами (одна позиция).
/// </summary>
public sealed record ClientPlaceBarOrderRequest(
    string PaymentMode,
    string? IdempotencyKey,
    IReadOnlyList<ClientPlaceBarOrderItemRequest>? Items = null,
    Guid? ProductId = null,
    decimal? Quantity = null);

public sealed record ClientBarOrderDto(
    Guid Id,
    string Number,
    string Status,
    string PaymentMode,
    decimal Total,
    DateTimeOffset CreatedAt,
    string? ItemsSummary = null,
    int ItemCount = 0);

public sealed record ClientEndSessionRequest(bool SaveRemainingToTimeBank);

public sealed record ClientStartSessionRequest(
    Guid TariffId,
    int DurationMinutes,
    string? IdempotencyKey,
    bool UseTimeBank = false);

public sealed record SoftwareAppDto(
    Guid Id,
    string Name,
    string Category,
    string ExePath,
    string? Arguments,
    string? WorkingDirectory,
    int SortOrder,
    bool FileExists,
    string? IconPath = null,
    string? LaunchSoundUrl = null);

public sealed record SoftwareAppAdminDto(
    Guid Id,
    Guid BranchId,
    string Name,
    string Category,
    string ExePath,
    string? Arguments,
    string? WorkingDirectory,
    string? IconPath,
    string? LaunchSoundUrl,
    int SortOrder,
    bool IsActive,
    int? MinAge);

public sealed record UpsertSoftwareAppRequest(
    string Name,
    string Category,
    string ExePath,
    string? Arguments,
    string? WorkingDirectory,
    string? IconPath,
    int SortOrder,
    bool IsActive,
    int? MinAge);

/// <summary>Файл со скана игрового сервера (Scan-GamesToShell.ps1).</summary>
public sealed record SoftwareAppsImportFile(
    string Schema,
    string Source,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<UpsertSoftwareAppRequest> Apps);

public sealed record SoftwareAppsImportResult(int Created, int Updated, int Skipped, int Total);

public sealed record AppLaunchResultRequest(bool Success, string? ErrorMessage);

public sealed record ClientHelpRequest(string? Message);

public sealed record ClientSecurityAlertRequest(string Reason, string? Detail = null);

public sealed record ClientExtendSessionRequest(
    int AdditionalMinutes,
    string? IdempotencyKey,
    Guid? TariffId = null);
