using ShiftClub.Shared.Contracts.Computers;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Application.Abstractions;

public interface IComputerService
{
    Task<RegisterComputerResponse> RegisterAsync(
        RegisterComputerRequest request,
        Guid? defaultBranchId,
        CancellationToken cancellationToken = default);

    Task<ApproveComputerResponse> ApproveAsync(
        Guid computerId,
        ApproveComputerRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    /// <summary>Создать ручную станцию (PS5 и т.п.) без Shell-агента.</summary>
    Task<ComputerDto> CreateManualStationAsync(
        CreateManualStationRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ComputerDto>> GetComputersAsync(
        Guid? branchId,
        CancellationToken cancellationToken = default);

    Task<ComputerDto?> GetByIdAsync(Guid computerId, CancellationToken cancellationToken = default);

    Task<ComputerDto> HeartbeatAsync(
        Guid computerId,
        ComputerHeartbeatRequest request,
        CancellationToken cancellationToken = default);

    Task<ComputerDto> UpdateLayoutAsync(
        Guid computerId,
        UpdateComputerLayoutRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<ComputerCommandDto> SendCommandAsync(
        Guid computerId,
        SendComputerCommandRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ComputerCommandDto>> GetPendingCommandsAsync(
        Guid computerId,
        CancellationToken cancellationToken = default);

    Task<ComputerCommandDto?> GetCommandAsync(
        Guid computerId,
        Guid commandId,
        CancellationToken cancellationToken = default);

    Task MarkCommandStatusAsync(
        Guid commandId,
        Guid computerId,
        ComputerCommandStatus status,
        string? error,
        string? resultJson = null,
        CancellationToken cancellationToken = default);

    Task<Guid?> ResolveComputerIdByDeviceTokenAsync(
        string deviceToken,
        CancellationToken cancellationToken = default);

    Task MarkStaleComputersOfflineAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid computerId, Guid employeeId, CancellationToken cancellationToken = default);

    Task<ComputerDto> RevokeAsync(Guid computerId, Guid employeeId, CancellationToken cancellationToken = default);

    Task<ComputerDto> UpdateAsync(
        Guid computerId,
        UpdateComputerRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    /// <summary>Wake-on-LAN magic packet (PC may be powered off — not a Shell command).</summary>
    Task<ComputerDto> WakeAsync(Guid computerId, Guid employeeId, CancellationToken cancellationToken = default);
}
