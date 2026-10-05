using ShiftClub.Shared.Contracts.Bookings;
using ShiftClub.Shared.Contracts.Sessions;

namespace ShiftClub.Application.Abstractions;

public interface IBookingService
{
    Task<IReadOnlyList<BookingDto>> GetForDayAsync(
        DateOnly date,
        Guid? zoneId,
        Guid? computerId,
        CancellationToken cancellationToken = default);

    Task<BookingDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailableComputerDto>> GetAvailableComputersAsync(
        DateTimeOffset startsAt,
        int durationMinutes,
        Guid? zoneId,
        Guid? branchId,
        CancellationToken cancellationToken = default,
        Guid? excludeBookingId = null);

    Task<BookingDto> CreateAsync(
        CreateBookingRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<BookingDto> ConfirmAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default);

    Task<BookingDto> MarkArrivedAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default);

    Task<BookingDto> CancelAsync(
        Guid id,
        CancelBookingRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<BookingDto> UpdateAsync(
        Guid id,
        UpdateBookingRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    /// <summary>Полное удаление брони из БД (не Active). Закрытые тоже можно удалить.</summary>
    Task DeleteAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Старт сеансов по брони на выбранных (или всех) ПК.</summary>
    Task<IReadOnlyList<SessionDto>> StartSessionsAsync(
        Guid bookingId,
        StartBookingSessionsRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task ProcessNoShowsAsync(CancellationToken cancellationToken = default);

    /// <summary>За N минут до начала Confirmed/Pending держит ПК как Reserved.</summary>
    Task ProcessSoftHoldsAsync(CancellationToken cancellationToken = default);
}
