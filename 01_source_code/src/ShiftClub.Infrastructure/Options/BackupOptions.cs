namespace ShiftClub.Infrastructure.Options;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>Делать ежедневные копии базы. Для продажи клубу — всегда true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Куда складывать дампы. Относительный путь считается от каталога API.</summary>
    public string Directory { get; set; } = "data/backups";

    /// <summary>Сколько дней держать копии. Старые удаляются после успешной новой.</summary>
    public int KeepDays { get; set; } = 14;

    /// <summary>Час по времени филиала, когда делать ежедневную копию (клуб ночью ещё работает — 6 утра тише всего).</summary>
    public int DailyHourLocal { get; set; } = 6;

    /// <summary>
    /// Путь к pg_dump. Пусто — ищем в PATH и в стандартных каталогах PostgreSQL для Windows.
    /// </summary>
    public string? PgDumpPath { get; set; }

    /// <summary>Сколько ждать завершения дампа.</summary>
    public int TimeoutMinutes { get; set; } = 30;
}
