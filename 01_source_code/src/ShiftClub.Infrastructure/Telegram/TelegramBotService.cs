using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts.Bar;
using ShiftClub.Shared.Contracts.Bookings;
using ShiftClub.Shared.Contracts.Computers;
using ShiftClub.Shared.Contracts.Customers;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.Enums;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ShiftClub.Infrastructure.Telegram;

public sealed class TelegramBotService
{
    private readonly IClubSettingsService _settings;
    private readonly IComputerService _computers;
    private readonly ISessionService _sessions;
    private readonly IBarService _bar;
    private readonly IBookingService _bookings;
    private readonly ICustomerService _customers;
    private readonly ICashService _cash;
    private readonly IReportService _reports;
    private readonly TelegramCallbackStore _callbacks;
    private readonly TelegramClientBotService _clientBot;
    private readonly ILogger<TelegramBotService> _logger;

    public TelegramBotService(
        IClubSettingsService settings,
        IComputerService computers,
        ISessionService sessions,
        IBarService bar,
        IBookingService bookings,
        ICustomerService customers,
        ICashService cash,
        IReportService reports,
        TelegramCallbackStore callbacks,
        TelegramClientBotService clientBot,
        ILogger<TelegramBotService> logger)
    {
        _settings = settings;
        _computers = computers;
        _sessions = sessions;
        _bar = bar;
        _bookings = bookings;
        _customers = customers;
        _cash = cash;
        _reports = reports;
        _callbacks = callbacks;
        _clientBot = clientBot;
        _logger = logger;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.CallbackQuery is { } cb)
            {
                await HandleCallbackAsync(bot, cb, ct);
                return;
            }

            if (update.Message is { } msg)
            {
                if (msg.WebAppData is { Data: { Length: > 0 } webData })
                {
                    var cfg = await _settings.GetTelegramBotStoredAsync(ct);
                    var userId = msg.From?.Id ?? msg.Chat.Id;
                    var displayName = string.Join(' ',
                        new[] { msg.From?.FirstName, msg.From?.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    await _clientBot.HandleWebAppDataAsync(
                        bot, msg.Chat.Id, userId, displayName, webData, IsAllowed(cfg, userId), ct);
                    return;
                }

                if (msg.Text is not null)
                    await HandleMessageAsync(bot, msg, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Telegram update failed");
            try
            {
                var chatId = update.CallbackQuery?.Message?.Chat.Id
                             ?? update.Message?.Chat.Id;
                if (chatId is not null)
                    await SendTextAsync(bot, chatId.Value, "⚠️ Ошибка: " + ex.Message, ct: ct);
            }
            catch { /* ignore */ }
        }
    }

    public async Task BroadcastAlertAsync(ITelegramBotClient bot, StaffAlertMessage alert, TelegramBotStoredSettings cfg, CancellationToken ct)
    {
        var enabled = alert.Kind switch
        {
            "help" => cfg.NotifyHelp,
            "security" => cfg.NotifySecurity,
            "bar" => cfg.NotifyBar,
            "session_warning" => cfg.NotifySessionWarning,
            "booking" => cfg.NotifyBooking,
            _ => true
        };
        if (!enabled)
            return;

        var body = alert.Body;
        if (alert.Kind == "session_warning" && alert.MinutesLeft is int ml && alert.ComputerName is not null)
            body = $"{alert.ComputerName}: осталось {TelegramText.FormatDuration(ml)}";

        var text = $"🔔 <b>{TelegramText.Html(alert.Title)}</b>\n{TelegramText.Html(body)}";
        var markup = BuildAlertKeyboard(alert);

        var chatIds = new HashSet<long>();
        foreach (var u in cfg.AllowedUsers.Where(u => u.ReceiveAlerts))
            chatIds.Add(u.TelegramUserId);
        foreach (var id in cfg.AlertChatIds)
            chatIds.Add(id);

        foreach (var chatId in chatIds)
        {
            try
            {
                await SendHtmlAsync(bot, chatId, text, markup, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send Telegram alert to {ChatId}", chatId);
            }
        }
    }

    private InlineKeyboardMarkup? BuildAlertKeyboard(StaffAlertMessage alert)
    {
        var buttons = new List<InlineKeyboardButton[]>();

        if (alert.ComputerId is Guid pcId)
        {
            var row = new List<InlineKeyboardButton>
            {
                TelegramKeyboards.B("К ПК", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, ComputerId = pcId, Label = "open" })),
                TelegramKeyboards.B("🔒", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, ComputerId = pcId, CommandType = ComputerCommandType.Lock, Label = "lock" })),
                TelegramKeyboards.B("🔓", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, ComputerId = pcId, CommandType = ComputerCommandType.Unlock, Label = "unlock" }))
            };
            buttons.Add(row.ToArray());

            if (alert.Kind is "help" or "security")
            {
                buttons.Add(
                [
                    TelegramKeyboards.B("Сообщение", Put(new TelegramPendingAction { Kind = TelegramPendingKind.MessageTemplate, ComputerId = pcId })),
                    TelegramKeyboards.B("✅ Принял", Put(new TelegramPendingAction { Kind = TelegramPendingKind.Confirm, Label = "ack", ComputerId = pcId }))
                ]);
            }

            if (alert.SessionId is Guid sid && alert.Kind == "session_warning")
            {
                buttons.Add(
                [
                    TelegramKeyboards.B("+15", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ExtendPay, SessionId = sid, ComputerId = pcId, Minutes = 15 })),
                    TelegramKeyboards.B("+30", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ExtendPay, SessionId = sid, ComputerId = pcId, Minutes = 30 })),
                    TelegramKeyboards.B("+1 ч", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ExtendPay, SessionId = sid, ComputerId = pcId, Minutes = 60 }))
                ]);
                buttons.Add(
                [
                    TelegramKeyboards.B("Конец", Put(new TelegramPendingAction
                    {
                        Kind = TelegramPendingKind.Confirm,
                        Label = "end",
                        SessionId = sid,
                        ComputerId = pcId
                    }))
                ]);
            }
        }

        if (alert.OrderId is Guid oid)
        {
            buttons.Add(
            [
                TelegramKeyboards.B("Принять", Put(new TelegramPendingAction { Kind = TelegramPendingKind.OrderStatus, OrderId = oid, OrderStatus = BarOrderStatus.Accepted })),
                TelegramKeyboards.B("Готово", Put(new TelegramPendingAction { Kind = TelegramPendingKind.OrderStatus, OrderId = oid, OrderStatus = BarOrderStatus.Ready })),
                TelegramKeyboards.B("Выдано", Put(new TelegramPendingAction { Kind = TelegramPendingKind.OrderStatus, OrderId = oid, OrderStatus = BarOrderStatus.Completed }))
            ]);
            buttons.Add(
            [
                TelegramKeyboards.B("Отмена", Put(new TelegramPendingAction { Kind = TelegramPendingKind.OrderStatus, OrderId = oid, OrderStatus = BarOrderStatus.Cancelled }))
            ]);
        }

        if (alert.BookingId is Guid bid)
        {
            buttons.Add(
            [
                TelegramKeyboards.B("Пришёл", Put(new TelegramPendingAction { Kind = TelegramPendingKind.BookingAction, BookingId = bid, BookingOp = "arrived" })),
                TelegramKeyboards.B("Отмена", Put(new TelegramPendingAction { Kind = TelegramPendingKind.BookingAction, BookingId = bid, BookingOp = "cancel" }))
            ]);
        }

        return buttons.Count == 0 ? null : new InlineKeyboardMarkup(buttons);
    }

    private async Task HandleMessageAsync(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var cfg = await _settings.GetTelegramBotStoredAsync(ct);
        var userId = msg.From?.Id ?? 0;
        var chatId = msg.Chat.Id;
        var text = msg.Text!.Trim();
        var isStaff = IsAllowed(cfg, userId);
        var displayName = string.Join(' ', new[] { msg.From?.FirstName, msg.From?.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        // /start with optional deep-link payload (QR login)
        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var payload = text.Length > 6 ? text[6..].Trim() : null;
            await _clientBot.HandleStartPayloadAsync(bot, chatId, userId, displayName, payload, isStaff, ct);
            return;
        }

        if (await _clientBot.TryHandleClientMessageAsync(bot, msg, userId, text, isStaff, ct))
            return;

        if (!isStaff)
        {
            await bot.SendMessage(
                chatId,
                "Используйте кнопки ниже: создать аккаунт или ввести код с ПК.",
                replyMarkup: TelegramKeyboards.GuestMenu(false),
                cancellationToken: ct);
            return;
        }

        if (_callbacks.TryGetWizard(userId, out var wizard)
            && wizard.Kind is TelegramWizardKind.AwaitCustomMessage
                or TelegramWizardKind.AwaitCustomerQuery
                or TelegramWizardKind.AwaitDepositAmount)
        {
            await HandleWizardAsync(bot, chatId, userId, text, wizard, cfg, ct);
            return;
        }

        if (text is "/menu" or TelegramKeyboards.BtnHelp)
        {
            await bot.SendMessage(
                chatId,
                "SHIFT Club — пульт смены.\nВыберите раздел кнопками внизу.",
                replyMarkup: TelegramKeyboards.MainMenu(),
                cancellationToken: ct);
            return;
        }

        switch (text)
        {
            case TelegramKeyboards.BtnFloor:
                await SendFloorOverviewAsync(bot, chatId, ct);
                break;
            case TelegramKeyboards.BtnBar:
                await SendBarOrdersAsync(bot, chatId, ct);
                break;
            case TelegramKeyboards.BtnBookings:
                await SendBookingsAsync(bot, chatId, ct);
                break;
            case TelegramKeyboards.BtnCustomers:
                _callbacks.SetWizard(userId, new TelegramWizardState { Kind = TelegramWizardKind.AwaitCustomerQuery });
                await bot.SendMessage(
                    chatId,
                    "Введите имя, телефон или логин клиента.",
                    replyMarkup: TelegramKeyboards.CancelOnly(CancelId()),
                    cancellationToken: ct);
                break;
            case TelegramKeyboards.BtnShift:
                await SendShiftAsync(bot, chatId, userId, cfg, ct);
                break;
            default:
                await bot.SendMessage(
                    chatId,
                    "Используйте кнопки меню внизу.",
                    replyMarkup: TelegramKeyboards.MainMenu(),
                    cancellationToken: ct);
                break;
        }
    }

    private async Task HandleWizardAsync(
        ITelegramBotClient bot,
        long chatId,
        long userId,
        string text,
        TelegramWizardState wizard,
        TelegramBotStoredSettings cfg,
        CancellationToken ct)
    {
        if (text is "/cancel" or "Отмена" or "❌ Отмена")
        {
            _callbacks.ClearWizard(userId);
            if (wizard.ComputerId is Guid backPc)
            {
                await bot.SendMessage(chatId, "❌ Отменено.", cancellationToken: ct);
                await SendPcCardAsync(bot, chatId, backPc, ct);
            }
            else
            {
                await bot.SendMessage(chatId, "❌ Отменено.", replyMarkup: TelegramKeyboards.MainMenu(), cancellationToken: ct);
            }

            return;
        }

        switch (wizard.Kind)
        {
            case TelegramWizardKind.AwaitCustomMessage:
            {
                _callbacks.ClearWizard(userId);
                if (wizard.ComputerId is not Guid pcId)
                {
                    await bot.SendMessage(chatId, "ПК не выбран.", cancellationToken: ct);
                    return;
                }

                var employeeId = ResolveEmployee(cfg, userId);
                await _computers.SendCommandAsync(
                    pcId,
                    new Shared.Contracts.Computers.SendComputerCommandRequest(
                        ComputerCommandType.ShowMessage,
                        System.Text.Json.JsonSerializer.Serialize(new { text }),
                        Guid.NewGuid().ToString("N")),
                    employeeId,
                    ct);
                await bot.SendMessage(chatId, "✅ Сообщение отправлено на ПК.", cancellationToken: ct);
                break;
            }
            case TelegramWizardKind.AwaitCustomerQuery:
            {
                _callbacks.ClearWizard(userId);
                var list = await _customers.SearchAsync(text, null, ct);
                if (list.Count == 0)
                {
                    await bot.SendMessage(chatId, "Клиенты не найдены.", cancellationToken: ct);
                    return;
                }

                var take = list.Take(8).ToList();
                var sb = new StringBuilder("Найдено:\n");
                var buttons = new List<InlineKeyboardButton[]>();
                foreach (var c in take)
                {
                    sb.AppendLine($"• {c.FullName} · {c.Phone} · {c.Balance:0} ₸");
                    buttons.Add(
                    [
                        TelegramKeyboards.B(
                            TelegramText.TruncateButton(c.FullName, 28),
                            Put(new TelegramPendingAction
                            {
                                Kind = TelegramPendingKind.CustomerPick,
                                CustomerId = c.Id,
                                Label = c.FullName
                            }))
                    ]);
                }

                await bot.SendMessage(
                    chatId,
                    sb.ToString(),
                    replyMarkup: TelegramKeyboards.WithCancel(new InlineKeyboardMarkup(buttons), CancelId()),
                    cancellationToken: ct);
                break;
            }
            case TelegramWizardKind.AwaitDepositAmount:
            {
                if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
                    || amount <= 0)
                {
                    await bot.SendMessage(
                        chatId,
                        "Введите сумму числом, например 2000.\nИли «Отмена».",
                        replyMarkup: TelegramKeyboards.CancelOnly(CancelId()),
                        cancellationToken: ct);
                    return;
                }

                _callbacks.ClearWizard(userId);
                await SendDepositPayPickAsync(bot, chatId, wizard.CustomerId!.Value, wizard.CustomerName ?? "Клиент", amount, ct);
                break;
            }
            default:
                _callbacks.ClearWizard(userId);
                break;
        }
    }

    private async Task HandleCallbackAsync(ITelegramBotClient bot, CallbackQuery cb, CancellationToken ct)
    {
        var cfg = await _settings.GetTelegramBotStoredAsync(ct);
        var userId = cb.From.Id;
        var chatId = cb.Message?.Chat.Id ?? userId;
        var data = cb.Data ?? "";

        if (!data.StartsWith("t:", StringComparison.Ordinal) || data.Length < 3)
        {
            await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
            return;
        }

        var token = data[2..];

        if (await _clientBot.TryHandleClientCallbackAsync(bot, cb, token, ct))
            return;

        // Cancel from client wizards
        if (_callbacks.TryGet(token, out var maybeCancel)
            && maybeCancel.Kind == TelegramPendingKind.Confirm
            && maybeCancel.Label is "cancel" or "noop")
        {
            _callbacks.ClearWizard(userId);
            await bot.AnswerCallbackQuery(cb.Id, "Отменено", cancellationToken: ct);
            try
            {
                await bot.SendMessage(chatId, "❌ Отменено.", replyMarkup: TelegramKeyboards.GuestMenu(IsAllowed(cfg, userId)), cancellationToken: ct);
            }
            catch { /* ignore */ }
            return;
        }

        if (!IsAllowed(cfg, userId))
        {
            await bot.AnswerCallbackQuery(cb.Id, "Нет доступа", showAlert: true, cancellationToken: ct);
            return;
        }

        if (!_callbacks.TryGet(token, out var action))
        {
            await bot.AnswerCallbackQuery(cb.Id, "Кнопка устарела — откройте заново", showAlert: true, cancellationToken: ct);
            return;
        }

        var employeeId = ResolveEmployee(cfg, userId);
        string? answer = null;
        string? editSuffix = null;

        try
        {
            switch (action.Kind)
            {
                case TelegramPendingKind.Confirm when action.Label == "ack":
                    answer = "Принято";
                    editSuffix = $"\n\n✅ Принял: {cb.From.FirstName}";
                    break;

                case TelegramPendingKind.Confirm when action.Label == "end" && action.SessionId is Guid endSid:
                    await _sessions.EndAsync(endSid, new Shared.Contracts.Sessions.EndSessionRequest(Reason: "Telegram"), employeeId, ct);
                    answer = "Сеанс завершён";
                    break;

                case TelegramPendingKind.Confirm when (action.Label is "shutdown" or "restart") && action.ComputerId is Guid confPc:
                {
                    var type = action.Label == "shutdown" ? ComputerCommandType.Shutdown : ComputerCommandType.Restart;
                    await _computers.SendCommandAsync(
                        confPc,
                        new SendComputerCommandRequest(type, null, Guid.NewGuid().ToString("N")),
                        employeeId,
                        ct);
                    answer = action.Label == "shutdown" ? "Выключение отправлено" : "Перезагрузка отправлена";
                    break;
                }

                case TelegramPendingKind.PcAction when action.Label == "open" && action.ComputerId is Guid openId:
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await SendPcCardAsync(bot, chatId, openId, ct);
                    return;

                case TelegramPendingKind.PcAction when action.ComputerId is Guid pcId && action.CommandType is ComputerCommandType cmd:
                    if (cmd is ComputerCommandType.Shutdown or ComputerCommandType.Restart)
                    {
                        await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                        await bot.SendMessage(
                            chatId,
                            $"Подтвердите {(cmd == ComputerCommandType.Shutdown ? "выключение" : "перезагрузку")}?",
                            replyMarkup: TelegramKeyboards.Cb(
                                ("Да", Put(new TelegramPendingAction
                                {
                                    Kind = TelegramPendingKind.Confirm,
                                    Label = cmd == ComputerCommandType.Shutdown ? "shutdown" : "restart",
                                    ComputerId = pcId
                                })),
                                ("❌ Отмена", CancelId(pcId))),
                            cancellationToken: ct);
                        return;
                    }

                    await _computers.SendCommandAsync(
                        pcId,
                        new SendComputerCommandRequest(cmd, null, Guid.NewGuid().ToString("N")),
                        employeeId,
                        ct);
                    answer = "Команда отправлена";
                    break;

                case TelegramPendingKind.PcAction when action.Label == "wake" && action.ComputerId is Guid wakeId:
                    await _computers.WakeAsync(wakeId, employeeId, ct);
                    answer = "Wake-on-LAN отправлен";
                    break;

                case TelegramPendingKind.PcAction when action.Label == "pause" && action.SessionId is Guid pauseSid:
                    await _sessions.PauseAsync(pauseSid, employeeId, ct);
                    answer = "Пауза";
                    break;

                case TelegramPendingKind.PcAction when action.Label == "resume" && action.SessionId is Guid resumeSid:
                    await _sessions.ResumeAsync(resumeSid, employeeId, ct);
                    answer = "Продолжено";
                    break;

                case TelegramPendingKind.PcAction when action.Label == "endask" && action.SessionId is Guid endAsk:
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await bot.SendMessage(
                        chatId,
                        "Завершить сеанс?",
                        replyMarkup: TelegramKeyboards.Cb(
                            ("Да, завершить", Put(new TelegramPendingAction
                            {
                                Kind = TelegramPendingKind.Confirm,
                                Label = "end",
                                SessionId = endAsk,
                                ComputerId = action.ComputerId
                            })),
                            ("❌ Отмена", CancelId(action.ComputerId, endAsk))),
                        cancellationToken: ct);
                    return;

                case TelegramPendingKind.PcAction when action.Label == "filter":
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await SendFloorOverviewAsync(bot, chatId, ct, action.MessageText);
                    return;

                case TelegramPendingKind.MessageTemplate when action.ComputerId is Guid msgPc:
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await SendMessageTemplatesAsync(bot, chatId, msgPc, ct);
                    return;

                case TelegramPendingKind.MessageCustom when action.ComputerId is Guid customPc && action.MessageText is { } readyMsg:
                    await _computers.SendCommandAsync(
                        customPc,
                        new Shared.Contracts.Computers.SendComputerCommandRequest(
                            ComputerCommandType.ShowMessage,
                            System.Text.Json.JsonSerializer.Serialize(new { text = readyMsg }),
                            Guid.NewGuid().ToString("N")),
                        employeeId,
                        ct);
                    answer = "Сообщение отправлено";
                    break;

                case TelegramPendingKind.MessageCustom when action.Label == "custom" && action.ComputerId is Guid awaitPc:
                    _callbacks.SetWizard(userId, new TelegramWizardState
                    {
                        Kind = TelegramWizardKind.AwaitCustomMessage,
                        ComputerId = awaitPc
                    });
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await bot.SendMessage(
                        chatId,
                        "Введите текст сообщения для ПК.\nЧтобы выйти — кнопка «❌ Отмена» или напишите «Отмена».",
                        replyMarkup: TelegramKeyboards.CancelOnly(CancelId(awaitPc)),
                        cancellationToken: ct);
                    return;

                case TelegramPendingKind.ExtendPay when action.SessionId is Guid extSid && action.Minutes is int mins
                    && action.PaymentMethod is null:
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await SendExtendPayPickAsync(bot, chatId, extSid, action.ComputerId, mins, ct);
                    return;

                case TelegramPendingKind.ExtendPay when action.SessionId is Guid extSid2 && action.Minutes is int mins2
                    && action.PaymentMethod is PaymentMethod pay:
                {
                    var quote = await _sessions.QuoteExtendAsync(extSid2, mins2, ct);
                    var tendered = pay is PaymentMethod.Cash or PaymentMethod.Card or PaymentMethod.KaspiQr
                        or PaymentMethod.Transfer or PaymentMethod.Mixed
                        ? quote.ExpectedAmount
                        : (decimal?)null;
                    var session = await _sessions.GetByIdAsync(extSid2, ct);
                    await _sessions.ExtendAsync(
                        extSid2,
                        new Shared.Contracts.Sessions.ExtendSessionRequest(
                            mins2,
                            pay,
                            tendered,
                            Guid.NewGuid().ToString("N"),
                            session?.CustomerId),
                        employeeId,
                        ct);
                    answer = $"Продлено +{TelegramText.FormatDuration(mins2)} · {quote.ExpectedAmount:0} ₸";
                    break;
                }

                case TelegramPendingKind.TransferPick when action.SessionId is Guid trSid && action.TargetComputerId is null:
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await SendTransferTargetsAsync(bot, chatId, trSid, action.ComputerId, ct);
                    return;

                case TelegramPendingKind.TransferPick when action.SessionId is Guid trSid2 && action.TargetComputerId is Guid target:
                    await _sessions.TransferAsync(
                        trSid2,
                        new Shared.Contracts.Sessions.TransferSessionRequest(target, Guid.NewGuid().ToString("N")),
                        employeeId,
                        ct);
                    answer = "Сеанс перенесён";
                    break;

                case TelegramPendingKind.OrderStatus when action.OrderId is Guid oid && action.OrderStatus is BarOrderStatus st:
                    await _bar.UpdateOrderStatusAsync(oid, new Shared.Contracts.Bar.UpdateBarOrderStatusRequest(st), employeeId, ct);
                    answer = $"Статус: {st}";
                    break;

                case TelegramPendingKind.BookingAction when action.BookingId is Guid bid:
                    answer = action.BookingOp switch
                    {
                        "confirm" => (await _bookings.ConfirmAsync(bid, employeeId, ct)).Status.ToString(),
                        "arrived" => (await _bookings.MarkArrivedAsync(bid, employeeId, ct)).Status.ToString(),
                        "cancel" => (await _bookings.CancelAsync(bid, new Shared.Contracts.Bookings.CancelBookingRequest("Telegram"), employeeId, ct)).Status.ToString(),
                        _ => "OK"
                    };
                    answer = "Бронь: " + answer;
                    break;

                case TelegramPendingKind.CustomerPick when action.CustomerId is Guid cid:
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await SendCustomerCardAsync(bot, chatId, cid, ct);
                    return;

                case TelegramPendingKind.DepositAmount when action.CustomerId is Guid depCid && action.Amount is null:
                    _callbacks.SetWizard(userId, new TelegramWizardState
                    {
                        Kind = TelegramWizardKind.AwaitDepositAmount,
                        CustomerId = depCid,
                        CustomerName = action.Label
                    });
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await bot.SendMessage(
                        chatId,
                        "Введите сумму пополнения.\nЧтобы выйти — «Отмена» или кнопка ниже.",
                        replyMarkup: TelegramKeyboards.CancelOnly(CancelId()),
                        cancellationToken: ct);
                    return;

                case TelegramPendingKind.DepositAmount when action.CustomerId is Guid depCid2 && action.Amount is decimal preset:
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await SendDepositPayPickAsync(bot, chatId, depCid2, action.Label ?? "Клиент", preset, ct);
                    return;

                case TelegramPendingKind.DepositPay when action.CustomerId is Guid payCid
                    && action.Amount is decimal payAmt && action.PaymentMethod is PaymentMethod depPay:
                    var depositResult = await _customers.DepositAsync(
                        payCid,
                        new DepositCustomerRequest(
                            payAmt,
                            depPay,
                            "Telegram",
                            Guid.NewGuid().ToString("N")),
                        employeeId,
                        ct);
                    answer = depositResult.Quote.TotalBonusAmount > 0
                        ? $"Пополнено {payAmt:0} ₸ · бонус +{depositResult.Quote.TotalBonusAmount:0} ₸"
                        : $"Пополнено {payAmt:0} ₸";
                    break;

                case TelegramPendingKind.StartSession when action.Label == "begin" && action.ComputerId is Guid startPc:
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await SendStartTariffPickAsync(bot, chatId, startPc, ct);
                    return;

                case TelegramPendingKind.StartSession when action.Label == "tariff"
                    && action.ComputerId is Guid stPc && action.TariffId is Guid tariffId:
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await SendStartDurationOrPayAsync(bot, chatId, stPc, tariffId, ct);
                    return;

                case TelegramPendingKind.StartSession when action.Label == "duration"
                    && action.ComputerId is Guid dPc && action.TariffId is Guid dTariff && action.Minutes is int dMin:
                    await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    await SendStartPayPickAsync(bot, chatId, dPc, dTariff, dMin, ct);
                    return;

                case TelegramPendingKind.StartSession when action.Label == "pay"
                    && action.ComputerId is Guid pPc
                    && action.TariffId is Guid pTariff
                    && action.Minutes is int pMin
                    && action.PaymentMethod is PaymentMethod pPay:
                {
                    await _sessions.StartGuestSessionAsync(
                        new StartGuestSessionRequest(
                            pPc,
                            pTariff,
                            pMin,
                            pPay,
                            GuestName: null,
                            CustomerId: null,
                            IdempotencyKey: Guid.NewGuid().ToString("N"),
                            UseTimeBank: false),
                        employeeId,
                        ct);
                    answer = $"Сеанс запущен · {TelegramText.FormatDuration(pMin)}";
                    await bot.AnswerCallbackQuery(cb.Id, answer, cancellationToken: ct);
                    await SendTextAsync(bot, chatId, "✅ " + answer, ct: ct);
                    await SendPcCardAsync(bot, chatId, pPc, ct);
                    return;
                }

                case TelegramPendingKind.Confirm when action.Label is "noop" or "cancel":
                {
                    _callbacks.ClearWizard(userId);
                    await bot.AnswerCallbackQuery(cb.Id, "Отменено", cancellationToken: ct);
                    if (action.ComputerId is Guid backPc)
                    {
                        await SendTextAsync(bot, chatId, "❌ Отменено.", ct: ct);
                        await SendPcCardAsync(bot, chatId, backPc, ct);
                    }
                    else
                    {
                        await SendTextAsync(bot, chatId, "❌ Отменено.", TelegramKeyboards.MainMenu(), ct);
                    }

                    return;
                }

                default:
                    answer = "Неизвестное действие";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram callback action failed");
            await bot.AnswerCallbackQuery(cb.Id, TelegramText.TruncateButton(ex.Message, 180), showAlert: true, cancellationToken: ct);
            await SendTextAsync(bot, chatId, "⚠️ " + ex.Message, ct: ct);
            return;
        }

        await bot.AnswerCallbackQuery(cb.Id, answer, cancellationToken: ct);

        if (editSuffix is not null && cb.Message is not null)
        {
            try
            {
                await bot.EditMessageText(
                    chatId,
                    cb.Message.MessageId,
                    (cb.Message.Text ?? "") + editSuffix,
                    replyMarkup: cb.Message.ReplyMarkup,
                    cancellationToken: ct);
            }
            catch { /* ignore edit failures */ }
        }
        else if (!string.IsNullOrEmpty(answer) && answer is not "Принято" and not "Отмена")
        {
            await SendTextAsync(bot, chatId, "✅ " + answer, ct: ct);
        }
    }

    private string CancelId(Guid? computerId = null, Guid? sessionId = null) =>
        Put(new TelegramPendingAction
        {
            Kind = TelegramPendingKind.Confirm,
            Label = "cancel",
            ComputerId = computerId,
            SessionId = sessionId
        });

    private async Task SendFloorOverviewAsync(ITelegramBotClient bot, long chatId, CancellationToken ct, string? filter = null)
    {
        var pcs = await _computers.GetComputersAsync(null, ct);
        var approved = pcs.Where(c => c.IsApproved).ToList();
        filter = filter?.ToLowerInvariant();

        var filtered = filter switch
        {
            "free" => approved.Where(c => c.Occupancy is "Free").ToList(),
            "busy" => approved.Where(c => c.Occupancy is "Busy" or "Paused" || c.CurrentSessionId is not null).ToList(),
            "offline" => approved.Where(c => c.Occupancy is "Offline" || c.Status == ComputerStatus.Offline).ToList(),
            "maint" => approved.Where(c => c.IsMaintenance || c.Occupancy is "Maintenance").ToList(),
            _ => approved
        };

        var free = approved.Count(c => c.Occupancy is "Free");
        var busy = approved.Count(c => c.CurrentSessionId is not null || c.Occupancy is "Busy" or "Paused");
        var offline = approved.Count(c => c.Occupancy is "Offline" || c.Status == ComputerStatus.Offline);
        var maint = approved.Count(c => c.IsMaintenance);

        var header =
            $"🖥 <b>Зал</b>\n" +
            $"Свободно <b>{free}</b> · Занято <b>{busy}</b> · Офлайн <b>{offline}</b> · Обслуж. <b>{maint}</b>\n\n";

        var lines = filtered
            .OrderBy(c => c.DisplayName)
            .Select(pc =>
            {
                var name = pc.DisplayName ?? pc.WindowsName;
                var status = TelegramText.OccupancyLabel(pc.Occupancy, pc.OccupancyDetail);
                return $"• <b>{TelegramText.Html(name)}</b> — {TelegramText.Html(status)}"
                       + (pc.RemainingSeconds is int s ? $" · <b>{TelegramText.FormatRemaining(s)}</b>" : "")
                       + (string.IsNullOrWhiteSpace(pc.SessionGuestName) ? "" : $" · {TelegramText.Html(pc.SessionGuestName)}");
            });

        var body = string.Join("\n", lines);
        if (string.IsNullOrWhiteSpace(body))
            body = "Нет ПК по фильтру.";

        var filters = TelegramKeyboards.Cb(
            ("Все", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, Label = "filter", MessageText = "all" })),
            ("Свободные", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, Label = "filter", MessageText = "free" })),
            ("Занятые", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, Label = "filter", MessageText = "busy" })),
            ("Офлайн", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, Label = "filter", MessageText = "offline" })),
            ("Обслуж.", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, Label = "filter", MessageText = "maint" })));

        await SendHtmlAsync(bot, chatId, header + body, filters, ct);

        // Все ПК кнопками — пачками по 24 (8 рядов × 3), без «ещё…»
        var ordered = filtered.OrderBy(c => c.DisplayName).ToList();
        for (var offset = 0; offset < ordered.Count; offset += 24)
        {
            var batch = ordered.Skip(offset).Take(24).ToList();
            var pcButtons = new List<InlineKeyboardButton[]>();
            var row = new List<InlineKeyboardButton>();
            foreach (var pc in batch)
            {
                var name = TelegramText.TruncateButton(pc.DisplayName ?? pc.WindowsName, 16);
                row.Add(TelegramKeyboards.B(name, Put(new TelegramPendingAction
                {
                    Kind = TelegramPendingKind.PcAction,
                    Label = "open",
                    ComputerId = pc.Id
                })));
                if (row.Count >= 3)
                {
                    pcButtons.Add(row.ToArray());
                    row = [];
                }
            }

            if (row.Count > 0)
                pcButtons.Add(row.ToArray());

            var caption = ordered.Count <= 24
                ? "Выберите ПК:"
                : $"Выберите ПК ({offset + 1}–{offset + batch.Count} из {ordered.Count}):";
            await bot.SendMessage(chatId, caption, replyMarkup: new InlineKeyboardMarkup(pcButtons), cancellationToken: ct);
        }
    }

    private async Task SendPcCardAsync(ITelegramBotClient bot, long chatId, Guid computerId, CancellationToken ct)
    {
        var pc = await _computers.GetByIdAsync(computerId, ct)
                 ?? throw new InvalidOperationException("ПК не найден");
        var name = pc.DisplayName ?? pc.WindowsName;
        var status = TelegramText.OccupancyLabel(pc.Occupancy, pc.OccupancyDetail);
        var sb = new StringBuilder();
        sb.AppendLine($"🖥 <b>{TelegramText.Html(name)}</b>");
        sb.AppendLine($"Статус: {TelegramText.Html(status)}");
        if (pc.ZoneName is not null)
            sb.AppendLine($"Зона: {TelegramText.Html(pc.ZoneName)}");
        if (pc.RemainingSeconds is int rem)
            sb.AppendLine($"Осталось: <b>{TelegramText.FormatRemaining(rem)}</b>");
        if (pc.SessionTotalPrice is decimal price && pc.CurrentSessionId is not null)
            sb.AppendLine($"Сумма сеанса: {price:0} ₸");
        if (!string.IsNullOrWhiteSpace(pc.SessionGuestName))
            sb.AppendLine($"Гость: {TelegramText.Html(pc.SessionGuestName)}");
        if (pc.IpAddress is not null)
            sb.AppendLine($"IP: {TelegramText.Html(pc.IpAddress)}");

        var sessionId = pc.CurrentSessionId;
        var rows = new List<InlineKeyboardButton[]>();
        var canStart = sessionId is null
                       && !pc.IsMaintenance
                       && pc.Occupancy is "Free" or "Offline"
                       && pc.Status is not ComputerStatus.Reserved and not ComputerStatus.Updating and not ComputerStatus.Error;

        if (sessionId is Guid sid)
        {
            var pauseLabel = pc.SessionStatus == SessionStatus.Paused ? "Продолжить" : "Пауза";
            var pauseAction = pc.SessionStatus == SessionStatus.Paused ? "resume" : "pause";
            rows.Add(
            [
                TelegramKeyboards.B(pauseLabel, Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, Label = pauseAction, SessionId = sid, ComputerId = computerId })),
                TelegramKeyboards.B("+Время", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ExtendPay, SessionId = sid, ComputerId = computerId, Minutes = 30 })),
                TelegramKeyboards.B("Перенос", Put(new TelegramPendingAction { Kind = TelegramPendingKind.TransferPick, SessionId = sid, ComputerId = computerId }))
            ]);
            rows.Add(
            [
                TelegramKeyboards.B("Конец сеанса", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, Label = "endask", SessionId = sid, ComputerId = computerId }))
            ]);
            rows.Add(
            [
                TelegramKeyboards.B("+15", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ExtendPay, SessionId = sid, ComputerId = computerId, Minutes = 15 })),
                TelegramKeyboards.B("+30", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ExtendPay, SessionId = sid, ComputerId = computerId, Minutes = 30 })),
                TelegramKeyboards.B("+60", Put(new TelegramPendingAction { Kind = TelegramPendingKind.ExtendPay, SessionId = sid, ComputerId = computerId, Minutes = 60 }))
            ]);
        }
        else if (canStart)
        {
            rows.Add(
            [
                TelegramKeyboards.B("▶️ Старт сеанса", Put(new TelegramPendingAction
                {
                    Kind = TelegramPendingKind.StartSession,
                    Label = "begin",
                    ComputerId = computerId
                }))
            ]);
        }

        rows.Add(
        [
            TelegramKeyboards.B("🔒 Блок", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, ComputerId = computerId, CommandType = ComputerCommandType.Lock })),
            TelegramKeyboards.B("🔓", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, ComputerId = computerId, CommandType = ComputerCommandType.Unlock })),
            TelegramKeyboards.B("Wake", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, Label = "wake", ComputerId = computerId }))
        ]);
        rows.Add(
        [
            TelegramKeyboards.B("Сообщение", Put(new TelegramPendingAction { Kind = TelegramPendingKind.MessageTemplate, ComputerId = computerId })),
            TelegramKeyboards.B("Рестарт", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, ComputerId = computerId, CommandType = ComputerCommandType.Restart })),
            TelegramKeyboards.B("Выкл", Put(new TelegramPendingAction { Kind = TelegramPendingKind.PcAction, ComputerId = computerId, CommandType = ComputerCommandType.Shutdown }))
        ]);

        await SendHtmlAsync(bot, chatId, sb.ToString(), new InlineKeyboardMarkup(rows), ct);
    }

    private async Task SendStartTariffPickAsync(ITelegramBotClient bot, long chatId, Guid computerId, CancellationToken ct)
    {
        var pc = await _computers.GetByIdAsync(computerId, ct)
                 ?? throw new InvalidOperationException("ПК не найден");
        if (pc.CurrentSessionId is not null)
            throw new InvalidOperationException("На ПК уже есть сеанс.");

        var tariffs = await _sessions.GetTariffsAsync(
            pc.BranchId,
            includeInactive: false,
            onlyAvailableNow: true,
            zoneId: pc.ZoneId,
            ct);

        var list = tariffs
            .Where(t => t.IsActive && t.IsAvailableNow)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .Take(20)
            .ToList();

        if (list.Count == 0)
        {
            await SendTextAsync(bot, chatId, "Нет доступных тарифов для этой зоны прямо сейчас.", ct: ct);
            return;
        }

        var buttons = list.Select(t =>
        {
            var preview = t.Kind == TariffKind.Package && t.FixedDurationMinutes is int fm
                ? $"{t.Name} · {TelegramText.FormatDuration(fm)}"
                : t.Kind == TariffKind.Hourly
                    ? $"{t.Name} · {t.PricePerHour:0} ₸/ч"
                    : t.Name;
            return new[]
            {
                TelegramKeyboards.B(
                    TelegramText.TruncateButton(preview, 40),
                    Put(new TelegramPendingAction
                    {
                        Kind = TelegramPendingKind.StartSession,
                        Label = "tariff",
                        ComputerId = computerId,
                        TariffId = t.Id
                    }))
            };
        }).ToArray();

        await SendHtmlAsync(
            bot,
            chatId,
            $"▶️ Старт на <b>{TelegramText.Html(pc.DisplayName ?? pc.WindowsName)}</b>\nВыберите тариф:",
            TelegramKeyboards.WithCancel(new InlineKeyboardMarkup(buttons), CancelId(computerId)),
            ct);
    }

    private async Task SendStartDurationOrPayAsync(
        ITelegramBotClient bot,
        long chatId,
        Guid computerId,
        Guid tariffId,
        CancellationToken ct)
    {
        var tariffs = await _sessions.GetTariffsAsync(null, false, false, null, ct);
        var tariff = tariffs.FirstOrDefault(t => t.Id == tariffId)
                     ?? throw new InvalidOperationException("Тариф не найден");

        // TimeWindow — длительность из окна, сразу к оплате
        if (tariff.DurationMode == TariffDurationMode.TimeWindow)
        {
            var mins = tariff.RemainingMinutesInWindow
                       ?? tariff.FixedDurationMinutes
                       ?? 60;
            await SendStartPayPickAsync(bot, chatId, computerId, tariffId, mins, ct);
            return;
        }

        // Пакет с фикс. длительностью
        if (tariff.Kind == TariffKind.Package && tariff.FixedDurationMinutes is int fixedMin and > 0)
        {
            await SendStartPayPickAsync(bot, chatId, computerId, tariffId, fixedMin, ct);
            return;
        }

        var presets = new[] { 60, 120, 180, 240 };
        var buttons = new List<InlineKeyboardButton[]>();
        var row = new List<InlineKeyboardButton>();
        foreach (var m in presets)
        {
            row.Add(TelegramKeyboards.B(
                TelegramText.FormatDuration(m),
                Put(new TelegramPendingAction
                {
                    Kind = TelegramPendingKind.StartSession,
                    Label = "duration",
                    ComputerId = computerId,
                    TariffId = tariffId,
                    Minutes = m
                })));
            if (row.Count >= 2)
            {
                buttons.Add(row.ToArray());
                row = [];
            }
        }

        if (row.Count > 0)
            buttons.Add(row.ToArray());

        // Пакеты 2+1 / 3+2 той же зоны — если есть
        foreach (var pack in tariffs.Where(t =>
                     t.IsActive
                     && t.Kind == TariffKind.Package
                     && t.FixedDurationMinutes is > 0
                     && (t.ZoneId is null || t.ZoneId == tariff.ZoneId)
                     && (t.Code.Contains("2P1", StringComparison.OrdinalIgnoreCase)
                         || t.Code.Contains("3P2", StringComparison.OrdinalIgnoreCase)
                         || t.Name is "2+1" or "3+2")).Take(4))
        {
            buttons.Add(
            [
                TelegramKeyboards.B(
                    TelegramText.TruncateButton($"{pack.Name} · {TelegramText.FormatDuration(pack.FixedDurationMinutes!.Value)} · {pack.FixedPrice:0} ₸", 42),
                    Put(new TelegramPendingAction
                    {
                        Kind = TelegramPendingKind.StartSession,
                        Label = "tariff",
                        ComputerId = computerId,
                        TariffId = pack.Id
                    }))
            ]);
        }

        await SendHtmlAsync(
            bot,
            chatId,
            $"Тариф: <b>{TelegramText.Html(tariff.Name)}</b>\nВыберите длительность:",
            TelegramKeyboards.WithCancel(new InlineKeyboardMarkup(buttons), CancelId(computerId)),
            ct);
    }

    private async Task SendStartPayPickAsync(
        ITelegramBotClient bot,
        long chatId,
        Guid computerId,
        Guid tariffId,
        int minutes,
        CancellationToken ct)
    {
        var tariffs = await _sessions.GetTariffsAsync(null, false, false, null, ct);
        var tariff = tariffs.FirstOrDefault(t => t.Id == tariffId);
        var priceHint = "";
        if (tariff is not null)
        {
            decimal? price = tariff.Kind == TariffKind.Package && tariff.FixedPrice is > 0
                ? tariff.FixedPrice
                : tariff.PricePerHour > 0
                    ? Math.Round(tariff.PricePerHour * minutes / 60m, 0, MidpointRounding.AwayFromZero)
                    : null;
            if (price is > 0)
                priceHint = $"\nОриентир: <b>{price:0} ₸</b>";
            if (!string.IsNullOrWhiteSpace(tariff.SalePreview))
                priceHint += $"\n{TelegramText.Html(tariff.SalePreview)}";
        }

        var text =
            $"Длительность: <b>{TelegramText.FormatDuration(minutes)}</b>{priceHint}\n" +
            "Способ оплаты (нужна открытая кассовая смена у привязанного сотрудника):";

        var markup = TelegramKeyboards.WithCancel(
            TelegramKeyboards.Cb(
                ("Нал", Put(StartPay(computerId, tariffId, minutes, PaymentMethod.Cash))),
                ("Kaspi", Put(StartPay(computerId, tariffId, minutes, PaymentMethod.KaspiQr))),
                ("Баланс", Put(StartPay(computerId, tariffId, minutes, PaymentMethod.Balance))),
                ("Бесплатно", Put(StartPay(computerId, tariffId, minutes, PaymentMethod.Free)))),
            CancelId(computerId));

        await SendHtmlAsync(bot, chatId, text, markup, ct);

        static TelegramPendingAction StartPay(Guid pc, Guid tar, int m, PaymentMethod p) => new()
        {
            Kind = TelegramPendingKind.StartSession,
            Label = "pay",
            ComputerId = pc,
            TariffId = tar,
            Minutes = m,
            PaymentMethod = p
        };
    }

    private async Task SendMessageTemplatesAsync(ITelegramBotClient bot, long chatId, Guid computerId, CancellationToken ct)
    {
        var templates = new (string label, string text)[]
        {
            ("Скоро конец", "Внимание: сеанс скоро закончится. Продлите у администратора."),
            ("Админ идёт", "Администратор уже идёт к вам."),
            ("Заказ готов", "Ваш заказ из бара готов."),
            ("Своё…", "")
        };

        var buttons = new List<InlineKeyboardButton[]>();
        foreach (var (label, text) in templates)
        {
            if (string.IsNullOrEmpty(text))
            {
                buttons.Add(
                [
                    TelegramKeyboards.B(label, Put(new TelegramPendingAction
                    {
                        Kind = TelegramPendingKind.MessageCustom,
                        Label = "custom",
                        ComputerId = computerId
                    }))
                ]);
            }
            else
            {
                buttons.Add(
                [
                    TelegramKeyboards.B(label, Put(new TelegramPendingAction
                    {
                        Kind = TelegramPendingKind.MessageCustom,
                        ComputerId = computerId,
                        MessageText = text
                    }))
                ]);
            }
        }

        await bot.SendMessage(
            chatId,
            "Сообщение на ПК:",
            replyMarkup: TelegramKeyboards.WithCancel(new InlineKeyboardMarkup(buttons), CancelId(computerId)),
            cancellationToken: ct);
    }

    private async Task SendExtendPayPickAsync(
        ITelegramBotClient bot,
        long chatId,
        Guid sessionId,
        Guid? computerId,
        int minutes,
        CancellationToken ct)
    {
        ExtendSessionQuoteDto? quote = null;
        try
        {
            quote = await _sessions.QuoteExtendAsync(sessionId, minutes, ct);
        }
        catch (Exception ex)
        {
            await bot.SendMessage(chatId, "Нельзя продлить: " + ex.Message, cancellationToken: ct);
            return;
        }

        var text = $"Продление <b>+{TelegramText.FormatDuration(minutes)}</b>\nК оплате: <b>{quote.ExpectedAmount:0.##} ₸</b>\nТариф: {TelegramText.Html(quote.TariffName)}\nВыберите оплату:";
        var markup = TelegramKeyboards.WithCancel(
            TelegramKeyboards.Cb(
                ("Нал", Put(ExtPay(sessionId, computerId, minutes, PaymentMethod.Cash))),
                ("Kaspi", Put(ExtPay(sessionId, computerId, minutes, PaymentMethod.KaspiQr))),
                ("Баланс", Put(ExtPay(sessionId, computerId, minutes, PaymentMethod.Balance)))),
            CancelId(computerId, sessionId));

        await SendHtmlAsync(bot, chatId, text, markup, ct);

        static TelegramPendingAction ExtPay(Guid sid, Guid? pc, int m, PaymentMethod p) => new()
        {
            Kind = TelegramPendingKind.ExtendPay,
            SessionId = sid,
            ComputerId = pc,
            Minutes = m,
            PaymentMethod = p
        };
    }

    private async Task SendTransferTargetsAsync(
        ITelegramBotClient bot,
        long chatId,
        Guid sessionId,
        Guid? sourceComputerId,
        CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(sessionId, ct)
                      ?? throw new InvalidOperationException("Сеанс не найден");
        var pcs = await _computers.GetComputersAsync(null, ct);
        var free = pcs
            .Where(c => c.IsApproved
                        && c.Id != sourceComputerId
                        && c.ZoneId == session.ZoneId
                        && c.CurrentSessionId is null
                        && !c.IsMaintenance
                        && c.Occupancy is "Free" or "Offline")
            .OrderBy(c => c.DisplayName)
            .Take(20)
            .ToList();

        if (free.Count == 0)
        {
            await bot.SendMessage(chatId, "Нет свободных ПК в той же зоне (включая выключенные).", cancellationToken: ct);
            return;
        }

        var buttons = free.Select(pc =>
        {
            var offline = string.Equals(pc.Occupancy, "Offline", StringComparison.OrdinalIgnoreCase)
                          || (pc.OccupancyDetail?.Contains("офлайн", StringComparison.OrdinalIgnoreCase) ?? false)
                          || (pc.OccupancyDetail?.Contains("offline", StringComparison.OrdinalIgnoreCase) ?? false);
            var label = TelegramText.TruncateButton(
                (pc.DisplayName ?? pc.WindowsName) + (offline ? " · выкл" : ""),
                28);
            return new[]
            {
                TelegramKeyboards.B(
                    label,
                    Put(new TelegramPendingAction
                    {
                        Kind = TelegramPendingKind.TransferPick,
                        SessionId = sessionId,
                        ComputerId = sourceComputerId,
                        TargetComputerId = pc.Id
                    }))
            };
        }).ToArray();

        await bot.SendMessage(
            chatId,
            "Куда перенести (та же зона):",
            replyMarkup: TelegramKeyboards.WithCancel(new InlineKeyboardMarkup(buttons), CancelId(sourceComputerId, sessionId)),
            cancellationToken: ct);
    }

    private async Task SendBarOrdersAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var orders = await _bar.GetOpenOrdersAsync(null, ct);
        if (orders.Count == 0)
        {
            await bot.SendMessage(chatId, "🍽 Открытых заказов нет.", cancellationToken: ct);
            return;
        }

        foreach (var o in orders.Take(15))
        {
            var items = string.Join(", ", o.Items.Select(i => $"{i.ProductName}×{i.Quantity:0}"));
            var text = $"🍽 <b>{TelegramText.Html(o.Number)}</b> · {o.Status}\n{(o.ComputerName is null ? "" : TelegramText.Html(o.ComputerName) + "\n")}{TelegramText.Html(items)}\n<b>{o.Total:0} ₸</b>";
            var markup = TelegramKeyboards.Cb(
                ("Принять", Put(new TelegramPendingAction { Kind = TelegramPendingKind.OrderStatus, OrderId = o.Id, OrderStatus = BarOrderStatus.Accepted })),
                ("Готово", Put(new TelegramPendingAction { Kind = TelegramPendingKind.OrderStatus, OrderId = o.Id, OrderStatus = BarOrderStatus.Ready })),
                ("Выдано", Put(new TelegramPendingAction { Kind = TelegramPendingKind.OrderStatus, OrderId = o.Id, OrderStatus = BarOrderStatus.Completed })),
                ("Отмена", Put(new TelegramPendingAction { Kind = TelegramPendingKind.OrderStatus, OrderId = o.Id, OrderStatus = BarOrderStatus.Cancelled })));
            await SendHtmlAsync(bot, chatId, text, markup, ct);
        }
    }

    private async Task SendBookingsAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var list = await _bookings.GetForDayAsync(today, null, null, ct);
        var active = list.Where(b => b.Status is not BookingStatus.Cancelled and not BookingStatus.Completed and not BookingStatus.NoShow)
            .OrderBy(b => b.StartsAt)
            .Take(20)
            .ToList();

        if (active.Count == 0)
        {
            await bot.SendMessage(chatId, $"📅 На сегодня ({today:dd.MM}) активных броней нет.", cancellationToken: ct);
            return;
        }

        foreach (var b in active)
        {
            var pcs = string.Join(", ", b.Computers.Select(c => c.ComputerName ?? "?"));
            var text =
                $"📅 <b>{b.StartsAt.ToLocalTime():HH:mm}</b>–{b.EndsAt.ToLocalTime():HH:mm} · {b.Status}\n" +
                $"{TelegramText.Html(b.ContactName)} · {TelegramText.Html(b.ContactPhone)}\n{TelegramText.Html(pcs)}";
            var markup = TelegramKeyboards.Cb(
                ("Пришёл", Put(new TelegramPendingAction { Kind = TelegramPendingKind.BookingAction, BookingId = b.Id, BookingOp = "arrived" })),
                ("Отмена", Put(new TelegramPendingAction { Kind = TelegramPendingKind.BookingAction, BookingId = b.Id, BookingOp = "cancel" })));
            await SendHtmlAsync(bot, chatId, text, markup, ct);
        }
    }

    private async Task SendCustomerCardAsync(ITelegramBotClient bot, long chatId, Guid customerId, CancellationToken ct)
    {
        var c = await _customers.GetByIdAsync(customerId, ct)
                ?? throw new InvalidOperationException("Клиент не найден");
        var text =
            $"👤 <b>{TelegramText.Html(c.FullName)}</b>\n{TelegramText.Html(c.Phone)}\n" +
            $"Баланс: <b>{c.Balance:0} ₸</b> · бонусы {c.BonusBalance:0}\n" +
            $"Time-bank: {TelegramText.FormatDuration(c.TimeBankMinutes)}";

        var markup = TelegramKeyboards.WithCancel(
            TelegramKeyboards.Cb(
                ("+1000", Put(new TelegramPendingAction { Kind = TelegramPendingKind.DepositAmount, CustomerId = c.Id, Amount = 1000, Label = c.FullName })),
                ("+2000", Put(new TelegramPendingAction { Kind = TelegramPendingKind.DepositAmount, CustomerId = c.Id, Amount = 2000, Label = c.FullName })),
                ("+5000", Put(new TelegramPendingAction { Kind = TelegramPendingKind.DepositAmount, CustomerId = c.Id, Amount = 5000, Label = c.FullName })),
                ("Своя сумма", Put(new TelegramPendingAction { Kind = TelegramPendingKind.DepositAmount, CustomerId = c.Id, Label = c.FullName }))),
            CancelId());

        await SendHtmlAsync(bot, chatId, text, markup, ct);
    }

    private async Task SendDepositPayPickAsync(
        ITelegramBotClient bot,
        long chatId,
        Guid customerId,
        string name,
        decimal amount,
        CancellationToken ct)
    {
        var text = $"Пополнение <b>{TelegramText.Html(name)}</b>\nСумма: <b>{amount:0} ₸</b>\nОплата:";
        var markup = TelegramKeyboards.WithCancel(
            TelegramKeyboards.Cb(
                ("Нал", Put(Dep(customerId, amount, PaymentMethod.Cash))),
                ("Kaspi", Put(Dep(customerId, amount, PaymentMethod.KaspiQr)))),
            CancelId());
        await SendHtmlAsync(bot, chatId, text, markup, ct);

        static TelegramPendingAction Dep(Guid id, decimal a, PaymentMethod p) => new()
        {
            Kind = TelegramPendingKind.DepositPay,
            CustomerId = id,
            Amount = a,
            PaymentMethod = p
        };
    }

    private async Task SendShiftAsync(
        ITelegramBotClient bot,
        long chatId,
        long telegramUserId,
        TelegramBotStoredSettings cfg,
        CancellationToken ct)
    {
        var employeeId = ResolveEmployee(cfg, telegramUserId);
        var shift = await _cash.GetOpenShiftAsync(null, employeeId, ct);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var overview = await _reports.GetOverviewAsync(today, today, ct);

        var sb = new StringBuilder();
        sb.AppendLine("🧾 <b>Смена</b>");
        if (shift is null)
            sb.AppendLine("Касса: <b>не открыта</b> (для этого сотрудника)");
        else
        {
            sb.AppendLine($"Касса: <b>открыта</b> · {TelegramText.Html(shift.CashRegisterName)} · {TelegramText.Html(shift.Number)}");
            sb.AppendLine($"Выручка сейчас: <b>{shift.RevenueNow:0} ₸</b>");
            sb.AppendLine($"В кассе ожидается: {shift.ExpectedCashNow:0} ₸");
        }

        sb.AppendLine();
        sb.AppendLine($"Сегодня: выручка <b>{overview.RevenueTotal:0} ₸</b>");
        sb.AppendLine($"Чеков: {overview.ReceiptCount} · сеансов: {overview.SessionCount}");

        await SendHtmlAsync(bot, chatId, sb.ToString(), TelegramKeyboards.MainMenu(), ct);
    }

    private async Task SendHtmlAsync(
        ITelegramBotClient bot,
        long chatId,
        string html,
        ReplyMarkup? markup = null,
        CancellationToken ct = default)
    {
        var parts = TelegramText.SplitMessage(html);
        for (var i = 0; i < parts.Count; i++)
        {
            var isLast = i == parts.Count - 1;
            await bot.SendMessage(
                chatId,
                parts[i],
                ParseMode.Html,
                replyMarkup: isLast ? markup : null,
                cancellationToken: ct);
        }
    }

    private async Task SendTextAsync(
        ITelegramBotClient bot,
        long chatId,
        string text,
        ReplyMarkup? markup = null,
        CancellationToken ct = default)
    {
        var parts = TelegramText.SplitMessage(text);
        for (var i = 0; i < parts.Count; i++)
        {
            var isLast = i == parts.Count - 1;
            await bot.SendMessage(
                chatId,
                parts[i],
                replyMarkup: isLast ? markup : null,
                cancellationToken: ct);
        }
    }

    private string Put(TelegramPendingAction action) => _callbacks.Put(action);

    private static bool IsAllowed(TelegramBotStoredSettings cfg, long userId) =>
        cfg.AllowedUsers.Any(u => u.TelegramUserId == userId);

    private Guid ResolveEmployee(TelegramBotStoredSettings cfg, long telegramUserId)
    {
        var user = cfg.AllowedUsers.FirstOrDefault(u => u.TelegramUserId == telegramUserId);
        if (user?.EmployeeId is Guid linked)
            return linked;
        if (cfg.DefaultEmployeeId is Guid def)
            return def;
        throw new InvalidOperationException(
            "Привяжите Telegram ID к сотруднику в настройках панели (или укажите сотрудника по умолчанию).");
    }
}
