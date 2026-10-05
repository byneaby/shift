using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Options;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.KaspiPos;

namespace ShiftClub.Infrastructure.Services;

public sealed class KaspiPosService : IKaspiPosService
{
    public const string SettingsKey = "kaspi.pos.settings";
    public const string AuthKey = "kaspi.pos.auth";
    public const string HttpClientName = "KaspiPos";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ShiftClubDbContext _db;
    private readonly KaspiPosOptions _options;
    private readonly ILogger<KaspiPosService> _logger;

    public KaspiPosService(
        IHttpClientFactory httpFactory,
        ShiftClubDbContext db,
        IOptions<KaspiPosOptions> options,
        ILogger<KaspiPosService> logger)
    {
        _httpFactory = httpFactory;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<KaspiPosStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var host = await ResolveHostAsync(cancellationToken);
        var auth = await LoadAuthAsync(cancellationToken);
        var configured = !string.IsNullOrWhiteSpace(host);
        var registered = auth?.AccessToken is { Length: > 0 };
        var tokenValid = auth is not null && !IsTokenExpired(auth, skewMinutes: 1);
        var ready = configured && registered && tokenValid && _options.Enabled;

        string? message = null;
        if (!configured)
            message = "Укажите IP терминала в настройках или secrets.env (KaspiPos__Host).";
        else if (!registered)
            message = "Терминал не зарегистрирован — нажмите «Подключить» и разрешите доступ на Kaspi POS.";
        else if (!tokenValid)
            message = "Токен истёк — выполните повторную регистрацию.";

        return new KaspiPosStatusDto(
            configured,
            registered,
            ready,
            host,
            _options.TerminalId,
            _options.RegisterName,
            message,
            auth?.ExpirationDate);
    }

    public async Task<KaspiPosConfigDto> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var host = await ResolveHostAsync(cancellationToken);
        return new KaspiPosConfigDto(host, _options.TerminalId, _options.RegisterName);
    }

    public async Task UpdateHostAsync(string? host, Guid? updatedBy, CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        var stored = await LoadSettingsAsync(cancellationToken) ?? new KaspiPosStoredSettings();
        stored.Host = normalized;

        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingsKey && s.BranchId == null, cancellationToken);
        var json = JsonSerializer.Serialize(stored, JsonOptions);
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = SettingsKey,
                Value = json,
                Description = "Kaspi Smart POS — IP терминала",
                CreatedBy = updatedBy,
                UpdatedBy = updatedBy,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedBy = updatedBy;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<KaspiPosRegisterResultDto> RegisterAsync(CancellationToken cancellationToken = default)
    {
        var host = await RequireHostAsync(cancellationToken);
        var name = Uri.EscapeDataString(_options.RegisterName);
        var envelope = await GetEnvelopeAsync(host, $"/v2/register?name={name}", auth: false, statusHeaders: false, cancellationToken);

        if (envelope.StatusCode != 0 || envelope.Data is not { } data)
            return new KaspiPosRegisterResultDto(false, envelope.ErrorText ?? envelope.DataMessage ?? "Ошибка регистрации", null);

        var access = data.TryGetProperty("accessToken", out var at) ? at.GetString() : null;
        var refresh = data.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;
        var expRaw = data.TryGetProperty("expirationDate", out var ed) ? ed.GetString() : null;

        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh))
            return new KaspiPosRegisterResultDto(false, "Терминал не вернул токены. Разрешили доступ на экране POS?", null);

        var expiration = ParseKaspiDate(expRaw);
        await SaveAuthAsync(new KaspiPosStoredAuth
        {
            AccessToken = access,
            RefreshToken = refresh,
            ExpirationDate = expiration
        }, cancellationToken);

        _logger.LogInformation("Kaspi POS registered for {Host}, expires {Exp}", host, expiration);
        return new KaspiPosRegisterResultDto(true, "Терминал подключён", expiration);
    }

    public async Task<KaspiPosPaymentResultDto> PayAndWaitAsync(int amountKzt, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken);
        if (!status.Ready)
            return new KaspiPosPaymentResultDto(false, "disabled", null, null, null, null,
                status.Message ?? "Kaspi POS не готов");

        if (amountKzt <= 0)
            return new KaspiPosPaymentResultDto(false, "fail", null, null, null, null, "Сумма должна быть больше 0");

        var host = await RequireHostAsync(cancellationToken);
        await EnsureAccessTokenAsync(host, cancellationToken);

        var own = _options.OwnCheque ? "true" : "false";
        var start = await GetEnvelopeAsync(
            host,
            $"/v2/payment?amount={amountKzt}&owncheque={own}",
            auth: true,
            statusHeaders: false,
            cancellationToken);

        if (start.StatusCode != 0 || start.Data is not { } startData)
            return Fail(start, null);

        var processId = startData.TryGetProperty("processId", out var pid) ? pid.GetString() : null;
        if (string.IsNullOrWhiteSpace(processId))
            return new KaspiPosPaymentResultDto(false, "fail", null, null, null, null, "Терминал не вернул processId");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.PaymentTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(_options.PollIntervalMs, cancellationToken);

            var poll = await GetEnvelopeAsync(
                host,
                $"/v2/status?processId={Uri.EscapeDataString(processId)}",
                auth: true,
                statusHeaders: true,
                cancellationToken);

            if (poll.StatusCode != 0 || poll.Data is not { } pollData)
                continue;

            var st = pollData.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
            var sub = pollData.TryGetProperty("subStatus", out var subEl) ? subEl.GetString() : null;

            if (st is "success")
            {
                var txId = pollData.TryGetProperty("transactionId", out var tx) ? tx.GetString() : null;
                var method = pollData.TryGetProperty("chequeInfo", out var ch) && ch.TryGetProperty("method", out var m)
                    ? m.GetString()
                    : null;
                return new KaspiPosPaymentResultDto(true, st, processId, txId, method, sub, null);
            }

            if (st is "fail")
            {
                var msg = pollData.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "Оплата отменена";
                return new KaspiPosPaymentResultDto(false, st, processId, null, null, sub, msg);
            }

            if (st is "unknown")
            {
                var actualize = await GetEnvelopeAsync(
                    host,
                    $"/v2/actualize?processId={Uri.EscapeDataString(processId)}",
                    auth: true,
                    statusHeaders: true,
                    cancellationToken);
                if (actualize.Data is { } actData && actData.TryGetProperty("status", out var ast))
                {
                    if (ast.GetString() is "success")
                    {
                        var txId = actData.TryGetProperty("transactionId", out var tx) ? tx.GetString() : null;
                        return new KaspiPosPaymentResultDto(true, "success", processId, txId, null, sub, null);
                    }

                    if (ast.GetString() is "fail")
                    {
                        var msg = actData.TryGetProperty("message", out var msgEl)
                            ? msgEl.GetString()
                            : "Оплата не прошла";
                        return new KaspiPosPaymentResultDto(false, "fail", processId, null, null, sub, msg);
                    }
                }
            }
        }

        return new KaspiPosPaymentResultDto(false, "timeout", processId, null, null, null, "Истекло время ожидания оплаты на терминале");
    }

    private async Task EnsureAccessTokenAsync(string host, CancellationToken cancellationToken)
    {
        var auth = await LoadAuthAsync(cancellationToken)
                   ?? throw new InvalidOperationException("Kaspi POS не зарегистрирован.");

        if (!IsTokenExpired(auth, skewMinutes: 5))
            return;

        if (string.IsNullOrWhiteSpace(auth.RefreshToken))
            throw new InvalidOperationException("Токен Kaspi POS истёк. Выполните регистрацию заново.");

        var name = Uri.EscapeDataString(_options.RegisterName);
        var refresh = Uri.EscapeDataString(auth.RefreshToken);
        var envelope = await GetEnvelopeAsync(
            host,
            $"/v2/revoke?name={name}&refreshToken={refresh}",
            auth: false,
            statusHeaders: false,
            cancellationToken);

        if (envelope.StatusCode != 0 || envelope.Data is not { } data)
            throw new InvalidOperationException(envelope.ErrorText ?? "Не удалось обновить токен Kaspi POS");

        var access = data.TryGetProperty("accessToken", out var at) ? at.GetString() : null;
        var newRefresh = data.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;
        var expRaw = data.TryGetProperty("expirationDate", out var ed) ? ed.GetString() : null;

        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(newRefresh))
            throw new InvalidOperationException("Терминал не вернул новый токен");

        await SaveAuthAsync(new KaspiPosStoredAuth
        {
            AccessToken = access,
            RefreshToken = newRefresh,
            ExpirationDate = ParseKaspiDate(expRaw)
        }, cancellationToken);
    }

    private async Task<KaspiEnvelope> GetEnvelopeAsync(
        string host,
        string path,
        bool auth,
        bool statusHeaders,
        CancellationToken cancellationToken)
    {
        var client = _httpFactory.CreateClient(HttpClientName);
        var url = $"https://{host.Trim()}:{8080}{path}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (auth)
        {
            var token = (await LoadAuthAsync(cancellationToken))?.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Kaspi POS не зарегистрирован.");
            request.Headers.TryAddWithoutValidation("accesstoken", token);
        }

        if (statusHeaders)
            request.Headers.TryAddWithoutValidation("terminalId", _options.TerminalId);

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Kaspi POS HTTP {Code} {Path}: {Body}", (int)response.StatusCode, path, body);
                return new KaspiEnvelope { StatusCode = (int)response.StatusCode, ErrorText = body };
            }

            return ParseEnvelope(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Kaspi POS request failed {Path}", path);
            throw new InvalidOperationException($"Нет связи с терминалом Kaspi ({host}). Проверьте IP и LAN.");
        }
    }

    private static KaspiEnvelope ParseEnvelope(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var statusCode = root.TryGetProperty("statusCode", out var sc) ? sc.GetInt32() : -1;
            var errorText = root.TryGetProperty("errorText", out var et) ? et.GetString() : null;
            JsonElement? data = root.TryGetProperty("data", out var d) ? d : null;
            string? dataMessage = null;
            if (data?.TryGetProperty("message", out var dm) == true)
                dataMessage = dm.GetString();
            return new KaspiEnvelope
            {
                StatusCode = statusCode,
                ErrorText = errorText,
                Data = data,
                DataMessage = dataMessage
            };
        }
        catch (JsonException)
        {
            return new KaspiEnvelope { StatusCode = -1, ErrorText = "Некорректный ответ терминала" };
        }
    }

    private static KaspiPosPaymentResultDto Fail(KaspiEnvelope envelope, string? processId) =>
        new(false, "fail", processId, null, null, null, envelope.ErrorText ?? envelope.DataMessage ?? "Ошибка терминала");

    private async Task<string?> ResolveHostAsync(CancellationToken cancellationToken)
    {
        var stored = await LoadSettingsAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(stored?.Host))
            return stored.Host.Trim();
        return string.IsNullOrWhiteSpace(_options.Host) ? null : _options.Host.Trim();
    }

    private async Task<string> RequireHostAsync(CancellationToken cancellationToken)
    {
        var host = await ResolveHostAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("IP терминала Kaspi POS не настроен.");
        return host;
    }

    private async Task<KaspiPosStoredSettings?> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var raw = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == SettingsKey && s.BranchId == null)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            return JsonSerializer.Deserialize<KaspiPosStoredSettings>(raw, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task<KaspiPosStoredAuth?> LoadAuthAsync(CancellationToken cancellationToken)
    {
        var raw = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == AuthKey && s.BranchId == null)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            return JsonSerializer.Deserialize<KaspiPosStoredAuth>(raw, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveAuthAsync(KaspiPosStoredAuth auth, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(auth, JsonOptions);
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AuthKey && s.BranchId == null, cancellationToken);
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = AuthKey,
                Value = json,
                Description = "Kaspi Smart POS — токены API",
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsTokenExpired(KaspiPosStoredAuth auth, int skewMinutes)
    {
        if (auth.ExpirationDate is null)
            return true;
        return auth.ExpirationDate.Value <= DateTimeOffset.UtcNow.AddMinutes(skewMinutes);
    }

    private static DateTimeOffset? ParseKaspiDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var formats = new[] { "yyyy-MM-dd HH:mm:ss", "dd.MM.yy HH:mm:ss", "dd.MM.yyyy HH:mm:ss" };
        if (DateTime.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return new DateTimeOffset(dt);
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
            return dto;
        return null;
    }

    private sealed class KaspiEnvelope
    {
        public int StatusCode { get; init; }
        public string? ErrorText { get; init; }
        public JsonElement? Data { get; init; }
        public string? DataMessage { get; init; }
    }

    private sealed class KaspiPosStoredSettings
    {
        public string? Host { get; set; }
    }

    private sealed class KaspiPosStoredAuth
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTimeOffset? ExpirationDate { get; set; }
    }
}
