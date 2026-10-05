using Microsoft.EntityFrameworkCore;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;

namespace ShiftClub.Infrastructure.Services;

/// <summary>
/// Прогресс лояльности по lifetime TotalSpent.
/// Уровень автоматически только повышается (не сбрасывается вниз).
/// </summary>
internal static class LoyaltyProgress
{
    public static async Task ApplySpendAsync(
        ShiftClubDbContext db,
        Customer customer,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var spend = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (spend <= 0)
            return;

        customer.TotalSpent += spend;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await RefreshUpgradeOnlyAsync(db, customer, cancellationToken);
    }

    public static async Task RefreshUpgradeOnlyAsync(
        ShiftClubDbContext db,
        Customer customer,
        CancellationToken cancellationToken)
    {
        if (customer.LoyaltyLevelLocked)
            return;

        var levels = await db.LoyaltyLevels.AsNoTracking()
            .Where(l => l.BranchId == customer.BranchId && l.IsActive)
            .OrderByDescending(l => l.MinSpent)
            .ToListAsync(cancellationToken);

        var match = levels.FirstOrDefault(l => customer.TotalSpent >= l.MinSpent);
        if (match is null)
            return;

        if (customer.LoyaltyLevelId == match.Id)
            return;

        // Не понижаем: сравниваем с текущим порогом (даже если уровень выключили).
        if (customer.LoyaltyLevelId is Guid currentId)
        {
            var currentMin = levels.FirstOrDefault(l => l.Id == currentId)?.MinSpent
                ?? await db.LoyaltyLevels.AsNoTracking()
                    .Where(l => l.Id == currentId)
                    .Select(l => (decimal?)l.MinSpent)
                    .FirstOrDefaultAsync(cancellationToken);

            if (currentMin is decimal min && match.MinSpent < min)
                return;
        }

        customer.LoyaltyLevelId = match.Id;
        customer.LoyaltyLevel = await db.LoyaltyLevels.FirstAsync(l => l.Id == match.Id, cancellationToken);
    }
}
