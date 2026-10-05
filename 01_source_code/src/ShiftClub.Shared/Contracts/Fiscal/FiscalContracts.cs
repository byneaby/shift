namespace ShiftClub.Shared.Contracts.Fiscal;

/// <summary>Строка чека в том виде, в котором её ждёт фискальный регистратор.</summary>
public sealed record FiscalLine(
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total,
    string? TaxCode);

public sealed record FiscalPaymentPart(string Method, decimal Amount);

/// <summary>Продажа или возврат, который нужно провести через фискальный регистратор.</summary>
public sealed record FiscalReceiptRequest(
    string Kind,
    Guid ReceiptId,
    string ReceiptNumber,
    DateTimeOffset At,
    decimal Total,
    string CurrencyCode,
    IReadOnlyList<FiscalLine> Lines,
    IReadOnlyList<FiscalPaymentPart> Payments,
    string? CashierName,
    string? CustomerPhone)
{
    public const string Sale = "sale";
    public const string Refund = "refund";
}

public sealed record FiscalReceiptResult(
    bool Ok,
    bool Skipped,
    string? FiscalNumber,
    string? DocumentUrl,
    string? Error)
{
    public static FiscalReceiptResult NotConfigured() =>
        new(true, true, null, null, null);

    public static FiscalReceiptResult Failed(string error) =>
        new(false, false, null, null, error);

    public static FiscalReceiptResult Registered(string fiscalNumber, string? documentUrl = null) =>
        new(true, false, fiscalNumber, documentUrl, null);
}

public sealed record FiscalStatusDto(
    bool Enabled,
    string Provider,
    bool RequireSuccess,
    string Summary,
    string? Warning);
