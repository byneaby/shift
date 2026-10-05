using Microsoft.Extensions.Configuration;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts.Fiscal;

namespace ShiftClub.Infrastructure.Services;

/// <summary>
/// Заглушка фискального регистратора: ничего не печатает и ничего не ломает.
/// Стоит в поставке по умолчанию, чтобы продажи работали в клубах, где
/// фискализация не подключена, и чтобы в коде уже было видно, куда её вставлять.
/// </summary>
public sealed class NullFiscalRegistrar : IFiscalRegistrar
{
    private readonly IConfiguration _config;

    public NullFiscalRegistrar(IConfiguration config)
    {
        _config = config;
    }

    public Task<FiscalStatusDto> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new FiscalStatusDto(
            false,
            "не подключён",
            _config.GetValue("Fiscal:RequireSuccess", false),
            "Фискальный регистратор не подключён — чеки печатаются нефискальные.",
            "Если по закону клуб обязан выдавать фискальные чеки, нужна отдельная интеграция."));

    public Task<FiscalReceiptResult> RegisterAsync(
        FiscalReceiptRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(FiscalReceiptResult.NotConfigured());
}
