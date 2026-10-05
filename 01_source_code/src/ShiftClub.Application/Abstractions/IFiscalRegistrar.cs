using ShiftClub.Shared.Contracts.Fiscal;

namespace ShiftClub.Application.Abstractions;

/// <summary>
/// Точка подключения фискального регистратора.
/// Требования к фискализации свои в каждой стране, поэтому в поставке стоит
/// заглушка, а конкретная интеграция делается отдельной реализацией этого
/// интерфейса и подменой регистрации в DI. Система вызывает её после того, как
/// чек уже сохранён: деньги с клиента взяты, и падение регистратора не должно
/// превращаться в потерянную продажу — если по закону должно, включается
/// Fiscal:RequireSuccess.
/// </summary>
public interface IFiscalRegistrar
{
    Task<FiscalStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<FiscalReceiptResult> RegisterAsync(
        FiscalReceiptRequest request,
        CancellationToken cancellationToken = default);
}
