using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Time;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Employees;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Infrastructure.Services;

public sealed class WorkPortalService : IWorkPortalService
{
    private readonly ShiftClubDbContext _db;
    private readonly IEmployeeAdminService _employees;
    private bool _schemaReady;

    public WorkPortalService(ShiftClubDbContext db, IEmployeeAdminService employees)
    {
        _db = db;
        _employees = employees;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (_schemaReady) return;

        await _db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE work_shifts ADD COLUMN IF NOT EXISTS "BreakStartedAt" timestamp with time zone NULL;
            """, cancellationToken);

        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS work_shift_notes (
                "Id" uuid NOT NULL,
                "WorkShiftId" uuid NOT NULL,
                "AuthorEmployeeId" uuid NOT NULL,
                "AuthorName" character varying(200) NOT NULL,
                "Kind" character varying(32) NOT NULL,
                "Text" character varying(2000) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "CreatedBy" uuid NULL,
                "UpdatedAt" timestamp with time zone NULL,
                "UpdatedBy" uuid NULL,
                CONSTRAINT "PK_work_shift_notes" PRIMARY KEY ("Id")
            );
            """, cancellationToken);

        await _db.Database.ExecuteSqlRawAsync("""
            DO $$
            BEGIN
              IF NOT EXISTS (
                SELECT 1 FROM pg_constraint WHERE conname = 'FK_work_shift_notes_work_shifts_WorkShiftId'
              ) THEN
                ALTER TABLE work_shift_notes
                  ADD CONSTRAINT "FK_work_shift_notes_work_shifts_WorkShiftId"
                  FOREIGN KEY ("WorkShiftId") REFERENCES work_shifts("Id") ON DELETE CASCADE;
              END IF;
            END $$;
            """, cancellationToken);

        await _db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_work_shift_notes_WorkShiftId" ON work_shift_notes ("WorkShiftId");
            CREATE INDEX IF NOT EXISTS "IX_work_shift_notes_CreatedAt" ON work_shift_notes ("CreatedAt");
            """, cancellationToken);

        _schemaReady = true;
    }

    public async Task<WorkPortalMeDto> GetMeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var employee = await _db.Employees.AsNoTracking()
            .Include(e => e.EmployeeRoles).ThenInclude(er => er.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Сотрудник не найден.");

        var permissions = employee.EmployeeRoles
            .SelectMany(er => er.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var canManage = permissions.Any(p =>
            string.Equals(p, PermissionCodes.SchedulesManage, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, PermissionCodes.EmployeesManage, StringComparison.OrdinalIgnoreCase));

        return new WorkPortalMeDto(employee.Id, employee.Login, employee.DisplayName, canManage, permissions);
    }

    public async Task<WorkBoardDto> GetBoardAsync(DateOnly? date, Guid employeeId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var tzId = await _db.Branches.AsNoTracking().Select(b => b.TimeZoneId).FirstOrDefaultAsync(cancellationToken)
                   ?? BranchTimeZone.DefaultId;
        var day = date ?? BranchTimeZone.TodayLocal(tzId);

        var shifts = await _db.WorkShifts.AsNoTracking()
            .Include(s => s.Employee)
            .Include(s => s.Notes)
            .Where(s => s.Status != WorkShiftStatus.Cancelled
                        && (s.WorkDate == day
                            || (s.WorkDate == day.AddDays(-1) && s.PlannedEnd < s.PlannedStart)))
            .OrderBy(s => s.WorkDate)
            .ThenBy(s => s.PlannedStart)
            .ThenBy(s => s.Employee.DisplayName)
            .ToListAsync(cancellationToken);

        var mapped = shifts.Select(s => MapBoard(s, employeeId)).ToList();
        return new WorkBoardDto(day, tzId, mapped.FirstOrDefault(s => s.IsMine), mapped);
    }

    public async Task<IReadOnlyList<WorkBoardShiftDto>> GetMyShiftsAsync(
        DateOnly from,
        DateOnly to,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var shifts = await _db.WorkShifts.AsNoTracking()
            .Include(s => s.Employee)
            .Include(s => s.Notes)
            .Where(s => s.EmployeeId == employeeId && s.WorkDate >= from && s.WorkDate <= to)
            .OrderBy(s => s.WorkDate)
            .ThenBy(s => s.PlannedStart)
            .ToListAsync(cancellationToken);

        return shifts.Select(s => MapBoard(s, employeeId)).ToList();
    }

    public Task<WorkShiftDto> ClockInAsync(Guid shiftId, ClockActionRequest request, Guid actorId, bool canManage, CancellationToken cancellationToken = default)
        => ActAsync(shiftId, actorId, canManage, () => _employees.ClockInAsync(shiftId, request, actorId, cancellationToken), cancellationToken);

    public Task<WorkShiftDto> ClockOutAsync(Guid shiftId, ClockActionRequest request, Guid actorId, bool canManage, CancellationToken cancellationToken = default)
        => ActAsync(shiftId, actorId, canManage, () => _employees.ClockOutAsync(shiftId, request, actorId, cancellationToken), cancellationToken);

    public Task<WorkShiftDto> StartBreakAsync(Guid shiftId, Guid actorId, bool canManage, CancellationToken cancellationToken = default)
        => ActAsync(shiftId, actorId, canManage, () => _employees.StartBreakAsync(shiftId, actorId, cancellationToken), cancellationToken);

    public Task<WorkShiftDto> EndBreakAsync(Guid shiftId, Guid actorId, bool canManage, CancellationToken cancellationToken = default)
        => ActAsync(shiftId, actorId, canManage, () => _employees.EndBreakAsync(shiftId, actorId, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<WorkShiftNoteDto>> GetNotesAsync(
        Guid shiftId,
        Guid actorId,
        bool canManage,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await EnsureAccessAsync(shiftId, actorId, canManage, cancellationToken);

        var notes = await _db.WorkShiftNotes.AsNoTracking()
            .Where(n => n.WorkShiftId == shiftId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
        return notes.Select(MapNote).ToList();
    }

    public async Task<WorkShiftNoteDto> AddNoteAsync(
        Guid shiftId,
        CreateWorkShiftNoteRequest request,
        Guid actorId,
        bool canManage,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await EnsureAccessAsync(shiftId, actorId, canManage, cancellationToken);

        var text = (request.Text ?? "").Trim();
        if (text.Length < 1)
            throw new InvalidOperationException("Текст заметки пуст.");
        if (text.Length > 2000)
            throw new InvalidOperationException("Заметка слишком длинная.");

        var kind = string.IsNullOrWhiteSpace(request.Kind) ? "General" : request.Kind.Trim();
        if (kind.Length > 32) kind = kind[..32];
        if (!canManage && string.Equals(kind, "Admin", StringComparison.OrdinalIgnoreCase))
            kind = "General";

        var author = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == actorId)
            .Select(e => e.DisplayName)
            .FirstOrDefaultAsync(cancellationToken) ?? "";

        var note = new WorkShiftNote
        {
            WorkShiftId = shiftId,
            AuthorEmployeeId = actorId,
            AuthorName = author,
            Kind = kind,
            Text = text,
            CreatedBy = actorId
        };
        _db.WorkShiftNotes.Add(note);
        await _db.SaveChangesAsync(cancellationToken);
        return MapNote(note);
    }

    private async Task<WorkShiftDto> ActAsync(
        Guid shiftId,
        Guid actorId,
        bool canManage,
        Func<Task<WorkShiftDto>> action,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await EnsureAccessAsync(shiftId, actorId, canManage, cancellationToken);
        return await action();
    }

    private async Task EnsureAccessAsync(Guid shiftId, Guid actorId, bool canManage, CancellationToken cancellationToken)
    {
        var shift = await _db.WorkShifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken)
            ?? throw new KeyNotFoundException("Смена не найдена.");

        if (!canManage && shift.EmployeeId != actorId)
            throw new UnauthorizedAccessException("Можно отмечать только свою смену.");
    }

    private static WorkBoardShiftDto MapBoard(WorkShift s, Guid actorId) => new(
        s.Id,
        s.EmployeeId,
        s.Employee?.DisplayName ?? "",
        s.WorkDate,
        s.PlannedStart,
        s.PlannedEnd,
        s.Status,
        s.ActualStartAt,
        s.ActualEndAt,
        s.BreakMinutes,
        WorkedHours(s),
        s.Comment,
        s.EmployeeId == actorId,
        (s.Notes ?? Array.Empty<WorkShiftNote>())
            .OrderByDescending(n => n.CreatedAt)
            .Select(MapNote)
            .ToList());

    private static WorkShiftNoteDto MapNote(WorkShiftNote n) => new(
        n.Id, n.WorkShiftId, n.AuthorEmployeeId, n.AuthorName, n.Kind, n.Text, n.CreatedAt);

    private static decimal WorkedHours(WorkShift s)
    {
        if (s.ActualStartAt is null || s.ActualEndAt is null)
            return 0;
        var minutes = (decimal)(s.ActualEndAt.Value - s.ActualStartAt.Value).TotalMinutes - s.BreakMinutes;
        return Math.Round(Math.Max(0, minutes) / 60m, 2, MidpointRounding.AwayFromZero);
    }
}
