using ShiftClub.Shared.Contracts.News;

namespace ShiftClub.Application.Abstractions;

public interface IClubNewsService
{
    Task<IReadOnlyList<ClubNewsDto>> ListAdminAsync(Guid? branchId, bool includeUnpublished, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClientNewsDto>> ListPublishedAsync(Guid branchId, int take = 20, CancellationToken cancellationToken = default);
    Task<ClubNewsDto> CreateAsync(Guid branchId, UpsertClubNewsRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<ClubNewsDto> UpdateAsync(Guid id, UpsertClubNewsRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
