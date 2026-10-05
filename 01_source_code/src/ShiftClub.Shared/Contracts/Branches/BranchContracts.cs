namespace ShiftClub.Shared.Contracts.Branches;

public sealed record BranchDto(
    Guid Id,
    string Name,
    string Code,
    string TimeZoneId,
    string CurrencyCode,
    bool IsActive,
    IReadOnlyList<ZoneDto> Zones);

public sealed record ZoneDto(
    Guid Id,
    Guid BranchId,
    string Name,
    string Code,
    string ColorHex,
    int SortOrder,
    int? MinSessionMinutes,
    bool IsActive,
    string Kind = "Hall",
    int GridColumns = 6,
    int GridRows = 4);

public sealed record UpsertZoneRequest(
    Guid? BranchId,
    string Name,
    string Code,
    string ColorHex,
    int SortOrder,
    int? MinSessionMinutes,
    bool IsActive,
    string? Kind = null,
    int? GridColumns = null,
    int? GridRows = null);
