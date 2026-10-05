using ShiftClub.Shared.Contracts.Customers;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Application.Abstractions;

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerDto>> SearchAsync(string? query, Guid? branchId, CancellationToken cancellationToken = default);
    Task<CustomerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CustomerDto> CreateAsync(CreateCustomerRequest request, Guid branchId, Guid employeeId, CancellationToken cancellationToken = default);
    Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<CustomerDepositQuoteDto> QuoteDepositAsync(Guid id, decimal amount, CancellationToken cancellationToken = default);
    Task<CustomerDepositResultDto> DepositAsync(Guid id, DepositCustomerRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<CustomerDto> AdjustAsync(Guid id, AdjustCustomerBalanceRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task ChargeForSessionAsync(Guid customerId, decimal amount, Guid sessionId, Guid? receiptId, Guid employeeId, string? idempotencyKey, CancellationToken cancellationToken = default);
    /// <summary>Списание с бонусов (сначала) и баланса. Lifetime TotalSpent / лояльность — если нет связанного чека (иначе учтёт касса).</summary>
    Task ChargeFromWalletAsync(
        Guid customerId,
        decimal amount,
        LedgerTransactionType type,
        Guid? sessionId,
        Guid? receiptId,
        Guid employeeId,
        string? idempotencyKey,
        string comment,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerBalanceTransactionDto>> GetTransactionsAsync(Guid customerId, int take = 50, CancellationToken cancellationToken = default);

    Task<CustomerDto> AdjustTimeBankAsync(Guid id, AdjustCustomerTimeBankRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<CustomerDto> SetCredentialsAsync(Guid id, SetCustomerCredentialsRequest request, Guid employeeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerTimeBankTransactionDto>> GetTimeBankTransactionsAsync(Guid customerId, int take = 50, CancellationToken cancellationToken = default);
    Task ApplyTimeBankChangeAsync(
        Guid customerId,
        Guid zoneId,
        int minutes,
        LedgerDirection direction,
        TimeBankReason reason,
        Guid? employeeId,
        Guid? sessionId,
        string? comment,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);
    Task<int> GetZoneTimeBankMinutesAsync(Guid customerId, Guid zoneId, CancellationToken cancellationToken = default);
}
