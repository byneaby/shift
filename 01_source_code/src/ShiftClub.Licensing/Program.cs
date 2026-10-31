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
app.Use(async (ctx, next) =>
{
    var kind = ShiftClub.Shared.Sites.PublicSiteFiles.KindForHost(ctx.Request.Host.Host);
    if (kind == ShiftClub.Shared.Sites.PublicSiteKind.None)
    {
        await next();
        return;
    }

    var root = Path.Combine(app.Environment.ContentRootPath, "wwwroot", ShiftClub.Shared.Sites.PublicSiteFiles.FolderName(kind));
    if (!ShiftClub.Shared.Sites.PublicSiteFiles.TryResolve(root, ctx.Request.Path.Value, out var file, out var contentType, out var cache))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        var missing = Path.Combine(root, "404.html");
        if (File.Exists(missing))
            await ctx.Response.SendFileAsync(missing);
        return;
    }

    ctx.Response.ContentType = contentType;
    ctx.Response.Headers.CacheControl = cache;
    await ctx.Response.SendFileAsync(file);
});
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

admin.MapPost("/{id}/seats", async (string id, SeatsRequest body, LicenseStore db, CancellationToken ct) =>
{
    if (body.MaxComputers is < 1 or > 5000)
        return Results.BadRequest(new { ok = false, message = "Число мест: от 1 до 5000." });
    var row = await db.SetSeatsAsync(id, body.MaxComputers, ct);
    return row is null
        ? Results.BadRequest(new { ok = false, message = "Менять места можно только у действующей лицензии." })
        : Results.Ok(ToAdmin(row));
});

admin.MapPost("/{id}/reissue", async (string id, LicenseStore db, CancellationToken ct) =>
{
    var row = await db.ReissueAsync(id, ct);
    return row is null
        ? Results.BadRequest(new { ok = false, message = "Новый ключ выдаётся только пока лицензия ещё действует." })
        : Results.Ok(ToAdmin(row));
});

var clubs = app.MapGroup("/api/admin/clubs").RequireAuthorization();

clubs.MapGet("/", async (LicenseStore db, CancellationToken ct) =>
{
    var rows = await db.ListClubsAsync(ct);
    return Results.Ok(rows.Select(ToClubList));
});

clubs.MapGet("/{id}", async (string id, LicenseStore db, CancellationToken ct) =>
{
    var (club, current, licenses, events) = await db.GetClubAsync(id, ct);
    if (club is null)
        return Results.NotFound(new { ok = false, message = "Клуб не найден." });
    return Results.Ok(new
    {
        club = ToClubList(new ClubOverview(club, current, LicenseStore.StatusOf(current), current is null ? 0 : LicenseStore.DaysRemaining(current.ExpiresAt))),
        licenses = licenses.Select(ToAdmin),
        events = events.Select(item => new { item.Action, item.Message, item.CreatedAt }),
    });
});

clubs.MapPost("/", async (CreateClubRequest body, LicenseStore db, CancellationToken ct) =>
{
    var error = ValidateIssue(body.Name, body.MaxComputers, body.Days, body.Kind, out var name, out var kind);
    if (error is not null)
        return Results.BadRequest(new { ok = false, message = error });
    if (CleanPhone(body.Phone) is null && !string.IsNullOrWhiteSpace(body.Phone))
        return Results.BadRequest(new { ok = false, message = "Телефон: цифры, плюс, пробелы и скобки." });

    var (club, license) = await db.CreateClubAsync(
        name,
        Clean(body.City, 80),
        Clean(body.ContactName, 80),
        CleanPhone(body.Phone),
        Clean(body.Telegram, 64),
        Clean(body.Note, 1000),
        body.MaxComputers,
        body.Days,
        kind,
        ct);
    return Results.Ok(new { clubId = club.Id, license = ToAdmin(license) });
});

clubs.MapPut("/{id}", async (string id, UpdateClubRequest body, LicenseStore db, CancellationToken ct) =>
{
    var name = (body.Name ?? "").Trim();
    if (name.Length is < 2 or > 120)
        return Results.BadRequest(new { ok = false, message = "Название клуба: от 2 до 120 символов." });
    if (CleanPhone(body.Phone) is null && !string.IsNullOrWhiteSpace(body.Phone))
        return Results.BadRequest(new { ok = false, message = "Телефон: цифры, плюс, пробелы и скобки." });

    var club = await db.UpdateClubAsync(
        id,
        name,
        Clean(body.City, 80),
        Clean(body.ContactName, 80),
        CleanPhone(body.Phone),
        Clean(body.Telegram, 64),
        Clean(body.Note, 1000),
        ct);
    return club is null
        ? Results.NotFound(new { ok = false, message = "Клуб не найден." })
        : Results.Ok(new { ok = true });
});

var index = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
app.MapGet("/", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    return Results.File(index, "text/html; charset=utf-8");
});
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.Headers.CacheControl = "no-store";
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
    clubId = row.ClubId,
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
    status = LicenseStore.StatusOf(row),
    daysRemaining = LicenseStore.DaysRemaining(row.ExpiresAt),
};

static object ToClubList(ClubOverview item) => new
{
    id = item.Club.Id,
    name = item.Club.Name,
    city = item.Club.City,
    contactName = item.Club.ContactName,
    phone = item.Club.Phone,
    telegram = item.Club.Telegram,
    note = item.Club.Note,
    status = item.Status,
    daysRemaining = item.DaysRemaining,
    license = item.License is null ? null : ToAdmin(item.License),
};

static string? ValidateIssue(string? rawName, int seats, int days, string? rawKind, out string name, out string kind)
{
    name = (rawName ?? "").Trim();
    kind = (rawKind ?? "").Trim().ToLowerInvariant();
    if (name.Length is < 2 or > 120)
        return "Название клуба: от 2 до 120 символов.";
    if (seats is < 1 or > 5000)
        return "Число мест: от 1 до 5000.";
    if (days is < 1 or > 3650)
        return "Срок: от 1 до 3650 дней.";
    if (kind is not ("trial" or "paid"))
        return "Тип: триал или оплата.";
    return null;
}

static string? Clean(string? value, int max)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;
    var text = value.Trim();
    return text.Length <= max ? text : text[..max];
}

static string? CleanPhone(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;
    var text = value.Trim();
    if (text.Length > 40 || text.Any(c => !char.IsDigit(c) && c is not '+' and not ' ' and not '-' and not '(' and not ')'))
        return null;
    return text;
}

sealed record LeaseRequest(string? Key, string? MachineId, string? MachineName);
sealed record LoginRequest(string? Password);
sealed record CreateLicenseRequest(string? ClubName, int MaxComputers, int Days, string? Kind, string? Note);
sealed record ExtendLicenseRequest(int Days);
sealed record SeatsRequest(int MaxComputers);
sealed record CreateClubRequest(string? Name, string? City, string? ContactName, string? Phone, string? Telegram, string? Note, int MaxComputers, int Days, string? Kind);
sealed record UpdateClubRequest(string? Name, string? City, string? ContactName, string? Phone, string? Telegram, string? Note);

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
