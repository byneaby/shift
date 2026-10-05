using ShiftClub.Shared.Contracts.Audit;

namespace ShiftClub.Application.Abstractions;

public interface IAuditQueryService
{
    Task<AuditLogPageDto> QueryAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? employeeId,
        string? action,
        string? search,
        bool includeTechnical,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListActionsAsync(
        bool includeTechnical,
        CancellationToken cancellationToken = default);
}
