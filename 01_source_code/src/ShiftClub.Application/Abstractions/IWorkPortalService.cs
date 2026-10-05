using ShiftClub.Shared.Contracts.Employees;

namespace ShiftClub.Application.Abstractions;

public interface IWorkPortalService
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<WorkPortalMeDto> GetMeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<WorkBoardDto> GetBoardAsync(DateOnly? date, Guid employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkBoardShiftDto>> GetMyShiftsAsync(
        DateOnly from,
        DateOnly to,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task<WorkShiftDto> ClockInAsync(Guid shiftId, ClockActionRequest request, Guid actorId, bool canManage, CancellationToken cancellationToken = default);
    Task<WorkShiftDto> ClockOutAsync(Guid shiftId, ClockActionRequest request, Guid actorId, bool canManage, CancellationToken cancellationToken = default);
    Task<WorkShiftDto> StartBreakAsync(Guid shiftId, Guid actorId, bool canManage, CancellationToken cancellationToken = default);
    Task<WorkShiftDto> EndBreakAsync(Guid shiftId, Guid actorId, bool canManage, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkShiftNoteDto>> GetNotesAsync(Guid shiftId, Guid actorId, bool canManage, CancellationToken cancellationToken = default);
    Task<WorkShiftNoteDto> AddNoteAsync(Guid shiftId, CreateWorkShiftNoteRequest request, Guid actorId, bool canManage, CancellationToken cancellationToken = default);
}
