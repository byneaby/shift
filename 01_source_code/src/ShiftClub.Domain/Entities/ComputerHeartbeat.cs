namespace ShiftClub.Domain.Entities;

public class ComputerHeartbeat : Common.Entity
{
    public Guid ComputerId { get; set; }
    public Computer Computer { get; set; } = null!;

    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public double? CpuLoadPercent { get; set; }
    public double? RamUsedPercent { get; set; }
    public long? FreeDiskMb { get; set; }
    public long? UptimeSeconds { get; set; }
    public string? IpAddress { get; set; }
    public string? ClientVersion { get; set; }
    public string? StatusNote { get; set; }
    public bool ShellRunning { get; set; }
}
