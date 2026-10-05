using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using ShiftClub.Shared.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Diagnostics;
using ShiftClub.Infrastructure.BackgroundJobs;
using ShiftClub.Infrastructure.Identity;
using ShiftClub.Infrastructure.Options;
using ShiftClub.Infrastructure.Options;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Infrastructure.Services;
using ShiftClub.Infrastructure.SignalR;
using ShiftClub.Infrastructure.Telegram;

namespace ShiftClub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<ShiftClubDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<LicenseCache>();
        services.AddScoped<ILicenseService, LicenseService>();
        services.AddSingleton<BackupRunState>();
        services.AddSingleton<BrandingCache>();
        services.AddScoped<IBrandingService, BrandingService>();
        services.AddScoped<IDatabaseBackupService, DatabaseBackupService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IZoneService, ZoneService>();
        services.AddScoped<IComputerService, ComputerService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ICashService, CashService>();
        services.AddScoped<IBarService, BarService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerImportService, CustomerImportService>();
        services.AddScoped<ISetupService, SetupService>();
        services.AddSingleton<ErrorLogStore>();
        // Фискализация своя в каждой стране: в поставке заглушка, конкретная
        // интеграция подменяет эту регистрацию.
        services.AddScoped<IFiscalRegistrar, NullFiscalRegistrar>();
        services.AddScoped<IDiagnosticsService, DiagnosticsService>();
        services.AddHostedService<ErrorReportWorker>();
        services.AddScoped<ILoyaltyService, LoyaltyService>();
        services.AddScoped<ICustomerEngagementService, CustomerEngagementService>();
        services.AddScoped<ITelegramAuthService, TelegramAuthService>();
        services.AddScoped<ITgWebAppService, TgWebAppService>();
        services.AddScoped<TelegramClientBotService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IEmployeeAdminService, EmployeeAdminService>();
        services.AddScoped<IWorkPortalService, WorkPortalService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IDocumentNumberService, DocumentNumberService>();
        services.AddScoped<IClientUpdateService, ClientUpdateService>();
        services.AddScoped<IServerUpdateService, ServerUpdateService>();
        services.AddScoped<IClientLauncherService, ClientLauncherService>();
        services.AddScoped<ISoftwareAppService, SoftwareAppService>();
        services.AddScoped<IClubNewsService, ClubNewsService>();
        services.AddScoped<IStaffWikiService, StaffWikiService>();
        services.AddScoped<IClubSettingsService, ClubSettingsService>();
        services.AddScoped<TelegramBroadcastService>();
        services.AddScoped<IFloorMapService, FloorMapService>();
        services.AddScoped<ICaseService, CaseService>();
        services.AddScoped<IKaspiPosService, KaspiPosService>();
        services.AddSingleton<TelegramBotRuntime>();
        services.AddSingleton<ITelegramBotRuntime>(sp => sp.GetRequiredService<TelegramBotRuntime>());
        services.AddSingleton<TelegramAlertSink>();
        services.AddSingleton<ITelegramAlertSink>(sp => sp.GetRequiredService<TelegramAlertSink>());
        services.AddSingleton<CustomerTelegramNotifySink>();
        services.AddSingleton<ICustomerTelegramNotifySink>(sp => sp.GetRequiredService<CustomerTelegramNotifySink>());
        services.AddSingleton<TelegramCallbackStore>();
        services.AddScoped<TelegramBotService>();
        services.AddScoped<ITelegramCrmService, TelegramCrmService>();
        services.Configure<ClientUpdateOptions>(configuration.GetSection(ClientUpdateOptions.SectionName));
        services.Configure<ServerUpdateOptions>(configuration.GetSection(ServerUpdateOptions.SectionName));
        services.AddHttpClient(ServerUpdateService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.Configure<BackupOptions>(configuration.GetSection(BackupOptions.SectionName));
        services.AddHostedService<LicenseWorker>();
        services.AddHostedService<DatabaseBackupWorker>();
        services.AddHostedService<ComputerPresenceWorker>();
        services.AddHostedService<SessionLifecycleWorker>();
        services.AddHostedService<BookingNoShowWorker>();
        services.AddHostedService<TelegramBotWorker>();
        services.AddHostedService<TelegramCrmWorker>();
        services.AddSignalR().AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
            options.PayloadSerializerOptions.Converters.Add(new FlexibleJsonEnumConverterFactory());
        });

        services.Configure<KaspiPosOptions>(configuration.GetSection(KaspiPosOptions.SectionName));
        services.AddHttpClient(KaspiPosService.HttpClientName, (sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(190);
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KaspiPosOptions>>().Value;
            var handler = new HttpClientHandler();
            if (opts.SkipSslValidation)
            {
                handler.ServerCertificateCustomValidationCallback =
                    static (HttpRequestMessage _, X509Certificate2? __, X509Chain? ___, SslPolicyErrors ____) => true;
            }

            return handler;
        });

        var signingKey = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException("Jwt:SigningKey is not configured. Use: dotnet user-secrets set \"Jwt:SigningKey\" \"...\"");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = configuration["Jwt:Issuer"] ?? "ShiftClub",
                    ValidAudience = configuration["Jwt:Audience"] ?? "ShiftClub.Web",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                            context.Token = accessToken;
                        return Task.CompletedTask;
                    }
                };
            })
            .AddScheme<AuthenticationSchemeOptions, DeviceAuthenticationHandler>(
                DeviceAuthDefaults.SchemeName,
                _ => { });

        services.AddAuthorization();

        return services;
    }
}
