using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using ShiftClub.Licensing;
using ShiftClub.Shared.Licensing;

var builder = WebApplication.CreateBuilder(args);

var privateKeyPath = builder.Configuration["LicenseSigning:PrivateKeyPath"];
if (string.IsNullOrWhiteSpace(privateKeyPath) || !File.Exists(privateKeyPath))
    throw new InvalidOperationException("Укажите LicenseSigning:PrivateKeyPath — PEM приватного ключа центра лицензий.");

var adminPassword = builder.Configuration["LicenseAdmin:Password"] ?? "";
if (adminPassword.Length < 12)
    throw new InvalidOperationException("LicenseAdmin:Password должен быть не короче 12 символов.");

var privateKey = RSA.Create();
privateKey.ImportFromPem(File.ReadAllText(privateKeyPath));
using (var embedded = RSA.Create())
{
    embedded.ImportFromPem(LicensePublicKey.Pem);
    var issued = privateKey.ExportParameters(false).Modulus;
    var expected = embedded.ExportParameters(false).Modulus;
    if (issued is null || expected is null || !issued.AsSpan().SequenceEqual(expected))
        throw new InvalidOperationException("Приватный ключ не совпадает с публичным ключом в клиентах SHIFT.");
}

var dbPath = builder.Configuration["LicenseStore:Path"];
if (string.IsNullOrWhiteSpace(dbPath))
    dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "licenses.db");
else if (!Path.IsPathRooted(dbPath))
    dbPath = Path.Combine(builder.Environment.ContentRootPath, dbPath);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "shift_license_admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var store = new LicenseStore(dbPath);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(new LicenseIssuer(store, privateKey));

var app = builder.Build();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

var loginGuard = new LoginGuard();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/v1/activate", async (LeaseRequest body, LicenseIssuer issuer, CancellationToken ct) =>
{
    var (lease, row, failure, message) = await issuer.IssueAsync(body.Key, body.MachineId, body.MachineName, firstBind: true, ct);
    return LeaseResult(lease, row, failure, message);
});

app.MapPost("/api/v1/heartbeat", async (LeaseRequest body, LicenseIssuer issuer, CancellationToken ct) =>
{
    var (lease, row, failure, message) = await issuer.IssueAsync(body.Key, body.MachineId, body.MachineName, firstBind: false, ct);
    return LeaseResult(lease, row, failure, message);
});

app.MapPost("/api/admin/login", (LoginRequest body, HttpContext http) =>
{
    var ip = http.Connection.RemoteIpAddress?.ToString() ?? "local";
    if (loginGuard.IsBlocked(ip))
        return Results.Json(new { ok = false, message = "Слишком много попыток. Подождите 5 минут." }, statusCode: StatusCodes.Status429TooManyRequests);

    var given = body.Password ?? "";
    var ok = given.Length == adminPassword.Length
        && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(given),
            System.Text.Encoding.UTF8.GetBytes(adminPassword));
    if (!ok)
    {
        loginGuard.Fail(ip);
        return Results.Json(new { ok = false, message = "Неверный пароль." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    loginGuard.Reset(ip);
    return Results.SignIn(
        new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim("role", "license-admin")],
                CookieAuthenticationDefaults.AuthenticationScheme)),
        authenticationScheme: CookieAuthenticationDefaults.AuthenticationScheme);
});

app.MapPost("/api/admin/logout", () => Results.SignOut(authenticationSchemes: new[] { CookieAuthenticationDefaults.AuthenticationScheme }))
    .RequireAuthorization();

app.MapGet("/api/admin/session", () => Results.Ok(new { ok = true }))
    .RequireAuthorization();

var admin = app.MapGroup("/api/admin/licenses").RequireAuthorization();

admin.MapGet("/", async (LicenseStore db, CancellationToken ct) =>
{
    var rows = await db.ListAsync(ct);
    return Results.Ok(rows.Select(ToAdmin));
});

admin.MapPost("/", async (CreateLicenseRequest body, LicenseStore db, CancellationToken ct) =>
{
    var club = (body.ClubName ?? "").Trim();
    if (club.Length is < 2 or > 120)
        return Results.BadRequest(new { ok = false, message = "Название клуба: от 2 до 120 символов." });

    if (body.MaxComputers is < 1 or > 5000)
        return Results.BadRequest(new { ok = false, message = "Число мест: от 1 до 5000." });

    if (body.Days is < 1 or > 3650)
        return Results.BadRequest(new { ok = false, message = "Срок: от 1 до 3650 дней." });

    var kind = (body.Kind ?? "").Trim().ToLowerInvariant();
    if (kind is not ("trial" or "paid"))
        return Results.BadRequest(new { ok = false, message = "Тип: trial или paid." });

    var note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();
    if (note is { Length: > 500 })
        note = note[..500];

    var row = await db.CreateAsync(club, body.MaxComputers, body.Days, kind, note, ct);
    return Results.Ok(ToAdmin(row));
});

admin.MapPost("/{id}/revoke", async (string id, LicenseStore db, CancellationToken ct) =>
{
    if (!await db.RevokeAsync(id, ct))
        return Results.NotFound(new { ok = false, message = "Лицензия не найдена." });
    return Results.Ok(new { ok = true });
});

admin.MapPost("/{id}/unbind", async (string id, LicenseStore db, CancellationToken ct) =>
{
    if (!await db.UnbindAsync(id, ct))
        return Results.NotFound(new { ok = false, message = "Лицензия не найдена." });
    return Results.Ok(new { ok = true });
});

admin.MapPost("/{id}/extend", async (string id, ExtendLicenseRequest body, LicenseStore db, CancellationToken ct) =>
{
    if (body.Days is < 1 or > 3650)
        return Results.BadRequest(new { ok = false, message = "Продление: от 1 до 3650 дней." });

    var row = await db.ExtendAsync(id, body.Days, ct);
    return row is null
        ? Results.NotFound(new { ok = false, message = "Лицензия не найдена." })
        : Results.Ok(ToAdmin(row));
});

var index = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
app.MapGet("/", () => Results.File(index, "text/html; charset=utf-8"));
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(index);
});

app.Run();

static IResult LeaseResult(string? lease, LicenseRow? row, LeaseFailure failure, string message)
{
    if (failure == LeaseFailure.None && lease is not null && row is not null)
    {
        return Results.Ok(new
        {
            ok = true,
            lease,
            club = row.ClubName,
            kind = row.Kind,
            maxComputers = row.MaxComputers,
            expiresAt = row.ExpiresAt,
        });
    }

    return Results.Json(new { ok = false, error = failure.ToString().ToLowerInvariant(), message }, statusCode: (int)failure);
}

static object ToAdmin(LicenseRow row) => new
{
    id = row.Id,
    key = row.Key,
    clubName = row.ClubName,
    maxComputers = row.MaxComputers,
    kind = row.Kind,
    createdAt = row.CreatedAt,
    expiresAt = row.ExpiresAt,
    revoked = row.Revoked,
    machineId = row.MachineId,
    machineName = row.MachineName,
    activatedAt = row.ActivatedAt,
    lastSeenAt = row.LastSeenAt,
    note = row.Note,
    expired = LicenseToken.IsExpired(row.ExpiresAt),
};

sealed record LeaseRequest(string? Key, string? MachineId, string? MachineName);
sealed record LoginRequest(string? Password);
sealed record CreateLicenseRequest(string? ClubName, int MaxComputers, int Days, string? Kind, string? Note);
sealed record ExtendLicenseRequest(int Days);

sealed class LoginGuard
{
    private readonly Dictionary<string, (int Fails, DateTimeOffset Until)> _state = new();
    private readonly object _gate = new();

    public bool IsBlocked(string ip)
    {
        lock (_gate)
        {
            if (!_state.TryGetValue(ip, out var item))
                return false;
            if (item.Until > DateTimeOffset.UtcNow && item.Fails >= 8)
                return true;
            if (item.Until <= DateTimeOffset.UtcNow)
                _state.Remove(ip);
            return false;
        }
    }

    public void Fail(string ip)
    {
        lock (_gate)
        {
            var fails = _state.TryGetValue(ip, out var item) && item.Until > DateTimeOffset.UtcNow ? item.Fails + 1 : 1;
            _state[ip] = (fails, DateTimeOffset.UtcNow.AddMinutes(5));
        }
    }

    public void Reset(string ip)
    {
        lock (_gate)
            _state.Remove(ip);
    }
}
