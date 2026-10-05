namespace ShiftClub.Shared.Contracts.ServerUpdates;

/// <summary>
/// Манифест выпуска сервера — то, что поставщик кладёт в канал обновлений.
/// </summary>
public sealed record ServerReleaseManifest(
    string Version,
    string Channel,
    string PackageFile,
    string Sha256,
    long SizeBytes,
    /// <summary>С версий ниже этой обновляться нельзя — сначала промежуточный выпуск.</summary>
    string? MinVersion,
    string? ReleaseNotes,
    DateTimeOffset? PublishedAt);

/// <summary>Чем закончилось последнее обновление. Пишет скрипт апдейтера.</summary>
public sealed record ServerUpdateRunDto(
    string Version,
    /// <summary>downloading, swapping, ok, rolled_back, failed.</summary>
    string State,
    string? Message,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record ServerUpdateStatusDto(
    bool FeedConfigured,
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string? ReleaseNotes,
    DateTimeOffset? PublishedAt,
    long SizeBytes,
    /// <summary>Почему обновиться нельзя: нет канала, не настроен апдейтер, выпуск требует промежуточного.</summary>
    string? Problem,
    /// <summary>Версия, пакет которой уже скачан и проверен по хешу.</summary>
    string? ReadyVersion,
    ServerUpdateRunDto? LastRun);

public sealed record StartServerUpdateRequest(string? Version);

public sealed record ServerUpdateStartResultDto(
    bool Started,
    string? Version,
    string Message);
