namespace ShiftClub.Shared.Contracts.Backups;

public sealed record BackupFileDto(
    string FileName,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string Kind);

public sealed record BackupStatusDto(
    bool Enabled,
    bool ToolAvailable,
    string? ToolPath,
    string Directory,
    int KeepDays,
    int DailyHourLocal,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastAttemptAt,
    string? LastError,
    long TotalSizeBytes,
    IReadOnlyList<BackupFileDto> Files,
    string Summary,
    string? Warning);

public sealed record RunBackupResultDto(
    bool Success,
    string? FileName,
    long SizeBytes,
    string Message);
