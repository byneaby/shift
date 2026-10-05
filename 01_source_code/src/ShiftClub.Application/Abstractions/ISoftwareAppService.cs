using ShiftClub.Shared.Contracts.ClientLauncher;

namespace ShiftClub.Application.Abstractions;

public interface ISoftwareAppService
{
    Task<IReadOnlyList<SoftwareAppAdminDto>> ListAsync(Guid? branchId, CancellationToken cancellationToken = default);
    Task<SoftwareAppAdminDto> CreateAsync(Guid branchId, UpsertSoftwareAppRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<SoftwareAppAdminDto> UpdateAsync(Guid id, UpsertSoftwareAppRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SoftwareAppAdminDto> SetIconPathAsync(Guid id, string iconPath, Guid employeeId, CancellationToken cancellationToken = default);
    Task<SoftwareAppAdminDto> SetLaunchSoundUrlAsync(Guid id, string? soundUrl, Guid employeeId, CancellationToken cancellationToken = default);
    Task<SoftwareAppsImportResult> ImportAsync(
        Guid branchId,
        IReadOnlyList<UpsertSoftwareAppRequest> apps,
        Guid employeeId,
        bool updateExisting,
        CancellationToken cancellationToken = default);
}
