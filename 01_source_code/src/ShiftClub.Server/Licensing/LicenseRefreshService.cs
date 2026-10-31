using Microsoft.Extensions.Options;

namespace ShiftClub.Server.Licensing;

public sealed class LicenseRefreshService : BackgroundService
{
    private readonly LicenseState _state;
    private readonly IOptions<LicenseOptions> _options;
    private readonly ILogger<LicenseRefreshService> _logger;

    public LicenseRefreshService(LicenseState state, IOptions<LicenseOptions> options, ILogger<LicenseRefreshService> logger)
    {
        _state = state;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = Math.Clamp(_options.Value.RefreshMinutes, 1, 180);
        var nextNetwork = DateTimeOffset.UtcNow.AddMinutes(minutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                if (DateTimeOffset.UtcNow >= nextNetwork)
                {
                    await _state.RefreshAsync(stoppingToken);
                    nextNetwork = DateTimeOffset.UtcNow.AddMinutes(minutes);
                }
                else
                {
                    _state.Reevaluate();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Проверка лицензии не выполнилась");
            }
        }
    }
}
