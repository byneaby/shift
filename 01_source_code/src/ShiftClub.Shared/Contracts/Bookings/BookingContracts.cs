using ShiftClub.Shared.Enums;

namespace ShiftClub.Shared.Contracts.Bookings;

public sealed record BookingComputerDto(
    Guid ComputerId,
    string? ComputerName,
    Guid? ZoneId,
    string? ZoneName,
    Guid? GamingSessionId);

public sealed record BookingDto(
    Guid Id,
    string Number,
    Guid BranchId,
    Guid? ZoneId,
    string? ZoneName,
    Guid? CustomerId,
    string? CustomerName,
    string ContactName,
    string ContactPhone,
    string? Comment,
    BookingStatus Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int DurationMinutes,
    decimal PrepaidAmount,
    decimal TotalEstimated,
    PaymentMethod? PrepayMethod,
    int GraceMinutes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<BookingComputerDto> Computers);

public sealed record CreateBookingRequest(
    Guid? ZoneId,
    Guid? CustomerId,
    string ContactName,
    string ContactPhone,
    string? Comment,
    /// <summary>UTC (или с offset). Если заданы LocalDate+LocalTime — они имеют приоритет.</summary>
    DateTimeOffset? StartsAt,
    /// <summary>Локальная дата филиала (YYYY-MM-DD).</summary>
    DateOnly? LocalDate,
    /// <summary>Локальное время филиала (HH:mm).</summary>
    string? LocalTime,
    int DurationMinutes,
    IReadOnlyList<Guid> ComputerIds,
    decimal PrepaidAmount,
    PaymentMethod? PrepayMethod,
    int? GraceMinutes,
    string? IdempotencyKey);

public sealed record CancelBookingRequest(string? Reason, bool RefundPrepay = true);

public sealed record UpdateBookingRequest(
    Guid? ZoneId,
    Guid? CustomerId,
    string ContactName,
    string ContactPhone,
    string? Comment,
    DateTimeOffset? StartsAt,
    DateOnly? LocalDate,
    string? LocalTime,
    int DurationMinutes,
    IReadOnlyList<Guid> ComputerIds,
    int? GraceMinutes);

public sealed record BookingDayQuery(
    DateOnly Date,
    Guid? ZoneId,
    Guid? ComputerId);

/// <summary>ПК, свободные под слот брони (нет конфликтующих броней и активных сеансов).</summary>
public sealed record AvailableComputerDto(
    Guid Id,
    string DisplayName,
    Guid? ZoneId,
    string? ZoneName,
    ComputerStatus Status,
    bool IsOnline);

public sealed record BookingAvailabilityQuery(
    DateTimeOffset StartsAt,
    int DurationMinutes,
    Guid? ZoneId,
    Guid? BranchId);

/// <summary>Запуск игровых сеансов по прибывшей/подтверждённой брони.</summary>
public sealed record StartBookingSessionsRequest(
    Guid TariffId,
    int DurationMinutes,
    PaymentMethod PaymentMethod,
    /// <summary>Подмножество ПК брони; null/пусто — все ещё без сеанса.</summary>
    IReadOnlyList<Guid>? ComputerIds,
    string? IdempotencyKey,
    bool UseTimeBank = false);
