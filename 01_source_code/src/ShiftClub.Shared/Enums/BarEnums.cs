namespace ShiftClub.Shared.Enums;

public enum InventoryMovementType
{
    Purchase = 0,
    Sale = 1,
    Adjustment = 2,
    WriteOff = 3,
    TransferIn = 4,
    TransferOut = 5,
    OrderFulfillment = 6,
    SaleReturn = 7
}

public enum BarOrderStatus
{
    New = 0,
    Accepted = 1,
    Preparing = 2,
    Ready = 3,
    Delivering = 4,
    Completed = 5,
    Cancelled = 6,
    Rejected = 7
}

public enum BarOrderPaymentMode
{
    CashOnDelivery = 0,
    CardOnDelivery = 1,
    PayAtCashier = 2,
    ChargeToSession = 3,
    Balance = 4,
    KaspiQrOnDelivery = 5
}
