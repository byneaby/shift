using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Npgsql;
using ShiftClub.Infrastructure;
using ShiftClub.Infrastructure.Persistence;

namespace ShiftClub.IntegrationTests;

/// <summary>
/// Настоящая база PostgreSQL для тестов на деньги.
/// Денежный контур проверять на подменённом хранилище бессмысленно: половина
/// гарантий (транзакции, уникальные индексы, точность decimal) живёт именно в
/// базе. Поэтому тесты идут к живому серверу, а если его нет — честно
/// пропускаются, а не делают вид, что всё проверено.
/// </summary>
public sealed class ClubDatabaseFixture : IAsyncLifetime
{
    /// <summary>Строку можно задать переменной окружения SHIFTCLUB_TEST_DB.</summary>
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=shiftclub_tests;Username=shiftclub;Password=devpass";

    public string? SkipReason { get; private set; }

    public bool Available => SkipReason is null;

    private ServiceProvider? _provider;
    private string _connectionString = DefaultConnectionString;

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable("SHIFTCLUB_TEST_DB")
                            ?? DefaultConnectionString;

        if (!await TryCreateDatabaseAsync())
            return;

        _provider = BuildProvider(_connectionString);

        try
        {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShiftClubDbContext>();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            SkipReason = $"Не удалось подготовить тестовую базу: {ex.GetBaseException().Message}";
        }
    }

    public Task DisposeAsync()
    {
        _provider?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Новая область на каждый тест: ровно так же, как на один запрос API.</summary>
    public IServiceScope CreateScope() =>
        _provider?.CreateScope()
        ?? throw new InvalidOperationException(SkipReason ?? "Тестовая база недоступна.");

    private async Task<bool> TryCreateDatabaseAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString);
        var databaseName = builder.Database
                           ?? throw new InvalidOperationException("В строке подключения нет имени базы.");

        builder.Database = "postgres";

        try
        {
            await using var admin = new NpgsqlConnection(builder.ConnectionString);
            await admin.OpenAsync();

            await using var exists = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @name",
                admin);
            exists.Parameters.AddWithValue("name", databaseName);

            if (await exists.ExecuteScalarAsync() is null)
            {
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            return true;
        }
        catch (Exception ex)
        {
            SkipReason =
                "PostgreSQL для интеграционных тестов недоступен "
                + $"({ex.GetBaseException().Message}). "
                + "Задайте SHIFTCLUB_TEST_DB или поднимите сервер.";
            return false;
        }
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Jwt:SigningKey"] = "integration-tests-signing-key-integration-tests-signing-key",
                // Копии базы и отправка отчётов в тестах не нужны.
                ["Backup:Enabled"] = "false",
                ["License:Enforce"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = "Testing",
            ApplicationName = "ShiftClub.IntegrationTests",
            ContentRootPath = AppContext.BaseDirectory
        });
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }
}

[CollectionDefinition(Name)]
public sealed class ClubDatabaseCollection : ICollectionFixture<ClubDatabaseFixture>
{
    public const string Name = "club-database";
}
