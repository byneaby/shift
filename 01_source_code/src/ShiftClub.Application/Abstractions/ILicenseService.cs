using ShiftClub.Shared.Contracts.Licensing;

namespace ShiftClub.Application.Abstractions;

public interface ILicenseService
{
    /// <summary>Состояние лицензии для панели. Кэшируется на короткий срок.</summary>
    Task<LicenseStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Устанавливает новый ключ. Бросает InvalidOperationException, если ключ не проходит проверку.</summary>
    Task<LicenseStatusDto> SetKeyAsync(string key, Guid? updatedBy, CancellationToken cancellationToken = default);

    Task RemoveKeyAsync(Guid? updatedBy, CancellationToken cancellationToken = default);

    /// <summary>Бросает InvalidOperationException, если запуск сеансов сейчас запрещён.</summary>
    Task EnsureCanStartSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Бросает InvalidOperationException, если новый ПК регистрировать нельзя (срок или лимит).</summary>
    Task EnsureCanRegisterComputerAsync(CancellationToken cancellationToken = default);

    /// <summary>Бросает InvalidOperationException, если продажи сейчас запрещены.</summary>
    Task EnsureCanSellAsync(CancellationToken cancellationToken = default);

    /// <summary>Включена ли фича. Без лицензии (dev / свой клуб) — включены все.</summary>
    Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default);

    /// <summary>Сбрасывает кэш — после установки ключа.</summary>
    void InvalidateCache();
}
