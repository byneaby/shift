using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Customers;

namespace ShiftClub.Infrastructure.Services;

public sealed class LoyaltyService : ILoyaltyService
{
    private readonly ShiftClubDbContext _db;

    public LoyaltyService(ShiftClubDbContext db) => _db = db;

    public async Task<IReadOnlyList<LoyaltyLevelDto>> ListAsync(
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var bid = await ResolveBranchIdAsync(branchId, cancellationToken);
        var rows = await _db.LoyaltyLevels.AsNoTracking()
            .Where(l => l.BranchId == bid)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.MinSpent)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<LoyaltyLevelDto> CreateAsync(
        Guid? branchId,
        UpsertLoyaltyLevelRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var bid = await ResolveBranchIdAsync(branchId, cancellationToken);
        Validate(request);

        var code = NormalizeCode(request.Code);
        var taken = await _db.LoyaltyLevels.AnyAsync(
            l => l.BranchId == bid && l.Code == code, cancellationToken);
        if (taken)
            throw new InvalidOperationException("Код уровня уже используется.");

        var entity = new LoyaltyLevel
        {
            BranchId = bid,
            CreatedBy = employeeId,
            UpdatedBy = employeeId
        };
        Apply(entity, request, code);
        _db.LoyaltyLevels.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<LoyaltyLevelDto> UpdateAsync(
        Guid id,
        UpsertLoyaltyLevelRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var entity = await _db.LoyaltyLevels.FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Уровень не найден");

        var code = NormalizeCode(request.Code);
        var taken = await _db.LoyaltyLevels.AnyAsync(
            l => l.BranchId == entity.BranchId && l.Code == code && l.Id != id, cancellationToken);
        if (taken)
            throw new InvalidOperationException("Код уровня уже используется.");

        Apply(entity, request, code);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.LoyaltyLevels.FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Уровень не найден");

        var inUse = await _db.Customers.AnyAsync(c => c.LoyaltyLevelId == id, cancellationToken);
        if (inUse)
            throw new InvalidOperationException(
                "Уровень назначен клиентам. Отключите его (IsActive=false) или переведите клиентов на другой уровень.");

        _db.LoyaltyLevels.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid> ResolveBranchIdAsync(Guid? branchId, CancellationToken ct)
    {
        if (branchId.HasValue)
            return branchId.Value;
        var id = await _db.Branches.AsNoTracking().OrderBy(b => b.CreatedAt).Select(b => b.Id)
            .FirstOrDefaultAsync(ct);
        if (id == Guid.Empty)
            throw new KeyNotFoundException("Филиал не найден");
        return id;
    }

    private static void Validate(UpsertLoyaltyLevelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Укажите название уровня");
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new InvalidOperationException("Укажите код уровня");
        if (request.MinSpent < 0)
            throw new InvalidOperationException("Порог трат не может быть отрицательным");
        if (request.BonusPercent is < 0 or > 100)
            throw new InvalidOperationException("Бонус при пополнении: 0–100%");
        if (request.TimeDiscountPercent is < 0 or > 100)
            throw new InvalidOperationException("Скидка на время: 0–100%");
    }

    private static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();

    private static void Apply(LoyaltyLevel entity, UpsertLoyaltyLevelRequest request, string code)
    {
        entity.Name = request.Name.Trim();
        entity.Code = code;
        entity.MinSpent = request.MinSpent;
        entity.BonusPercent = Math.Round(request.BonusPercent, 2, MidpointRounding.AwayFromZero);
        entity.TimeDiscountPercent = Math.Round(request.TimeDiscountPercent, 2, MidpointRounding.AwayFromZero);
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
    }

    private static LoyaltyLevelDto Map(LoyaltyLevel l) => new(
        l.Id,
        l.BranchId,
        l.Name,
        l.Code,
        l.MinSpent,
        l.BonusPercent,
        l.TimeDiscountPercent,
        l.SortOrder,
        l.IsActive);
}
