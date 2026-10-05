# Подключение фискального регистратора

## Что есть в поставке

Фискализации в поставке **нет**. Есть только точка подключения: интерфейс
`IFiscalRegistrar` и заглушка `NullFiscalRegistrar`, которая ничего не печатает
и не мешает продажам. Это сделано сознательно: требования к фискальным чекам
свои в каждой стране, и «универсальная» реализация была бы неправдой.

Если клуб обязан выдавать фискальные чеки, интеграция делается отдельным
проектом под конкретную юрисдикцию и конкретное оборудование или ОФД.

## Где система вызывает регистратор

`ShiftClub.Infrastructure.Services.CashService`:

- `CreateSaleAsync` — после того, как чек сохранён, вызывается
  `RegisterFiscalAsync(..., FiscalReceiptRequest.Sale, ...)`;
- `RefundAsync` — то же с `FiscalReceiptRequest.Refund`.

Вызов идёт **после** `SaveChangesAsync`. Это важно: деньги с клиента уже
взяты, и недоступный регистратор не должен превращаться в потерянную продажу.
Если по закону чек без фискализации недопустим, включается
`Fiscal__RequireSuccess=true` — тогда отказ регистратора приводит к ошибке
операции, и кассир видит её сразу.

Результат пишется в журнал аудита (`audit_logs`) действием
`fiscal.sale.registered` / `fiscal.sale.failed` с номером фискального документа
в `DetailsJson`. Отдельного поля в таблице `receipts` для этого нет —
добавлять его стоит вместе с реальной интеграцией, когда станет понятно, какие
именно реквизиты требует регулятор.

## Как сделать свою реализацию

1. Добавить класс, реализующий `ShiftClub.Application.Abstractions.IFiscalRegistrar`.

```csharp
public sealed class MyOfdRegistrar : IFiscalRegistrar
{
    public Task<FiscalStatusDto> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalStatusDto(
            Enabled: true,
            Provider: "МойОФД, касса №12",
            RequireSuccess: true,
            Summary: "Регистратор на связи",
            Warning: null));

    public async Task<FiscalReceiptResult> RegisterAsync(
        FiscalReceiptRequest request,
        CancellationToken ct = default)
    {
        // request.Kind — "sale" или "refund"
        // request.Lines — позиции чека, request.Payments — способы оплаты
        var number = await SendToDeviceAsync(request, ct);
        return FiscalReceiptResult.Registered(number);
    }
}
```

2. Заменить регистрацию в `ShiftClub.Infrastructure.DependencyInjection`:

```csharp
services.AddScoped<IFiscalRegistrar, MyOfdRegistrar>();
```

3. Реализация обязана сама быть идемпотентной по `request.ReceiptId`:
   при повторном вызове того же чека второй фискальный документ создаваться
   не должен. Система может вызвать регистратор повторно после сбоя сети.

4. Что возвращать:
   - `FiscalReceiptResult.Registered(номер)` — чек проведён;
   - `FiscalReceiptResult.Failed(текст)` — не проведён, текст попадёт в журнал
     и (при `RequireSuccess`) кассиру;
   - `FiscalReceiptResult.NotConfigured()` — «меня нет», система молча идёт дальше.

## Настройки

| Переменная | Что делает |
|------------|------------|
| `Fiscal__RequireSuccess` | `true` — отказ регистратора отменяет операцию. По умолчанию `false`. |

Состояние регистратора видно в панели: «Система → Состояние», строка
«Фискальный регистратор».
