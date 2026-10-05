using ShiftClub.Shared.Contracts.Branches;

namespace ShiftClub.Application.Abstractions;

public interface IBranchService
{
    Task<IReadOnlyList<BranchDto>> GetBranchesAsync(CancellationToken cancellationToken = default);
}
