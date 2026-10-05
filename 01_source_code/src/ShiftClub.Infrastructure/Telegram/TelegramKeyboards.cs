using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace ShiftClub.Infrastructure.Telegram;

public static class TelegramKeyboards
{
    public const string BtnFloor = "Зал";
    public const string BtnBar = "Бар";
    public const string BtnBookings = "Брони";
    public const string BtnCustomers = "Клиенты";
    public const string BtnShift = "Смена";
    public const string BtnHelp = "Справка";

    // Legacy reply labels (still handled → redirect to Mini App)
    public const string BtnClientBalance = "Баланс";
    public const string BtnClientSession = "Мой сеанс";
    public const string BtnClientBar = "Мои заказы";
    public const string BtnClientRewards = "Награды";
    public const string BtnClientComfort = "Комфорт";
    public const string BtnClientRegister = "Открыть аккаунт";
    public const string BtnClientOpenApp = "Открыть SHIFT";
    public const string BtnClientMenu = "Меню";

    public const string BtnClientEnterCode = "Ввести код";
    public const string BtnClientScanQr = "Сканировать QR";
    public const string BtnModeClient = "Панель клиента";
    public const string BtnModeStaff = "Панель сотрудника";

    public static ReplyKeyboardMarkup MainMenu() => new(
    [
        [new KeyboardButton(BtnFloor), new KeyboardButton(BtnBar)],
        [new KeyboardButton(BtnBookings), new KeyboardButton(BtnCustomers)],
        [new KeyboardButton(BtnShift), new KeyboardButton(BtnHelp)],
        [new KeyboardButton(BtnModeClient)]
    ])
    {
        ResizeKeyboard = true,
        IsPersistent = true
    };

    public static ReplyKeyboardMarkup MainMenuWithClientSwitch() => MainMenu();

    public static string? AppUrl(string? webAppBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(webAppBaseUrl)
            || !webAppBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;
        return webAppBaseUrl.TrimEnd('/') + "/tg-webapp/index.html";
    }

    public static string? ScanUrl(string? webAppBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(webAppBaseUrl)
            || !webAppBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;
        return webAppBaseUrl.TrimEnd('/') + "/tg-webapp/scan.html";
    }

    /// <summary>
    /// Inline WebApp — единственный способ получить initData (reply-клавиатура его не даёт).
    /// </summary>
    public static InlineKeyboardMarkup? OpenAppInline(string? webAppBaseUrl)
    {
        var url = AppUrl(webAppBaseUrl);
        if (url is null) return null;
        return new InlineKeyboardMarkup(
        [
            [InlineKeyboardButton.WithWebApp(BtnClientOpenApp, new WebAppInfo { Url = url })]
        ]);
    }

    public static KeyboardButton ScanOrCodeButton(string? webAppScanUrl)
    {
        var url = ScanUrl(webAppScanUrl);
        if (url is not null)
        {
            return new KeyboardButton(BtnClientScanQr)
            {
                WebApp = new WebAppInfo { Url = url }
            };
        }

        return new KeyboardButton(BtnClientScanQr);
    }

    /// <summary>Только вход на ПК + переключение режима. Баланс/бар/профиль — в Mini App.</summary>
    public static ReplyKeyboardMarkup ClientMenu(bool showStaffSwitch, string? webAppScanUrl = null)
    {
        var scan = ScanOrCodeButton(webAppScanUrl);
        var rows = new List<KeyboardButton[]>
        {
            new[] { scan, new KeyboardButton(BtnClientEnterCode) }
        };
        if (showStaffSwitch)
            rows.Add([new KeyboardButton(BtnModeStaff)]);

        return new ReplyKeyboardMarkup(rows)
        {
            ResizeKeyboard = true,
            IsPersistent = true
        };
    }

    public static ReplyKeyboardMarkup GuestMenu(bool isStaff, string? webAppScanUrl = null)
        => ClientMenu(isStaff, webAppScanUrl);

    public static InlineKeyboardMarkup Cb(params (string text, string callbackId)[] buttons)
    {
        var rows = new List<InlineKeyboardButton[]>();
        var row = new List<InlineKeyboardButton>();
        foreach (var (text, id) in buttons)
        {
            row.Add(InlineKeyboardButton.WithCallbackData(text, "t:" + id));
            if (row.Count >= 3)
            {
                rows.Add(row.ToArray());
                row = [];
            }
        }

        if (row.Count > 0)
            rows.Add(row.ToArray());

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup Rows(params InlineKeyboardButton[][] rows)
        => new(rows);

    public static InlineKeyboardButton B(string text, string callbackId)
        => InlineKeyboardButton.WithCallbackData(text, "t:" + callbackId);

    public static InlineKeyboardMarkup WithCancel(InlineKeyboardMarkup markup, string cancelCallbackId)
    {
        var rows = markup.InlineKeyboard
            .Select(r => r.ToArray())
            .ToList();
        rows.Add([B("Отмена", cancelCallbackId)]);
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup CancelOnly(string cancelCallbackId) =>
        new([[B("Отмена", cancelCallbackId)]]);
}
