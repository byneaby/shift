using ShiftClub.Shared.Contracts.Backups;

namespace ShiftClub.Application.Abstractions;

public interface IDatabaseBackupService
{
    Task<BackupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Делает дамп базы. kind попадает в имя файла: daily, manual, pre-migration.
    /// Не бросает исключений при сбое pg_dump — возвращает Success=false с текстом.
    /// </summary>
    Task<RunBackupResultDto> RunAsync(string kind, CancellationToken cancellationToken = default);

    /// <summary>Полный путь к файлу копии для скачивания, либо null, если файла нет.</summary>
    string? ResolveFilePath(string fileName);
}
