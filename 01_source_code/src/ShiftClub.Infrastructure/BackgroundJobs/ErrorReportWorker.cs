using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Diagnostics;

namespace ShiftClub.Infrastructure.BackgroundJobs;

/// <summary>
/// Отправляет поставщику новые ошибки клуба, если задан Diagnostics:WebhookUrl.
/// Без адреса не делает ничего: клуб, который не хочет ничего отправлять,
/// просто не заполняет настройку. Тексты уже очищены от персональных данных
/// в <see cref="ErrorLogStore"/>, здесь добавляется только, какой это клуб и версия.
/// </summary>
public sealed class ErrorReportWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ErrorLogStore _store;
    private readonly IConfiguration _config;
    private readonly ILogger<ErrorReportWorker> _logger;

    public ErrorReportWorker(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ErrorLogStore store,
        IConfiguration config,
        ILogger<ErrorReportWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _store = store;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = _config["Diagnostics:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var target)
            || (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp))
        {
            _logger.LogWarning("Diagnostics:WebhookUrl is not a valid http(s) address — error reporting disabled");
            return;
        }

        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendPendingAsync(target, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Про сбой отправки отчётов нельзя писать LogError: он сам попадёт
                // в список ошибок и клуб будет видеть бесконечно растущую проблему.
                _logger.LogWarning(ex, "Error report upload failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task SendPendingAsync(Uri target, CancellationToken cancellationToken)
    {
        var pending = _store.TakeUnreported();
        if (pending.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var branding = await scope.ServiceProvider.GetRequiredService<IBrandingService>()
            .GetAsync(cancellationToken);
        var license = await scope.ServiceProvider.GetRequiredService<ILicenseService>()
            .GetStatusAsync(cancellationToken);

        var payload = new
        {
            club = branding.ClubName,
            licenseClub = license.ClubName,
            licenseState = license.State.ToString(),
            version = Services.DiagnosticsService.Version,
            machine = Environment.MachineName,
            sentAt = DateTimeOffset.UtcNow,
            errors = pending
        };

        var client = _httpClientFactory.CreateClient(nameof(ErrorReportWorker));
        client.Timeout = TimeSpan.FromSeconds(20);

        using var response = await client.PostAsJsonAsync(target, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Error report rejected with {Status}", (int)response.StatusCode);
            return;
        }

        _store.MarkReported(pending.Select(p => p.Fingerprint));
        _logger.LogInformation("Reported {Count} error groups", pending.Count);
    }
}
