using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;

namespace ShiftClub.Infrastructure.BackgroundJobs;

public sealed class SessionLifecycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionLifecycleWorker> _logger;

    public SessionLifecycleWorker(IServiceScopeFactory scopeFactory, ILogger<SessionLifecycleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
                await sessions.ProcessDueSessionsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Session lifecycle check failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
