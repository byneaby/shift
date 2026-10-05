namespace ShiftClub.Shared.Contracts.ClientUpdates;

public sealed record ClientUpdateCheckResponse(
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? Sha256,
    string? DownloadPath,
    string? ReleaseNotes,
    string? Channel,
    DateTimeOffset? PublishedAt);

public sealed record ClientUpdateManifestDto(
    string Version,
    string Channel,
    string PackageFile,
    string Sha256,
    string? MinVersion,
    string? ReleaseNotes,
    DateTimeOffset PublishedAt);
