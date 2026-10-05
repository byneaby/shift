using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class Computer : Common.Entity
{
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid? ZoneId { get; set; }
    public Zone? Zone { get; set; }

    public string? DisplayName { get; set; }
    public string WindowsName { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? MacAddress { get; set; }

    public double? MapX { get; set; }
    public double? MapY { get; set; }

    /// <summary>Slot column on zone/floor grid (0-based).</summary>
    public int? GridCol { get; set; }

    /// <summary>Slot row on zone/floor grid (0-based).</summary>
    public int? GridRow { get; set; }

    /// <summary>How many grid columns this station occupies (min 1).</summary>
    public int GridColSpan { get; set; } = 1;

    /// <summary>How many grid rows this station occupies (min 1).</summary>
    public int GridRowSpan { get; set; } = 1;

    public string? ClientVersion { get; set; }
    public string? WindowsVersion { get; set; }
    public string? CpuName { get; set; }
    public string? GpuName { get; set; }
    public int? RamMb { get; set; }
    public string? ScreenResolution { get; set; }

    public ComputerStatus Status { get; set; } = ComputerStatus.PendingApproval;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public bool IsApproved { get; set; }
    public string? RegistrationCode { get; set; }
    public DateTimeOffset? RegistrationCodeExpiresAt { get; set; }
    public string? DeviceTokenHash { get; set; }

    public bool IsMaintenance { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }
    public DateTimeOffset? LastDiagnosticsAt { get; set; }

    /// <summary>Pc — Shell/агент; Console — PS5 и т.п., только ручной учёт времени.</summary>
    public StationKind StationKind { get; set; } = StationKind.Pc;

    /// <summary>Soft-delete: скрыт из зала/списка, история сеансов сохраняется.</summary>
    public bool IsDeleted { get; set; }

    public Guid? CurrentSessionId { get; set; }
    public GamingSession? CurrentSession { get; set; }

    public Guid? RowVersion { get; set; } = Guid.NewGuid();

    public ICollection<ComputerHeartbeat> Heartbeats { get; set; } = new List<ComputerHeartbeat>();
    public ICollection<ComputerCommand> Commands { get; set; } = new List<ComputerCommand>();
}
