using ShiftClub.Shared.Contracts.Diagnostics;

namespace ShiftClub.Application.Abstractions;

public interface IDiagnosticsService
{
    Task<SystemStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<ErrorGroupDto> GetErrors(int limit = 50);

    void ClearErrors();
}
