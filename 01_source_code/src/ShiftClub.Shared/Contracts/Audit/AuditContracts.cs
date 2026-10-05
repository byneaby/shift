namespace ShiftClub.Shared.Contracts.Audit;

public sealed record AuditLogDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid? EmployeeId,
    string? EmployeeName,
    string? EmployeeLogin,
    string Action,
    string EntityType,
    string? EntityId,
    string? DetailsJson,
    string? IpAddress);

public sealed record AuditLogPageDto(
    IReadOnlyList<AuditLogDto> Items,
    int Total,
    int Page,
    int PageSize);
