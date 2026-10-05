using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Branches;

namespace ShiftClub.Infrastructure.Services;

public sealed class BranchService : IBranchService
{
    private readonly ShiftClubDbContext _db;

    public BranchService(ShiftClubDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BranchDto>> GetBranchesAsync(CancellationToken cancellationToken = default)
    {
        var branches = await _db.Branches
            .AsNoTracking()
            .Include(b => b.Zones)
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        return branches.Select(b => new BranchDto(
            b.Id,
            b.Name,
            b.Code,
            b.TimeZoneId,
            b.CurrencyCode,
            b.IsActive,
            b.Zones
                .OrderBy(z => z.SortOrder)
                .Select(z => new ZoneDto(z.Id, z.BranchId, z.Name, z.Code, z.ColorHex, z.SortOrder, z.MinSessionMinutes, z.IsActive, z.Kind, z.GridColumns, z.GridRows))
                .ToList())).ToList();
    }
}
