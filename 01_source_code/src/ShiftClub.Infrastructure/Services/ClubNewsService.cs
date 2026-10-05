using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.News;

namespace ShiftClub.Infrastructure.Services;

public sealed class ClubNewsService : IClubNewsService
{
    private readonly ShiftClubDbContext _db;

    public ClubNewsService(ShiftClubDbContext db) => _db = db;

    public async Task<IReadOnlyList<ClubNewsDto>> ListAdminAsync(
        Guid? branchId,
        bool includeUnpublished,
        CancellationToken cancellationToken = default)
    {
        var q = _db.ClubNewsPosts.AsNoTracking().AsQueryable();
        if (branchId.HasValue)
            q = q.Where(n => n.BranchId == branchId.Value);
        if (!includeUnpublished)
            q = q.Where(n => n.IsPublished);

        var list = await q
            .OrderByDescending(n => n.IsPinned)
            .ThenBy(n => n.SortOrder)
            .ThenByDescending(n => n.PublishAt ?? n.CreatedAt)
            .ToListAsync(cancellationToken);
        return list.Select(MapAdmin).ToList();
    }

    public async Task<IReadOnlyList<ClientNewsDto>> ListPublishedAsync(
        Guid branchId,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        take = Math.Clamp(take, 1, 50);
        var list = await _db.ClubNewsPosts.AsNoTracking()
            .Where(n => n.BranchId == branchId && n.IsPublished)
            .Where(n => n.PublishAt == null || n.PublishAt <= now)
            .Where(n => n.ExpireAt == null || n.ExpireAt > now)
            .OrderByDescending(n => n.IsPinned)
            .ThenBy(n => n.SortOrder)
            .ThenByDescending(n => n.PublishAt ?? n.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return list.Select(n => new ClientNewsDto(
            n.Id,
            n.Title,
            n.Body,
            n.ImageUrl,
            n.Category,
            n.IsPinned,
            n.PublishAt ?? n.CreatedAt)).ToList();
    }

    public async Task<ClubNewsDto> CreateAsync(
        Guid branchId,
        UpsertClubNewsRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var entity = new ClubNewsPost
        {
            BranchId = branchId,
            CreatedBy = employeeId,
            UpdatedBy = employeeId
        };
        Apply(entity, request);
        _db.ClubNewsPosts.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return MapAdmin(entity);
    }

    public async Task<ClubNewsDto> UpdateAsync(
        Guid id,
        UpsertClubNewsRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var entity = await _db.ClubNewsPosts.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Новость не найдена");
        Apply(entity, request);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);
        return MapAdmin(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ClubNewsPosts.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Новость не найдена");
        _db.ClubNewsPosts.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(UpsertClubNewsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("Укажите заголовок");
        if (string.IsNullOrWhiteSpace(request.Body))
            throw new InvalidOperationException("Укажите текст новости");
    }

    private static void Apply(ClubNewsPost entity, UpsertClubNewsRequest request)
    {
        entity.Title = request.Title.Trim();
        entity.Body = request.Body.Trim();
        entity.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
        entity.Category = string.IsNullOrWhiteSpace(request.Category) ? "Info" : request.Category.Trim();
        entity.IsPublished = request.IsPublished;
        entity.IsPinned = request.IsPinned;
        entity.SortOrder = request.SortOrder;
        entity.PublishAt = request.PublishAt;
        entity.ExpireAt = request.ExpireAt;
    }

    private static ClubNewsDto MapAdmin(ClubNewsPost n) => new(
        n.Id, n.BranchId, n.Title, n.Body, n.ImageUrl, n.Category,
        n.IsPublished, n.IsPinned, n.SortOrder, n.PublishAt, n.ExpireAt,
        n.CreatedAt, n.UpdatedAt);
}
