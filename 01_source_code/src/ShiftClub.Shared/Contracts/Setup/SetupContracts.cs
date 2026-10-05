namespace ShiftClub.Shared.Contracts.Setup;

/// <summary>Один пункт чек-листа установки.</summary>
public sealed record SetupStepDto(
    string Code,
    string Title,
    string Hint,
    bool Done,
    bool Required);

public sealed record SetupStatusDto(
    bool Completed,
    bool Required,
    int DoneCount,
    int TotalCount,
    IReadOnlyList<SetupStepDto> Steps,
    string Summary);

public sealed record ApplySetupRequest(
    string? ClubName,
    string? ShortName,
    string? TimeZoneId,
    string? CurrencyCode,
    string? Address,
    string? OwnerPassword,
    string? LicenseKey,
    bool MarkCompleted);

public sealed record ApplySetupResultDto(
    SetupStatusDto Status,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Problems);
