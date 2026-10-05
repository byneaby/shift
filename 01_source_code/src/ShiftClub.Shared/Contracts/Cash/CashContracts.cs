using ShiftClub.Shared.Enums;

namespace ShiftClub.Shared.Contracts.Cash;

public sealed record CashRegisterDto(Guid Id, Guid BranchId, string Name, string Code, bool IsActive);

public sealed record CashShiftDto(
    Guid Id,
    Guid CashRegisterId,
    string CashRegisterName,
    Guid BranchId,
    string Number,
    CashShiftStatus Status,
    Guid OpenedByEmployeeId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal OpeningCash,
    decimal? ClosingCashActual,
    decimal? ClosingCashExpected,
    decimal? Discrepancy,
    decimal SalesCash,
    decimal SalesCard,
    decimal SalesKaspi,
    decimal SalesTransfer,
    decimal SalesOther,
    decimal RefundsCash,
    decimal RefundsCard,
    decimal RefundsKaspi,
    decimal RefundsTransfer,
    decimal RefundsOther,
    decimal DepositsTotal,
    decimal CashInTotal,
    decimal CashOutTotal,
    decimal ExpenseTotal,
    decimal ExpectedCashNow,
    decimal CollectedNow,
    decimal RevenueNow);

public sealed record OpenCashShiftRequest(
    Guid CashRegisterId,
    decimal OpeningCash,
    string? Comment);

public sealed record CloseCashShiftRequest(
    decimal ClosingCashActual,
    string? DiscrepancyReason,
    string? Comment);

public sealed record CashMovementRequest(
    CashMovementType Type,
    decimal Amount,
    string? Category,
    string? Comment);

public sealed record PaymentPartDto(PaymentMethod Method, decimal Amount);

public sealed record ReceiptItemDto(
    Guid Id,
    ReceiptItemType ItemType,
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal LineTotal,
    Guid? ReferenceId);

public sealed record ReceiptDto(
    Guid Id,
    string Number,
    ReceiptStatus Status,
    Guid CashShiftId,
    Guid? ComputerId,
    Guid? GamingSessionId,
    Guid? CustomerId,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    decimal PaidTotal,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReceiptItemDto> Items,
    IReadOnlyList<PaymentPartDto> Payments,
    string? ComputerName = null,
    StationKind? StationKind = null);

public sealed record CreateSaleRequest(
    Guid? ComputerId,
    Guid? GamingSessionId,
    string? Comment,
    string? IdempotencyKey,
    IReadOnlyList<CreateSaleItemRequest> Items,
    IReadOnlyList<PaymentPartDto> Payments,
    Guid? CustomerId = null);

public sealed record CreateSaleItemRequest(
    ReceiptItemType ItemType,
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    Guid? ReferenceId);

public sealed record CashZReportDto(
    Guid ShiftId,
    string Number,
    string CashRegisterName,
    CashShiftStatus Status,
    Guid OpenedByEmployeeId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal OpeningCash,
    decimal? ClosingCashActual,
    decimal? ClosingCashExpected,
    decimal? Discrepancy,
    string? DiscrepancyReason,
    decimal SalesCash,
    decimal SalesCard,
    decimal SalesKaspi,
    decimal SalesTransfer,
    decimal SalesOther,
    decimal RefundsCash,
    decimal RefundsCard,
    decimal RefundsKaspi,
    decimal RefundsTransfer,
    decimal RefundsOther,
    decimal CashInTotal,
    decimal CashOutTotal,
    decimal ExpenseTotal,
    decimal ExpectedCash,
    decimal CollectedTotal,
    decimal RevenueTotal,
    decimal DepositsTotal,
    int PaidReceiptCount,
    int RefundedReceiptCount,
    IReadOnlyList<MoneyKeyDto> ByPaymentMethod,
    IReadOnlyList<MoneyKeyDto> ByItemType,
    IReadOnlyList<CashMovementRowDto> Movements);

public sealed record MoneyKeyDto(string Key, decimal Amount, int Count);

public sealed record CashMovementRowDto(
    CashMovementType Type,
    decimal Amount,
    string? Category,
    string? Comment,
    DateTimeOffset CreatedAt);
