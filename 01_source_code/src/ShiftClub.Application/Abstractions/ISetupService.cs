using ShiftClub.Shared.Contracts.Setup;

namespace ShiftClub.Application.Abstractions;

public interface ISetupService
{
    Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ApplySetupResultDto> ApplyAsync(
        ApplySetupRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);
}
