using Microsoft.EntityFrameworkCore;
using ShiftClub.Infrastructure.Persistence;

namespace ShiftClub.Server.Licensing;

public sealed class LicenseSeatGuard
{
    private readonly LicenseState _state;
    private readonly ShiftClubDbContext _db;

    public LicenseSeatGuard(LicenseState state, ShiftClubDbContext db)
    {
        _state = state;
        _db = db;
    }

    public async Task EnsureCanAddSeatAsync(CancellationToken cancellationToken)
    {
        var snap = _state.Current;
        if (!snap.Enforce || !snap.Operational || snap.MaxComputers is null)
            return;

        var count = await _db.Computers.CountAsync(c => c.IsApproved && !c.IsDeleted, cancellationToken);
        if (count >= snap.MaxComputers.Value)
        {
            throw new InvalidOperationException(
                $"Лимит лицензии: {snap.MaxComputers.Value} мест. Сейчас занято {count}. Новые ПК не подключаются, пока не освободится место или лицензию не расширят.");
        }
    }

    public async Task EnsureCanApproveAsync(Guid computerId, CancellationToken cancellationToken)
    {
        var snap = _state.Current;
        if (!snap.Enforce || !snap.Operational || snap.MaxComputers is null)
            return;

        var already = await _db.Computers.AnyAsync(
            c => c.Id == computerId && c.IsApproved && !c.IsDeleted,
            cancellationToken);
        if (already)
            return;

        await EnsureCanAddSeatAsync(cancellationToken);
    }
}
