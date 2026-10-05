namespace ShiftClub.Shared.Contracts.Settings;

public sealed record TelegramAllowedUserDto(
    long TelegramUserId,
    Guid? EmployeeId,
    string? DisplayName,
    bool ReceiveAlerts = true);

public sealed record TelegramBotSettingsDto(
    bool Enabled,
    bool HasToken,
    string? BotTokenMasked,
    string? BotUsername,
    IReadOnlyList<TelegramAllowedUserDto> AllowedUsers,
    IReadOnlyList<long> AlertChatIds,
    bool NotifyHelp,
    bool NotifySecurity,
    bool NotifyBar,
    bool NotifySessionWarning,
    bool NotifyBooking,
    Guid? DefaultEmployeeId,
    string RuntimeStatus,
    string? RuntimeDetail,
    string? PublicWebAppBaseUrl = null);

public sealed record UpdateTelegramBotSettingsRequest(
    bool Enabled,
    /// <summary>Null or whitespace = keep existing token. Empty string = clear token.</summary>
    string? BotToken,
    IReadOnlyList<TelegramAllowedUserDto>? AllowedUsers,
    IReadOnlyList<long>? AlertChatIds,
    bool NotifyHelp,
    bool NotifySecurity,
    bool NotifyBar,
    bool NotifySessionWarning,
    bool NotifyBooking,
    Guid? DefaultEmployeeId,
    string? PublicWebAppBaseUrl = null);

/// <summary>Stored JSON shape for app_settings key telegram.settings</summary>
public sealed class TelegramBotStoredSettings
{
    public bool Enabled { get; set; }
    public string? BotToken { get; set; }
    public string? BotUsername { get; set; }
    public List<TelegramAllowedUserDto> AllowedUsers { get; set; } = [];
    public List<long> AlertChatIds { get; set; } = [];
    public bool NotifyHelp { get; set; } = true;
    public bool NotifySecurity { get; set; } = true;
    public bool NotifyBar { get; set; } = true;
    public bool NotifySessionWarning { get; set; } = true;
    public bool NotifyBooking { get; set; } = true;
    public Guid? DefaultEmployeeId { get; set; }
    /// <summary>HTTPS origin for Mini App (сканер QR в боте), без слэша в конце.</summary>
    public string? PublicWebAppBaseUrl { get; set; }
}

public sealed record StaffAlertMessage(
    string Kind,
    string Title,
    string Body,
    Guid? ComputerId = null,
    string? ComputerName = null,
    Guid? OrderId = null,
    Guid? SessionId = null,
    Guid? BookingId = null,
    int? MinutesLeft = null);

public sealed record TelegramUsersAdminDto(
    int CustomersLinked,
    int EmployeesLinked,
    int AllowlistCount,
    IReadOnlyList<TelegramLinkedCustomerDto> Customers,
    IReadOnlyList<TelegramLinkedEmployeeDto> Employees);

public sealed record TelegramLinkedCustomerDto(
    Guid Id,
    string FullName,
    string Phone,
    long TelegramUserId,
    DateTimeOffset? LinkedAt,
    bool IsActive);

public sealed record TelegramLinkedEmployeeDto(
    Guid Id,
    string Login,
    string DisplayName,
    string? Phone,
    long? TelegramUserId,
    DateTimeOffset? LinkedAt,
    bool InAllowlist,
    bool IsActive);

public sealed record TelegramMakeStaffRequest(Guid EmployeeId, Guid? CustomerId = null, long? TelegramUserId = null);
