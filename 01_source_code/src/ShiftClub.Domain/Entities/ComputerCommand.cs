using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class ComputerCommand : Common.Entity
{
    public Guid ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;

    public ComputerCommandType Type { get; set; }
    public ComputerCommandStatus Status { get; set; } = ComputerCommandStatus.Created;
    public Guid? InitiatedByEmployeeId { get; set; }
    public string? PayloadJson { get; set; }
    public string? IdempotencyKey { get; set; }

    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset? ExecutedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
}
