using ShiftClub.Shared.Contracts.Sessions;

namespace ShiftClub.Application.Abstractions;

public interface ISessionService
{
    Task<IReadOnlyList<TariffDto>> GetTariffsAsync(
        Guid? branchId,
        bool includeInactive = false,
        bool onlyAvailableNow = false,
        Guid? zoneId = null,
        CancellationToken cancellationToken = default);

    Task<TariffDto> CreateTariffAsync(UpsertTariffRequest request, CancellationToken cancellationToken = default);
    Task<TariffDto> UpdateTariffAsync(Guid id, UpsertTariffRequest request, CancellationToken cancellationToken = default);
    Task DeactivateTariffAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SessionDto> StartGuestSessionAsync(StartGuestSessionRequest request, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Публичный /free: 5ч на ПК без кассы и отчётов.</summary>
    Task<SessionDto> StartComplimentaryAsync(
        Guid computerId,
        int durationMinutes,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<StartGuestSessionsBatchResult> StartGuestSessionsBatchAsync(
        StartGuestSessionsBatchRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<SessionDto> ExtendAsync(Guid sessionId, ExtendSessionRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<ExtendSessionQuoteDto> QuoteExtendAsync(
        Guid sessionId,
        int additionalMinutes,
        CancellationToken cancellationToken = default,
        Guid? tariffId = null);
    Task<SessionDto> EndAsync(Guid sessionId, EndSessionRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<SessionDto> TransferAsync(Guid sessionId, TransferSessionRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<SessionDto> PauseAsync(Guid sessionId, Guid employeeId, CancellationToken cancellationToken = default);
    Task<SessionDto> ResumeAsync(Guid sessionId, Guid employeeId, CancellationToken cancellationToken = default);

    Task<SessionDto?> GetActiveByComputerAsync(Guid computerId, CancellationToken cancellationToken = default);
    Task<SessionDto?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task ProcessDueSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Привязать клиента к гостевому сеансу (Telegram QR).</summary>
    Task<SessionDto> AttachCustomerAsync(Guid sessionId, Guid customerId, CancellationToken cancellationToken = default);
}
