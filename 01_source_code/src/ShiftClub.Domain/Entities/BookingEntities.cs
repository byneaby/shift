using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class Booking : Common.Entity
{
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid? ZoneId { get; set; }
    public Zone? Zone { get; set; }

    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string ContactName { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string? Comment { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public int DurationMinutes { get; set; }

    public decimal PrepaidAmount { get; set; }
    public decimal TotalEstimated { get; set; }
    public PaymentMethod? PrepayMethod { get; set; }
    public Guid? PrepayReceiptId { get; set; }

    /// <summary>Допустимое опоздание в минутах до авто-NoShow.</summary>
    public int GraceMinutes { get; set; } = 15;

    public Guid CreatedByEmployeeId { get; set; }
    public Guid? CancelledByEmployeeId { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? ArrivedAt { get; set; }
    public DateTimeOffset? NoShowAt { get; set; }

    public string? IdempotencyKey { get; set; }
    public string Number { get; set; } = string.Empty;

    public ICollection<BookingComputer> Computers { get; set; } = new List<BookingComputer>();
}

public class BookingComputer : Common.Entity
{
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;

    public Guid ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;

    public Guid? GamingSessionId { get; set; }
}
