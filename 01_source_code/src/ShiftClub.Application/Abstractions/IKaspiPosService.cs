using ShiftClub.Shared.Contracts.KaspiPos;

namespace ShiftClub.Application.Abstractions;

public interface IKaspiPosService
{
    Task<KaspiPosStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<KaspiPosConfigDto> GetConfigAsync(CancellationToken cancellationToken = default);

    Task UpdateHostAsync(string? host, Guid? updatedBy, CancellationToken cancellationToken = default);

    Task<KaspiPosRegisterResultDto> RegisterAsync(CancellationToken cancellationToken = default);

    Task<KaspiPosPaymentResultDto> PayAndWaitAsync(int amountKzt, CancellationToken cancellationToken = default);
}
