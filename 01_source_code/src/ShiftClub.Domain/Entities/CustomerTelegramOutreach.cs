namespace ShiftClub.Domain.Entities;

/// <summary>Лог автосообщений Telegram CRM (win-back / ключ / промо).</summary>
public class CustomerTelegramOutreach : Common.Entity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>winback | unused_key | promo | promo:{campaignId}</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }

    public string? Preview { get; set; }
}
