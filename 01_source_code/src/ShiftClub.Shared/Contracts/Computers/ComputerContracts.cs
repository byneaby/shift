using ShiftClub.Shared.Enums;

namespace ShiftClub.Shared.Contracts.Computers;

public sealed record RegisterComputerRequest(
    string InstallationId,
    string WindowsName,
    string? IpAddress,
    string? MacAddress,
    string? ClientVersion,
    string? WindowsVersion,
    string? CpuName,
    string? GpuName,
    int? RamMb,
    string? ScreenResolution);

/// <summary>
/// Ответ регистрации по MAC (как CCboot): если ПК уже approved — сразу DeviceToken, иначе ждём кассу.
/// </summary>
public sealed record RegisterComputerResponse(
    Guid ComputerId,
    bool IsApproved,
    string? DeviceToken,
    string? DisplayName,
    string? MacAddress);

[Obsolete("Регистрация по коду снята — используйте Register с MAC.")]
public sealed record ActivateComputerRequest(string InstallationId, string RegistrationCode);

public sealed record ApproveComputerRequest(
    string DisplayName,
    Guid ZoneId,
    double? MapX,
    double? MapY);

public sealed record ApproveComputerResponse(
    Guid ComputerId,
    string DisplayName,
    string DeviceToken);

public sealed record ComputerHeartbeatRequest(
    double? CpuLoadPercent,
    double? RamUsedPercent,
    long? FreeDiskMb,
    long? UptimeSeconds,
    string? IpAddress,
    string? ClientVersion,
    bool ShellRunning,
    string? StatusNote);

public sealed record SendComputerCommandRequest(
    ComputerCommandType Type,
    string? PayloadJson,
    string? IdempotencyKey);

public sealed record UpdateComputerLayoutRequest(
    double? MapX,
    double? MapY,
    int? GridCol = null,
    int? GridRow = null,
    int? GridColSpan = null,
    int? GridRowSpan = null);

public sealed record UpdateComputerRequest(
    string? DisplayName,
    Guid? ZoneId,
    bool? IsMaintenance,
    string? Notes);

/// <summary>Ручная станция без Shell (PS5 и т.п.) — только учёт сеанса с кассы.</summary>
public sealed record CreateManualStationRequest(
    string DisplayName,
    Guid ZoneId,
    Guid? BranchId = null,
    int? GridCol = null,
    int? GridRow = null,
    StationKind StationKind = StationKind.Console,
    string? Notes = null);

public sealed record ComputerDto(
    Guid Id,
    Guid BranchId,
    Guid? ZoneId,
    string? ZoneName,
    string? ZoneColorHex,
    string? DisplayName,
    string WindowsName,
    string? IpAddress,
    string? MacAddress,
    double? MapX,
    double? MapY,
    ComputerStatus Status,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? LastHeartbeatAt,
    bool IsApproved,
    string? RegistrationCode,
    string? ClientVersion,
    bool IsMaintenance,
    string? Notes,
    Guid? CurrentSessionId = null,
    int? RemainingSeconds = null,
    decimal? SessionTotalPrice = null,
    string? SessionGuestName = null,
    SessionStatus? SessionStatus = null,
    PaymentMethod? SessionPaymentMethod = null,
    Guid? SessionCustomerId = null,
    /// <summary>Гостевой статус: Free / Busy / Reserved / Offline / Maintenance / Updating / Setup.</summary>
    string Occupancy = "Free",
    /// <summary>Уточнение занятости: Аккаунт, Чек, Пауза…</summary>
    string? OccupancyDetail = null,
    int? GridCol = null,
    int? GridRow = null,
    string? ZoneKind = null,
    int? ZoneGridColumns = null,
    int? ZoneGridRows = null,
    StationKind StationKind = StationKind.Pc,
    int GridColSpan = 1,
    int GridRowSpan = 1);

public sealed record ComputerCommandDto(
    Guid Id,
    Guid ComputerId,
    ComputerCommandType Type,
    ComputerCommandStatus Status,
    string? PayloadJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? ResultJson = null,
    string? ErrorMessage = null);

public sealed record ComputerStatusChangedEvent(
    Guid ComputerId,
    ComputerStatus Status,
    DateTimeOffset? LastSeenAt,
    string? DisplayName);

/// <summary>Снимок процессов + телеметрия ПК (только по запросу GetDiagnostics).</summary>
public sealed record ComputerProcessSnapshotDto(
    DateTimeOffset CapturedAt,
    IReadOnlyList<ComputerProcessDto> Processes,
    double? CpuLoadPercent = null,
    double? RamUsedPercent = null,
    long? RamUsedMb = null,
    long? RamTotalMb = null,
    long? FreeDiskMb = null,
    double? CpuTempC = null,
    double? GpuTempC = null,
    string? GpuName = null,
    double? GpuLoadPercent = null,
    string? ForegroundProcess = null,
    string? ForegroundTitle = null);

public sealed record ComputerProcessDto(
    int Pid,
    string Name,
    long MemoryMb,
    string? WindowTitle,
    bool CanKill);
