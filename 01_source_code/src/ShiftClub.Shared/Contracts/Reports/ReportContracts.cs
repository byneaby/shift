using ShiftClub.Shared.Enums;

namespace ShiftClub.Shared.Contracts.Reports;

public sealed record ReportPeriodRequest(DateOnly From, DateOnly To);

public sealed record MoneyByKeyDto(string Key, decimal Amount, int Count);

public sealed record SalesReportDto(
    DateOnly From,
    DateOnly To,
    int ReceiptCount,
    decimal GrossTotal,
    decimal DiscountTotal,
    /// <summary>Реализованная выручка (игры + бар + пакеты + списания с баланса), без пополнений и предоплат.</summary>
    decimal RevenueTotal,
    /// <summary>Собрано в кассу/безнал по чекам (включая депозиты).</summary>
    decimal CollectedTotal,
    /// <summary>Пополнения баланса + предоплаты броней.</summary>
    decimal DepositsTotal,
    /// <summary>Оплата игрового времени/бара с баланса клиента.</summary>
    decimal WalletRedeemedTotal,
    decimal AverageCheck,
    IReadOnlyList<MoneyByKeyDto> ByPaymentMethod,
    IReadOnlyList<MoneyByKeyDto> ByItemType,
    IReadOnlyList<MoneyByKeyDto> ByDay);

public sealed record ShiftReportRowDto(
    Guid Id,
    string Number,
    string CashRegisterName,
    CashShiftStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal OpeningCash,
    decimal? ClosingCashActual,
    decimal? ClosingCashExpected,
    decimal? Discrepancy,
    decimal SalesCash,
    decimal SalesCard,
    decimal SalesKaspi,
    decimal SalesTransfer,
    decimal SalesOther,
    decimal RefundsCash,
    decimal DepositsTotal,
    decimal CashInTotal,
    decimal CashOutTotal,
    decimal ExpenseTotal,
    decimal ReceiptsTotal,
    int ReceiptCount);

public sealed record ShiftsReportDto(
    DateOnly From,
    DateOnly To,
    int ShiftCount,
    decimal TotalSalesCash,
    decimal TotalSalesCard,
    decimal TotalSalesKaspi,
    decimal TotalSalesTransfer,
    decimal TotalSalesOther,
    decimal TotalDiscrepancyAbs,
    IReadOnlyList<ShiftReportRowDto> Shifts);

public sealed record ComputerLoadDto(
    Guid ComputerId,
    string ComputerName,
    string? ZoneName,
    int SessionCount,
    int MinutesPlayed,
    decimal Revenue,
    decimal UtilizationPercent);

public sealed record LoadReportDto(
    DateOnly From,
    DateOnly To,
    int SessionCount,
    int TotalMinutes,
    decimal TotalRevenue,
    decimal AverageDurationMinutes,
    decimal AverageCheck,
    IReadOnlyList<ComputerLoadDto> ByComputer,
    IReadOnlyList<MoneyByKeyDto> PeakHours);

public sealed record BarProductSalesDto(
    Guid ProductId,
    string Name,
    string CategoryName,
    decimal QtySold,
    decimal Revenue,
    decimal Cost,
    decimal Profit);

public sealed record BarReportDto(
    DateOnly From,
    DateOnly To,
    decimal Revenue,
    decimal Cost,
    decimal Profit,
    decimal MarginPercent,
    int ItemsSold,
    decimal AverageBarCheck,
    decimal WalletBarRevenue,
    IReadOnlyList<BarProductSalesDto> TopProducts,
    IReadOnlyList<MoneyByKeyDto> ByCategory,
    IReadOnlyList<MoneyByKeyDto> LowStock);

public sealed record CustomerReportDto(
    DateOnly From,
    DateOnly To,
    int TotalCustomers,
    int NewCustomers,
    int ActiveCustomers,
    decimal AverageBalance,
    decimal TotalBalances,
    decimal TotalBonuses,
    decimal TotalTimeBankMinutes,
    IReadOnlyList<MoneyByKeyDto> TopSpenders);

public sealed record OverviewReportDto(
    DateOnly From,
    DateOnly To,
    /// <summary>Реализованная выручка клуба за период.</summary>
    decimal RevenueTotal,
    /// <summary>Собрано деньгами/безналом (включая депозиты).</summary>
    decimal CollectedTotal,
    decimal DepositsTotal,
    decimal WalletRedeemedTotal,
    int ReceiptCount,
    int SessionCount,
    int BookingCount,
    decimal BarRevenue,
    int NewCustomers,
    decimal OpenShiftsCollected,
    decimal LiabilityBalances,
    decimal LiabilityBonuses,
    /// <summary>Игровое время (чеки + списания с баланса).</summary>
    decimal GamesRevenue,
    /// <summary>Пакеты из чеков.</summary>
    decimal PackagesRevenue,
    /// <summary>Продажа ключей SHIFT CASE.</summary>
    decimal CaseKeysRevenue = 0,
    int CaseKeysSold = 0,
    int CaseOpenings = 0,
    /// <summary>Оценка стоимости выпавших призов (CostEstimateKzt).</summary>
    decimal CasePrizeCostEstimate = 0,
    /// <summary>Начисления на баланс/бонусы из кейса (не выручка).</summary>
    decimal CasePrizeBalanceCredits = 0,
    int CasePrizeTimeMinutes = 0,
    /// <summary>Выручка за предыдущий период той же длины.</summary>
    decimal PrevRevenueTotal = 0,
    /// <summary>Δ% к предыдущему периоду (null если prev=0).</summary>
    decimal? RevenueDeltaPercent = null);
