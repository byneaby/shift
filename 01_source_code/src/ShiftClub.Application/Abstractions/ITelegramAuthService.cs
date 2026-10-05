using ShiftClub.Shared.Contracts.Auth;
using ShiftClub.Shared.Contracts.ClientLauncher;

namespace ShiftClub.Application.Abstractions;

public interface ITelegramAuthService
{
    Task<ClientTelegramTicketDto> CreateTicketAsync(
        Guid computerId,
        string purpose,
        Guid? sessionId,
        Guid? customerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>QR/deep-link вход сотрудника в веб-панель (без ПК).</summary>
    Task<StaffTelegramTicketDto> CreateStaffLoginTicketAsync(
        string clientNonce,
        string? clientIp = null,
        CancellationToken cancellationToken = default);

    Task<StaffTelegramTicketStatusDto> GetStaffLoginStatusAsync(
        Guid ticketId,
        string? clientNonce = null,
        CancellationToken cancellationToken = default);

    /// <summary>Извлекает код тикета из deep link / QR / ручного ввода.</summary>
    string? TryExtractTicketCode(string? raw);

    Task<ClientTelegramTicketStatusDto> GetTicketStatusAsync(
        Guid computerId,
        Guid ticketId,
        CancellationToken cancellationToken = default);

    Task CancelTicketAsync(
        Guid ticketId,
        string? clientNonce = null,
        CancellationToken cancellationToken = default);

    Task<string> CompleteTicketFromTelegramAsync(
        string code,
        long telegramUserId,
        string? telegramDisplayName,
        CancellationToken cancellationToken = default);

    /// <summary>Завершить регистрацию на ПК после AwaitingRegistration.</summary>
    Task<ClientTelegramTicketStatusDto> CompleteRegistrationOnPcAsync(
        Guid computerId,
        Guid ticketId,
        ClientTelegramCompleteRegistrationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Завершить регистрацию/привязку в Mini App после AwaitingRegistration.</summary>
    Task<(string Message, Guid CustomerId)> CompleteRegistrationFromTelegramAsync(
        string ticketCode,
        long telegramUserId,
        string phone,
        string firstName,
        string lastName,
        string password,
        string? iin,
        bool linkExisting,
        CancellationToken cancellationToken = default);

    Task LinkTelegramToCustomerAsync(
        Guid customerId,
        long telegramUserId,
        CancellationToken cancellationToken = default);

    Task<CustomerTelegramProfile?> FindCustomerByTelegramAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default);
}

public sealed record CustomerTelegramProfile(
    Guid Id,
    Guid BranchId,
    string FullName,
    string Phone,
    decimal Balance,
    decimal BonusBalance,
    int TimeBankMinutes,
    int VisitStreakDays,
    int PendingBarRewards,
    string? LoyaltyLevelName,
    bool ComfortHideBalance,
    bool ComfortSoundEnabled,
    string ComfortLanguage,
    int ComfortBrightness,
    DateOnly? BirthDate);
