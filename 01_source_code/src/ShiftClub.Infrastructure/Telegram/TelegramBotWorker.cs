using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts.Settings;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ShiftClub.Infrastructure.Telegram;

public sealed class TelegramBotWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramAlertSink _alerts;
    private readonly CustomerTelegramNotifySink _customerNotices;
    private readonly ITelegramBotRuntime _runtime;
    private readonly ILogger<TelegramBotWorker> _logger;

    public TelegramBotWorker(
        IServiceScopeFactory scopeFactory,
        TelegramAlertSink alerts,
        CustomerTelegramNotifySink customerNotices,
        ITelegramBotRuntime runtime,
        ILogger<TelegramBotWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _alerts = alerts;
        _customerNotices = customerNotices;
        _runtime = runtime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _runtime.SetStatus("stopped", "Ожидание настроек");

        while (!stoppingToken.IsCancellationRequested)
        {
            TelegramBotStoredSettings cfg;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                cfg = await scope.ServiceProvider.GetRequiredService<IClubSettingsService>()
                    .GetTelegramBotStoredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _runtime.SetStatus("error", ex.Message);
                _logger.LogError(ex, "Failed to load Telegram settings");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.BotToken))
            {
                _runtime.SetStatus(
                    "stopped",
                    !cfg.Enabled ? "Выключен в настройках" : "Токен не задан");
                await WaitRestartOrDelay(TimeSpan.FromSeconds(3), stoppingToken);
                continue;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _runtime.RestartToken);
            try
            {
                await RunSessionAsync(cfg, linked.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Telegram bot restart requested");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _runtime.SetStatus("error", Truncate(ex.Message, 200));
                _logger.LogError(ex, "Telegram bot session failed");
                await WaitRestartOrDelay(TimeSpan.FromSeconds(8), stoppingToken);
            }
        }

        _runtime.SetStatus("stopped", "Сервер остановлен");
    }

    private async Task RunSessionAsync(TelegramBotStoredSettings cfg, CancellationToken ct)
    {
        var bot = new TelegramBotClient(cfg.BotToken!);
        var me = await bot.GetMe(ct);
        var username = me.Username is null ? null : me.Username.Trim().TrimStart('@');
        _runtime.SetStatus("running", $"Онлайн · {(username is null ? "без @username" : "@" + username)}",
            username is null ? null : "@" + username);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<IClubSettingsService>();
            await settings.PersistTelegramBotUsernameAsync(username, ct);

            var tg = await settings.GetTelegramBotStoredAsync(ct);
            var eng = await settings.GetEngagementStoredAsync(ct);
            var web = !string.IsNullOrWhiteSpace(tg.PublicWebAppBaseUrl)
                ? tg.PublicWebAppBaseUrl
                : eng.PublicWebAppBaseUrl;
            var appUrl = TelegramKeyboards.AppUrl(web);
            if (appUrl is not null)
            {
                await bot.SetChatMenuButton(
                    menuButton: new MenuButtonWebApp
                    {
                        Text = "SHIFT",
                        WebApp = new WebAppInfo { Url = appUrl }
                    },
                    cancellationToken: ct);
                _logger.LogInformation("Telegram menu button set to Mini App {Url}", appUrl);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to persist Telegram bot username / menu button");
        }

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
            DropPendingUpdates = true
        };

        var alertTask = PumpAlertsAsync(bot, ct);
        var noticeTask = PumpCustomerNoticesAsync(bot, ct);
        try
        {
            await bot.ReceiveAsync(
                async (client, update, token) =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<TelegramBotService>();
                    await svc.HandleUpdateAsync(client, update, token);
                },
                (_, ex, _) =>
                {
                    _logger.LogWarning(ex, "Telegram polling error");
                    return Task.CompletedTask;
                },
                receiverOptions,
                ct);
        }
        finally
        {
            await Task.WhenAny(Task.WhenAll(alertTask, noticeTask), Task.Delay(500, CancellationToken.None));
        }
    }

    private async Task PumpCustomerNoticesAsync(ITelegramBotClient bot, CancellationToken ct)
    {
        try
        {
            await foreach (var notice in _customerNotices.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await bot.SendMessage(
                        notice.TelegramUserId,
                        notice.HtmlText,
                        parseMode: ParseMode.Html,
                        cancellationToken: ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to send customer Telegram notice to {UserId}", notice.TelegramUserId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal
        }
    }

    private async Task PumpAlertsAsync(ITelegramBotClient bot, CancellationToken ct)
    {
        try
        {
            await foreach (var alert in _alerts.Reader.ReadAllAsync(ct))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var settings = scope.ServiceProvider.GetRequiredService<IClubSettingsService>();
                    var cfg = await settings.GetTelegramBotStoredAsync(ct);
                    if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.BotToken))
                        continue;

                    var svc = scope.ServiceProvider.GetRequiredService<TelegramBotService>();
                    await svc.BroadcastAlertAsync(bot, alert, cfg, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to broadcast Telegram alert");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal
        }
    }

    private async Task WaitRestartOrDelay(TimeSpan delay, CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _runtime.RestartToken);
        try
        {
            await Task.Delay(delay, linked.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // restart
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
