using System.Net;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Public;

namespace ShiftClub.Infrastructure.Services;

public sealed class TelegramBroadcastService
{
    private readonly ShiftClubDbContext _db;
    private readonly IClubSettingsService _settings;
    private readonly ICustomerTelegramNotifySink _notify;
    private readonly IBrandingService _branding;

    public TelegramBroadcastService(
        ShiftClubDbContext db,
        IClubSettingsService settings,
        ICustomerTelegramNotifySink notify,
        IBrandingService branding)
    {
        _db = db;
        _settings = settings;
        _notify = notify;
        _branding = branding;
    }

    public async Task<TelegramBroadcastResultDto> BroadcastAsync(
        TelegramBroadcastRequest request,
        CancellationToken cancellationToken = default)
    {
        var text = (request.Message ?? "").Trim();
        if (text.Length < 2)
            throw new InvalidOperationException("Введите текст сообщения.");
        if (text.Length > 3500)
            throw new InvalidOperationException("Сообщение слишком длинное (макс. ~3500 символов).");

        var audience = (request.Audience ?? "customers").Trim().ToLowerInvariant();
        if (audience is not ("customers" or "staff" or "both"))
            throw new InvalidOperationException("Аудитория: customers, staff или both.");

        var cfg = await _settings.GetTelegramBotStoredAsync(cancellationToken);
        if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.BotToken))
            throw new InvalidOperationException("Telegram-бот выключен или без токена.");

        var customerIds = new List<long>();
        var staffIds = new List<long>();

        if (audience is "customers" or "both")
        {
            customerIds = await _db.Customers.AsNoTracking()
                .Where(c => c.IsActive && c.TelegramUserId != null && c.AllowNotifications)
                .Select(c => c.TelegramUserId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        if (audience is "staff" or "both")
        {
            var set = new HashSet<long>();
            foreach (var u in cfg.AllowedUsers)
                set.Add(u.TelegramUserId);
            foreach (var chat in cfg.AlertChatIds)
                set.Add(chat);
            var employeeTg = await _db.Employees.AsNoTracking()
                .Where(e => e.IsActive && e.TelegramUserId != null)
                .Select(e => e.TelegramUserId!.Value)
                .ToListAsync(cancellationToken);
            foreach (var id in employeeTg)
                set.Add(id);
            staffIds = set.ToList();
        }

        var ids = customerIds.Concat(staffIds).Distinct().ToList();

        // Telegram HTML: только ограниченный набор тегов; <br/> не поддерживается — оставляем \n.
        var club = (await _branding.GetAsync(cancellationToken)).TelegramSignature;
        var html = $"<b>{WebUtility.HtmlEncode(club)}</b>\n{WebUtility.HtmlEncode(text)}";

        foreach (var id in ids)
            await _notify.PublishAsync(new CustomerTelegramNotice(id, html), cancellationToken);

        var preview = text.Length <= 120 ? text : text[..117] + "…";
        return new TelegramBroadcastResultDto(ids.Count, customerIds.Count, staffIds.Count, preview);
    }
}
