namespace ShiftClub.Shared.Enums;

public enum LedgerDirection
{
    Credit = 0,
    Debit = 1
}

public enum LedgerTransactionType
{
    Deposit = 0,
    SessionCharge = 1,
    ProductPurchase = 2,
    Refund = 3,
    BonusCredit = 4,
    BonusDebit = 5,
    ManualAdjustment = 6,
    DebtPayment = 7,
    Transfer = 8
}

public enum LedgerTransactionStatus
{
    Posted = 0,
    Voided = 1
}

public enum TimeBankReason
{
    SessionSaved = 0,
    SessionSpent = 1,
    ManualCredit = 2,
    ManualDebit = 3
}
