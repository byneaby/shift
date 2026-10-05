using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftClub.Infrastructure.Services;

namespace ShiftClub.Infrastructure.BackgroundJobs;

/// <summary>Тик автосообщений Telegram CRM (~каждый час).</summary>
public sealed class TelegramCrmWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramCrmWorker> _logger;

    public TelegramCrmWorker(IServiceScopeFactory scopeFactory, ILogger<TelegramCrmWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Не спамить сразу на старте API — подождать прогрев.
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var crm = scope.ServiceProvider.GetRequiredService<ITelegramCrmService>();
                var n = await crm.ProcessTickAsync(stoppingToken);
                if (n > 0)
                    _logger.LogInformation("Telegram CRM tick sent {Count}", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Telegram CRM tick failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(50), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
