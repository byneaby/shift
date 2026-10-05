using ShiftClub.Shared.Contracts.Branding;

namespace ShiftClub.Application.Abstractions;

public interface IBrandingService
{
    Task<BrandingDto> GetAsync(CancellationToken cancellationToken = default);

    Task<BrandingDto> UpdateAsync(
        UpdateBrandingRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default);
}
