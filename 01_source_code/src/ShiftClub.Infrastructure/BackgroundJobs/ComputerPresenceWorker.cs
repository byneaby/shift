using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;

namespace ShiftClub.Infrastructure.BackgroundJobs;

public sealed class ComputerPresenceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ComputerPresenceWorker> _logger;

    public ComputerPresenceWorker(IServiceScopeFactory scopeFactory, ILogger<ComputerPresenceWorker> logger)
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
                var computers = scope.ServiceProvider.GetRequiredService<IComputerService>();
                await computers.MarkStaleComputersOfflineAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Computer presence check failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
