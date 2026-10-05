using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Time;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Bookings;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookings;
    private readonly ShiftClubDbContext _db;

    public BookingsController(IBookingService bookings, ShiftClubDbContext db)
    {
        _bookings = bookings;
        _db = db;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.BookingsView, PermissionCodes.BookingsManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BookingDto>>>> ForDay(
        [FromQuery] DateOnly? date,
        [FromQuery] Guid? zoneId,
        [FromQuery] Guid? computerId,
        CancellationToken cancellationToken)
    {
        var tzId = await _db.Branches.AsNoTracking().Select(b => b.TimeZoneId).FirstOrDefaultAsync(cancellationToken);
        var day = date ?? BranchTimeZone.TodayLocal(tzId);
        var list = await _bookings.GetForDayAsync(day, zoneId, computerId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BookingDto>>.Ok(list));
    }

    [HttpGet("availability")]
    [RequirePermission(PermissionCodes.BookingsView, PermissionCodes.BookingsManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AvailableComputerDto>>>> Availability(
        [FromQuery] DateTimeOffset? startsAt,
        [FromQuery] DateOnly? localDate,
        [FromQuery] string? localTime,
        [FromQuery] int durationMinutes,
        [FromQuery] Guid? zoneId,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? excludeBookingId,
        CancellationToken cancellationToken)
    {
        try
        {
            DateTimeOffset start;
            if (localDate is DateOnly d && !string.IsNullOrWhiteSpace(localTime))
            {
                if (!TimeOnly.TryParse(localTime, out var t))
                    return BadRequest(ApiResponse<IReadOnlyList<AvailableComputerDto>>.Fail(
                        CommonErrorCodes.ValidationFailed, "Некорректное localTime."));
                var tzId = await _db.Branches.AsNoTracking().Select(b => b.TimeZoneId).FirstOrDefaultAsync(cancellationToken);
                start = BranchTimeZone.ToUtc(d, t, tzId);
            }
            else if (startsAt is DateTimeOffset s)
            {
                start = s;
            }
            else
            {
                return BadRequest(ApiResponse<IReadOnlyList<AvailableComputerDto>>.Fail(
                    CommonErrorCodes.ValidationFailed, "Укажите startsAt или localDate+localTime."));
            }

            var list = await _bookings.GetAvailableComputersAsync(
                start, durationMinutes, zoneId, branchId, cancellationToken, excludeBookingId);
            return Ok(ApiResponse<IReadOnlyList<AvailableComputerDto>>.Ok(list));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<IReadOnlyList<AvailableComputerDto>>.Fail(
                CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.BookingsView, PermissionCodes.BookingsManage)]
    public async Task<ActionResult<ApiResponse<BookingDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var booking = await _bookings.GetByIdAsync(id, cancellationToken);
        if (booking is null)
            return NotFound(ApiResponse<BookingDto>.Fail(CommonErrorCodes.NotFound, "Бронь не найдена."));
        return Ok(ApiResponse<BookingDto>.Ok(booking));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.BookingsManage)]
    public async Task<ActionResult<ApiResponse<BookingDto>>> Create(
        [FromBody] CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var booking = await _bookings.CreateAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<BookingDto>.Ok(booking));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<BookingDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<BookingDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/confirm")]
    [RequirePermission(PermissionCodes.BookingsManage)]
    public async Task<ActionResult<ApiResponse<BookingDto>>> Confirm(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var booking = await _bookings.ConfirmAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<BookingDto>.Ok(booking));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<BookingDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<BookingDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/arrived")]
    [RequirePermission(PermissionCodes.BookingsManage)]
    public async Task<ActionResult<ApiResponse<BookingDto>>> Arrived(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var booking = await _bookings.MarkArrivedAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<BookingDto>.Ok(booking));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<BookingDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<BookingDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/start")]
    [RequirePermission(PermissionCodes.BookingsManage, PermissionCodes.SessionsStart)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SessionDto>>>> Start(
        Guid id,
        [FromBody] StartBookingSessionsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var sessions = await _bookings.StartSessionsAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<IReadOnlyList<SessionDto>>.Ok(sessions));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<IReadOnlyList<SessionDto>>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<IReadOnlyList<SessionDto>>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.BookingsCancel, PermissionCodes.BookingsManage)]
    public async Task<ActionResult<ApiResponse<BookingDto>>> Cancel(
        Guid id,
        [FromBody] CancelBookingRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var booking = await _bookings.CancelAsync(id, request ?? new CancelBookingRequest(null), GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<BookingDto>.Ok(booking));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<BookingDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<BookingDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.BookingsManage)]
    public async Task<ActionResult<ApiResponse<BookingDto>>> Update(
        Guid id,
        [FromBody] UpdateBookingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var booking = await _bookings.UpdateAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<BookingDto>.Ok(booking));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<BookingDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<BookingDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.BookingsCancel, PermissionCodes.BookingsManage)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _bookings.DeleteAsync(id, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { id, deleted = true }));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}
