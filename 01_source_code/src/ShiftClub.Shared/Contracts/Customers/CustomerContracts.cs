using ShiftClub.Shared.Enums;

namespace ShiftClub.Shared.Contracts.Customers;

public sealed record CustomerZoneTimeBankDto(Guid ZoneId, string ZoneName, int Minutes);

public sealed record CustomerDto(
    Guid Id,
    string FirstName,
    string LastName,
    string FullName,
    string Phone,
    string? Email,
    decimal Balance,
    decimal BonusBalance,
    int TimeBankMinutes,
    string? LoyaltyLevelName,
    int VisitCount,
    decimal TotalSpent,
    bool IsBlocked,
    string? BlockReason,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CustomerZoneTimeBankDto>? TimeBanks = null,
    string? Notes = null,
    string? Login = null,
    bool HasPassword = false,
    bool HasPin = false,
    Guid? LoyaltyLevelId = null,
    decimal LoyaltyBonusPercent = 0,
    decimal LoyaltyTimeDiscountPercent = 0,
    bool LoyaltyLevelLocked = false,
    int CaseKeysBalance = 0);

public sealed record CreateCustomerRequest(
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    string? Notes,
    string? Pin = null,
    string? Login = null,
    string? Password = null);

public sealed record UpdateCustomerRequest(
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    string? Notes,
    bool IsBlocked,
    string? BlockReason,
    Guid? LoyaltyLevelId = null,
    bool? LoyaltyLevelLocked = null);

public sealed record DepositCustomerRequest(
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? Comment,
    string? IdempotencyKey);

/// <summary>Превью бонусов при пополнении (как начислит сервер).</summary>
public sealed record CustomerDepositQuoteDto(
    decimal Amount,
    decimal LoyaltyBonusPercent,
    decimal LoyaltyBonusAmount,
    decimal TierMinAmount,
    decimal TierBonusAmount,
    decimal TotalBonusAmount,
    bool DepositBonusEnabled,
    bool StackWithLoyaltyPercent,
    decimal BalanceAfterDeposit,
    decimal BonusBalanceAfterDeposit);

public sealed record CustomerDepositResultDto(
    CustomerDto Customer,
    CustomerDepositQuoteDto Quote);

public sealed record AdjustCustomerBalanceRequest(
    decimal Amount,
    LedgerDirection Direction,
    string Comment,
    string? IdempotencyKey);

public sealed record AdjustCustomerTimeBankRequest(
    Guid ZoneId,
    int Minutes,
    LedgerDirection Direction,
    string Comment,
    string? IdempotencyKey);

public sealed record CustomerBalanceTransactionDto(
    Guid Id,
    LedgerTransactionType Type,
    LedgerDirection Direction,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string? Comment,
    DateTimeOffset CreatedAt,
    Guid? ReceiptId,
    Guid? GamingSessionId);

public sealed record CustomerTimeBankTransactionDto(
    Guid Id,
    Guid ZoneId,
    string? ZoneName,
    TimeBankReason Reason,
    LedgerDirection Direction,
    int Minutes,
    int BalanceBefore,
    int BalanceAfter,
    string? Comment,
    DateTimeOffset CreatedAt,
    Guid? GamingSessionId,
    Guid? EmployeeId);

public sealed record LoyaltyLevelDto(
    Guid Id,
    Guid BranchId,
    string Name,
    string Code,
    int MinSpent,
    decimal BonusPercent,
    decimal TimeDiscountPercent,
    int SortOrder,
    bool IsActive);

public sealed record UpsertLoyaltyLevelRequest(
    string Name,
    string Code,
    int MinSpent,
    decimal BonusPercent,
    decimal TimeDiscountPercent,
    int SortOrder = 0,
    bool IsActive = true);

