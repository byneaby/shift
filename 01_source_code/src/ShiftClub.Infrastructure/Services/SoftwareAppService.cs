using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.ClientLauncher;

namespace ShiftClub.Infrastructure.Services;

public sealed class SoftwareAppService : ISoftwareAppService
{
    private readonly ShiftClubDbContext _db;

    public SoftwareAppService(ShiftClubDbContext db) => _db = db;

    public async Task<IReadOnlyList<SoftwareAppAdminDto>> ListAsync(
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var q = _db.SoftwareApps.AsNoTracking().AsQueryable();
        if (branchId.HasValue)
            q = q.Where(a => a.BranchId == branchId.Value);

        var list = await q.OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(cancellationToken);
        return list.Select(Map).ToList();
    }

    public async Task<SoftwareAppAdminDto> CreateAsync(
        Guid branchId,
        UpsertSoftwareAppRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var entity = new SoftwareApp
        {
            BranchId = branchId,
            CreatedBy = employeeId,
            UpdatedBy = employeeId
        };
        Apply(entity, request);
        _db.SoftwareApps.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<SoftwareAppAdminDto> UpdateAsync(
        Guid id,
        UpsertSoftwareAppRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var entity = await _db.SoftwareApps.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Программа не найдена");
        Apply(entity, request);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.SoftwareApps.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Программа не найдена");
        _db.SoftwareApps.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SoftwareAppAdminDto> SetIconPathAsync(
        Guid id,
        string iconPath,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.SoftwareApps.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Программа не найдена");
        entity.IconPath = iconPath;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<SoftwareAppAdminDto> SetLaunchSoundUrlAsync(
        Guid id,
        string? soundUrl,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.SoftwareApps.FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Программа не найдена");
        entity.LaunchSoundUrl = string.IsNullOrWhiteSpace(soundUrl) ? null : soundUrl.Trim();
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<SoftwareAppsImportResult> ImportAsync(
        Guid branchId,
        IReadOnlyList<UpsertSoftwareAppRequest> apps,
        Guid employeeId,
        bool updateExisting,
        CancellationToken cancellationToken = default)
    {
        if (apps is null || apps.Count == 0)
            return new SoftwareAppsImportResult(0, 0, 0, 0);

        var existing = await _db.SoftwareApps
            .Where(a => a.BranchId == branchId)
            .ToListAsync(cancellationToken);

        var byPath = existing
            .GroupBy(a => a.ExePath.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var req in apps)
        {
            if (string.IsNullOrWhiteSpace(req.ExePath) || string.IsNullOrWhiteSpace(req.Name))
            {
                skipped++;
                continue;
            }

            var path = req.ExePath.Trim();
            if (byPath.TryGetValue(path, out var entity))
            {
                if (!updateExisting)
                {
                    skipped++;
                    continue;
                }

                Apply(entity, req);
                entity.IsActive = true;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                entity.UpdatedBy = employeeId;
                updated++;
            }
            else
            {
                Validate(req);
                entity = new SoftwareApp
                {
                    BranchId = branchId,
                    CreatedBy = employeeId,
                    UpdatedBy = employeeId
                };
                Apply(entity, req);
                entity.IsActive = true;
                _db.SoftwareApps.Add(entity);
                byPath[path] = entity;
                created++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new SoftwareAppsImportResult(created, updated, skipped, apps.Count);
    }

    private static void Validate(UpsertSoftwareAppRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Укажите название");
        if (string.IsNullOrWhiteSpace(request.ExePath))
            throw new InvalidOperationException("Укажите путь к exe");
    }

    // При update не затираем обложку, если в запросе iconPath не передан (null from form still ok if intentional)
    private static void Apply(SoftwareApp entity, UpsertSoftwareAppRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.Category = string.IsNullOrWhiteSpace(request.Category) ? "Games" : request.Category.Trim();
        entity.ExePath = request.ExePath.Trim();
        entity.Arguments = string.IsNullOrWhiteSpace(request.Arguments) ? null : request.Arguments.Trim();
        entity.WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory) ? null : request.WorkingDirectory.Trim();
        if (request.IconPath is not null)
            entity.IconPath = string.IsNullOrWhiteSpace(request.IconPath) ? null : request.IconPath.Trim();
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        entity.MinAge = request.MinAge;
    }

    private static SoftwareAppAdminDto Map(SoftwareApp a) => new(
        a.Id, a.BranchId, a.Name, a.Category, a.ExePath, a.Arguments, a.WorkingDirectory,
        a.IconPath, a.LaunchSoundUrl, a.SortOrder, a.IsActive, a.MinAge);
}
