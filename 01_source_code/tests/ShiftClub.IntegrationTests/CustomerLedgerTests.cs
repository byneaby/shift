using Microsoft.EntityFrameworkCore;
using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Contracts.Customers;
using ShiftClub.Shared.Enums;

namespace ShiftClub.IntegrationTests;

/// <summary>
/// Баланс клиента. Поле Balance в таблице — это кэш; правда лежит в ленте
/// операций. Эти тесты следят, чтобы кэш и лента не расходились, потому что
/// расхождение здесь — это деньги клуба или деньги клиента.
/// </summary>
[Collection(ClubDatabaseCollection.Name)]
public class CustomerLedgerTests
{
    private readonly ClubDatabaseFixture _fixture;

    public CustomerLedgerTests(ClubDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task DepositRaisesTheBalanceAndLeavesATrace()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var customer = await club.AddCustomerAsync();
        await club.Cash.OpenShiftAsync(new OpenCashShiftRequest(club.Register.Id, 0, null), club.Cashier.Id);

        await club.Customers.DepositAsync(
            customer.Id,
            new DepositCustomerRequest(3000, PaymentMethod.Cash, null, null),
            club.Cashier.Id);

        var stored = await club.ReloadCustomerAsync(customer.Id);
        Assert.Equal(3000m, stored.Balance);

        var ledger = await LedgerOf(club, customer.Id);
        var deposit = Assert.Single(ledger, t => t.Type == LedgerTransactionType.Deposit);
        Assert.Equal(0m, deposit.BalanceBefore);
        Assert.Equal(3000m, deposit.BalanceAfter);
        Assert.Equal(LedgerDirection.Credit, deposit.Direction);
    }

    [SkippableFact]
    public async Task RepeatedDepositWithTheSameKeyDoesNotDoubleTheMoney()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var customer = await club.AddCustomerAsync();
        await club.Cash.OpenShiftAsync(new OpenCashShiftRequest(club.Register.Id, 0, null), club.Cashier.Id);

        var request = new DepositCustomerRequest(2500, PaymentMethod.Cash, null, $"dep-{Guid.NewGuid()}");

        await club.Customers.DepositAsync(customer.Id, request, club.Cashier.Id);
        await club.Customers.DepositAsync(customer.Id, request, club.Cashier.Id);

        var stored = await club.ReloadCustomerAsync(customer.Id);
        Assert.Equal(2500m, stored.Balance);

        var deposits = await club.Db.CustomerBalanceTransactions.AsNoTracking()
            .CountAsync(t => t.CustomerId == customer.Id && t.Type == LedgerTransactionType.Deposit);
        Assert.Equal(1, deposits);
    }

    [SkippableFact]
    public async Task EveryLedgerRowContinuesFromThePreviousOne()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var customer = await club.AddCustomerAsync();
        await club.Cash.OpenShiftAsync(new OpenCashShiftRequest(club.Register.Id, 0, null), club.Cashier.Id);

        foreach (var amount in new decimal[] { 1000, 500, 2000 })
        {
            await club.Customers.DepositAsync(
                customer.Id,
                new DepositCustomerRequest(amount, PaymentMethod.Cash, null, null),
                club.Cashier.Id);
        }

        var ledger = (await LedgerOf(club, customer.Id))
            .Where(t => t.Type == LedgerTransactionType.Deposit)
            .ToList();

        foreach (var row in ledger)
        {
            var expected = row.Direction == LedgerDirection.Credit
                ? row.BalanceBefore + row.Amount
                : row.BalanceBefore - row.Amount;

            Assert.Equal(expected, row.BalanceAfter);
        }

        var stored = await club.ReloadCustomerAsync(customer.Id);
        Assert.Equal(3500m, stored.Balance);
        Assert.Equal(stored.Balance, ledger[^1].BalanceAfter);
    }

    [SkippableFact]
    public async Task RefundOfADepositTakesTheMoneyBackOffTheBalance()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var customer = await club.AddCustomerAsync();
        await club.Cash.OpenShiftAsync(new OpenCashShiftRequest(club.Register.Id, 0, null), club.Cashier.Id);

        var result = await club.Customers.DepositAsync(
            customer.Id,
            new DepositCustomerRequest(4000, PaymentMethod.Cash, null, null),
            club.Cashier.Id);

        var receiptId = await club.Db.Receipts.AsNoTracking()
            .Where(r => r.CustomerId == customer.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Id)
            .FirstAsync();

        await club.Cash.RefundReceiptAsync(receiptId, "вернули по просьбе гостя", club.Cashier.Id);

        var stored = await club.ReloadCustomerAsync(customer.Id);
        Assert.Equal(0m, stored.Balance);

        var refund = Assert.Single(
            await LedgerOf(club, customer.Id),
            t => t.Type == LedgerTransactionType.Refund);
        Assert.Equal(4000m, refund.Amount);
        Assert.Equal(0m, refund.BalanceAfter);
        Assert.NotNull(result);
    }

    [SkippableFact]
    public async Task DepositCannotBeRefundedIfTheGuestAlreadySpentIt()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var customer = await club.AddCustomerAsync();
        await club.Cash.OpenShiftAsync(new OpenCashShiftRequest(club.Register.Id, 0, null), club.Cashier.Id);

        await club.Customers.DepositAsync(
            customer.Id,
            new DepositCustomerRequest(1000, PaymentMethod.Cash, null, null),
            club.Cashier.Id);

        var receiptId = await club.Db.Receipts.AsNoTracking()
            .Where(r => r.CustomerId == customer.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Id)
            .FirstAsync();

        // Гость успел потратить деньги с баланса — возврат пополнения ушёл бы в минус.
        var spent = await club.Db.Customers.FirstAsync(c => c.Id == customer.Id);
        spent.Balance = 200;
        await club.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            club.Cash.RefundReceiptAsync(receiptId, null, club.Cashier.Id));

        Assert.Contains("Нельзя вернуть пополнение", error.Message);
    }

    [SkippableFact]
    public async Task DepositWithoutAnOpenShiftIsRejected()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var customer = await club.AddCustomerAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            club.Customers.DepositAsync(
                customer.Id,
                new DepositCustomerRequest(1000, PaymentMethod.Cash, null, null),
                club.Cashier.Id));

        var stored = await club.ReloadCustomerAsync(customer.Id);
        Assert.Equal(0m, stored.Balance);
    }

    [SkippableFact]
    public async Task NonPositiveDepositIsRejected()
    {
        Skip.If(!_fixture.Available, _fixture.SkipReason);
        using var club = await ClubScenario.CreateAsync(_fixture);

        var customer = await club.AddCustomerAsync();
        await club.Cash.OpenShiftAsync(new OpenCashShiftRequest(club.Register.Id, 0, null), club.Cashier.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            club.Customers.DepositAsync(
                customer.Id,
                new DepositCustomerRequest(0, PaymentMethod.Cash, null, null),
                club.Cashier.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            club.Customers.DepositAsync(
                customer.Id,
                new DepositCustomerRequest(-500, PaymentMethod.Cash, null, null),
                club.Cashier.Id));
    }

    private static async Task<List<Domain.Entities.CustomerBalanceTransaction>> LedgerOf(
        ClubScenario club,
        Guid customerId) =>
        await club.Db.CustomerBalanceTransactions.AsNoTracking()
            .Where(t => t.CustomerId == customerId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
}
