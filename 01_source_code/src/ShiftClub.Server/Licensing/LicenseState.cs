using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShiftClub.Shared.Licensing;

namespace ShiftClub.Server.Licensing;

public sealed class LicenseState
{
    public const string HttpName = "shift-license";

    private readonly IHttpClientFactory _http;
    private readonly IOptions<LicenseOptions> _options;
    private readonly ILogger<LicenseState> _logger;
    private readonly string _directory;
    private readonly object _gate = new();
    private LicenseSnapshot _current;
    private string? _lease;

    public LicenseState(
        IHttpClientFactory http,
        IOptions<LicenseOptions> options,
        IHostEnvironment environment,
        ILogger<LicenseState> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
        _directory = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(_directory);
        _current = Describe(null, null);
    }

    public LicenseSnapshot Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!options.Enforce)
        {
            Set(Describe(null, null));
            return;
        }

        var key = ReadKey(options);
        if (!LicenseKeyFormat.IsValid(key))
        {
            Set(Locked("Лицензия не активирована. Введите ключ SHIFT."));
            return;
        }

        var machineId = GetOrCreateMachineId();
        var cached = ReadLease();
        var haveLease = LicenseToken.TryRead(cached, out var cachedDoc, out _)
            && string.Equals(cachedDoc!.Key, key, StringComparison.Ordinal)
            && string.Equals(cachedDoc.MachineId, machineId, StringComparison.Ordinal);

        try
        {
            var path = haveLease ? "/api/v1/heartbeat" : "/api/v1/activate";
            var outcome = await CallAsync(path, key, machineId, cancellationToken);
            if (outcome.Ok && outcome.Lease is not null && LicenseToken.TryRead(outcome.Lease, out var fresh, out _))
            {
                WriteLease(outcome.Lease);
                Set(Describe(fresh, outcome.Lease));
                return;
            }

            if (outcome.Definitive)
            {
                if (path == "/api/v1/heartbeat" && outcome.Status == 409)
                {
                    var again = await CallAsync("/api/v1/activate", key, machineId, cancellationToken);
                    if (again.Ok && again.Lease is not null && LicenseToken.TryRead(again.Lease, out var rebound, out _))
                    {
                        WriteLease(again.Lease);
                        Set(Describe(rebound, again.Lease));
                        return;
                    }

                    outcome = again.Ok ? outcome : again;
                    if (outcome.Ok)
                        return;
                }

                ClearLease();
                Set(Locked(outcome.Message));
                return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Центр лицензий недоступен");
        }

        if (haveLease && cachedDoc is not null && !LicenseToken.IsExpired(cachedDoc.ExpiresAt))
        {
            Set(Describe(cachedDoc, cached));
            return;
        }

        if (cachedDoc is not null && LicenseToken.IsExpired(cachedDoc.ExpiresAt))
        {
            Set(Locked("Срок лицензии истёк."));
            return;
        }

        Set(Locked("Нет связи с центром лицензий, а сохранённой лицензии нет."));
    }

    public void Reevaluate()
    {
        var options = _options.Value;
        if (!options.Enforce)
            return;

        string? lease;
        lock (_gate)
            lease = _lease;

        if (string.IsNullOrEmpty(lease))
            return;

        if (!LicenseToken.TryRead(lease, out var doc, out var error))
            Set(Locked(string.IsNullOrEmpty(error) ? "Срок лицензии истёк." : error));
        else
            Set(Describe(doc, lease));
    }

    public async Task<(bool Ok, string Message)> ActivateAsync(string? rawKey, CancellationToken cancellationToken)
    {
        var key = LicenseKeyFormat.Normalize(rawKey);
        if (!LicenseKeyFormat.IsValid(key))
            return (false, "Ключ должен быть вида SHIFT-XXXX-XXXX-XXXX.");

        var machineId = GetOrCreateMachineId();
        LeaseCall outcome;
        try
        {
            outcome = await CallAsync("/api/v1/activate", key, machineId, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Активация лицензии не дошла до центра");
            return (false, "Центр лицензий сейчас недоступен. Проверьте адрес licence.shift-club.kz.");
        }

        LicenseDocument? doc = null;
        var error = "";
        var parsed = outcome.Ok && outcome.Lease is not null && LicenseToken.TryRead(outcome.Lease, out doc, out error);
        if (!parsed || doc is null || outcome.Lease is null)
            return (false, outcome.Message.Length > 0 ? outcome.Message : error);

        File.WriteAllText(KeyPath, key + Environment.NewLine);
        WriteLease(outcome.Lease);
        Set(Describe(doc, outcome.Lease));
        _logger.LogInformation("Лицензия активирована для {Club}, мест {Seats}, до {Expires}", doc!.Club, doc.MaxComputers, doc.ExpiresAt);
        return (true, "Лицензия принята. Зал снова работает.");
    }

    private async Task<LeaseCall> CallAsync(string path, string key, string machineId, CancellationToken cancellationToken)
    {
        var root = (_options.Value.ServerUrl ?? "").Trim().TrimEnd('/');
        if (root.Length == 0)
            return new LeaseCall(false, true, 0, null, "Не задан адрес центра лицензий.");

        var client = _http.CreateClient(HttpName);
        using var response = await client.PostAsJsonAsync(
            root + path,
            new { key, machineId, machineName = Environment.MachineName },
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        LeasePayload? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<LeasePayload>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            payload = null;
        }

        var definitive = response.StatusCode is System.Net.HttpStatusCode.BadRequest
            or System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.NotFound
            or System.Net.HttpStatusCode.Conflict;

        if (response.IsSuccessStatusCode && payload?.Ok == true && !string.IsNullOrWhiteSpace(payload.Lease))
            return new LeaseCall(true, true, (int)response.StatusCode, payload.Lease, "ok");

        var message = payload?.Message;
        if (string.IsNullOrWhiteSpace(message))
            message = "Центр лицензий отклонил ключ.";

        return new LeaseCall(false, definitive, (int)response.StatusCode, null, message);
    }

    private LicenseSnapshot Describe(LicenseDocument? doc, string? lease)
    {
        var options = _options.Value;
        if (!options.Enforce)
        {
            return new LicenseSnapshot(false, true, "Проверка лицензии выключена.", null, null, null, null, null, false);
        }

        if (doc is null)
            return Locked("Лицензия не активирована.");

        var left = doc.ExpiresAt - DateTimeOffset.UtcNow;
        var days = left <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(left.TotalDays);
        var warning = LicenseToken.IsWarning(doc.ExpiresAt);
        var message = warning
            ? $"Лицензия клуба {doc.Club} действует ещё {days} дн. Продлите её до {doc.ExpiresAt:dd.MM.yyyy}."
            : $"Лицензия {doc.Club} активна до {doc.ExpiresAt:dd.MM.yyyy}.";

        lock (_gate)
            _lease = lease;

        return new LicenseSnapshot(
            true,
            true,
            message,
            doc.Club,
            doc.Kind,
            doc.MaxComputers,
            doc.ExpiresAt,
            days,
            warning);
    }

    private LicenseSnapshot Locked(string message)
    {
        lock (_gate)
            _lease = null;
        return new LicenseSnapshot(true, false, message, null, null, null, null, 0, false);
    }

    private void Set(LicenseSnapshot snapshot)
    {
        lock (_gate)
            _current = snapshot;
    }

    private string ReadKey(LicenseOptions options)
    {
        var configured = LicenseKeyFormat.Normalize(options.Key);
        if (LicenseKeyFormat.IsValid(configured))
            return configured;

        if (!File.Exists(KeyPath))
            return "";

        return LicenseKeyFormat.Normalize(File.ReadAllText(KeyPath));
    }

    private string? ReadLease()
    {
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(_lease))
                return _lease;
        }

        return File.Exists(LeasePath) ? File.ReadAllText(LeasePath).Trim() : null;
    }

    private void WriteLease(string lease)
    {
        File.WriteAllText(LeasePath, lease);
        lock (_gate)
            _lease = lease;
    }

    private void ClearLease()
    {
        lock (_gate)
            _lease = null;
        if (File.Exists(LeasePath))
            File.Delete(LeasePath);
    }

    private string GetOrCreateMachineId()
    {
        if (File.Exists(MachinePath))
        {
            var existing = File.ReadAllText(MachinePath).Trim();
            if (existing.Length is >= 8 and <= 80)
                return existing;
        }

        var created = Guid.NewGuid().ToString("D");
        File.WriteAllText(MachinePath, created + Environment.NewLine);
        return created;
    }

    private string KeyPath => Path.Combine(_directory, "license-key.txt");
    private string LeasePath => Path.Combine(_directory, "license-lease.txt");
    private string MachinePath => Path.Combine(_directory, "license-machine.id");

    private sealed record LeasePayload(bool Ok, string? Lease, string? Message);
    private readonly record struct LeaseCall(bool Ok, bool Definitive, int Status, string? Lease, string Message);
}
