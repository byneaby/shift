using ShiftClub.Shared.Contracts.ServerUpdates;

namespace ShiftClub.Application.Abstractions;

/// <summary>
/// Обновление самого сервера клуба. Сервер не может переписать свои файлы, пока
/// работает, поэтому подмену делает внешний скрипт: здесь только проверка канала
/// обновлений, скачивание пакета и запуск этого скрипта.
/// </summary>
public interface IServerUpdateService
{
    Task<ServerUpdateStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Спрашивает канал обновлений, не считаясь с кэшем проверки.</summary>
    Task<ServerUpdateStatusDto> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Скачивает и проверяет пакет, после чего запускает апдейтер. Сервер при этом
    /// будет остановлен, поэтому ответ приходит до начала подмены файлов.
    /// </summary>
    Task<ServerUpdateStartResultDto> StartAsync(Guid employeeId, string? version, CancellationToken cancellationToken = default);
}
