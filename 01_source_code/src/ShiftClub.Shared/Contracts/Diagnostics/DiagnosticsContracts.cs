namespace ShiftClub.Shared.Contracts.Diagnostics;

/// <summary>Группа одинаковых по сути ошибок: сколько раз и когда последний раз.</summary>
public sealed record ErrorGroupDto(
    string Fingerprint,
    string Level,
    string Source,
    string Kind,
    string Message,
    string? Where,
    int Count,
    DateTimeOffset FirstAt,
    DateTimeOffset LastAt,
    bool Reported);

public sealed record SystemCheckDto(
    string Code,
    string Title,
    bool Ok,
    string Value,
    string? Problem);

public sealed record SystemStatusDto(
    string Version,
    DateTimeOffset StartedAt,
    string Uptime,
    string ClubName,
    string? LicenseClub,
    string LicenseState,
    string Environment,
    string MachineName,
    DateTimeOffset ServerTimeUtc,
    string ClubTimeZone,
    string ClubTimeLocal,
    bool Ok,
    IReadOnlyList<SystemCheckDto> Checks,
    int ErrorsLastDay,
    IReadOnlyList<ErrorGroupDto> RecentErrors);
