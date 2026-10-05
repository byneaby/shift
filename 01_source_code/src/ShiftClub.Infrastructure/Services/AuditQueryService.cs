using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Audit;

namespace ShiftClub.Infrastructure.Services;

public sealed class AuditQueryService : IAuditQueryService
{
    /// <summary>Шумные события клиента ПК — скрываем в журнале сотрудников по умолчанию.</summary>
    private static readonly string[] TechnicalActions =
    [
        "computer.command",
        "computer.wake",
    ];

    private readonly ShiftClubDbContext _db;

    public AuditQueryService(ShiftClubDbContext db)
    {
        _db = db;
    }

    public async Task<AuditLogPageDto> QueryAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? employeeId,
        string? action,
        string? search,
        bool includeTechnical,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var q = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (fromUtc is not null)
            q = q.Where(x => x.CreatedAt >= fromUtc);
        if (toUtc is not null)
            q = q.Where(x => x.CreatedAt <= toUtc);
        if (employeeId is Guid eid)
            q = q.Where(x => x.EmployeeId == eid);
        if (!string.IsNullOrWhiteSpace(action))
        {
            var a = action.Trim();
            q = q.Where(x => x.Action == a || x.Action.StartsWith(a + "."));
        }

        if (!includeTechnical)
            q = q.Where(x => !TechnicalActions.Contains(x.Action));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(x =>
                x.Action.ToLower().Contains(s)
                || (x.EntityType != null && x.EntityType.ToLower().Contains(s))
                || (x.EntityId != null && x.EntityId.ToLower().Contains(s))
                || (x.DetailsJson != null && x.DetailsJson.ToLower().Contains(s))
                || (x.IpAddress != null && x.IpAddress.ToLower().Contains(s)));
        }

        var total = await q.CountAsync(cancellationToken);

        var rows = await q
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.CreatedAt,
                x.EmployeeId,
                x.Action,
                x.EntityType,
                x.EntityId,
                x.DetailsJson,
                x.IpAddress,
            })
            .ToListAsync(cancellationToken);

        var empIds = rows.Where(r => r.EmployeeId is not null).Select(r => r.EmployeeId!.Value).Distinct().ToList();
        var names = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.DisplayName, e.Login })
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var items = rows.Select(r =>
        {
            string? name = null;
            string? login = null;
            if (r.EmployeeId is Guid id && names.TryGetValue(id, out var e))
            {
                name = e.DisplayName;
                login = e.Login;
            }

            return new AuditLogDto(
                r.Id,
                r.CreatedAt,
                r.EmployeeId,
                name,
                login,
                r.Action,
                r.EntityType,
                r.EntityId,
                r.DetailsJson,
                r.IpAddress);
        }).ToList();

        return new AuditLogPageDto(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<string>> ListActionsAsync(
        bool includeTechnical,
        CancellationToken cancellationToken = default)
    {
        var q = _db.AuditLogs.AsNoTracking().Select(x => x.Action).Distinct();
        if (!includeTechnical)
            q = q.Where(a => !TechnicalActions.Contains(a));
        return await q.OrderBy(a => a).ToListAsync(cancellationToken);
    }
}
