using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class GamingSession : Common.Entity
{
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;

    public Guid ZoneId { get; set; }
    public Zone Zone { get; set; } = null!;

    public Guid TariffId { get; set; }
    public Tariff Tariff { get; set; } = null!;

    /// <summary>Снимок режима тарифа на момент покупки (чтобы смена настроек не ломала сеанс).</summary>
    public TariffDurationMode DurationMode { get; set; } = TariffDurationMode.FixedDuration;

    /// <summary>Для TimeWindow: начало/конец тарифного периода (UTC), зафиксированные при старте.</summary>
    public DateTimeOffset? WindowPeriodStartsAt { get; set; }
    public DateTimeOffset? WindowPeriodEndsAt { get; set; }

    public Guid? CustomerId { get; set; }

    public string? GuestName { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Draft;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? PlannedEndsAt { get; set; }
    public DateTimeOffset? ActualEndedAt { get; set; }

    public int DurationMinutes { get; set; }
    public decimal BasePrice { get; set; }
    public decimal DiscountAmount { get; set; }
    /// <summary>Сколько предоплаты брони реально списано в эту сессию (не путать с общей DiscountAmount).</summary>
    public decimal PrepaidAppliedAmount { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DebtAmount { get; set; }

    public Guid StartedByEmployeeId { get; set; }
    public Guid? EndedByEmployeeId { get; set; }
    public string? CancelReason { get; set; }
    public string? IdempotencyKey { get; set; }

    /// <summary>Бесплатный «своим» через /free — не в кассе и не в отчётах.</summary>
    public bool IsComplimentary { get; set; }

    public bool Warning15Sent { get; set; }
    public bool Warning10Sent { get; set; }
    public bool Warning5Sent { get; set; }
    public bool Warning1Sent { get; set; }

    /// <summary>Пауза: момент постановки на паузу и сохранённый остаток (сек).</summary>
    public DateTimeOffset? PausedAt { get; set; }
    public int? RemainingSecondsAtPause { get; set; }
    public int TotalPausedSeconds { get; set; }

    public Guid? RowVersion { get; set; } = Guid.NewGuid();

    public ICollection<SessionHistoryEntry> History { get; set; } = new List<SessionHistoryEntry>();
}
