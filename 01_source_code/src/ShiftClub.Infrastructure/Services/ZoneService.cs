using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Branches;

namespace ShiftClub.Infrastructure.Services;

public sealed class ZoneService : IZoneService
{
    private readonly ShiftClubDbContext _db;

    public ZoneService(ShiftClubDbContext db) => _db = db;

    public async Task<IReadOnlyList<ZoneDto>> ListAsync(
        Guid? branchId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var q = _db.Zones.AsNoTracking().AsQueryable();
        if (branchId.HasValue)
            q = q.Where(z => z.BranchId == branchId.Value);
        if (!includeInactive)
            q = q.Where(z => z.IsActive);

        var list = await q.OrderBy(z => z.SortOrder).ThenBy(z => z.Name).ToListAsync(cancellationToken);
        return list.Select(Map).ToList();
    }

    public async Task<ZoneDto> CreateAsync(
        UpsertZoneRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var branchId = request.BranchId
                       ?? await _db.Branches.Select(b => b.Id).FirstAsync(cancellationToken);

        if (await _db.Zones.AnyAsync(z => z.BranchId == branchId && z.Code == request.Code.Trim().ToUpperInvariant(), cancellationToken))
            throw new InvalidOperationException("Зона с таким кодом уже есть");

        var zone = new Zone
        {
            BranchId = branchId,
            CreatedBy = employeeId,
            UpdatedBy = employeeId
        };
        Apply(zone, request);
        _db.Zones.Add(zone);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(zone);
    }

    public async Task<ZoneDto> UpdateAsync(
        Guid id,
        UpsertZoneRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var zone = await _db.Zones.FirstOrDefaultAsync(z => z.Id == id, cancellationToken)
                   ?? throw new KeyNotFoundException("Зона не найдена");

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Zones.AnyAsync(z => z.BranchId == zone.BranchId && z.Code == code && z.Id != id, cancellationToken))
            throw new InvalidOperationException("Зона с таким кодом уже есть");

        Apply(zone, request);
        zone.UpdatedAt = DateTimeOffset.UtcNow;
        zone.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(zone);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var zone = await _db.Zones.FirstOrDefaultAsync(z => z.Id == id, cancellationToken)
                   ?? throw new KeyNotFoundException("Зона не найдена");
        zone.IsActive = false;
        zone.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(UpsertZoneRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Укажите название зоны");
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new InvalidOperationException("Укажите код зоны");
    }

    private static void Apply(Zone zone, UpsertZoneRequest request)
    {
        zone.Name = request.Name.Trim();
        zone.Code = request.Code.Trim().ToUpperInvariant();
        zone.ColorHex = string.IsNullOrWhiteSpace(request.ColorHex) ? "#FF6A00" : request.ColorHex.Trim();
        if (!zone.ColorHex.StartsWith('#'))
            zone.ColorHex = "#" + zone.ColorHex;
        zone.SortOrder = request.SortOrder;
        zone.MinSessionMinutes = request.MinSessionMinutes;
        zone.IsActive = request.IsActive;
        if (!string.IsNullOrWhiteSpace(request.Kind))
            zone.Kind = NormalizeKind(request.Kind);
        if (request.GridColumns is > 0 and <= 24)
            zone.GridColumns = request.GridColumns.Value;
        if (request.GridRows is > 0 and <= 24)
            zone.GridRows = request.GridRows.Value;
    }

    private static string NormalizeKind(string kind)
    {
        var k = kind.Trim();
        return k.Equals("Restroom", StringComparison.OrdinalIgnoreCase) ? "Restroom"
            : k.Equals("Cafe", StringComparison.OrdinalIgnoreCase) ? "Cafe"
            : k.Equals("Other", StringComparison.OrdinalIgnoreCase) ? "Other"
            : "Hall";
    }

    private static ZoneDto Map(Zone z) => new(
        z.Id, z.BranchId, z.Name, z.Code, z.ColorHex, z.SortOrder, z.MinSessionMinutes, z.IsActive,
        z.Kind, z.GridColumns, z.GridRows);
}
