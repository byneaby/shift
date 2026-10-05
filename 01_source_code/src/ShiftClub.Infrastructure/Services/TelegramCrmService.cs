using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Services;

public interface ITelegramCrmService
{
    Task<int> ProcessTickAsync(CancellationToken cancellationToken = default);
    Task<TelegramCrmStatsDto> GetStatsAsync(TelegramCrmStoredSettings cfg, CancellationToken cancellationToken = default);
}

/// <summary>
/// Мягкие автосообщения: win-back, неиспользованный ключ CASE, напоминание про акцию.
/// Лимиты по дням / тихим часам / 1 сообщение за раз на клиента.
/// </summary>
public sealed class TelegramCrmService : ITelegramCrmService
{
    private readonly ShiftClubDbContext _db;
    private readonly IClubSettingsService _settings;
    private readonly ICustomerTelegramNotifySink _notify;
    private readonly IBrandingService _branding;
    private readonly ILogger<TelegramCrmService> _logger;

    public TelegramCrmService(
        ShiftClubDbContext db,
        IClubSettingsService settings,
        ICustomerTelegramNotifySink notify,
        IBrandingService branding,
        ILogger<TelegramCrmService> logger)
    {
        _db = db;
        _settings = settings;
        _notify = notify;
        _branding = branding;
        _logger = logger;
    }

    public async Task<int> ProcessTickAsync(CancellationToken cancellationToken = default)
    {
        var bot = await _settings.GetTelegramBotStoredAsync(cancellationToken);
        if (!bot.Enabled || string.IsNullOrWhiteSpace(bot.BotToken))
            return 0;

        var cfg = await _settings.GetTelegramCrmStoredAsync(cancellationToken);
        if (!cfg.Enabled)
            return 0;

        var tz = await ResolveBranchTzAsync(cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        var local = TimeZoneInfo.ConvertTime(nowUtc, tz);
        if (!IsWithinSendWindow(local.Hour, cfg.QuietHourFrom, cfg.QuietHourTo))
            return 0;

        var dayStartLocal = new DateTimeOffset(local.Date, local.Offset);
        var dayStartUtc = dayStartLocal.ToUniversalTime();
        var sentToday = await _db.CustomerTelegramOutreach.AsNoTracking()
            .CountAsync(x => x.SentAt >= dayStartUtc, cancellationToken);
        if (sentToday >= cfg.MaxSendsPerDay)
            return 0;

        var budget = Math.Min(cfg.MaxSendsPerTick, cfg.MaxSendsPerDay - sentToday);
        if (budget <= 0)
            return 0;

        var promo = await _settings.GetMarketingPromoStoredAsync(cancellationToken);
        var promoActive = MarketingPromoMath.IsActive(promo, nowUtc);
        var promoCampaignId = promoActive
            ? $"promo:{(promo.EndsAt?.ToUnixTimeSeconds() ?? 0)}:{Math.Round(promo.Percent)}"
            : null;

        var candidates = await LoadCandidatesAsync(cfg, nowUtc, promoActive, promoCampaignId, cancellationToken);
        if (candidates.Count == 0)
            return 0;

        var club = (await _branding.GetAsync(cancellationToken)).TelegramSignature;

        // Перемешиваем, чтобы не всегда одним и тем же первым в списке
        var rng = Random.Shared;
        for (var i = candidates.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        var sent = 0;
        foreach (var c in candidates)
        {
            if (sent >= budget)
                break;

            var pick = PickKind(c, cfg, promoActive, promoCampaignId, nowUtc);
            if (pick is null)
                continue;

            var html = BuildMessage(pick.Value.Kind, c, promo, local, club);
            if (string.IsNullOrWhiteSpace(html))
                continue;

            await _notify.PublishAsync(new CustomerTelegramNotice(c.TelegramUserId, html), cancellationToken);

            _db.CustomerTelegramOutreach.Add(new CustomerTelegramOutreach
            {
                CustomerId = c.CustomerId,
                Kind = pick.Value.LogKind,
                SentAt = nowUtc,
                Preview = StripPreview(html),
                CreatedAt = nowUtc
            });
            sent++;
        }

        if (sent > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Telegram CRM sent {Count} auto messages", sent);
        }

        return sent;
    }

    public async Task<TelegramCrmStatsDto> GetStatsAsync(
        TelegramCrmStoredSettings cfg,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var tz = await ResolveBranchTzAsync(cancellationToken);
        var local = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var dayStartUtc = new DateTimeOffset(local.Date, local.Offset).ToUniversalTime();

        var linked = await _db.Customers.AsNoTracking()
            .CountAsync(c => c.IsActive && c.TelegramUserId != null && c.AllowNotifications, cancellationToken);
        var sentToday = await _db.CustomerTelegramOutreach.AsNoTracking()
            .CountAsync(x => x.SentAt >= dayStartUtc, cancellationToken);

        var promo = await _settings.GetMarketingPromoStoredAsync(cancellationToken);
        var promoActive = MarketingPromoMath.IsActive(promo, nowUtc);
        var promoCampaignId = promoActive
            ? $"promo:{(promo.EndsAt?.ToUnixTimeSeconds() ?? 0)}:{Math.Round(promo.Percent)}"
            : null;

        var candidates = await LoadCandidatesAsync(cfg, nowUtc, promoActive, promoCampaignId, cancellationToken);
        var winback = 0;
        var unused = 0;
        var promoN = 0;
        foreach (var c in candidates)
        {
            var pick = PickKind(c, cfg, promoActive, promoCampaignId, nowUtc);
            if (pick is null) continue;
            if (pick.Value.Kind == TelegramCrmKinds.Winback) winback++;
            else if (pick.Value.Kind == TelegramCrmKinds.UnusedKey) unused++;
            else if (pick.Value.Kind == TelegramCrmKinds.Promo) promoN++;
        }

        return new TelegramCrmStatsDto(linked, sentToday, winback, unused, promoN);
    }

    private sealed record Candidate(
        Guid CustomerId,
        long TelegramUserId,
        string FirstName,
        int CaseKeysBalance,
        DateTimeOffset? LastSessionAt,
        DateTimeOffset? LastAnyOutreachAt,
        DateTimeOffset? LastWinbackAt,
        DateTimeOffset? LastUnusedKeyAt,
        DateTimeOffset? LastPromoAt,
        string? LastPromoKind);

    private async Task<List<Candidate>> LoadCandidatesAsync(
        TelegramCrmStoredSettings cfg,
        DateTimeOffset nowUtc,
        bool promoActive,
        string? promoCampaignId,
        CancellationToken cancellationToken)
    {
        var baseCustomers = await _db.Customers.AsNoTracking()
            .Where(c => c.IsActive && c.TelegramUserId != null && c.AllowNotifications)
            .Select(c => new
            {
                c.Id,
                Tg = c.TelegramUserId!.Value,
                c.FirstName,
                c.CaseKeysBalance
            })
            .ToListAsync(cancellationToken);

        if (baseCustomers.Count == 0)
            return [];

        var ids = baseCustomers.Select(c => c.Id).ToList();

        var lastSessions = await _db.GamingSessions.AsNoTracking()
            .Where(s => s.CustomerId != null && ids.Contains(s.CustomerId.Value)
                        && s.Status != SessionStatus.Cancelled)
            .GroupBy(s => s.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Last = g.Max(x => x.StartedAt) })
            .ToDictionaryAsync(x => x.CustomerId, x => (DateTimeOffset?)x.Last, cancellationToken);

        var outreachRows = await _db.CustomerTelegramOutreach.AsNoTracking()
            .Where(o => ids.Contains(o.CustomerId))
            .Select(o => new { o.CustomerId, o.Kind, o.SentAt })
            .ToListAsync(cancellationToken);

        var outreachMap = outreachRows
            .GroupBy(x => x.CustomerId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var list = g.ToList();
                    var lastAny = list.Max(x => x.SentAt);
                    var lastWinback = list.Where(x => x.Kind == TelegramCrmKinds.Winback)
                        .Select(x => (DateTimeOffset?)x.SentAt).DefaultIfEmpty(null).Max();
                    var lastKey = list.Where(x => x.Kind == TelegramCrmKinds.UnusedKey)
                        .Select(x => (DateTimeOffset?)x.SentAt).DefaultIfEmpty(null).Max();
                    var promoRows = list.Where(x => x.Kind.StartsWith(TelegramCrmKinds.Promo, StringComparison.Ordinal))
                        .OrderByDescending(x => x.SentAt).ToList();
                    return new
                    {
                        LastAny = (DateTimeOffset?)lastAny,
                        LastWinback = lastWinback,
                        LastKey = lastKey,
                        LastPromo = promoRows.Count > 0 ? promoRows[0].SentAt : (DateTimeOffset?)null,
                        LastPromoKind = promoRows.Count > 0 ? promoRows[0].Kind : null
                    };
                });

        var list = new List<Candidate>();
        foreach (var c in baseCustomers)
        {
            lastSessions.TryGetValue(c.Id, out var lastSession);
            outreachMap.TryGetValue(c.Id, out var o);

            list.Add(new Candidate(
                c.Id,
                c.Tg,
                c.FirstName,
                c.CaseKeysBalance,
                lastSession,
                o?.LastAny,
                o?.LastWinback,
                o?.LastKey,
                o?.LastPromo,
                o?.LastPromoKind));
        }

        return list;
    }

    private static (string Kind, string LogKind)? PickKind(
        Candidate c,
        TelegramCrmStoredSettings cfg,
        bool promoActive,
        string? promoCampaignId,
        DateTimeOffset nowUtc)
    {
        if (c.LastAnyOutreachAt is { } any
            && any > nowUtc.AddDays(-Math.Max(1, cfg.MinDaysBetweenMessages)))
            return null;

        var idleDays = c.LastSessionAt is { } ls
            ? (nowUtc - ls).TotalDays
            : 999;

        // 1) Ключ CASE лежит без дела — самый уместный пуш
        if (cfg.UnusedKeyEnabled
            && c.CaseKeysBalance > 0
            && idleDays >= cfg.UnusedKeyMinIdleDays
            && (c.LastUnusedKeyAt is null || c.LastUnusedKeyAt < nowUtc.AddDays(-cfg.UnusedKeyCooldownDays)))
            return (TelegramCrmKinds.UnusedKey, TelegramCrmKinds.UnusedKey);

        // 2) Win-back: давно не был
        if (cfg.WinbackEnabled
            && idleDays >= cfg.WinbackAfterDays
            && (c.LastWinbackAt is null || c.LastWinbackAt < nowUtc.AddDays(-cfg.WinbackCooldownDays)))
            return (TelegramCrmKinds.Winback, TelegramCrmKinds.Winback);

        // 3) Промо: не чаще cooldown, один раз на кампанию; не слать тем, кто был вчера
        if (cfg.PromoNudgeEnabled
            && promoActive
            && promoCampaignId is not null
            && idleDays >= 2
            && c.LastPromoKind != promoCampaignId
            && (c.LastPromoAt is null || c.LastPromoAt < nowUtc.AddDays(-cfg.PromoNudgeCooldownDays)))
            return (TelegramCrmKinds.Promo, promoCampaignId);

        return null;
    }

    private static string BuildMessage(
        string kind,
        Candidate c,
        MarketingPromoStoredSettings promo,
        DateTimeOffset localNow,
        string clubName)
    {
        var club = WebUtility.HtmlEncode(clubName);
        var name = SanitizeName(c.FirstName);
        var hi = string.IsNullOrEmpty(name) ? "Привет" : $"Привет, {WebUtility.HtmlEncode(name)}";

        return kind switch
        {
            TelegramCrmKinds.UnusedKey => Pick(new[]
            {
                $"{hi}!\n\nУ тебя есть <b>ключ кейса</b> — можно открыть на кассе и забрать приз (время, скидка, баланс или бар).\n\nЗагляни, когда будешь рядом 🎮",
                $"{hi}!\n\nКлюч кейса ждёт на аккаунте. На кассе откроем <b>кейс</b> — рулетка на экране, приз сразу.\n\nБез обязательств, просто не забудь 🔑",
                $"{hi}!\n\nНапоминалка по-дружески: <b>ключ кейса</b> ещё не открыт. Как будешь в {club} — скажи на кассе."
            }, c.CustomerId.GetHashCode(), localNow.Day),

            TelegramCrmKinds.Winback => Pick(new[]
            {
                $"{hi}!\n\nДавно не виделись в <b>{club}</b>. Если захочешь зайти — на кассе подскажут актуальные пакеты и места.\n\nБудем рады 👋",
                $"{hi}!\n\nСоскучились по тебе в зале. Есть свободные ПК и нормальный вайб — заходи, когда удобно.\n\n{club}",
                $"{hi}!\n\nКоротко: мы на месте, ПК живые, бар тоже. Если пропадёт настроение «поиграть» — знаешь, куда 😉"
            }, c.CustomerId.GetHashCode(), localNow.DayOfYear),

            TelegramCrmKinds.Promo => BuildPromo(hi, promo, localNow, club),

            _ => ""
        };
    }

    private static string BuildPromo(
        string hi,
        MarketingPromoStoredSettings promo,
        DateTimeOffset localNow,
        string club)
    {
        var pct = Math.Round(MarketingPromoMath.ClampPercent(promo.Percent));
        var title = string.IsNullOrWhiteSpace(promo.Title) ? promo.Label : promo.Title!;
        var until = promo.EndsAt is { } end
            ? TimeZoneInfo.ConvertTime(end, ResolveTzSafe("Asia/Almaty")).ToString("dd.MM", CultureInfo.InvariantCulture)
            : null;
        var untilBit = until is null ? "" : $"\nДо <b>{WebUtility.HtmlEncode(until)}</b>.";

        return Pick(new[]
        {
            $"{hi}!\n\nСейчас в {club}: <b>{WebUtility.HtmlEncode(title)}</b> — минус {pct}% на пакеты (2+1, 3+2, день, ночь).{untilBit}\n\nПочасовка без этой скидки. За подробностями — на кассе.",
            $"{hi}!\n\nНапоминание без спама: акция <b>−{pct}%</b> на пакеты ещё идёт.{untilBit}\n\nЕсли планировал зайти — сейчас пакеты выгоднее обычного."
        }, (int)pct, localNow.Day);
    }

    private static string Pick(string[] variants, int saltA, int saltB)
    {
        if (variants.Length == 0) return "";
        var i = Math.Abs(HashCode.Combine(saltA, saltB)) % variants.Length;
        return variants[i];
    }

    private static string SanitizeName(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length is < 2 or > 24) return "";
        if (s.Any(ch => ch is '<' or '>' or '&')) return "";
        return s;
    }

    private static string StripPreview(string html)
    {
        var t = html
            .Replace("<b>", "", StringComparison.OrdinalIgnoreCase)
            .Replace("</b>", "", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("\n", " ");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
        return t.Length <= 200 ? t : t[..197] + "…";
    }

    private static bool IsWithinSendWindow(int localHour, int from, int to)
    {
        from = Math.Clamp(from, 0, 23);
        to = Math.Clamp(to, 1, 24);
        if (from == to) return true;
        if (from < to)
            return localHour >= from && localHour < to;
        // через полночь
        return localHour >= from || localHour < to;
    }

    private async Task<TimeZoneInfo> ResolveBranchTzAsync(CancellationToken cancellationToken)
    {
        var id = await _db.Branches.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => b.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        return ResolveTzSafe(id);
    }

    private static TimeZoneInfo ResolveTzSafe(string? id)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(id))
                return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
            /* ignore */
        }

        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Almaty"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Central Asia Standard Time"); }
            catch { return TimeZoneInfo.Utc; }
        }
    }
}
