using ShiftClub.Shared.Contracts.Import;

namespace ShiftClub.Application.Abstractions;

public interface ICustomerImportService
{
    Task<CustomerImportPreviewDto> PreviewAsync(
        CustomerImportRequest request,
        Guid branchId,
        CancellationToken cancellationToken = default);

    Task<CustomerImportResultDto> ImportAsync(
        CustomerImportRequest request,
        Guid branchId,
        Guid employeeId,
        CancellationToken cancellationToken = default);
}
