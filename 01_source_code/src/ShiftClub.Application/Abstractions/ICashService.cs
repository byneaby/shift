using ShiftClub.Shared.Contracts.Cash;

namespace ShiftClub.Application.Abstractions;

/// <summary>
/// Владелец провёл операцию без своей смены — чек/действие привязаны к смене кассира.
/// </summary>
public sealed record CashOwnerProxyInfo(
    Guid ShiftId,
    string ShiftNumber,
    Guid CashierEmployeeId,
    string CashierName,
    string OwnerName);

public interface ICashService
{
    Task<IReadOnlyList<CashRegisterDto>> GetRegistersAsync(Guid? branchId, CancellationToken cancellationToken = default);
    Task<CashShiftDto?> GetOpenShiftAsync(Guid? cashRegisterId, Guid? employeeId, CancellationToken cancellationToken = default);
    Task<CashShiftDto> OpenShiftAsync(OpenCashShiftRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<CashShiftDto> CloseShiftAsync(Guid shiftId, CloseCashShiftRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<CashShiftDto> ForceCloseShiftAsync(Guid shiftId, CloseCashShiftRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<CashShiftDto> AddMovementAsync(Guid shiftId, CashMovementRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<ReceiptDto> CreateSaleAsync(CreateSaleRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<ReceiptDto> RefundReceiptAsync(Guid receiptId, string? comment, Guid employeeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReceiptDto>> GetRecentReceiptsAsync(Guid shiftId, int take = 30, CancellationToken cancellationToken = default);
    Task<CashZReportDto> GetShiftReportAsync(Guid shiftId, CancellationToken cancellationToken = default);
    Task EnsureEmployeeHasOpenShiftAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Если сотрудник — владелец без своей смены, но есть чужая открытая: контекст для истории.
    /// Иначе null (своя смена или не владелец).
    /// </summary>
    Task<CashOwnerProxyInfo?> GetOwnerCashProxyInfoAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
