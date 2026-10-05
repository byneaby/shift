using ShiftClub.Shared.Contracts.Bookings;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.Contracts.TgWebApp;

namespace ShiftClub.Application.Abstractions;
public interface ITgWebAppService
{
    Task<TgWebAppAuthDto> AuthenticateAsync(string initData, CancellationToken cancellationToken = default);

    Task<TgWebAppAuthDto> RegisterAsync(TgWebAppRegisterRequest request, CancellationToken cancellationToken = default);

    Task<TgWebAppAuthDto> LinkExistingAsync(TgWebAppLinkRequest request, CancellationToken cancellationToken = default);

    Task<TgWebAppQrConfirmDto> ConfirmQrAsync(
        TgWebAppQrConfirmRequest request,
        string? bearerToken,
        CancellationToken cancellationToken = default);

    Task<TgWebAppQrConfirmDto> CompleteQrRegistrationAsync(
        TgWebAppQrRegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<TgWebAppHomeDto> GetHomeAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<TgWebAppAuthDto> GetProfileAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<TgWebAppAuthDto> UpdateProfileAsync(
        Guid customerId,
        long telegramUserId,
        TgWebAppUpdateProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<TgWebAppSessionDto?> GetActiveSessionAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task EndSessionAsync(
        Guid customerId,
        bool saveRemainingToTimeBank,
        CancellationToken cancellationToken = default);

    Task<TgWebAppBarCatalogDto> GetBarCatalogAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<TgWebAppOrderDto> PlaceBarOrderAsync(
        Guid customerId,
        TgWebAppPlaceOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TgWebAppOrderDto>> GetBarOrdersAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<TgWebAppAuthDto> UpdateComfortAsync(
        Guid customerId,
        TgWebAppComfortRequest request,
        CancellationToken cancellationToken = default);

    Task<TgStaffHomeDto> GetStaffHomeAsync(long telegramUserId, Guid? employeeId, CancellationToken cancellationToken = default);

    Task<TgFloorMapDto> GetFloorMapAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TgStaffTariffDto>> GetStaffTariffsAsync(
        Guid employeeId,
        Guid? computerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TgStaffCustomerHitDto>> SearchStaffCustomersAsync(
        Guid employeeId,
        string query,
        CancellationToken cancellationToken = default);

    Task<SessionDto> StaffStartSessionAsync(
        Guid employeeId,
        TgStaffStartSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<SessionDto> StaffExtendSessionAsync(
        Guid employeeId,
        Guid sessionId,
        TgStaffExtendSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<ExtendSessionQuoteDto> StaffQuoteExtendAsync(
        Guid employeeId,
        Guid sessionId,
        int additionalMinutes,
        CancellationToken cancellationToken = default,
        Guid? tariffId = null);

    Task<SessionDto> StaffEndSessionAsync(
        Guid employeeId,
        Guid sessionId,
        TgStaffEndSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<SessionDto> StaffPauseSessionAsync(
        Guid employeeId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<SessionDto> StaffResumeSessionAsync(
        Guid employeeId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task StaffWakeComputerAsync(
        Guid employeeId,
        Guid computerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TgStaffTransferTargetDto>> StaffTransferTargetsAsync(
        Guid employeeId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<SessionDto> StaffTransferSessionAsync(
        Guid employeeId,
        Guid sessionId,
        TgStaffTransferRequest request,
        CancellationToken cancellationToken = default);

    Task<BookingDto> StaffMarkBookingArrivedAsync(
        Guid employeeId,
        Guid bookingId,
        CancellationToken cancellationToken = default);

    Task<BookingDto> StaffCancelBookingAsync(
        Guid employeeId,
        Guid bookingId,
        string? reason,
        CancellationToken cancellationToken = default);

    TgAccessSession? ValidateAccessToken(string token);
}

public sealed record TgAccessSession(
    long TelegramUserId,
    Guid? CustomerId,
    Guid? EmployeeId,
    bool IsStaff);
