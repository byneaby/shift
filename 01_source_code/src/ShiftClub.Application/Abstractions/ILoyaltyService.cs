using ShiftClub.Shared.Contracts.Customers;

namespace ShiftClub.Application.Abstractions;

public interface ILoyaltyService
{
    Task<IReadOnlyList<LoyaltyLevelDto>> ListAsync(Guid? branchId, CancellationToken cancellationToken = default);

    Task<LoyaltyLevelDto> CreateAsync(
        Guid? branchId,
        UpsertLoyaltyLevelRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<LoyaltyLevelDto> UpdateAsync(
        Guid id,
        UpsertLoyaltyLevelRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
