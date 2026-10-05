namespace ShiftClub.Shared.Enums;

public enum TariffKind
{
    Hourly = 0,
    Package = 1,
    Postpay = 2,
    Free = 3
}

/// <summary>
/// Как считается конец сеанса: фиксированная длительность от покупки
/// или до конца заданного суточного окна (день/ночь).
/// </summary>
public enum TariffDurationMode
{
    FixedDuration = 0,
    TimeWindow = 1
}

public enum BillingMode
{
    PerMinute = 0,
    Block15 = 1,
    Block30 = 2
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    Mixed = 2,
    Balance = 3,
    Free = 4,
    Postpay = 5,
    KaspiQr = 6,
    Transfer = 7
}

public enum SessionStatus
{
    Draft = 0,
    Reserved = 1,
    Waiting = 2,
    Active = 3,
    Paused = 4,
    Completed = 5,
    Cancelled = 6,
    Interrupted = 7,
    PaymentPending = 8
}

public enum SessionHistoryAction
{
    Started = 0,
    Extended = 1,
    Reduced = 2,
    Moved = 3,
    Paused = 4,
    Resumed = 5,
    Ended = 6,
    Cancelled = 7,
    Warning15 = 8,
    Warning10 = 9,
    Warning5 = 10,
    Warning1 = 11,
    AutoEnded = 12,
    PriceRecalculated = 13,
    Recovered = 14
}
