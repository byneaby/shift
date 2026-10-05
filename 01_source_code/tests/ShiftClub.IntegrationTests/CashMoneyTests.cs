using Microsoft.EntityFrameworkCore;
using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Enums;

namespace ShiftClub.IntegrationTests;

/// <summary>
/// Деньги в кассе. Здесь проверяется то, за что клуб спросит в первую очередь:
/// совпадает ли сумма, не задваивается ли чек, сходится ли смена.
/// </summary>
[Collection(ClubDatabaseCollection.Name)]
public class CashMoneyTests
{
    private readonly ClubDatabaseFixture _fixture;

    public CashMoneyTests(ClubDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task SaleAddsExactlyItsAmountToTheShift()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var shift = await club.Cash.OpenShiftAsync(
            new OpenCashShiftRequest(club.Register.Id, 5000, null),
            club.Cashier.Id);

        await club.Cash.CreateSaleAsync(
            Sale(
                [Item(ReceiptItemType.Product, "Кола", 2, 500)],
                [new PaymentPartDto(PaymentMethod.Cash, 1000)]),
            club.Cashier.Id);

        await club.Cash.CreateSaleAsync(
            Sale(
                [Item(ReceiptItemType.GamingTime, "Час игры", 1, 800)],
                [new PaymentPartDto(PaymentMethod.Card, 800)]),
            club.Cashier.Id);

        var stored = await club.ReloadShiftAsync(shift.Id);

        Assert.Equal(1000m, stored.SalesCash);
        Assert.Equal(800m, stored.SalesCard);
        Assert.Equal(0m, stored.SalesKaspi);
    }

    [SkippableFact]
    public async Task PaymentsThatDoNotAddUpToTheTotalAreRejected()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        await club.Cash.OpenShiftAsync(new OpenCashShiftRequest(club.Register.Id, 0, null), club.Cashier.Id);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            club.Cash.CreateSaleAsync(
                Sale(
                    [Item(ReceiptItemType.Product, "Кола", 1, 1000)],
                    [new PaymentPartDto(PaymentMethod.Cash, 900)]),
                club.Cashier.Id));

        Assert.Contains("не равна итогу", error.Message);
    }

    [SkippableFact]
    public async Task RepeatedRequestWithTheSameKeyDoesNotChargeTwice()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var shift = await club.Cash.OpenShiftAsync(
            new OpenCashShiftRequest(club.Register.Id, 0, null),
            club.Cashier.Id);

        var key = $"test-{Guid.NewGuid()}";
        var request = Sale(
            [Item(ReceiptItemType.Product, "Кола", 1, 700)],
            [new PaymentPartDto(PaymentMethod.Cash, 700)],
            key);

        var first = await club.Cash.CreateSaleAsync(request, club.Cashier.Id);
        var second = await club.Cash.CreateSaleAsync(request, club.Cashier.Id);

        Assert.Equal(first.Id, second.Id);

        var stored = await club.ReloadShiftAsync(shift.Id);
        Assert.Equal(700m, stored.SalesCash);

        var receipts = await club.Db.Receipts.AsNoTracking()
            .CountAsync(r => r.CashShiftId == shift.Id);
        Assert.Equal(1, receipts);
    }

    [SkippableFact]
    public async Task SaleWithoutAnOpenShiftIsRejected()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            club.Cash.CreateSaleAsync(
                Sale(
                    [Item(ReceiptItemType.Product, "Кола", 1, 500)],
                    [new PaymentPartDto(PaymentMethod.Cash, 500)]),
                club.Cashier.Id));

        Assert.Contains("смен", error.Message);
    }

    [SkippableFact]
    public async Task RefundRollsTheShiftCountersBack()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var shift = await club.Cash.OpenShiftAsync(
            new OpenCashShiftRequest(club.Register.Id, 0, null),
            club.Cashier.Id);

        var receipt = await club.Cash.CreateSaleAsync(
            Sale(
                [Item(ReceiptItemType.Product, "Кола", 1, 1200)],
                [new PaymentPartDto(PaymentMethod.Cash, 1200)]),
            club.Cashier.Id);

        await club.Cash.RefundReceiptAsync(receipt.Id, "ошибка кассира", club.Cashier.Id);

        var stored = await club.ReloadShiftAsync(shift.Id);

        Assert.Equal(1200m, stored.SalesCash);
        Assert.Equal(1200m, stored.RefundsCash);
    }

    [SkippableFact]
    public async Task RefundIsNotAppliedTwice()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var shift = await club.Cash.OpenShiftAsync(
            new OpenCashShiftRequest(club.Register.Id, 0, null),
            club.Cashier.Id);

        var receipt = await club.Cash.CreateSaleAsync(
            Sale(
                [Item(ReceiptItemType.Product, "Кола", 1, 1200)],
                [new PaymentPartDto(PaymentMethod.Cash, 1200)]),
            club.Cashier.Id);

        await club.Cash.RefundReceiptAsync(receipt.Id, null, club.Cashier.Id);
        await club.Cash.RefundReceiptAsync(receipt.Id, null, club.Cashier.Id);

        var stored = await club.ReloadShiftAsync(shift.Id);
        Assert.Equal(1200m, stored.RefundsCash);
    }

    [SkippableFact]
    public async Task RefundReturnsProductToStock()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var product = await club.AddProductAsync("Кола 0.5", 500, stock: 10);
        await club.Cash.OpenShiftAsync(new OpenCashShiftRequest(club.Register.Id, 0, null), club.Cashier.Id);

        var receipt = await club.Cash.CreateSaleAsync(
            Sale(
                [Item(ReceiptItemType.Product, product.Name, 3, 500, product.Id)],
                [new PaymentPartDto(PaymentMethod.Cash, 1500)]),
            club.Cashier.Id);

        await club.Cash.RefundReceiptAsync(receipt.Id, null, club.Cashier.Id);

        var stored = await club.Db.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(13m, stored.StockQty);
    }

    [SkippableFact]
    public async Task ClosingTheShiftShowsTheShortfallInCash()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var shift = await club.Cash.OpenShiftAsync(
            new OpenCashShiftRequest(club.Register.Id, 2000, null),
            club.Cashier.Id);

        await club.Cash.CreateSaleAsync(
            Sale(
                [Item(ReceiptItemType.Product, "Кола", 1, 1000)],
                [new PaymentPartDto(PaymentMethod.Cash, 1000)]),
            club.Cashier.Id);

        // В кассе 2500 вместо ожидаемых 3000 — недостача 500.
        var closed = await club.Cash.CloseShiftAsync(
            shift.Id,
            new CloseCashShiftRequest(2500, "пересчитали", null),
            club.Cashier.Id);

        Assert.Equal(3000m, closed.ClosingCashExpected);
        Assert.Equal(2500m, closed.ClosingCashActual);
        Assert.Equal(-500m, closed.Discrepancy);
        Assert.Equal(CashShiftStatus.Closed, closed.Status);
    }

    [SkippableFact]
    public async Task SecondShiftOnTheSameRegisterIsRejected()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        await club.Cash.OpenShiftAsync(new OpenCashShiftRequest(club.Register.Id, 0, null), club.Cashier.Id);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            club.Cash.OpenShiftAsync(new OpenCashShiftRequest(club.Register.Id, 0, null), club.Cashier.Id));
    }

    [SkippableFact]
    public async Task ReceiptNumbersDoNotRepeat()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        await club.Cash.OpenShiftAsync(new OpenCashShiftRequest(club.Register.Id, 0, null), club.Cashier.Id);

        var numbers = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var receipt = await club.Cash.CreateSaleAsync(
                Sale(
                    [Item(ReceiptItemType.Product, "Кола", 1, 100)],
                    [new PaymentPartDto(PaymentMethod.Cash, 100)]),
                club.Cashier.Id);
            numbers.Add(receipt.Number);
        }

        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    private static CreateSaleRequest Sale(
        IReadOnlyList<CreateSaleItemRequest> items,
        IReadOnlyList<PaymentPartDto> payments,
        string? idempotencyKey = null,
        Guid? customerId = null) =>
        new(null, null, null, idempotencyKey, items, payments, customerId);

    private static CreateSaleItemRequest Item(
        ReceiptItemType type,
        string name,
        decimal quantity,
        decimal unitPrice,
        Guid? referenceId = null) =>
        new(type, name, quantity, unitPrice, 0, referenceId);
}
