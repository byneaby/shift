using Serilog;
using ShiftClub.Application;
using ShiftClub.Infrastructure;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Infrastructure.SignalR;
using ShiftClub.Server.Hiring;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/shiftclub-.log", rollingInterval: RollingInterval.Day));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<HiringOptions>(builder.Configuration.GetSection(HiringOptions.SectionName));
builder.Services.Configure<ShiftClub.Infrastructure.Options.KaspiPosOptions>(
    builder.Configuration.GetSection(ShiftClub.Infrastructure.Options.KaspiPosOptions.SectionName));
builder.Services.AddSingleton<HiringService>();
builder.Services.AddSingleton<ShiftClub.Infrastructure.Services.DeskDisplayStore>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Numbers in output (Shell 0.5.0); strings OR numbers on input (web panel).
        options.JsonSerializerOptions.Converters.Add(new FlexibleJsonEnumConverterFactory());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "SHIFT Club API",
        Version = "v1",
        Description = "Система управления компьютерным клубом SHIFT Club"
    });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ShiftClubDbContext>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("WebPanel", policy =>
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(_ => true));
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<ShiftClub.Server.Middleware.ApiExceptionMiddleware>();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("EnableSwagger", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("WebPanel");

var wwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwroot))
{
    // SHIFT CASE UI — только с кассы (?desk=1). Статика иначе отдаст case.html кому угодно.
    // Public B2B product landing — never fall through to staff SPA / login.
    app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path.Value ?? "";
        var isProduct =
            path.Equals("/product", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/product/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/product/index.html", StringComparison.OrdinalIgnoreCase);
        if (isProduct)
        {
            var productFile = Path.Combine(wwwroot, "product", "index.html");
            if (!System.IO.File.Exists(productFile))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Response.Headers.Pragma = "no-cache";
            await ctx.Response.SendFileAsync(productFile);
            return;
        }

        var isWork =
            path.Equals("/work", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/work/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/work/index.html", StringComparison.OrdinalIgnoreCase);
        if (isWork)
        {
            var workFile = Path.Combine(wwwroot, "work", "index.html");
            if (!System.IO.File.Exists(workFile))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Response.Headers.Pragma = "no-cache";
            await ctx.Response.SendFileAsync(workFile);
            return;
        }

        var isRetiredFree =
            path.Equals("/free", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/site/free", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/site/free.html", StringComparison.OrdinalIgnoreCase);
        if (isRetiredFree)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var isCasePage =
            path.Equals("/case", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/site/case", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/site/case.html", StringComparison.OrdinalIgnoreCase);
        if (isCasePage)
        {
            var deskOk = ctx.Request.Query.TryGetValue("desk", out var desk)
                         && string.Equals(desk.ToString(), "1", StringComparison.Ordinal);
            if (!deskOk)
            {
                ctx.Response.Redirect("/site/");
                return;
            }

            // Короткий URL → сама страница (не уходить в MapFallback 404)
            if (!path.Equals("/site/case.html", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.Redirect("/site/case.html?desk=1");
                return;
            }
        }

        await next();
    });

    // No UseDefaultFiles — `/` is routed (landing on shift-club.kz, admin SPA on LAN).
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            var name = ctx.File.Name;
            if (name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                ctx.Context.Response.Headers.Pragma = "no-cache";
                ctx.Context.Response.Headers.Expires = "0";
            }
            else if (ctx.Context.Request.Path.StartsWithSegments("/tg-webapp"))
            {
                // Mini App assets must not sit in CF/browser cache for hours
                ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                ctx.Context.Response.Headers.Pragma = "no-cache";
                ctx.Context.Response.Headers.Expires = "0";
            }
            else if (ctx.Context.Request.Path.StartsWithSegments("/site")
                     && (name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                         || name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                         || name.EndsWith(".css", StringComparison.OrdinalIgnoreCase)))
            {
                ctx.Context.Response.Headers.CacheControl = "public, max-age=120";
            }
            else if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
            {
                ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            }
        }
    });
}

var barImagesPath = Path.Combine(app.Environment.ContentRootPath, "data", "bar-images");
Directory.CreateDirectory(barImagesPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(barImagesPath),
    RequestPath = "/media/bar"
});

var softwareImagesPath = Path.Combine(app.Environment.ContentRootPath, "data", "software-covers");
Directory.CreateDirectory(softwareImagesPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(softwareImagesPath),
    RequestPath = "/media/software"
});

var softwareSoundsPath = Path.Combine(app.Environment.ContentRootPath, "data", "software-sounds");
Directory.CreateDirectory(softwareSoundsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(softwareSoundsPath),
    RequestPath = "/media/software-sounds"
});

var brandingPath = Path.Combine(app.Environment.ContentRootPath, "data", "branding");
Directory.CreateDirectory(brandingPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(brandingPath),
    RequestPath = "/media/branding"
});

var wikiImagesPath = Path.Combine(app.Environment.ContentRootPath, "data", "wiki");
Directory.CreateDirectory(wikiImagesPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wikiImagesPath),
    RequestPath = "/media/wiki"
});

var caseImagesPath = Path.Combine(app.Environment.ContentRootPath, "data", "case-images");
Directory.CreateDirectory(caseImagesPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(caseImagesPath),
    RequestPath = "/media/case"
});

var soundsPath = Path.Combine(app.Environment.ContentRootPath, "data", "sounds");
Directory.CreateDirectory(soundsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(soundsPath),
    RequestPath = "/media/sounds"
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<StaffHub>("/hubs/staff");
app.MapHub<ComputerHub>("/hubs/computer");
app.MapHealthChecks("/health");

if (Directory.Exists(wwwroot))
{
    // Mini App must NEVER fall through to staff SPA (admin login).
    // Do not register both /tg-webapp and /tg-webapp/ — trailing-slash matching makes them ambiguous.
    var tgIndex = Path.Combine(wwwroot, "tg-webapp", "index.html");
    var tgScan = Path.Combine(wwwroot, "tg-webapp", "scan.html");
    var siteIndex = Path.Combine(wwwroot, "site", "index.html");
    app.MapGet("/tg-webapp", () => Results.Redirect("/tg-webapp/index.html"));
    app.MapGet("/tg-webapp/index.html", (HttpContext ctx) =>
    {
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        ctx.Response.Headers.Pragma = "no-cache";
        return Results.File(tgIndex, "text/html; charset=utf-8");
    });
    app.MapGet("/tg-webapp/scan.html", (HttpContext ctx) =>
    {
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        return Results.File(tgScan, "text/html; charset=utf-8");
    });

    app.MapGet("/site", () => Results.Redirect("/site/index.html"));
    app.MapGet("/site/index.html", () => Results.File(siteIndex, "text/html; charset=utf-8"));

    var siteStart = Path.Combine(wwwroot, "site", "start.html");
    app.MapGet("/start", () => Results.Redirect("/site/start.html"));
    app.MapGet("/site/start", () => Results.Redirect("/site/start.html"));
    app.MapGet("/site/start.html", (HttpContext ctx) =>
    {
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        return Results.File(siteStart, "text/html; charset=utf-8");
    });
    var siteLoyalty = Path.Combine(wwwroot, "site", "loyalty.html");
    app.MapGet("/site/loyalty", () => Results.Redirect("/site/loyalty.html"));
    app.MapGet("/site/loyalty.html", (HttpContext ctx) =>
    {
        ctx.Response.Headers.CacheControl = "public, max-age=60";
        return Results.File(siteLoyalty, "text/html; charset=utf-8");
    });
    app.MapGet("/loyalty", () => Results.Redirect("/site/loyalty.html"));

    var siteHire = Path.Combine(wwwroot, "site", "hire.html");
    app.MapGet("/hire", () => Results.Redirect("/site/hire.html"));
    app.MapGet("/site/hire", () => Results.Redirect("/site/hire.html"));
    app.MapGet("/site/hire.html", (HttpContext ctx) =>
    {
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        return Results.File(siteHire, "text/html; charset=utf-8");
    });

    var siteCall = Path.Combine(wwwroot, "site", "call.html");
    app.MapGet("/call", () => Results.Redirect("/site/call.html"));
    app.MapGet("/site/call", () => Results.Redirect("/site/call.html"));
    app.MapGet("/site/call.html", (HttpContext ctx) =>
    {
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        return Results.File(siteCall, "text/html; charset=utf-8");
    });

    var siteCase = Path.Combine(wwwroot, "site", "case.html");
    app.MapGet("/case", (HttpContext ctx) =>
    {
        // Только с кассы (кнопка в панели /cases?desk=1). Прямые ссылки с сайта/TG — нет.
        if (!ctx.Request.Query.TryGetValue("desk", out var desk) || desk != "1")
            return Results.Redirect("/site/");
        return Results.Redirect("/site/case.html?desk=1");
    });
    app.MapGet("/site/case", (HttpContext ctx) =>
    {
        if (!ctx.Request.Query.TryGetValue("desk", out var desk) || desk != "1")
            return Results.Redirect("/site/");
        return Results.Redirect("/site/case.html?desk=1");
    });
    app.MapGet("/site/case.html", (HttpContext ctx) =>
    {
        if (!ctx.Request.Query.TryGetValue("desk", out var desk) || desk != "1")
            return Results.Redirect("/site/");
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        return Results.File(siteCase, "text/html; charset=utf-8");
    });

    // TV kiosk boards (session price + bar menu) — do not fall through to staff SPA.
    var priceIndex = Path.Combine(wwwroot, "price", "index.html");
    var priceBarIndex = Path.Combine(wwwroot, "price", "bar", "index.html");
    app.MapGet("/price", () => Results.Redirect("/price/index.html"));
    app.MapGet("/price/index.html", (HttpContext ctx) =>
    {
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        return Results.File(priceIndex, "text/html; charset=utf-8");
    });
    app.MapGet("/price/bar", () => Results.Redirect("/price/bar/index.html"));
    app.MapGet("/price/bar/index.html", (HttpContext ctx) =>
    {
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        return Results.File(priceBarIndex, "text/html; charset=utf-8");
    });

    // Promo / TV advertising boards (static files serve index.html; redirects avoid trailing-slash ambiguity)
    app.MapGet("/promo", () => Results.Redirect("/promo/index.html"));
    app.MapGet("/promo/tg", () => Results.Redirect("/promo/tg/index.html"));
    app.MapGet("/promo/bonuses", () => Results.Redirect("/promo/bonuses/index.html"));
    app.MapGet("/promo/review", () => Results.Redirect("/promo/review/index.html"));
    app.MapGet("/promo/account", () => Results.Redirect("/promo/account/index.html"));
    app.MapGet("/promo/wifi", () => Results.Redirect("/promo/wifi/index.html"));
    app.MapGet("/promo/case", () => Results.Redirect("/promo/case/index.html"));
    app.MapGet("/promo/upgrade", () => Results.Redirect("/promo/upgrade/index.html"));

    static bool IsMarketingHost(string host) =>
        host.Equals("shift-club.kz", StringComparison.OrdinalIgnoreCase)
        || host.Equals("www.shift-club.kz", StringComparison.OrdinalIgnoreCase);
    // panel.shift-club.kz and LAN IPs → staff SPA (not landing)

    // Public landing on main domain; staff SPA stays on LAN / other hosts.
    app.MapGet("/", async (HttpContext ctx) =>
    {
        var host = ctx.Request.Host.Host;
        if (IsMarketingHost(host) && System.IO.File.Exists(siteIndex))
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.CacheControl = "public, max-age=120";
            await ctx.Response.SendFileAsync(siteIndex);
            return;
        }

        var index = Path.Combine(wwwroot, "index.html");
        if (!System.IO.File.Exists(index))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        await ctx.Response.SendFileAsync(index);
    });

    // Staff SPA only — never catch /tg-webapp, /site, /api, hubs, media.
    app.MapFallback(async context =>
    {
        var path = context.Request.Path.Value ?? "";
        var host = context.Request.Host.Host;
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/media", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/tg-webapp", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/site", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/product", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/work", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hire", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/free", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/call", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/case", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/price", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/promo", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/start", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (IsMarketingHost(host) && System.IO.File.Exists(siteIndex))
        {
            // Deep links on marketing host → club landing (not staff SPA / login)
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(siteIndex);
            return;
        }

        var index = Path.Combine(wwwroot, "index.html");
        if (!System.IO.File.Exists(index))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
        await context.Response.SendFileAsync(index);
    });
}
else
{
    app.MapGet("/", () => ApiResponse.Ok("SHIFT Club API"));
}

await DbSeeder.SeedAsync(app.Services);

app.Run();

public partial class Program;
