using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using ShiftClub.Application.Security;

namespace ShiftClub.Server.Security;

/// <summary>
/// Защита от «постороннего в сети»: ограничение источников, ограничение частоты
/// запросов к входу и базовые заголовки браузера.
/// Всё настраивается, но по умолчанию включено — клуб, который купил систему,
/// не должен сам доводить её до безопасного состояния.
/// </summary>
public static class SecuritySetup
{
    public const string CorsPolicyName = "WebPanel";

    /// <summary>Вход сотрудника и подбор PIN — самое ценное для взлома, их и ограничиваем жёстче.</summary>
    public const string LoginPolicy = "login";

    /// <summary>Анонимные формы с сайта (анкета, вызов администратора) — защита от спама.</summary>
    public const string PublicFormPolicy = "public-form";

    public static IServiceCollection AddShiftClubSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration
            .GetSection(CorsOriginPolicy.AllowedOriginsKey)
            .Get<string[]>() ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                policy.AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .SetIsOriginAllowed(origin => CorsOriginPolicy.IsAllowed(origin, allowedOrigins));
            });
        });

        var options = new SecurityOptions();
        configuration.GetSection(SecurityOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        if (options.TrustForwardedHeaders)
        {
            services.Configure<ForwardedHeadersOptions>(fh =>
            {
                fh.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                // Пустые списки = доверять любому прокси. Это осознанный выбор: включают
                // только когда перед сервером реально стоит свой reverse proxy.
                fh.KnownNetworks.Clear();
                fh.KnownProxies.Clear();
            });
        }

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(
                    JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorCode = "rate_limited",
                        message = "Слишком много попыток. Подождите немного и попробуйте снова."
                    }),
                    cancellationToken);
            };

            limiter.AddPolicy(LoginPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, options.LoginAttemptsPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            limiter.AddPolicy(PublicFormPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, options.PublicFormsPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });

        return services;
    }

    public static WebApplication UseShiftClubSecurity(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<SecurityOptions>();
        var allowedOrigins = app.Configuration
            .GetSection(CorsOriginPolicy.AllowedOriginsKey)
            .Get<string[]>() ?? [];

        if (CorsOriginPolicy.AllowsEverything(allowedOrigins))
        {
            app.Logger.LogWarning(
                "{Key} содержит \"*\" — API отвечает на запросы с любого сайта. "
                + "Для проданного клуба укажите домены панели вместо звёздочки.",
                CorsOriginPolicy.AllowedOriginsKey);
        }

        if (options.TrustForwardedHeaders)
            app.UseForwardedHeaders();

        if (options.RequireHttps)
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Панель сотрудника не должна открываться в чужом iframe (кликджекинг).
            // Telegram Mini App и табло — наоборот, живут во встроенном окне, их не трогаем.
            var path = context.Request.Path.Value ?? "";
            if (!path.StartsWith("/tg-webapp", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/promo", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/price", StringComparison.OrdinalIgnoreCase))
            {
                headers["X-Frame-Options"] = "SAMEORIGIN";
            }

            await next();
        });

        return app;
    }

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Перенаправлять HTTP на HTTPS. Включают, когда панель смотрит в интернет.</summary>
    public bool RequireHttps { get; set; }

    /// <summary>Доверять X-Forwarded-For. Включать только если перед сервером стоит свой прокси.</summary>
    public bool TrustForwardedHeaders { get; set; }

    public int LoginAttemptsPerMinute { get; set; } = 10;

    public int PublicFormsPerMinute { get; set; } = 10;
}
