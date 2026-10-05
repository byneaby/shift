using ShiftClub.Shared.Contracts.StaffWiki;

namespace ShiftClub.Application.Abstractions;

public interface IStaffWikiService
{
    Task EnsureSeededAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffWikiPageListItemDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<StaffWikiPageDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<StaffWikiPageDto> CreateAsync(
        UpsertStaffWikiPageRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<StaffWikiPageDto> UpdateAsync(
        Guid id,
        UpsertStaffWikiPageRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> IsOwnerAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
