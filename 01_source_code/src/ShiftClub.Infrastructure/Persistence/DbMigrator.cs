using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;

namespace ShiftClub.Infrastructure.Persistence;

/// <summary>
/// Применение миграций при старте.
/// На своём клубе удобно «накатывается само», но на чужом сервере плохая миграция
/// испортит базу с деньгами клиентов. Поэтому: в продакшене автоприменение можно
/// выключить (Database:AutoMigrate=false), а если оно включено — перед изменением
/// структуры делается копия базы.
/// </summary>
public static class DbMigrator
{
    public const string AutoMigrateKey = "Database:AutoMigrate";
    public const string BackupBeforeMigrateKey = "Database:BackupBeforeMigrate";

    public static async Task ApplyAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<ShiftClubDbContext>();
        var config = services.GetRequiredService<IConfiguration>();

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
            return;

        var autoMigrate = config.GetValue(AutoMigrateKey, true);
        if (!autoMigrate)
        {
            logger.LogWarning(
                "Есть непримененные миграции ({Count}): {List}. Автоприменение выключено "
                + "({Key}=false) — примените вручную после резервной копии.",
                pending.Count,
                string.Join(", ", pending),
                AutoMigrateKey);
            return;
        }

        var isFreshDatabase = !(await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Any();
        var backupBefore = config.GetValue(BackupBeforeMigrateKey, true);

        if (backupBefore && !isFreshDatabase)
        {
            var backups = services.GetService<IDatabaseBackupService>();
            if (backups is null)
            {
                logger.LogWarning("Сервис копий недоступен — миграции применяются без копии");
            }
            else
            {
                logger.LogInformation("Делаем копию базы перед применением миграций…");
                var result = await backups.RunAsync("pre-migration", cancellationToken);
                if (result.Success)
                {
                    logger.LogInformation("Копия перед миграцией готова: {File}", result.FileName);
                }
                else
                {
                    // Не применяем миграции без страховки: лучше не обновиться, чем потерять данные.
                    throw new InvalidOperationException(
                        "Не удалось сделать копию базы перед миграциями: "
                        + result.Message
                        + $". Исправьте настройку копий или снимите {BackupBeforeMigrateKey}, "
                        + "если осознанно обновляетесь без копии.");
                }
            }
        }

        logger.LogInformation(
            "Применяем {Count} миграций: {List}",
            pending.Count,
            string.Join(", ", pending));

        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Миграции применены");
    }
}
