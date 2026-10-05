using ShiftClub.Shared.Contracts.Branches;

namespace ShiftClub.Application.Abstractions;

public interface IZoneService
{
    Task<IReadOnlyList<ZoneDto>> ListAsync(Guid? branchId, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<ZoneDto> CreateAsync(UpsertZoneRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<ZoneDto> UpdateAsync(Guid id, UpsertZoneRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default);
}
