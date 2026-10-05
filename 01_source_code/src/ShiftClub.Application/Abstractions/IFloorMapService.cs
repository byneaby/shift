using ShiftClub.Shared.Contracts.FloorMap;

namespace ShiftClub.Application.Abstractions;

public interface IFloorMapService
{
    Task<FloorMapDto> GetMapAsync(Guid? branchId, CancellationToken cancellationToken = default);

    Task<FloorMapDto> UpdateSettingsAsync(
        Guid? branchId,
        UpdateFloorMapSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<FloorMapElementDto> CreateElementAsync(
        Guid? branchId,
        UpsertFloorMapElementRequest request,
        CancellationToken cancellationToken = default);

    Task<FloorMapElementDto> UpdateElementAsync(
        Guid id,
        UpsertFloorMapElementRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteElementAsync(Guid id, CancellationToken cancellationToken = default);
}
