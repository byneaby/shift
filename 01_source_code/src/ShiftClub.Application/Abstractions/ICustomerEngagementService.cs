using ShiftClub.Domain.Entities;

namespace ShiftClub.Application.Abstractions;

public interface ICustomerEngagementService
{
    /// <summary>Обновляет стрик визита и выдаёт награды 3/5/7. Возвращает текст для UI/бота.</summary>
    Task<string?> RecordVisitAsync(Customer customer, DateOnly todayLocal, CancellationToken cancellationToken = default);

    /// <summary>Подарок на день рождения (раз в год). Возвращает текст или null.</summary>
    Task<string?> TryGrantBirthdayGiftAsync(
        Customer customer,
        Guid? zoneId,
        DateOnly todayLocal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Визиты и текущая серия — по календарным дням стартов сеансов с аккаунтом (источник истины).
    /// </summary>
    Task ReconcileVisitStatsFromSessionsAsync(Guid customerId, CancellationToken cancellationToken = default);
}
