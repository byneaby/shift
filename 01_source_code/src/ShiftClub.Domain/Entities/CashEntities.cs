using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class CashRegister : Common.Entity
{
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<CashShift> Shifts { get; set; } = new List<CashShift>();
}

public class CashShift : Common.Entity
{
    public Guid CashRegisterId { get; set; }
    public CashRegister CashRegister { get; set; } = null!;

    public Guid BranchId { get; set; }
    public string Number { get; set; } = string.Empty;
    public CashShiftStatus Status { get; set; } = CashShiftStatus.Open;

    public Guid OpenedByEmployeeId { get; set; }
    public Guid? ClosedByEmployeeId { get; set; }

    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public decimal OpeningCash { get; set; }
    public decimal? ClosingCashActual { get; set; }
    public decimal? ClosingCashExpected { get; set; }
    public decimal? Discrepancy { get; set; }
    public string? DiscrepancyReason { get; set; }
    public string? OpenComment { get; set; }
    public string? CloseComment { get; set; }

    public decimal SalesCash { get; set; }
    public decimal SalesCard { get; set; }
    public decimal SalesKaspi { get; set; }
    public decimal SalesTransfer { get; set; }
    public decimal SalesOther { get; set; }
    public decimal RefundsCash { get; set; }
    public decimal RefundsCard { get; set; }
    public decimal RefundsKaspi { get; set; }
    public decimal RefundsTransfer { get; set; }
    public decimal RefundsOther { get; set; }
    /// <summary>Собрано как обязательства: пополнения баланса + предоплаты броней.</summary>
    public decimal DepositsTotal { get; set; }
    public decimal CashInTotal { get; set; }
    public decimal CashOutTotal { get; set; }
    public decimal ExpenseTotal { get; set; }

    public ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();
    public ICollection<CashMovement> Movements { get; set; } = new List<CashMovement>();
}

public class CashMovement : Common.Entity
{
    public Guid CashShiftId { get; set; }
    public CashShift CashShift { get; set; } = null!;

    public CashMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public Guid EmployeeId { get; set; }
    public string? Category { get; set; }
    public string? Comment { get; set; }
}

public class Receipt : Common.Entity
{
    public Guid BranchId { get; set; }
    public Guid CashShiftId { get; set; }
    public CashShift CashShift { get; set; } = null!;

    public string Number { get; set; } = string.Empty;
    public ReceiptStatus Status { get; set; } = ReceiptStatus.Draft;

    public Guid? CustomerId { get; set; }
    public Guid? ComputerId { get; set; }
    public Guid? GamingSessionId { get; set; }
    public Guid CreatedByEmployeeId { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Total { get; set; }
    public decimal PaidTotal { get; set; }
    public string? Comment { get; set; }
    public string? IdempotencyKey { get; set; }

    public ICollection<ReceiptItem> Items { get; set; } = new List<ReceiptItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class ReceiptItem : Common.Entity
{
    public Guid ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;

    public ReceiptItemType ItemType { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
    public Guid? ReferenceId { get; set; }
}

public class Payment : Common.Entity
{
    public Guid ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;

    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? ExternalReference { get; set; }
}

public class DocumentSequence : Common.Entity
{
    public Guid BranchId { get; set; }
    public DocumentSequenceType Type { get; set; }
    public int Year { get; set; }
    public int LastValue { get; set; }
}
