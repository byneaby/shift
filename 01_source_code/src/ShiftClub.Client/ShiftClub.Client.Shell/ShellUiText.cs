using ShiftClub.Shared.Enums;

namespace ShiftClub.Client.Shell;

/// <summary>Гостевые подписи UI — всё на русском, без сырых enum/English.</summary>
internal static class ShellUiText
{
    public static string Payment(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Наличные",
        PaymentMethod.Card => "Карта",
        PaymentMethod.Balance => "Баланс",
        PaymentMethod.Free => "Бесплатно",
        PaymentMethod.Postpay => "Постоплата",
        PaymentMethod.KaspiQr => "Kaspi QR",
        PaymentMethod.Mixed => "Смешанная",
        PaymentMethod.Transfer => "Перевод",
        _ => "Оплата"
    };

    public static string Payment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Оплата";
        return Enum.TryParse<PaymentMethod>(raw, ignoreCase: true, out var m)
            ? Payment(m)
            : raw switch
            {
                "Cash" => "Наличные",
                "Card" => "Карта",
                "Balance" => "Баланс",
                "Free" => "Бесплатно",
                "Postpay" => "Постоплата",
                "KaspiQr" => "Kaspi QR",
                "Mixed" => "Смешанная",
                "Transfer" => "Перевод",
                "CashOnDelivery" => "Наличные",
                "KaspiQrOnDelivery" => "Kaspi QR",
                "CardOnDelivery" => "Карта",
                "PayAtCashier" => "У кассы",
                "ChargeToSession" => "К сеансу",
                _ => "Оплата"
            };
    }

    public static string BarOrderStatus(string? status) => status?.Trim() switch
    {
        "New" => "Новый",
        "Accepted" => "Принят",
        "Preparing" => "Готовится",
        "Ready" => "Готов",
        "Delivering" => "Доставляется",
        "Completed" or "Done" => "Выполнен",
        "Cancelled" or "Canceled" => "Отменён",
        "Rejected" => "Отклонён",
        null or "" => "Оформлен",
        _ => status!
    };

    public static string NewsCategory(string? category) => category?.Trim().ToLowerInvariant() switch
    {
        "promo" => "Акция",
        "event" => "Событие",
        "maintenance" => "Техработы",
        "info" => "Инфо",
        null or "" => "Новость",
        _ => category!
    };

    public static string GuestError(string? technical)
    {
        if (string.IsNullOrWhiteSpace(technical))
            return "";
        var t = technical.Trim();
        if (t.StartsWith("SignalR", StringComparison.OrdinalIgnoreCase)
            || t.Contains("переподключен", StringComparison.OrdinalIgnoreCase)
            || t.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || t.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "Нет связи с сервером";
        if (t.StartsWith("HTTP", StringComparison.OrdinalIgnoreCase)
            || t.Contains("JSON", StringComparison.OrdinalIgnoreCase))
            return "Ошибка данных";
        // Keep short Russian messages as-is; strip noisy prefixes
        if (t.StartsWith("commands ", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("session ", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Команды:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Сеанс:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Каталог программ:", StringComparison.OrdinalIgnoreCase))
            return "Ошибка данных";
        return t.Length > 80 ? t[..77] + "…" : t;
    }

    public static string LedgerType(LedgerTransactionType type) => type switch
    {
        LedgerTransactionType.Deposit => "Пополнение",
        LedgerTransactionType.SessionCharge => "Игровое время",
        LedgerTransactionType.ProductPurchase => "Покупка в баре",
        LedgerTransactionType.Refund => "Возврат",
        LedgerTransactionType.BonusCredit => "Начисление бонусов",
        LedgerTransactionType.BonusDebit => "Списание бонусов",
        LedgerTransactionType.ManualAdjustment => "Корректировка",
        LedgerTransactionType.DebtPayment => "Оплата долга",
        LedgerTransactionType.Transfer => "Перевод",
        _ => "Операция"
    };

    public static string TimeBankReasonLabel(TimeBankReason reason) => reason switch
    {
        TimeBankReason.SessionSaved => "Сохранено с сеанса",
        TimeBankReason.SessionSpent => "Списано на сеанс",
        TimeBankReason.ManualCredit => "Начислено вручную",
        TimeBankReason.ManualDebit => "Списано вручную",
        _ => "Операция с временем"
    };

    public static string SessionStatus(string? status) => status?.Trim() switch
    {
        "Active" => "Активен",
        "Paused" => "Пауза",
        "Completed" => "Завершён",
        "Interrupted" => "Прерван",
        "PaymentPending" => "Ожидает оплату",
        "Waiting" => "Ожидание",
        "Reserved" => "Бронь",
        _ => status ?? "Сеанс"
    };

    public static string BookingStatus(string? status) => status?.Trim() switch
    {
        "Pending" => "Ожидает",
        "Confirmed" => "Подтверждена",
        "Arrived" => "Гость на месте",
        "Active" => "Идёт",
        "Completed" => "Завершена",
        "Cancelled" or "Canceled" => "Отменена",
        "NoShow" => "Неявка",
        _ => status ?? "Бронь"
    };
}
