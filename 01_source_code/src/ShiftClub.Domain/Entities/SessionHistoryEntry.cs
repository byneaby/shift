using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class SessionHistoryEntry : Common.Entity
{
    public Guid SessionId { get; set; }
    public GamingSession Session { get; set; } = null!;

    public SessionHistoryAction Action { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? DetailsJson { get; set; }
    public decimal? PriceBefore { get; set; }
    public decimal? PriceAfter { get; set; }
    public DateTimeOffset? PlannedEndsAtBefore { get; set; }
    public DateTimeOffset? PlannedEndsAtAfter { get; set; }
}
