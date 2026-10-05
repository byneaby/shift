namespace ShiftClub.Infrastructure.Options;

public sealed class ServerUpdateOptions
{
    public const string SectionName = "ServerUpdates";

    /// <summary>
    /// Канал обновлений поставщика. Пусто — клуб обновляется руками, и кнопка в
    /// панели просто ничего не предлагает.
    /// </summary>
    public string FeedUrl { get; set; } = "";

    public string Channel { get; set; } = "stable";

    /// <summary>Куда складывать скачанные пакеты и отчёт апдейтера.</summary>
    public string DownloadPath { get; set; } = "data/server-updates";

    /// <summary>
    /// Скрипт, который останавливает сервер, подменяет файлы и поднимает обратно.
    /// Пусто — обновление из панели выключено.
    /// </summary>
    public string UpdaterPath { get; set; } = "";

    /// <summary>Как часто перепроверять канал при обычном открытии страницы.</summary>
    public int CheckIntervalMinutes { get; set; } = 180;

    public int DownloadTimeoutMinutes { get; set; } = 30;
}
