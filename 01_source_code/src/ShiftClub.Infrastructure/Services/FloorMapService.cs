using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.FloorMap;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Services;

public sealed class FloorMapService : IFloorMapService
{
    private readonly ShiftClubDbContext _db;
    private readonly IComputerService _computers;

    public FloorMapService(ShiftClubDbContext db, IComputerService computers)
    {
        _db = db;
        _computers = computers;
    }

    public async Task<FloorMapDto> GetMapAsync(Guid? branchId, CancellationToken cancellationToken = default)
    {
        var branch = await ResolveBranchAsync(branchId, cancellationToken);
        EnsureGridDefaults(branch);
        var computers = await _computers.GetComputersAsync(branch.Id, cancellationToken);
        var approved = computers.Where(c => c.IsApproved).ToList();

        var elements = await _db.FloorMapElements.AsNoTracking()
            .Where(e => e.BranchId == branch.Id)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.CreatedAt)
            .Select(e => MapElement(e))
            .ToListAsync(cancellationToken);

        return new FloorMapDto(
            branch.Id,
            branch.FloorGridCols,
            branch.FloorGridRows,
            approved,
            elements,
            NormalizeHex(branch.FloorBackgroundHex));
    }

    public async Task<FloorMapDto> UpdateSettingsAsync(
        Guid? branchId,
        UpdateFloorMapSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var branch = await ResolveBranchAsync(branchId, cancellationToken, track: true);
        branch.FloorGridCols = Math.Clamp(request.FloorGridCols, 4, 48);
        branch.FloorGridRows = Math.Clamp(request.FloorGridRows, 4, 36);
        branch.FloorBackgroundHex = NormalizeHex(request.FloorBackgroundHex);
        branch.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return await GetMapAsync(branch.Id, cancellationToken);
    }

    public async Task<FloorMapElementDto> CreateElementAsync(
        Guid? branchId,
        UpsertFloorMapElementRequest request,
        CancellationToken cancellationToken = default)
    {
        var branch = await ResolveBranchAsync(branchId, cancellationToken);
        ValidateRequest(request, branch);

        var entity = new FloorMapElement { BranchId = branch.Id };
        Apply(entity, request);
        _db.FloorMapElements.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return MapElement(entity);
    }

    public async Task<FloorMapElementDto> UpdateElementAsync(
        Guid id,
        UpsertFloorMapElementRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.FloorMapElements.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Элемент карты не найден");
        var branch = await ResolveBranchAsync(entity.BranchId, cancellationToken);
        ValidateRequest(request, branch);
        Apply(entity, request);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return MapElement(entity);
    }

    public async Task DeleteElementAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.FloorMapElements.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Элемент карты не найден");
        _db.FloorMapElements.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Branch> ResolveBranchAsync(Guid? branchId, CancellationToken ct, bool track = false)
    {
        IQueryable<Branch> q = track ? _db.Branches : _db.Branches.AsNoTracking();
        Branch? branch;
        if (branchId.HasValue)
            branch = await q.FirstOrDefaultAsync(b => b.Id == branchId.Value, ct);
        else
            branch = await q.OrderBy(b => b.CreatedAt).FirstOrDefaultAsync(ct);

        return branch ?? throw new KeyNotFoundException("Филиал не найден");
    }

    private static void EnsureGridDefaults(Branch branch)
    {
        if (branch.FloorGridCols <= 0) branch.FloorGridCols = 24;
        if (branch.FloorGridRows <= 0) branch.FloorGridRows = 16;
    }

    private static void ValidateRequest(UpsertFloorMapElementRequest request, Branch branch)
    {
        EnsureGridDefaults(branch);
        if (request.GridCol < 0 || request.GridRow < 0
            || request.GridCol >= branch.FloorGridCols
            || request.GridRow >= branch.FloorGridRows)
            throw new InvalidOperationException("Координаты вне сетки зала");

        if (request.ColSpan < 1 || request.RowSpan < 1)
            throw new InvalidOperationException("Размер элемента должен быть ≥ 1");

        if (request.GridCol + request.ColSpan > branch.FloorGridCols
            || request.GridRow + request.RowSpan > branch.FloorGridRows)
            throw new InvalidOperationException("Элемент выходит за границы сетки");

        if (request.StrokeWidth is < 1 or > 24)
            throw new InvalidOperationException("Толщина линии: 1–24");

        if (request.FontSize is < 8 or > 96)
            throw new InvalidOperationException("Размер шрифта: 8–96");

        if (request.RotationDeg is < 0 or > 359)
            throw new InvalidOperationException("Поворот: 0–359");

        if (request.Kind == FloorMapElementKind.Wall)
        {
            if (request.EndCol is null || request.EndRow is null)
                throw new InvalidOperationException("Для стены укажите конечную точку");
            if (request.EndCol < 0 || request.EndRow < 0
                || request.EndCol >= branch.FloorGridCols
                || request.EndRow >= branch.FloorGridRows)
                throw new InvalidOperationException("Конец стены вне сетки зала");
        }
    }

    private static void Apply(FloorMapElement entity, UpsertFloorMapElementRequest request)
    {
        entity.Kind = request.Kind;
        entity.Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
        if (entity.Label is { Length: > 500 })
            entity.Label = entity.Label[..500];
        entity.GridCol = request.GridCol;
        entity.GridRow = request.GridRow;
        entity.ColSpan = Math.Max(1, request.ColSpan);
        entity.RowSpan = Math.Max(1, request.RowSpan);
        entity.EndCol = request.Kind == FloorMapElementKind.Wall ? request.EndCol : null;
        entity.EndRow = request.Kind == FloorMapElementKind.Wall ? request.EndRow : null;
        entity.ColorHex = NormalizeHex(request.ColorHex);
        entity.FillHex = NormalizeHex(request.FillHex);
        entity.StrokeWidth = request.StrokeWidth;
        entity.FontSize = request.FontSize;
        entity.ShowIcon = request.ShowIcon;
        entity.RotationDeg = ((request.RotationDeg % 360) + 360) % 360;
        entity.SortOrder = request.SortOrder;
        entity.IsVisible = request.IsVisible;
    }

    private static string? NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        var t = hex.Trim();
        if (!t.StartsWith('#'))
            t = "#" + t;
        if (t.Length is not (4 or 7 or 9))
            return null;
        return t.Length > 16 ? t[..16] : t;
    }

    private static FloorMapElementDto MapElement(FloorMapElement e) => new(
        e.Id,
        e.BranchId,
        e.Kind,
        e.Label,
        e.GridCol,
        e.GridRow,
        e.ColSpan,
        e.RowSpan,
        e.EndCol,
        e.EndRow,
        e.ColorHex,
        e.SortOrder,
        e.IsVisible,
        e.FillHex,
        e.StrokeWidth,
        e.FontSize,
        e.ShowIcon,
        e.RotationDeg);
}
