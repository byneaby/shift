using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Time;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Employees;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Services;

public sealed class EmployeeAdminService : IEmployeeAdminService
{
    private readonly ShiftClubDbContext _db;
    private readonly IPasswordHasher _passwordHasher;

    public EmployeeAdminService(ShiftClubDbContext db, IPasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task<IReadOnlyList<EmployeeListItemDto>> ListEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var list = await _db.Employees.AsNoTracking()
            .Include(e => e.EmployeeRoles).ThenInclude(er => er.Role)
            .Where(e => !e.Login.StartsWith("__del__"))
            .OrderBy(e => e.DisplayName)
            .ToListAsync(cancellationToken);
        return list.Select(MapEmployee).ToList();
    }

    public async Task<EmployeeListItemDto> CreateEmployeeAsync(
        CreateEmployeeRequest request,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var login = request.Login.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("Логин и имя обязательны.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            throw new InvalidOperationException("Пароль должен быть не короче 6 символов.");

        if (await _db.Employees.AnyAsync(e => e.Login == login, cancellationToken))
            throw new InvalidOperationException("Логин уже занят.");

        var branchId = await _db.Branches.Select(b => b.Id).FirstAsync(cancellationToken);
        var roles = await ResolveRolesAsync(request.RoleCodes, cancellationToken);

        var display = request.DisplayName.Trim();
        var (first, last) = SplitDisplayName(display);
        var employee = new Employee
        {
            BranchId = branchId,
            Login = login,
            DisplayName = display,
            FirstName = first,
            LastName = last,
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim(),
            IsActive = true,
            PayType = request.PayType,
            HourlyRate = request.HourlyRate,
            MonthlySalary = request.MonthlySalary,
            ShiftRate = request.ShiftRate,
            Credential = new EmployeeCredential
            {
                PasswordHash = _passwordHasher.Hash(request.Password),
                PasswordChangedAt = DateTimeOffset.UtcNow
            }
        };

        foreach (var role in roles)
            employee.EmployeeRoles.Add(new EmployeeRole { RoleId = role.Id });

        _db.Employees.Add(employee);
        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = branchId,
            EmployeeId = actorId,
            Action = "employee.create",
            EntityType = nameof(Employee),
            EntityId = employee.Id.ToString(),
            DetailsJson = $"{{\"login\":\"{login}\"}}"
        });
        await _db.SaveChangesAsync(cancellationToken);

        return (await ListEmployeesAsync(cancellationToken)).First(e => e.Id == employee.Id);
    }

    public async Task<EmployeeListItemDto> UpdateEmployeeAsync(
        Guid id,
        UpdateEmployeeRequest request,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var employee = await _db.Employees
            .Include(e => e.EmployeeRoles)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Сотрудник не найден.");

        employee.DisplayName = request.DisplayName.Trim();
        var (first, last) = SplitDisplayName(employee.DisplayName);
        employee.FirstName = first;
        employee.LastName = last;
        employee.Email = request.Email?.Trim();
        employee.Phone = request.Phone?.Trim();
        employee.IsActive = request.IsActive;
        employee.PayType = request.PayType;
        employee.HourlyRate = request.HourlyRate;
        employee.MonthlySalary = request.MonthlySalary;
        employee.ShiftRate = request.ShiftRate;
        employee.UpdatedAt = DateTimeOffset.UtcNow;
        employee.UpdatedBy = actorId;

        var roles = await ResolveRolesAsync(request.RoleCodes, cancellationToken);
        _db.EmployeeRoles.RemoveRange(employee.EmployeeRoles);
        foreach (var role in roles)
            employee.EmployeeRoles.Add(new EmployeeRole { EmployeeId = employee.Id, RoleId = role.Id });

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = employee.BranchId,
            EmployeeId = actorId,
            Action = "employee.update",
            EntityType = nameof(Employee),
            EntityId = employee.Id.ToString(),
            DetailsJson = $"{{\"login\":\"{employee.Login}\",\"active\":{(employee.IsActive ? "true" : "false")},\"roles\":\"{string.Join(',', roles.Select(r => r.Code))}\"}}"
        });

        await _db.SaveChangesAsync(cancellationToken);
        return (await ListEmployeesAsync(cancellationToken)).First(e => e.Id == id);
    }

    public async Task SetPasswordAsync(
        Guid id,
        SetEmployeePasswordRequest request,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            throw new InvalidOperationException("Пароль должен быть не короче 6 символов.");

        var employee = await _db.Employees.Include(e => e.Credential)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Сотрудник не найден.");

        if (employee.Credential is null)
        {
            employee.Credential = new EmployeeCredential { EmployeeId = employee.Id };
            _db.EmployeeCredentials.Add(employee.Credential);
        }

        employee.Credential.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        employee.Credential.PasswordChangedAt = DateTimeOffset.UtcNow;
        employee.Credential.AccessFailedCount = 0;
        employee.Credential.LockoutEnd = null;

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = employee.BranchId,
            EmployeeId = actorId,
            Action = "employee.password.reset",
            EntityType = nameof(Employee),
            EntityId = employee.Id.ToString()
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteEmployeeAsync(
        Guid id,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (id == actorId)
            throw new InvalidOperationException("Нельзя удалить свой аккаунт.");

        var employee = await _db.Employees
            .Include(e => e.EmployeeRoles).ThenInclude(er => er.Role)
            .Include(e => e.Credential)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Сотрудник не найден.");

        var isOwner = employee.EmployeeRoles.Any(r => r.Role.Code.Equals("owner", StringComparison.OrdinalIgnoreCase));
        if (isOwner)
        {
            var otherOwners = await _db.Employees.AsNoTracking()
                .CountAsync(
                    e => e.Id != id
                         && e.IsActive
                         && !e.Login.StartsWith("__del__")
                         && e.EmployeeRoles.Any(er => er.Role.Code == "owner"),
                    cancellationToken);
            if (otherOwners == 0)
                throw new InvalidOperationException("Нельзя удалить последнего владельца.");
        }

        // Soft-delete: история смен/кассы остаётся, логин освобождается.
        employee.IsActive = false;
        employee.Login = $"__del__{employee.Id:N}";
        if (!employee.DisplayName.StartsWith("[удалён]", StringComparison.OrdinalIgnoreCase))
            employee.DisplayName = $"[удалён] {employee.DisplayName}";
        employee.UpdatedAt = DateTimeOffset.UtcNow;
        employee.UpdatedBy = actorId;

        _db.EmployeeRoles.RemoveRange(employee.EmployeeRoles);
        if (employee.Credential is not null)
            _db.EmployeeCredentials.Remove(employee.Credential);

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = employee.BranchId,
            EmployeeId = actorId,
            Action = "employee.delete",
            EntityType = nameof(Employee),
            EntityId = employee.Id.ToString(),
            DetailsJson = $"{{\"login\":\"{employee.Login}\"}}"
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _db.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        return roles.Select(r => new RoleDto(
            r.Id, r.Code, r.Name, r.Description,
            r.RolePermissions.Select(rp => rp.Permission.Code).OrderBy(c => c).ToList())).ToList();
    }

    public async Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var list = await _db.Permissions.AsNoTracking()
            .OrderBy(p => p.GroupName).ThenBy(p => p.Code)
            .ToListAsync(cancellationToken);
        return list.Select(p => new PermissionDto(p.Id, p.Code, p.Name, p.GroupName)).ToList();
    }

    public async Task<IReadOnlyList<WorkShiftDto>> GetShiftsAsync(
        DateOnly from,
        DateOnly to,
        Guid? employeeId,
        CancellationToken cancellationToken = default)
    {
        var q = _db.WorkShifts.AsNoTracking()
            .Include(s => s.Employee)
            .Where(s => s.WorkDate >= from && s.WorkDate <= to);
        if (employeeId.HasValue)
            q = q.Where(s => s.EmployeeId == employeeId.Value);

        var list = await q.OrderBy(s => s.WorkDate).ThenBy(s => s.PlannedStart).ToListAsync(cancellationToken);
        return list.Select(MapShift).ToList();
    }

    public async Task<WorkShiftDto> CreateShiftAsync(
        CreateWorkShiftRequest request,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        WorkShiftTiming.EnsureValidDuration(request.PlannedStart, request.PlannedEnd);

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Сотрудник не найден.");

        var branchId = employee.BranchId ?? await _db.Branches.Select(b => b.Id).FirstAsync(cancellationToken);
        var tzId = await ResolveBranchTzAsync(branchId, cancellationToken);

        if (await HasOverlapAsync(request.EmployeeId, request.WorkDate, request.PlannedStart, request.PlannedEnd, excludeId: null, tzId, cancellationToken))
            throw new InvalidOperationException("Пересечение с другой сменой.");

        var shift = new WorkShift
        {
            BranchId = branchId,
            EmployeeId = employee.Id,
            WorkDate = request.WorkDate,
            PlannedStart = request.PlannedStart,
            PlannedEnd = request.PlannedEnd,
            Status = WorkShiftStatus.Scheduled,
            Comment = request.Comment,
            CreatedByEmployeeId = actorId
        };
        _db.WorkShifts.Add(shift);
        await _db.SaveChangesAsync(cancellationToken);
        await _db.Entry(shift).Reference(s => s.Employee).LoadAsync(cancellationToken);
        return MapShift(shift);
    }

    public async Task<IReadOnlyList<WorkShiftDto>> BulkCreateShiftsAsync(
        BulkCreateWorkShiftsRequest request,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (request.To < request.From)
            throw new InvalidOperationException("Неверный диапазон дат.");
        WorkShiftTiming.EnsureValidDuration(request.PlannedStart, request.PlannedEnd);
        if ((request.To.DayNumber - request.From.DayNumber) > 62)
            throw new InvalidOperationException("Максимум 62 дня за раз.");

        var weekdays = request.Weekdays is { Count: > 0 }
            ? request.Weekdays.Where(d => d is >= 0 and <= 6).Distinct().ToHashSet()
            : null;

        var created = new List<WorkShiftDto>();
        for (var d = request.From; d <= request.To; d = d.AddDays(1))
        {
            if (weekdays is not null && !weekdays.Contains((int)d.DayOfWeek))
                continue;

            try
            {
                created.Add(await CreateShiftAsync(
                    new CreateWorkShiftRequest(request.EmployeeId, d, request.PlannedStart, request.PlannedEnd, request.Comment),
                    actorId,
                    cancellationToken));
            }
            catch (InvalidOperationException)
            {
                // skip overlaps / duplicates in bulk
            }
        }

        return created;
    }

    public async Task<WorkShiftDto> UpdateShiftAsync(
        Guid shiftId,
        UpdateWorkShiftRequest request,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        WorkShiftTiming.EnsureValidDuration(request.PlannedStart, request.PlannedEnd);

        var shift = await LoadShiftAsync(shiftId, cancellationToken);
        if (shift.Status is WorkShiftStatus.Completed or WorkShiftStatus.Cancelled)
            throw new InvalidOperationException("Нельзя менять закрытую смену.");

        var employeeId = request.EmployeeId ?? shift.EmployeeId;
        if (employeeId != shift.EmployeeId)
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.IsActive, cancellationToken)
                ?? throw new KeyNotFoundException("Сотрудник не найден.");
            shift.EmployeeId = employee.Id;
            shift.BranchId = employee.BranchId ?? shift.BranchId;
        }

        var tzId = await ResolveBranchTzAsync(shift.BranchId, cancellationToken);
        if (await HasOverlapAsync(employeeId, request.WorkDate, request.PlannedStart, request.PlannedEnd, shift.Id, tzId, cancellationToken))
            throw new InvalidOperationException("Пересечение с другой сменой.");

        shift.WorkDate = request.WorkDate;
        shift.PlannedStart = request.PlannedStart;
        shift.PlannedEnd = request.PlannedEnd;
        shift.Comment = request.Comment;
        shift.UpdatedAt = DateTimeOffset.UtcNow;
        shift.UpdatedBy = actorId;
        await _db.SaveChangesAsync(cancellationToken);
        await _db.Entry(shift).Reference(s => s.Employee).LoadAsync(cancellationToken);
        return MapShift(shift);
    }

    public async Task<WorkShiftDto> CancelShiftAsync(Guid shiftId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var shift = await LoadShiftAsync(shiftId, cancellationToken);
        if (shift.Status is WorkShiftStatus.Completed)
            throw new InvalidOperationException("Завершённую смену нельзя отменить.");
        if (shift.Status is WorkShiftStatus.Working or WorkShiftStatus.OnBreak or WorkShiftStatus.Late)
            throw new InvalidOperationException("Сначала отметьте уход, затем отменяйте.");

        shift.Status = WorkShiftStatus.Cancelled;
        shift.UpdatedAt = DateTimeOffset.UtcNow;
        shift.UpdatedBy = actorId;
        await _db.SaveChangesAsync(cancellationToken);
        return MapShift(shift);
    }

    public async Task<WorkShiftDto> ClockInAsync(
        Guid shiftId,
        ClockActionRequest request,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var shift = await LoadShiftAsync(shiftId, cancellationToken);
        if (shift.Status is not (WorkShiftStatus.Scheduled or WorkShiftStatus.Late or WorkShiftStatus.Absent))
            throw new InvalidOperationException("Нельзя начать смену в текущем статусе.");

        var tzId = await ResolveBranchTzAsync(shift.BranchId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var plannedStart = BranchTimeZone.ToUtc(shift.WorkDate, shift.PlannedStart, tzId);
        shift.ActualStartAt = now;
        shift.Status = now > plannedStart.AddMinutes(10) ? WorkShiftStatus.Late : WorkShiftStatus.Working;
        if (!string.IsNullOrWhiteSpace(request.Comment))
            shift.Comment = request.Comment;
        shift.UpdatedAt = now;
        shift.UpdatedBy = actorId;
        await _db.SaveChangesAsync(cancellationToken);
        return MapShift(shift);
    }

    public async Task<WorkShiftDto> ClockOutAsync(
        Guid shiftId,
        ClockActionRequest request,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var shift = await LoadShiftAsync(shiftId, cancellationToken);
        if (shift.Status is not (WorkShiftStatus.Working or WorkShiftStatus.OnBreak or WorkShiftStatus.Late))
            throw new InvalidOperationException("Смена не в работе.");
        if (shift.ActualStartAt is null)
            throw new InvalidOperationException("Нет фактического начала.");

        var now = DateTimeOffset.UtcNow;
        if (shift.Status == WorkShiftStatus.OnBreak && shift.BreakStartedAt is not null)
        {
            shift.BreakMinutes += Math.Max(0, (int)(now - shift.BreakStartedAt.Value).TotalMinutes);
            shift.BreakStartedAt = null;
        }

        shift.ActualEndAt = now;
        shift.Status = WorkShiftStatus.Completed;
        if (!string.IsNullOrWhiteSpace(request.Comment))
            shift.Comment = request.Comment;
        shift.UpdatedAt = now;
        shift.UpdatedBy = actorId;
        await _db.SaveChangesAsync(cancellationToken);
        return MapShift(shift);
    }

    public async Task<WorkShiftDto> StartBreakAsync(Guid shiftId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var shift = await LoadShiftAsync(shiftId, cancellationToken);
        if (shift.Status is not (WorkShiftStatus.Working or WorkShiftStatus.Late))
            throw new InvalidOperationException("Перерыв можно начать только во время смены.");
        if (shift.BreakStartedAt is not null)
            throw new InvalidOperationException("Перерыв уже начат.");

        var now = DateTimeOffset.UtcNow;
        shift.BreakStartedAt = now;
        shift.Status = WorkShiftStatus.OnBreak;
        shift.UpdatedAt = now;
        shift.UpdatedBy = actorId;
        await _db.SaveChangesAsync(cancellationToken);
        return MapShift(shift);
    }

    public async Task<WorkShiftDto> EndBreakAsync(Guid shiftId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var shift = await LoadShiftAsync(shiftId, cancellationToken);
        if (shift.Status != WorkShiftStatus.OnBreak || shift.BreakStartedAt is null)
            throw new InvalidOperationException("Сейчас нет активного перерыва.");

        var now = DateTimeOffset.UtcNow;
        shift.BreakMinutes += Math.Max(0, (int)(now - shift.BreakStartedAt.Value).TotalMinutes);
        shift.BreakStartedAt = null;
        shift.Status = WorkShiftStatus.Working;
        shift.UpdatedAt = now;
        shift.UpdatedBy = actorId;
        await _db.SaveChangesAsync(cancellationToken);
        return MapShift(shift);
    }

    public async Task<WorkShiftDto> MarkAbsentAsync(
        Guid shiftId,
        ClockActionRequest request,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var shift = await LoadShiftAsync(shiftId, cancellationToken);
        if (shift.Status is WorkShiftStatus.Completed or WorkShiftStatus.Cancelled)
            throw new InvalidOperationException("Смена уже закрыта.");

        shift.Status = WorkShiftStatus.Absent;
        shift.Comment = string.IsNullOrWhiteSpace(request.Comment) ? "Отсутствие" : request.Comment;
        shift.UpdatedAt = DateTimeOffset.UtcNow;
        shift.UpdatedBy = actorId;
        await _db.SaveChangesAsync(cancellationToken);
        return MapShift(shift);
    }

    private async Task<bool> HasOverlapAsync(
        Guid employeeId,
        DateOnly workDate,
        TimeOnly plannedStart,
        TimeOnly plannedEnd,
        Guid? excludeId,
        string tzId,
        CancellationToken cancellationToken)
    {
        var from = workDate.AddDays(-1);
        var to = workDate.AddDays(1);
        var candidates = await _db.WorkShifts.AsNoTracking()
            .Where(s => s.EmployeeId == employeeId
                        && s.Status != WorkShiftStatus.Cancelled
                        && s.WorkDate >= from
                        && s.WorkDate <= to
                        && (excludeId == null || s.Id != excludeId.Value))
            .Select(s => new { s.WorkDate, s.PlannedStart, s.PlannedEnd })
            .ToListAsync(cancellationToken);

        return candidates.Any(s => WorkShiftTiming.WindowsOverlap(
            workDate, plannedStart, plannedEnd,
            s.WorkDate, s.PlannedStart, s.PlannedEnd,
            tzId));
    }

    private async Task<string> ResolveBranchTzAsync(Guid branchId, CancellationToken cancellationToken)
    {
        var tz = await _db.Branches.AsNoTracking()
            .Where(b => b.Id == branchId)
            .Select(b => b.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(tz) ? BranchTimeZone.DefaultId : tz;
    }

    public async Task<IReadOnlyList<PayrollAccrualDto>> GetAccrualsAsync(
        DateOnly from,
        DateOnly to,
        Guid? employeeId,
        CancellationToken cancellationToken = default)
    {
        var q = _db.PayrollAccruals.AsNoTracking()
            .Include(a => a.Employee)
            .Where(a => a.PeriodFrom <= to && a.PeriodTo >= from && a.Status != PayrollAccrualStatus.Cancelled);
        if (employeeId.HasValue)
            q = q.Where(a => a.EmployeeId == employeeId.Value);

        var list = await q.OrderByDescending(a => a.CreatedAt).ToListAsync(cancellationToken);
        return list.Select(MapAccrual).ToList();
    }

    public async Task<PayrollAccrualDto> CreateAccrualAsync(
        CreatePayrollAccrualRequest request,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount == 0)
            throw new InvalidOperationException("Сумма не может быть 0.");
        if (request.PeriodTo < request.PeriodFrom)
            throw new InvalidOperationException("Неверный период.");

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Сотрудник не найден.");

        var branchId = employee.BranchId ?? await _db.Branches.Select(b => b.Id).FirstAsync(cancellationToken);
        var accrual = new PayrollAccrual
        {
            BranchId = branchId,
            EmployeeId = employee.Id,
            PeriodFrom = request.PeriodFrom,
            PeriodTo = request.PeriodTo,
            Type = request.Type,
            Status = PayrollAccrualStatus.Draft,
            Amount = request.Amount,
            Basis = request.Basis,
            Comment = request.Comment,
            CreatedByEmployeeId = actorId
        };
        _db.PayrollAccruals.Add(accrual);
        await _db.SaveChangesAsync(cancellationToken);
        await _db.Entry(accrual).Reference(a => a.Employee).LoadAsync(cancellationToken);
        return MapAccrual(accrual);
    }

    public async Task<PayrollAccrualDto> ApproveAccrualAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        var accrual = await _db.PayrollAccruals.Include(a => a.Employee)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Начисление не найдено.");
        if (accrual.Status != PayrollAccrualStatus.Draft)
            throw new InvalidOperationException("Утвердить можно только Draft.");

        accrual.Status = PayrollAccrualStatus.Approved;
        accrual.ApprovedByEmployeeId = actorId;
        accrual.UpdatedAt = DateTimeOffset.UtcNow;
        accrual.UpdatedBy = actorId;
        await _db.SaveChangesAsync(cancellationToken);
        return MapAccrual(accrual);
    }

    public async Task<PayrollAccrualDto> MarkAccrualPaidAsync(Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        var accrual = await _db.PayrollAccruals.Include(a => a.Employee)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Начисление не найдено.");
        if (accrual.Status is not (PayrollAccrualStatus.Approved or PayrollAccrualStatus.Draft))
            throw new InvalidOperationException("Выплатить можно Approved/Draft.");

        accrual.Status = PayrollAccrualStatus.Paid;
        accrual.PaidAt = DateTimeOffset.UtcNow;
        accrual.ApprovedByEmployeeId ??= actorId;
        accrual.UpdatedAt = accrual.PaidAt;
        accrual.UpdatedBy = actorId;
        await _db.SaveChangesAsync(cancellationToken);
        return MapAccrual(accrual);
    }

    public async Task<IReadOnlyList<PayrollSummaryDto>> GetPayrollSummaryAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var employees = await _db.Employees.AsNoTracking().Where(e => e.IsActive).ToListAsync(cancellationToken);
        var accruals = await _db.PayrollAccruals.AsNoTracking()
            .Where(a => a.PeriodFrom <= to && a.PeriodTo >= from && a.Status != PayrollAccrualStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var shifts = await _db.WorkShifts.AsNoTracking()
            .Where(s => s.WorkDate >= from && s.WorkDate <= to)
            .ToListAsync(cancellationToken);

        return employees.Select(e =>
        {
            var ea = accruals.Where(a => a.EmployeeId == e.Id).ToList();
            var es = shifts.Where(s => s.EmployeeId == e.Id).ToList();
            var hours = es.Where(s => s.Status == WorkShiftStatus.Completed).Sum(WorkedHours);
            var completed = es.Count(s => s.Status == WorkShiftStatus.Completed);
            var baseAuto = e.PayType switch
            {
                EmployeePayType.Hourly => Math.Round(hours * e.HourlyRate, 2, MidpointRounding.AwayFromZero),
                EmployeePayType.PerShift => Math.Round(completed * e.ShiftRate, 2, MidpointRounding.AwayFromZero),
                EmployeePayType.FixedMonthly => e.MonthlySalary,
                _ => 0
            };
            var manualAdjustments = ea
                .Where(a => a.Status is PayrollAccrualStatus.Draft or PayrollAccrualStatus.Approved or PayrollAccrualStatus.Paid)
                .Where(a => a.Type is not (PayrollAccrualType.Salary or PayrollAccrualType.Hourly or PayrollAccrualType.ShiftPay))
                .Sum(a => a.Amount);
            var accrued = Math.Round(baseAuto + manualAdjustments, 2, MidpointRounding.AwayFromZero);
            var paid = ea.Where(a => a.Status == PayrollAccrualStatus.Paid).Sum(a => a.Amount);
            return new PayrollSummaryDto(e.Id, e.DisplayName, accrued, paid, accrued - paid, hours, completed);
        }).OrderBy(x => x.EmployeeName).ToList();
    }

    private async Task<List<Role>> ResolveRolesAsync(IReadOnlyList<string> codes, CancellationToken cancellationToken)
    {
        if (codes is null || codes.Count == 0)
            throw new InvalidOperationException("Укажите хотя бы одну роль.");

        var normalized = codes.Select(c => c.Trim().ToLowerInvariant()).Distinct().ToList();
        var roles = await _db.Roles.Where(r => normalized.Contains(r.Code.ToLower())).ToListAsync(cancellationToken);
        if (roles.Count != normalized.Count)
            throw new InvalidOperationException("Одна или несколько ролей не найдены.");
        return roles;
    }

    private async Task<WorkShift> LoadShiftAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _db.WorkShifts.Include(s => s.Employee)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Смена не найдена.");
    }

    private static decimal WorkedHours(WorkShift s)
    {
        if (s.ActualStartAt is null || s.ActualEndAt is null)
            return 0;
        var minutes = (decimal)(s.ActualEndAt.Value - s.ActualStartAt.Value).TotalMinutes - s.BreakMinutes;
        return Math.Round(Math.Max(0, minutes) / 60m, 2, MidpointRounding.AwayFromZero);
    }

    private static EmployeeListItemDto MapEmployee(Employee e) => new(
        e.Id, e.Login, e.DisplayName, e.Email, e.Phone, e.BranchId, e.IsActive,
        e.PayType, e.HourlyRate, e.MonthlySalary, e.ShiftRate, e.LastLoginAt,
        e.EmployeeRoles.Select(r => r.Role.Code).OrderBy(x => x).ToList());

    private static WorkShiftDto MapShift(WorkShift s) => new(
        s.Id, s.EmployeeId, s.Employee?.DisplayName ?? "",
        s.WorkDate, s.PlannedStart, s.PlannedEnd, s.Status,
        s.ActualStartAt, s.ActualEndAt, s.BreakMinutes,
        WorkedHours(s), s.Comment);

    private static PayrollAccrualDto MapAccrual(PayrollAccrual a) => new(
        a.Id, a.EmployeeId, a.Employee?.DisplayName ?? "",
        a.PeriodFrom, a.PeriodTo, a.Type, a.Status, a.Amount,
        a.Basis, a.Comment, a.CreatedAt, a.PaidAt);

    private static (string First, string Last) SplitDisplayName(string display)
    {
        var parts = display.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("", ""),
            1 => (parts[0], ""),
            _ => (parts[0], parts[1])
        };
    }
}
