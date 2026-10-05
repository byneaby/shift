using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared;
using ShiftClub.Shared.Enums;
using System.Net;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ShiftClub.Infrastructure.Telegram;

/// <summary>Клиентские сценарии единого Telegram-бота (кнопки, без команд).</summary>
public sealed class TelegramClientBotService
{
    private readonly ShiftClubDbContext _db;
    private readonly ITelegramAuthService _telegramAuth;
    private readonly TelegramCallbackStore _callbacks;
    private readonly IClubSettingsService _settings;

    public TelegramClientBotService(
        ShiftClubDbContext db,
        ITelegramAuthService telegramAuth,
        TelegramCallbackStore callbacks,
        IClubSettingsService settings)
    {
        _db = db;
        _telegramAuth = telegramAuth;
        _callbacks = callbacks;
        _settings = settings;
    }

    public static bool IsClientButton(string text) =>
        text is TelegramKeyboards.BtnClientBalance
            or TelegramKeyboards.BtnClientSession
            or TelegramKeyboards.BtnClientBar
            or TelegramKeyboards.BtnClientRewards
            or TelegramKeyboards.BtnClientComfort
            or TelegramKeyboards.BtnClientEnterCode
            or TelegramKeyboards.BtnClientScanQr
            or TelegramKeyboards.BtnClientOpenApp
            or TelegramKeyboards.BtnClientRegister
            or TelegramKeyboards.BtnClientMenu
            or TelegramKeyboards.BtnModeClient
            or TelegramKeyboards.BtnModeStaff;

    private async Task<string?> GetWebAppBaseAsync(CancellationToken ct)
    {
        var tg = await _settings.GetTelegramBotStoredAsync(ct);
        if (!string.IsNullOrWhiteSpace(tg.PublicWebAppBaseUrl))
            return tg.PublicWebAppBaseUrl;
        var eng = await _settings.GetEngagementStoredAsync(ct);
        return eng.PublicWebAppBaseUrl;
    }

    private async Task<ReplyKeyboardMarkup> MenuForAsync(long userId, bool isStaff, CancellationToken ct)
    {
        var web = await GetWebAppBaseAsync(ct);
        return TelegramKeyboards.ClientMenu(isStaff, web);
    }

    /// <summary>
    /// Reply-клавиатура обновляет меню ПК; отдельно — inline «Открыть SHIFT» (с initData).
    /// </summary>
    private async Task SendClientShellAsync(
        ITelegramBotClient bot,
        long chatId,
        string text,
        bool isStaff,
        string? web,
        CancellationToken ct)
    {
        await bot.SendMessage(
            chatId,
            text,
            parseMode: ParseMode.Html,
            replyMarkup: TelegramKeyboards.ClientMenu(isStaff, web),
            cancellationToken: ct);

        var open = TelegramKeyboards.OpenAppInline(web);
        if (open is not null)
        {
            await bot.SendMessage(
                chatId,
                "Приложение клуба (баланс, сеанс, бар, профиль):",
                replyMarkup: open,
                cancellationToken: ct);
        }
    }

    public async Task HandleWebAppDataAsync(
        ITelegramBotClient bot,
        long chatId,
        long userId,
        string? displayName,
        string data,
        bool isStaff,
        CancellationToken ct)
    {
        var code = _telegramAuth.TryExtractTicketCode(data);
        if (string.IsNullOrWhiteSpace(code))
        {
            await bot.SendMessage(
                chatId,
                "Не удалось распознать код в QR. Отсканируйте код с экрана ПК или введите его вручную.",
                replyMarkup: await MenuForAsync(userId, isStaff, ct),
                cancellationToken: ct);
            return;
        }

        try
        {
            var msg = await _telegramAuth.CompleteTicketFromTelegramAsync(code, userId, displayName, ct);
            await bot.SendMessage(
                chatId,
                msg,
                parseMode: ParseMode.Html,
                replyMarkup: await MenuForAsync(userId, isStaff, ct),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await bot.SendMessage(
                chatId,
                WebUtility.HtmlEncode(ex.Message),
                parseMode: ParseMode.Html,
                replyMarkup: await MenuForAsync(userId, isStaff, ct),
                cancellationToken: ct);
        }
    }

    public async Task HandleStartPayloadAsync(
        ITelegramBotClient bot,
        long chatId,
        long userId,
        string? displayName,
        string? payload,
        bool isStaff,
        CancellationToken ct)
    {
        payload = payload?.Trim();
        if (!string.IsNullOrWhiteSpace(payload) && payload.StartsWith("L", StringComparison.OrdinalIgnoreCase))
        {
            var code = payload[1..];
            try
            {
                var msg = await _telegramAuth.CompleteTicketFromTelegramAsync(code, userId, displayName, ct);
                await bot.SendMessage(
                    chatId,
                    msg,
                    parseMode: ParseMode.Html,
                    replyMarkup: await MenuForAsync(userId, isStaff, ct),
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await bot.SendMessage(
                    chatId,
                    WebUtility.HtmlEncode(ex.Message),
                    parseMode: ParseMode.Html,
                    replyMarkup: await MenuForAsync(userId, isStaff, ct),
                    cancellationToken: ct);
            }
            return;
        }

        var customer = await _telegramAuth.FindCustomerByTelegramAsync(userId, ct);
        var web = await GetWebAppBaseAsync(ct);
        var hasApp = !string.IsNullOrWhiteSpace(web)
                     && web.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        if (customer is not null)
        {
            await SendClientShellAsync(
                bot,
                chatId,
                $"Здравствуйте, <b>{WebUtility.HtmlEncode(customer.FullName)}</b>.\n" +
                $"Счёт: <b>{customer.Balance:0} ₸</b> · бонусы <b>{customer.BonusBalance:0} ₸</b>.\n\n" +
                (hasApp
                    ? "Нажмите синюю кнопку <b>Открыть SHIFT</b> ниже (или SHIFT в меню чата)."
                    : "Для входа на ПК — QR или код с экрана."),
                isStaff,
                web,
                ct);
            return;
        }

        if (isStaff)
        {
            await bot.SendMessage(
                chatId,
                "SHIFT Club — панель сотрудника (кнопки зала ниже).\nОткройте приложение для обзора зала/бара или переключитесь в «Панель клиента».",
                replyMarkup: TelegramKeyboards.MainMenuWithClientSwitch(),
                cancellationToken: ct);
            if (hasApp)
            {
                await bot.SendMessage(
                    chatId,
                    "Панель сотрудника в приложении:",
                    replyMarkup: TelegramKeyboards.OpenAppInline(web),
                    cancellationToken: ct);
            }
            return;
        }

        var guestText = hasApp
            ? "Добро пожаловать в <b>SHIFT Club</b>.\n\n" +
              "• <b>Новый гость</b> — синяя кнопка «Открыть SHIFT», регистрация по телефону\n" +
              "• <b>Уже есть аккаунт</b> — тот же телефон в приложении\n" +
              "• <b>Вход на ПК</b> — «Сканировать QR» или «Ввести код»"
            : "Добро пожаловать в <b>SHIFT Club</b>.\n\nПодтвердите вход кодом / QR с экрана ПК.";

        await SendClientShellAsync(bot, chatId, guestText, false, web, ct);
    }

    public async Task<bool> TryHandleClientMessageAsync(
        ITelegramBotClient bot,
        Message msg,
        long userId,
        string text,
        bool isStaff,
        CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var displayName = string.Join(' ', new[] { msg.From?.FirstName, msg.From?.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var web = await GetWebAppBaseAsync(ct);

        if (_callbacks.TryGetWizard(userId, out var wizard)
            && wizard.Kind is TelegramWizardKind.AwaitClientPhone
                or TelegramWizardKind.AwaitClientName
                or TelegramWizardKind.AwaitClientCode
                or TelegramWizardKind.AwaitClientBirthDate)
        {
            await HandleWizardAsync(bot, chatId, userId, displayName, text, wizard, isStaff, ct);
            return true;
        }

        if (text == TelegramKeyboards.BtnModeClient)
        {
            var c = await _telegramAuth.FindCustomerByTelegramAsync(userId, ct);
            if (c is null)
            {
                await SendClientShellAsync(
                    bot,
                    chatId,
                    "Клиентский профиль ещё не привязан.\nОткройте SHIFT (синяя кнопка) или войдите по QR/коду с ПК.",
                    isStaff,
                    web,
                    ct);
            }
            else
            {
                await SendClientShellAsync(bot, chatId, $"С возвращением, <b>{WebUtility.HtmlEncode(c.FullName)}</b>.", isStaff, web, ct);
            }
            return true;
        }

        if (text == TelegramKeyboards.BtnModeStaff && isStaff)
        {
            await bot.SendMessage(
                chatId,
                "Панель сотрудника — кнопки зала ниже. Обзор зала/бара также в приложении.",
                replyMarkup: TelegramKeyboards.MainMenu(),
                cancellationToken: ct);
            var open = TelegramKeyboards.OpenAppInline(web);
            if (open is not null)
            {
                await bot.SendMessage(
                    chatId,
                    "Открыть панель:",
                    replyMarkup: open,
                    cancellationToken: ct);
            }
            return true;
        }

        if (text == TelegramKeyboards.BtnClientMenu)
        {
            await SendClientShellAsync(bot, chatId, "Меню:", isStaff, web, ct);
            return true;
        }

        if (text == TelegramKeyboards.BtnClientOpenApp
            || text == TelegramKeyboards.BtnClientRegister
            || text == TelegramKeyboards.BtnClientBalance
            || text == TelegramKeyboards.BtnClientSession
            || text == TelegramKeyboards.BtnClientBar
            || text == TelegramKeyboards.BtnClientRewards
            || text == TelegramKeyboards.BtnClientComfort)
        {
            await SendClientShellAsync(
                bot,
                chatId,
                "Это теперь в приложении. Нажмите синюю кнопку <b>Открыть SHIFT</b> (или SHIFT в меню чата).",
                isStaff,
                web,
                ct);
            return true;
        }

        if (text == TelegramKeyboards.BtnClientScanQr)
        {
            // Если WebApp-кнопка настроена, этот текст обычно не приходит — срабатывает Mini App.
            // Без HTTPS подсказываем альтернативу.
            if (string.IsNullOrWhiteSpace(web) || !web.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                await bot.SendMessage(
                    chatId,
                    "Сканер в боте доступен после настройки HTTPS-адреса Mini App в панели администратора.\n\n" +
                    "Сейчас: откройте камеру телефона, отсканируйте QR на ПК — откроется этот бот, — или нажмите «Ввести код».",
                    replyMarkup: await MenuForAsync(userId, isStaff, ct),
                    cancellationToken: ct);
            }
            else
            {
                await bot.SendMessage(
                    chatId,
                    "Нажмите кнопку «Сканировать QR» ещё раз (откроется встроенный сканер).",
                    replyMarkup: await MenuForAsync(userId, isStaff, ct),
                    cancellationToken: ct);
            }
            return true;
        }

        if (text == TelegramKeyboards.BtnClientEnterCode)
        {
            _callbacks.SetWizard(userId, new TelegramWizardState
            {
                Kind = TelegramWizardKind.AwaitClientCode,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            await bot.SendMessage(
                chatId,
                "Введите <b>код с экрана ПК</b> (6 цифр):",
                parseMode: ParseMode.Html,
                replyMarkup: TelegramKeyboards.CancelOnly(CancelId()),
                cancellationToken: ct);
            return true;
        }

        // Remaining client feature buttons are redirected above; no extra chat handlers.
        return false;
    }

    public async Task<bool> TryHandleClientCallbackAsync(
        ITelegramBotClient bot,
        CallbackQuery cb,
        string actionId,
        CancellationToken ct)
    {
        if (!_callbacks.TryGet(actionId, out var action))
            return false;
        if (action.Kind is not (TelegramPendingKind.ClientComfortToggle
            or TelegramPendingKind.ClientComfortLang
            or TelegramPendingKind.ClientSetBirth))
            return false;

        var userId = cb.From.Id;
        var chatId = cb.Message?.Chat.Id ?? userId;
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.TelegramUserId == userId && c.IsActive, ct);
        if (customer is null)
        {
            await bot.AnswerCallbackQuery(cb.Id, "Аккаунт не найден", showAlert: true, cancellationToken: ct);
            return true;
        }

        switch (action.Kind)
        {
            case TelegramPendingKind.ClientComfortToggle when action.Label == "hide":
                customer.ComfortHideBalance = !customer.ComfortHideBalance;
                break;
            case TelegramPendingKind.ClientComfortToggle when action.Label == "sound":
                customer.ComfortSoundEnabled = !customer.ComfortSoundEnabled;
                break;
            case TelegramPendingKind.ClientComfortLang when action.MessageText is { } lang:
                customer.ComfortLanguage = lang;
                break;
            case TelegramPendingKind.ClientComfortToggle when action.Label == "bright_down":
                customer.ComfortBrightness = Math.Max(40, customer.ComfortBrightness - 10);
                break;
            case TelegramPendingKind.ClientComfortToggle when action.Label == "bright_up":
                customer.ComfortBrightness = Math.Min(100, customer.ComfortBrightness + 10);
                break;
        }

        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await bot.AnswerCallbackQuery(cb.Id, "Сохранено", cancellationToken: ct);
        var profile = await _telegramAuth.FindCustomerByTelegramAsync(userId, ct);
        if (profile is not null)
            await SendComfortAsync(bot, chatId, profile, ct);
        return true;
    }

    private async Task HandleWizardAsync(
        ITelegramBotClient bot,
        long chatId,
        long userId,
        string displayName,
        string text,
        TelegramWizardState wizard,
        bool isStaff,
        CancellationToken ct)
    {
        if (text.Equals("отмена", StringComparison.OrdinalIgnoreCase))
        {
            _callbacks.ClearWizard(userId);
            await bot.SendMessage(chatId, "Отменено.", replyMarkup: await MenuForAsync(userId, isStaff, ct), cancellationToken: ct);
            return;
        }

        switch (wizard.Kind)
        {
            case TelegramWizardKind.AwaitClientCode:
                _callbacks.ClearWizard(userId);
                try
                {
                    var msg = await _telegramAuth.CompleteTicketFromTelegramAsync(text, userId, displayName, ct);
                    await bot.SendMessage(
                        chatId,
                        msg,
                        replyMarkup: await MenuForAsync(userId, isStaff, ct),
                        cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    await bot.SendMessage(chatId, ex.Message, replyMarkup: await MenuForAsync(userId, isStaff, ct), cancellationToken: ct);
                }
                break;

            case TelegramWizardKind.AwaitClientPhone:
            {
                var phone = new string(text.Where(char.IsDigit).ToArray());
                if (phone.Length < 10)
                {
                    await bot.SendMessage(chatId, "Укажите номер телефона — не менее 10 цифр.", cancellationToken: ct);
                    return;
                }

                wizard.Kind = TelegramWizardKind.AwaitClientName;
                wizard.MessageText = phone;
                wizard.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
                _callbacks.SetWizard(userId, wizard);
                await bot.SendMessage(chatId, "Как к вам обращаться? (Имя и фамилия)", cancellationToken: ct);
                break;
            }

            case TelegramWizardKind.AwaitClientName:
            {
                var phone = wizard.MessageText ?? "";
                var parts = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var first = parts.Length > 0 ? parts[0] : "Гость";
                var last = parts.Length > 1 ? parts[1] : "Club";
                try
                {
                    await RegisterCustomerAsync(userId, phone, first, last, ct);
                    _callbacks.ClearWizard(userId);
                    var web = await GetWebAppBaseAsync(ct);
                    await bot.SendMessage(
                        chatId,
                        $"Аккаунт открыт, {WebUtility.HtmlEncode(first)}.\nТелефон: <code>{phone}</code>\nПИН для ПК можно задать при первом входе или на кассе.",
                        parseMode: ParseMode.Html,
                        replyMarkup: TelegramKeyboards.ClientMenu(isStaff, web),
                        cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    _callbacks.ClearWizard(userId);
                    await bot.SendMessage(chatId, ex.Message, replyMarkup: await MenuForAsync(userId, isStaff, ct), cancellationToken: ct);
                }
                break;
            }
        }
    }

    private async Task RegisterCustomerAsync(long telegramUserId, string phone, string first, string last, CancellationToken ct)
    {
        if (await _db.Customers.AnyAsync(c => c.TelegramUserId == telegramUserId, ct))
            throw new InvalidOperationException("Telegram уже привязан.");

        var branchId = await _db.Branches.AsNoTracking().OrderBy(b => b.CreatedAt).Select(b => b.Id).FirstAsync(ct);
        var phoneNorm = PhoneDigits.Normalize(phone);
        var last10 = PhoneDigits.Last10(phoneNorm);
        var existing = await _db.Customers.FirstOrDefaultAsync(
            c => c.BranchId == branchId && (c.Phone == phoneNorm || c.Phone.EndsWith(last10)),
            ct);
        if (existing is not null)
        {
            if (existing.TelegramUserId is not null)
                throw new InvalidOperationException("Этот телефон уже привязан к другому Telegram. Обратитесь на кассу.");
            existing.TelegramUserId = telegramUserId;
            existing.TelegramLinkedAt = DateTimeOffset.UtcNow;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.Phone = phoneNorm;
            if (string.IsNullOrWhiteSpace(existing.FirstName) || existing.FirstName == "Гость")
                existing.FirstName = first.Trim();
            await _db.SaveChangesAsync(ct);
            return;
        }

        var loyalty = await _db.LoyaltyLevels
            .Where(l => l.BranchId == branchId && l.IsActive)
            .OrderBy(l => l.SortOrder)
            .FirstOrDefaultAsync(ct);

        _db.Customers.Add(new Customer
        {
            BranchId = branchId,
            FirstName = first.Trim(),
            LastName = last.Trim(),
            Phone = phoneNorm,
            LoyaltyLevelId = loyalty?.Id,
            TelegramUserId = telegramUserId,
            TelegramLinkedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            AllowNotifications = true
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task SendSessionAsync(
        ITelegramBotClient bot, long chatId, Guid customerId, bool isStaff, string? web, CancellationToken ct)
    {
        var session = await _db.GamingSessions.AsNoTracking()
            .Include(s => s.Computer)
            .Include(s => s.Tariff)
            .Where(s => s.CustomerId == customerId && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (session is null)
        {
            await bot.SendMessage(
                chatId,
                "Активного сеанса на ПК сейчас нет.",
                replyMarkup: TelegramKeyboards.ClientMenu(isStaff, web),
                cancellationToken: ct);
            return;
        }

        var left = session.PlannedEndsAt is { } ends
            ? Math.Max(0, (int)Math.Ceiling((ends - DateTimeOffset.UtcNow).TotalMinutes))
            : session.DurationMinutes;
        var pc = session.Computer.DisplayName ?? session.Computer.WindowsName ?? "ПК";
        await bot.SendMessage(
            chatId,
            $"<b>{WebUtility.HtmlEncode(pc)}</b>\nТариф: {WebUtility.HtmlEncode(session.Tariff?.Name ?? "—")}\nОсталось: <b>{left} мин</b>",
            parseMode: ParseMode.Html,
            replyMarkup: TelegramKeyboards.ClientMenu(isStaff, web),
            cancellationToken: ct);
    }

    private async Task SendBarOrdersAsync(
        ITelegramBotClient bot, long chatId, Guid customerId, bool isStaff, string? web, CancellationToken ct)
    {
        var orders = await _db.BarOrders.AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId
                        && o.Status != BarOrderStatus.Completed
                        && o.Status != BarOrderStatus.Cancelled
                        && o.Status != BarOrderStatus.Rejected)
            .OrderByDescending(o => o.CreatedAt)
            .Take(5)
            .ToListAsync(ct);

        if (orders.Count == 0)
        {
            await bot.SendMessage(
                chatId,
                "Активных заказов бара нет.\nОформить заказ можно в приложении на ПК.",
                replyMarkup: TelegramKeyboards.ClientMenu(isStaff, web),
                cancellationToken: ct);
            return;
        }

        var lines = orders.Select(o =>
        {
            var items = string.Join(", ", o.Items.Select(i => i.ProductName));
            var ready = o.Status == BarOrderStatus.Ready ? " · готов" : $" · {o.Status}";
            return $"• {WebUtility.HtmlEncode(items)}{ready}";
        });
        await bot.SendMessage(
            chatId,
            "<b>Заказы бара</b>\n" + string.Join("\n", lines),
            parseMode: ParseMode.Html,
            replyMarkup: TelegramKeyboards.ClientMenu(isStaff, web),
            cancellationToken: ct);
    }

    private async Task SendComfortAsync(ITelegramBotClient bot, long chatId, CustomerTelegramProfile c, CancellationToken ct)
    {
        var kb = TelegramKeyboards.Rows(
            new[]
            {
                TelegramKeyboards.B(
                    c.ComfortHideBalance ? "Показать баланс" : "Скрыть баланс",
                    Put(new TelegramPendingAction { Kind = TelegramPendingKind.ClientComfortToggle, Label = "hide" })),
                TelegramKeyboards.B(
                    c.ComfortSoundEnabled ? "Звук выкл" : "Звук вкл",
                    Put(new TelegramPendingAction { Kind = TelegramPendingKind.ClientComfortToggle, Label = "sound" }))
            },
            new[]
            {
                TelegramKeyboards.B("RU", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ClientComfortLang, MessageText = "ru" })),
                TelegramKeyboards.B("KK", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ClientComfortLang, MessageText = "kk" })),
                TelegramKeyboards.B("EN", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ClientComfortLang, MessageText = "en" }))
            },
            new[]
            {
                TelegramKeyboards.B("Яркость −", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ClientComfortToggle, Label = "bright_down" })),
                TelegramKeyboards.B("Яркость +", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ClientComfortToggle, Label = "bright_up" }))
            });

        await bot.SendMessage(
            chatId,
            $"<b>Комфорт</b>\nСкрыть баланс: {(c.ComfortHideBalance ? "да" : "нет")}\nЗвук: {(c.ComfortSoundEnabled ? "вкл" : "выкл")}\nЯзык: {c.ComfortLanguage}\nЯркость: {c.ComfortBrightness}%\n\nНастройки применяются на ПК после обновления профиля.",
            parseMode: ParseMode.Html,
            replyMarkup: kb,
            cancellationToken: ct);
    }

    private static string FormatBalance(CustomerTelegramProfile c)
    {
        var bal = c.ComfortHideBalance ? "•••" : $"{c.Balance:0} ₸";
        var bonus = c.ComfortHideBalance ? "•••" : $"{c.BonusBalance:0} ₸";
        return $"<b>Счёт</b>\nОсновной: <b>{bal}</b>\nБонусный: <b>{bonus}</b>\nБанк времени: <b>{c.TimeBankMinutes} мин</b>\nУровень: {WebUtility.HtmlEncode(c.LoyaltyLevelName ?? "—")}";
    }

    private async Task<string> FormatRewardsAsync(CustomerTelegramProfile c, CancellationToken ct)
    {
        var eng = await _settings.GetEngagementStoredAsync(ct);
        var tiers = eng.StreakTiers.OrderBy(t => t.Days)
            .Select(t =>
            {
                var bar = t.BarRewards > 0 ? $" + напиток×{t.BarRewards}" : "";
                return $"{t.Days} дн. → {t.BonusAmount:0} ₸{bar}";
            });
        var bar = c.PendingBarRewards > 0
            ? $"Бесплатный напиток к выдаче: <b>{c.PendingBarRewards}</b> (обратитесь на кассу)"
            : "Бесплатных напитков к выдаче нет.";
        return $"<b>Награды</b>\nСерия посещений: <b>{c.VisitStreakDays}</b> дн.\n{bar}\n\nПороги: {string.Join("; ", tiers)}.";
    }

    private string Put(TelegramPendingAction action) => _callbacks.Put(action);

    private string CancelId() =>
        Put(new TelegramPendingAction { Kind = TelegramPendingKind.Confirm, Label = "cancel" });
}
