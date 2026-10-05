using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;

namespace ShiftClub.Infrastructure.BackgroundJobs;

public sealed class BookingNoShowWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingNoShowWorker> _logger;

    public BookingNoShowWorker(IServiceScopeFactory scopeFactory, ILogger<BookingNoShowWorker> logger)
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
                var bookings = scope.ServiceProvider.GetRequiredService<IBookingService>();
                await bookings.ProcessNoShowsAsync(stoppingToken);
                await bookings.ProcessSoftHoldsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Booking no-show check failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
