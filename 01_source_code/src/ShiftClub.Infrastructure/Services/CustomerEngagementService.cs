using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Time;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Services;

public sealed class CustomerEngagementService : ICustomerEngagementService
{
    private readonly ShiftClubDbContext _db;
    private readonly ICustomerTelegramNotifySink _notify;
    private readonly IClubSettingsService _settings;
    private readonly ICaseService _cases;

    public CustomerEngagementService(
        ShiftClubDbContext db,
        ICustomerTelegramNotifySink notify,
        IClubSettingsService settings,
        ICaseService cases)
    {
        _db = db;
        _notify = notify;
        _settings = settings;
        _cases = cases;
    }

    public async Task<string?> RecordVisitAsync(
        Customer customer,
        DateOnly todayLocal,
        CancellationToken cancellationToken = default)
    {
        // Визит = старт игрового сеанса с аккаунтом (не логин на Shell).
        var cfg = await _settings.GetEngagementStoredAsync(cancellationToken);
        if (!cfg.Enabled)
            return null;

        var messages = new List<string>();
        var last = customer.VisitStreakLastDate;

        if (last == todayLocal)
            return null;

        customer.VisitCount += 1;

        if (last == todayLocal.AddDays(-1))
            customer.VisitStreakDays = Math.Max(1, customer.VisitStreakDays) + 1;
        else
            customer.VisitStreakDays = 1;

        customer.VisitStreakLastDate = todayLocal;

        var streak = customer.VisitStreakDays;
        var tier = cfg.StreakTiers.FirstOrDefault(t => t.Days == streak);
        if (tier is not null)
        {
            var reward = await GrantStreakRewardAsync(customer, streak, tier, cancellationToken);
            if (reward is not null)
                messages.Add(reward);
        }

        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        if (messages.Count == 0 && streak > 1)
            messages.Add($"Серия посещений: {streak} дн. подряд.");

        var text = messages.Count == 0 ? null : string.Join("\n", messages);
        if (text is not null && customer.TelegramUserId is long tg && customer.AllowNotifications)
        {
            await _notify.PublishAsync(new CustomerTelegramNotice(
                tg,
                $"<b>{TelegramEscape(text)}</b>"), cancellationToken);
        }

        return text;
    }

    public async Task<string?> TryGrantBirthdayGiftAsync(
        Customer customer,
        Guid? zoneId,
        DateOnly todayLocal,
        CancellationToken cancellationToken = default)
    {
        var cfg = await _settings.GetEngagementStoredAsync(cancellationToken);
        if (!cfg.Enabled)
            return null;
        if (customer.BirthDate is null)
            return null;
        if (customer.BirthDate.Value.Month != todayLocal.Month
            || customer.BirthDate.Value.Day != todayLocal.Day)
            return null;
        if (customer.BirthdayGiftYear == todayLocal.Year)
            return null;

        customer.BirthdayGiftYear = todayLocal.Year;

        var bonus = Math.Max(0, cfg.BirthdayBonusAmount);
        var minutes = Math.Max(0, cfg.BirthdayTimeBankMinutes);
        var parts = new List<string>();

        if (bonus > 0)
        {
            customer.BonusBalance += bonus;
            _db.CustomerBalanceTransactions.Add(new CustomerBalanceTransaction
            {
                CustomerId = customer.Id,
                Type = LedgerTransactionType.BonusCredit,
                Direction = LedgerDirection.Credit,
                Amount = bonus,
                BalanceBefore = customer.Balance,
                BalanceAfter = customer.Balance,
                SourceType = "Birthday",
                Comment = $"Подарок на день рождения {todayLocal.Year}"
            });
            parts.Add($"+{bonus:0} ₸ на бонусный счёт");
        }

        var zone = zoneId
                   ?? await _db.Zones.AsNoTracking()
                       .Where(z => z.BranchId == customer.BranchId)
                       .OrderBy(z => z.SortOrder)
                       .Select(z => (Guid?)z.Id)
                       .FirstOrDefaultAsync(cancellationToken);

        if (minutes > 0 && zone is Guid zid)
        {
            var bank = await _db.CustomerZoneTimeBanks
                .FirstOrDefaultAsync(b => b.CustomerId == customer.Id && b.ZoneId == zid, cancellationToken);
            if (bank is null)
            {
                bank = new CustomerZoneTimeBank { CustomerId = customer.Id, ZoneId = zid, Minutes = 0 };
                _db.CustomerZoneTimeBanks.Add(bank);
            }

            var before = bank.Minutes;
            bank.Minutes += minutes;
            customer.TimeBankMinutes += minutes;

            _db.CustomerTimeBankTransactions.Add(new CustomerTimeBankTransaction
            {
                CustomerId = customer.Id,
                ZoneId = zid,
                Reason = TimeBankReason.ManualCredit,
                Direction = LedgerDirection.Credit,
                Minutes = minutes,
                BalanceBefore = before,
                BalanceAfter = bank.Minutes,
                Comment = $"Подарок на день рождения {todayLocal.Year}"
            });
            parts.Add($"+{minutes} мин в банк времени");
        }

        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        if (parts.Count == 0)
            return null;

        var msg = $"С днём рождения! Вам начислено: {string.Join(" и ", parts)}.";
        try
        {
            await _cases.TryGrantBirthdayKeyAsync(customer.Id, todayLocal.Year, cancellationToken);
        }
        catch
        {
            // ignore
        }

        if (customer.TelegramUserId is long tg && customer.AllowNotifications)
        {
            await _notify.PublishAsync(new CustomerTelegramNotice(tg, $"<b>{TelegramEscape(msg)}</b>"), cancellationToken);
        }

        return msg;
    }

    private async Task<string?> GrantStreakRewardAsync(
        Customer customer,
        int streak,
        EngagementStreakTier tier,
        CancellationToken cancellationToken)
    {
        var bonus = Math.Max(0, tier.BonusAmount);
        var barExtra = Math.Max(0, tier.BarRewards);
        if (bonus <= 0 && barExtra <= 0)
            return null;

        var idem = $"streak-{customer.Id}-{customer.VisitStreakLastDate:yyyyMMdd}-{streak}";
        if (await _db.CustomerBalanceTransactions.AnyAsync(t => t.IdempotencyKey == idem, cancellationToken))
            return null;

        if (bonus > 0)
        {
            customer.BonusBalance += bonus;
            _db.CustomerBalanceTransactions.Add(new CustomerBalanceTransaction
            {
                CustomerId = customer.Id,
                Type = LedgerTransactionType.BonusCredit,
                Direction = LedgerDirection.Credit,
                Amount = bonus,
                BalanceBefore = customer.Balance,
                BalanceAfter = customer.Balance,
                SourceType = "Streak",
                IdempotencyKey = idem,
                Comment = $"Бонус · серия {streak} дн. подряд"
            });
        }

        if (barExtra > 0)
            customer.PendingBarRewards += barExtra;

        await Task.CompletedTask;

        var parts = new List<string>();
        if (bonus > 0) parts.Add($"+{bonus:0} ₸ на бонусный счёт");
        if (barExtra > 0) parts.Add(barExtra == 1
            ? "бесплатный напиток (оформите на кассе)"
            : $"{barExtra} бесплатных напитка (оформите на кассе)");

        return $"Серия {streak} дн.: {string.Join(", ", parts)}.";
    }

    public async Task ReconcileVisitStatsFromSessionsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
            return;

        var tzId = await _db.Branches.AsNoTracking()
            .Where(b => b.Id == customer.BranchId)
            .Select(b => b.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        var tz = BranchTimeZone.Resolve(tzId);

        var startedAts = await _db.GamingSessions.AsNoTracking()
            .Where(s => s.CustomerId == customerId
                        && s.Status != SessionStatus.Cancelled
                        && s.Status != SessionStatus.Waiting
                        && s.Status != SessionStatus.Reserved)
            .Select(s => s.StartedAt)
            .ToListAsync(cancellationToken);

        var localDays = startedAts
            .Select(d => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(d, tz).DateTime))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var visitCount = localDays.Count;
        var streakDays = 0;
        DateOnly? streakLast = null;
        if (localDays.Count > 0)
        {
            streakLast = localDays[^1];
            streakDays = 1;
            for (var i = localDays.Count - 2; i >= 0; i--)
            {
                if (localDays[i] == streakLast.Value.AddDays(-streakDays))
                    streakDays++;
                else
                    break;
            }
        }

        if (customer.VisitCount == visitCount
            && customer.VisitStreakDays == streakDays
            && customer.VisitStreakLastDate == streakLast)
            return;

        customer.VisitCount = visitCount;
        customer.VisitStreakDays = streakDays;
        customer.VisitStreakLastDate = streakLast;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string TelegramEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
