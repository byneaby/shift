using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Time;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Reports;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Services;

public sealed class ReportService : IReportService
{
    private static readonly ReceiptItemType[] DepositTypes =
    [
        ReceiptItemType.BalanceTopUp,
        ReceiptItemType.BookingDeposit
    ];

    private static readonly ReceiptItemType[] RevenueTypes =
    [
        ReceiptItemType.GamingTime,
        ReceiptItemType.Package,
        ReceiptItemType.Product,
        ReceiptItemType.Other,
        ReceiptItemType.CaseKey
    ];

    private readonly ShiftClubDbContext _db;

    public ReportService(ShiftClubDbContext db)
    {
        _db = db;
    }

    public async Task<OverviewReportDto> GetOverviewAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = await RangeAsync(from, to, cancellationToken);

        var receipts = await _db.Receipts.AsNoTracking()
            .Include(r => r.Items)
            .Where(r => r.Status == ReceiptStatus.Paid && r.CreatedAt >= start && r.CreatedAt < end)
            .ToListAsync(cancellationToken);

        var collected = receipts.Sum(r => r.Total);
        var deposits = receipts.SelectMany(r => r.Items)
            .Where(i => DepositTypes.Contains(i.ItemType))
            .Sum(i => i.LineTotal);
        var receiptRevenue = receipts.SelectMany(r => r.Items)
            .Where(i => RevenueTypes.Contains(i.ItemType))
            .Sum(i => i.LineTotal);

        var walletGaming = await SumWalletAsync(
            start, end,
            [LedgerTransactionType.SessionCharge],
            "GamingSession",
            cancellationToken);
        var walletBar = await SumWalletAsync(
            start, end,
            [LedgerTransactionType.ProductPurchase],
            "Product",
            cancellationToken);

        var sessions = await _db.GamingSessions.AsNoTracking()
            .CountAsync(s => s.StartedAt >= start && s.StartedAt < end && !s.IsComplimentary, cancellationToken);

        var bookings = await _db.Bookings.AsNoTracking()
            .CountAsync(b => b.StartsAt >= start && b.StartsAt < end, cancellationToken);

        var barRevenue = receipts.SelectMany(r => r.Items)
            .Where(i => i.ItemType == ReceiptItemType.Product)
            .Sum(i => i.LineTotal) + walletBar;

        var gamesRevenue = receipts.SelectMany(r => r.Items)
            .Where(i => i.ItemType == ReceiptItemType.GamingTime)
            .Sum(i => i.LineTotal) + walletGaming;

        var packagesRevenue = receipts.SelectMany(r => r.Items)
            .Where(i => i.ItemType == ReceiptItemType.Package)
            .Sum(i => i.LineTotal);

        var caseKeyItems = receipts.SelectMany(r => r.Items)
            .Where(i => i.ItemType == ReceiptItemType.CaseKey)
            .ToList();
        var caseKeysRevenue = caseKeyItems.Sum(i => i.LineTotal);
        var caseKeysSold = (int)Math.Round(caseKeyItems.Sum(i => i.Quantity), 0, MidpointRounding.AwayFromZero);

        var openings = await _db.CaseOpenings.AsNoTracking()
            .Where(o => o.CreatedAt >= start && o.CreatedAt < end)
            .Select(o => new { o.PrizeCodeSnapshot, o.CasePrizeId })
            .ToListAsync(cancellationToken);
        var caseOpenings = openings.Count;

        var prizeIds = openings.Select(o => o.CasePrizeId).Distinct().ToList();
        var prizeCosts = await _db.CasePrizes.AsNoTracking()
            .Where(p => prizeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.CostEstimateKzt })
            .ToListAsync(cancellationToken);
        var costById = prizeCosts.ToDictionary(p => p.Id, p => p.CostEstimateKzt);
        var casePrizeCost = openings.Sum(o => costById.TryGetValue(o.CasePrizeId, out var c) ? c : 0m);

        var caseBalanceCredits = await _db.CustomerBalanceTransactions.AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt < end
                        && t.Direction == LedgerDirection.Credit
                        && t.SourceType == "ShiftCase")
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0;

        var caseTimeMinutes = await _db.CustomerTimeBankTransactions.AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt < end
                        && t.Direction == LedgerDirection.Credit
                        && t.IdempotencyKey != null
                        && t.IdempotencyKey.StartsWith("case-reward-time:"))
            .SumAsync(t => (int?)t.Minutes, cancellationToken) ?? 0;

        var revenueTotal = receiptRevenue + walletGaming + walletBar;

        var periodDays = Math.Max(1, to.DayNumber - from.DayNumber + 1);
        var prevTo = from.AddDays(-1);
        var prevFrom = prevTo.AddDays(-(periodDays - 1));
        var (prevStart, prevEnd) = await RangeAsync(prevFrom, prevTo, cancellationToken);
        var prevReceipts = await _db.Receipts.AsNoTracking()
            .Include(r => r.Items)
            .Where(r => r.Status == ReceiptStatus.Paid && r.CreatedAt >= prevStart && r.CreatedAt < prevEnd)
            .ToListAsync(cancellationToken);
        var prevReceiptRevenue = prevReceipts.SelectMany(r => r.Items)
            .Where(i => RevenueTypes.Contains(i.ItemType))
            .Sum(i => i.LineTotal);
        var prevWalletGaming = await SumWalletAsync(
            prevStart, prevEnd,
            [LedgerTransactionType.SessionCharge],
            "GamingSession",
            cancellationToken);
        var prevWalletBar = await SumWalletAsync(
            prevStart, prevEnd,
            [LedgerTransactionType.ProductPurchase],
            "Product",
            cancellationToken);
        var prevRevenue = prevReceiptRevenue + prevWalletGaming + prevWalletBar;
        decimal? deltaPct = prevRevenue <= 0
            ? null
            : Math.Round((revenueTotal - prevRevenue) / prevRevenue * 100m, 1, MidpointRounding.AwayFromZero);

        var newCustomers = await _db.Customers.AsNoTracking()
            .CountAsync(c => c.CreatedAt >= start && c.CreatedAt < end, cancellationToken);

        var openSales = await _db.CashShifts.AsNoTracking()
            .Where(s => s.Status == CashShiftStatus.Open)
            .SumAsync(s => (decimal?)(
                s.SalesCash + s.SalesCard + s.SalesKaspi + s.SalesTransfer + s.SalesOther
                - s.RefundsCash - s.RefundsCard - s.RefundsKaspi - s.RefundsTransfer - s.RefundsOther), cancellationToken) ?? 0;

        var liabilities = await _db.Customers.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new { c.Balance, c.BonusBalance })
            .ToListAsync(cancellationToken);

        return new OverviewReportDto(
            from, to,
            revenueTotal,
            collected,
            deposits,
            walletGaming + walletBar,
            receipts.Count,
            sessions,
            bookings,
            barRevenue,
            newCustomers,
            openSales,
            liabilities.Sum(x => x.Balance),
            liabilities.Sum(x => x.BonusBalance),
            gamesRevenue,
            packagesRevenue,
            caseKeysRevenue,
            caseKeysSold,
            caseOpenings,
            casePrizeCost,
            caseBalanceCredits,
            caseTimeMinutes,
            prevRevenue,
            deltaPct);
    }

    public async Task<SalesReportDto> GetSalesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = await RangeAsync(from, to, cancellationToken);
        var tzId = await ResolveTzAsync(cancellationToken);

        var receipts = await _db.Receipts.AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.Payments)
            .Where(r => r.Status == ReceiptStatus.Paid && r.CreatedAt >= start && r.CreatedAt < end)
            .ToListAsync(cancellationToken);

        var byPay = receipts
            .SelectMany(r => r.Payments)
            .GroupBy(p => p.Method.ToString())
            .Select(g => new MoneyByKeyDto(g.Key, g.Sum(x => x.Amount), g.Count()))
            .ToList();

        var walletGaming = await SumWalletAsync(
            start, end,
            [LedgerTransactionType.SessionCharge],
            "GamingSession",
            cancellationToken);
        var walletProducts = await SumWalletAsync(
            start, end,
            [LedgerTransactionType.ProductPurchase],
            "Product",
            cancellationToken);

        if (walletGaming > 0)
            byPay.Add(new MoneyByKeyDto("Balance (игры)", walletGaming, 1));
        if (walletProducts > 0)
            byPay.Add(new MoneyByKeyDto("Balance (бар)", walletProducts, 1));

        byPay = byPay.OrderByDescending(x => x.Amount).ToList();

        var byType = receipts
            .SelectMany(r => r.Items)
            .GroupBy(i => i.ItemType.ToString())
            .Select(g => new MoneyByKeyDto(g.Key, g.Sum(x => x.LineTotal), (int)g.Sum(x => x.Quantity)))
            .ToList();

        if (walletGaming > 0)
            byType.Add(new MoneyByKeyDto("GamingTimeBalance", walletGaming, 1));
        if (walletProducts > 0)
            byType.Add(new MoneyByKeyDto("ProductBalance", walletProducts, 1));

        byType = byType.OrderByDescending(x => x.Amount).ToList();

        var tz = BranchTimeZone.Resolve(tzId);
        var byDay = receipts
            .GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.CreatedAt, tz).DateTime))
            .Select(g => new MoneyByKeyDto(g.Key.ToString("yyyy-MM-dd"), g.Sum(x => x.Total), g.Count()))
            .OrderBy(x => x.Key)
            .ToList();

        var collected = receipts.Sum(r => r.Total);
        var deposits = receipts.SelectMany(r => r.Items)
            .Where(i => DepositTypes.Contains(i.ItemType))
            .Sum(i => i.LineTotal);
        var receiptRevenue = receipts.SelectMany(r => r.Items)
            .Where(i => RevenueTypes.Contains(i.ItemType))
            .Sum(i => i.LineTotal);
        var wallet = walletGaming + walletProducts;
        var revenue = receiptRevenue + wallet;
        var gross = receipts.Sum(r => r.Subtotal) + wallet;
        var discount = receipts.Sum(r => r.DiscountAmount);
        var count = receipts.Count;
        var avg = count == 0 ? 0 : Math.Round(collected / count, 2, MidpointRounding.AwayFromZero);

        return new SalesReportDto(
            from, to, count, gross, discount, revenue, collected, deposits, wallet, avg,
            byPay, byType, byDay);
    }

    public async Task<ShiftsReportDto> GetShiftsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = await RangeAsync(from, to, cancellationToken);

        var shifts = await _db.CashShifts.AsNoTracking()
            .Include(s => s.CashRegister)
            .Where(s => s.OpenedAt >= start && s.OpenedAt < end)
            .OrderByDescending(s => s.OpenedAt)
            .ToListAsync(cancellationToken);

        var shiftIds = shifts.Select(s => s.Id).ToList();
        var receiptAggs = await _db.Receipts.AsNoTracking()
            .Where(r => shiftIds.Contains(r.CashShiftId) && r.Status == ReceiptStatus.Paid)
            .GroupBy(r => r.CashShiftId)
            .Select(g => new { ShiftId = g.Key, Count = g.Count(), Total = g.Sum(x => x.Total) })
            .ToListAsync(cancellationToken);
        var map = receiptAggs.ToDictionary(x => x.ShiftId);

        var rows = shifts.Select(s =>
        {
            map.TryGetValue(s.Id, out var agg);
            return new ShiftReportRowDto(
                s.Id,
                s.Number,
                s.CashRegister?.Name ?? "",
                s.Status,
                s.OpenedAt,
                s.ClosedAt,
                s.OpeningCash,
                s.ClosingCashActual,
                s.ClosingCashExpected,
                s.Discrepancy,
                s.SalesCash,
                s.SalesCard,
                s.SalesKaspi,
                s.SalesTransfer,
                s.SalesOther,
                s.RefundsCash,
                s.DepositsTotal,
                s.CashInTotal,
                s.CashOutTotal,
                s.ExpenseTotal,
                agg?.Total ?? 0,
                agg?.Count ?? 0);
        }).ToList();

        return new ShiftsReportDto(
            from, to,
            rows.Count,
            rows.Sum(r => r.SalesCash),
            rows.Sum(r => r.SalesCard),
            rows.Sum(r => r.SalesKaspi),
            rows.Sum(r => r.SalesTransfer),
            rows.Sum(r => r.SalesOther),
            rows.Sum(r => Math.Abs(r.Discrepancy ?? 0)),
            rows);
    }

    public async Task<LoadReportDto> GetLoadAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = await RangeAsync(from, to, cancellationToken);
        var tzId = await ResolveTzAsync(cancellationToken);
        var tz = BranchTimeZone.Resolve(tzId);
        var periodMinutes = Math.Max(1, (end - start).TotalMinutes);

        var sessions = await _db.GamingSessions.AsNoTracking()
            .Include(s => s.Computer)
            .Include(s => s.Zone)
            .Where(s => s.StartedAt >= start && s.StartedAt < end && !s.IsComplimentary)
            .ToListAsync(cancellationToken);

        var totalMinutes = sessions.Sum(s => s.DurationMinutes);
        var revenue = sessions.Sum(s => s.PaidAmount);
        var avgDuration = sessions.Count == 0 ? 0 : Math.Round((decimal)totalMinutes / sessions.Count, 1);
        var avgCheck = sessions.Count == 0 ? 0 : Math.Round(revenue / sessions.Count, 2, MidpointRounding.AwayFromZero);

        var byPc = sessions
            .GroupBy(s => new
            {
                s.ComputerId,
                Name = s.Computer?.DisplayName ?? s.Computer?.WindowsName ?? s.ComputerId.ToString(),
                Zone = s.Zone?.Name
            })
            .Select(g =>
            {
                var minutes = g.Sum(x => x.DurationMinutes);
                var util = Math.Round((decimal)(minutes / periodMinutes * 100), 1, MidpointRounding.AwayFromZero);
                return new ComputerLoadDto(
                    g.Key.ComputerId,
                    g.Key.Name,
                    g.Key.Zone,
                    g.Count(),
                    minutes,
                    g.Sum(x => x.PaidAmount),
                    util);
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var peak = sessions
            .GroupBy(s => TimeZoneInfo.ConvertTime(s.StartedAt, tz).Hour)
            .Select(g => new MoneyByKeyDto($"{g.Key:00}:00", g.Sum(x => x.PaidAmount), g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(12)
            .ToList();

        return new LoadReportDto(
            from, to,
            sessions.Count,
            totalMinutes,
            revenue,
            avgDuration,
            avgCheck,
            byPc,
            peak);
    }

    public async Task<BarReportDto> GetBarAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = await RangeAsync(from, to, cancellationToken);

        var items = await _db.ReceiptItems.AsNoTracking()
            .Include(i => i.Receipt)
            .Where(i => i.ItemType == ReceiptItemType.Product
                        && i.Receipt.Status == ReceiptStatus.Paid
                        && i.Receipt.CreatedAt >= start && i.Receipt.CreatedAt < end)
            .ToListAsync(cancellationToken);

        var productIds = items.Where(i => i.ReferenceId.HasValue).Select(i => i.ReferenceId!.Value).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var sold = items
            .Where(i => i.ReferenceId.HasValue && products.ContainsKey(i.ReferenceId.Value))
            .GroupBy(i => i.ReferenceId!.Value)
            .Select(g =>
            {
                var p = products[g.Key];
                var qty = g.Sum(x => x.Quantity);
                var revenue = g.Sum(x => x.LineTotal);
                var cost = Math.Round(qty * p.CostPrice, 2, MidpointRounding.AwayFromZero);
                return new BarProductSalesDto(
                    p.Id, p.Name, p.Category?.Name ?? "",
                    qty, revenue, cost, revenue - cost);
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();

        var walletBar = await SumWalletAsync(
            start, end,
            [LedgerTransactionType.ProductPurchase],
            "Product",
            cancellationToken);

        var revenue = sold.Sum(x => x.Revenue) + walletBar;
        var cost = sold.Sum(x => x.Cost);
        var profit = revenue - cost;
        var margin = revenue == 0 ? 0 : Math.Round(profit / revenue * 100, 1, MidpointRounding.AwayFromZero);
        var itemsSold = (int)sold.Sum(x => x.QtySold);

        var barReceiptIds = items.Select(i => i.ReceiptId).Distinct().Count();
        var avgCheck = barReceiptIds == 0 ? 0 : Math.Round(sold.Sum(x => x.Revenue) / barReceiptIds, 2, MidpointRounding.AwayFromZero);

        var byCat = sold
            .GroupBy(x => x.CategoryName)
            .Select(g => new MoneyByKeyDto(g.Key, g.Sum(x => x.Revenue), (int)g.Sum(x => x.QtySold)))
            .OrderByDescending(x => x.Amount)
            .ToList();
        if (walletBar > 0)
            byCat.Add(new MoneyByKeyDto("С баланса", walletBar, 1));

        var lowStock = await _db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive && p.StockQty <= p.MinStockQty)
            .OrderBy(p => p.StockQty)
            .Take(20)
            .Select(p => new MoneyByKeyDto($"{p.Name} ({p.Category!.Name})", p.StockQty, (int)p.MinStockQty))
            .ToListAsync(cancellationToken);

        return new BarReportDto(
            from, to, revenue, cost, profit, margin, itemsSold, avgCheck, walletBar,
            sold.Take(20).ToList(), byCat, lowStock);
    }

    public async Task<CustomerReportDto> GetCustomersAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = await RangeAsync(from, to, cancellationToken);

        var customers = await _db.Customers.AsNoTracking()
            .Where(c => c.IsActive)
            .ToListAsync(cancellationToken);

        var newCount = customers.Count(c => c.CreatedAt >= start && c.CreatedAt < end);

        var activeIds = await _db.GamingSessions.AsNoTracking()
            .Where(s => s.CustomerId != null && s.StartedAt >= start && s.StartedAt < end)
            .Select(s => s.CustomerId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var ledgerActive = await _db.CustomerBalanceTransactions.AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt < end)
            .Select(t => t.CustomerId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var activeSet = activeIds.Concat(ledgerActive).ToHashSet();
        var active = customers.Count(c => activeSet.Contains(c.Id));

        var avgBalance = customers.Count == 0
            ? 0
            : Math.Round(customers.Average(c => c.Balance), 2, MidpointRounding.AwayFromZero);

        var periodSpend = await _db.CustomerBalanceTransactions.AsNoTracking()
            .Where(t => t.Direction == LedgerDirection.Debit && t.CreatedAt >= start && t.CreatedAt < end)
            .GroupBy(t => t.CustomerId)
            .Select(g => new { CustomerId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);
        var spendMap = periodSpend.ToDictionary(x => x.CustomerId, x => x.Amount);

        var top = customers
            .Select(c =>
            {
                spendMap.TryGetValue(c.Id, out var spent);
                return new { c, Spent = spent };
            })
            .Where(x => x.Spent > 0)
            .OrderByDescending(x => x.Spent)
            .Take(15)
            .Select(x => new MoneyByKeyDto(
                $"{x.c.FirstName} {x.c.LastName}".Trim(),
                x.Spent,
                x.c.VisitCount))
            .ToList();

        return new CustomerReportDto(
            from, to,
            customers.Count,
            newCount,
            active,
            avgBalance,
            customers.Sum(c => c.Balance),
            customers.Sum(c => c.BonusBalance),
            customers.Sum(c => c.TimeBankMinutes),
            top);
    }

    private async Task<decimal> SumWalletAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        LedgerTransactionType[] types,
        string bonusSource,
        CancellationToken cancellationToken)
    {
        return await _db.CustomerBalanceTransactions.AsNoTracking()
            .Where(t => t.Direction == LedgerDirection.Debit
                        && t.CreatedAt >= start && t.CreatedAt < end
                        && (types.Contains(t.Type)
                            || (t.Type == LedgerTransactionType.BonusDebit && t.SourceType == bonusSource)))
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0;
    }

    private async Task<(DateTimeOffset Start, DateTimeOffset End)> RangeAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (to < from)
            (from, to) = (to, from);

        var tzId = await ResolveTzAsync(cancellationToken);
        var start = BranchTimeZone.DayBoundsUtc(from, tzId).DayStartUtc;
        var end = BranchTimeZone.DayBoundsUtc(to, tzId).DayEndUtc;
        return (start, end);
    }

    private async Task<string?> ResolveTzAsync(CancellationToken cancellationToken)
    {
        return await _db.Branches.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => b.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
