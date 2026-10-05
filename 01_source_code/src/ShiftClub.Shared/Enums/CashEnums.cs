namespace ShiftClub.Shared.Enums;

public enum CashShiftStatus
{
    Open = 0,
    Closing = 1,
    Closed = 2,
    ForceClosed = 3
}

public enum CashMovementType
{
    CashIn = 0,
    CashOut = 1,
    Expense = 2,
    ServiceIssue = 3
}

public enum ReceiptStatus
{
    Draft = 0,
    Paid = 1,
    Cancelled = 2,
    Refunded = 3,
    PartiallyRefunded = 4
}

public enum ReceiptItemType
{
    GamingTime = 0,
    Package = 1,
    Product = 2,
    BalanceTopUp = 3,
    Other = 4,
    BookingDeposit = 5,
    CaseKey = 6
}

public enum DocumentSequenceType
{
    Receipt = 0,
    CashShift = 1,
    Refund = 2,
    BarOrder = 3,
    Booking = 4
}
