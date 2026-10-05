using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts.Licensing;

namespace ShiftClub.Infrastructure.BackgroundJobs;

/// <summary>
/// Раз в час сверяет срок лицензии и пишет в лог, когда состояние меняется.
/// Нужен, чтобы блокировка наступала без перезапуска сервера и чтобы
/// в логах клуба было видно, когда именно лицензия перестала действовать.
/// </summary>
public sealed class LicenseWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LicenseWorker> _logger;

    private LicenseState? _lastState;

    public LicenseWorker(IServiceScopeFactory scopeFactory, ILogger<LicenseWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Дать серверу применить миграции и подняться.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var license = scope.ServiceProvider.GetRequiredService<ILicenseService>();
                license.InvalidateCache();
                var status = await license.GetStatusAsync(stoppingToken);

                if (_lastState != status.State)
                {
                    if (status.State is LicenseState.Grace or LicenseState.Expired or LicenseState.Invalid)
                    {
                        _logger.LogWarning(
                            "License state changed to {State}: {Summary}",
                            status.State,
                            status.Summary);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "License state: {State} ({Summary})",
                            status.State,
                            status.Summary);
                    }

                    _lastState = status.State;
                }
                else if (status.State == LicenseState.Active
                         && status.DaysLeft is { } left
                         && left <= Application.Licensing.LicensePolicy.WarnBeforeDays)
                {
                    _logger.LogWarning("License expires in {Days} day(s)", left);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "License check failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
