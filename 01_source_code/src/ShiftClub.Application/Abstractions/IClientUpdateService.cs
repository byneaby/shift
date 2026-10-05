using ShiftClub.Shared.Contracts.ClientUpdates;

namespace ShiftClub.Application.Abstractions;

public interface IClientUpdateService
{
    Task<ClientUpdateCheckResponse> CheckAsync(string? currentVersion, CancellationToken cancellationToken = default);

    Task<(Stream Stream, string FileName, string ContentType)?> OpenPackageAsync(
        string? version,
        CancellationToken cancellationToken = default);

    Task<ClientUpdateManifestDto?> GetCurrentManifestAsync(CancellationToken cancellationToken = default);

    Task<ClientUpdateManifestDto> PublishAsync(
        Stream packageStream,
        string version,
        string? releaseNotes,
        string? channel,
        CancellationToken cancellationToken = default);
}
