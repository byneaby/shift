using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Services;

public sealed class CashService : ICashService
{
    private static readonly ReceiptItemType[] DepositItemTypes =
    [
        ReceiptItemType.BalanceTopUp,
        ReceiptItemType.BookingDeposit
    ];

    private static readonly ReceiptItemType[] RevenueItemTypes =
    [
        ReceiptItemType.GamingTime,
        ReceiptItemType.Package,
        ReceiptItemType.Product,
        ReceiptItemType.Other,
        ReceiptItemType.CaseKey
    ];

    private readonly ShiftClubDbContext _db;
    private readonly IDocumentNumberService _numbers;

    public CashService(ShiftClubDbContext db, IDocumentNumberService numbers)
    {
        _db = db;
        _numbers = numbers;
    }

    public async Task<IReadOnlyList<CashRegisterDto>> GetRegistersAsync(
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.CashRegisters.AsNoTracking().Where(r => r.IsActive);
        if (branchId.HasValue)
            query = query.Where(r => r.BranchId == branchId.Value);

        var list = await query.OrderBy(r => r.Name).ToListAsync(cancellationToken);
        return list.Select(r => new CashRegisterDto(r.Id, r.BranchId, r.Name, r.Code, r.IsActive)).ToList();
    }

    public async Task<CashShiftDto?> GetOpenShiftAsync(
        Guid? cashRegisterId,
        Guid? employeeId,
        CancellationToken cancellationToken = default)
    {
        if (employeeId.HasValue)
        {
            var resolved = await ResolveOpenShiftAsync(employeeId.Value, forUpdate: false, cancellationToken);
            if (resolved is null)
                return null;
            if (cashRegisterId.HasValue && resolved.Shift.CashRegisterId != cashRegisterId.Value)
                return null;
            return MapShift(resolved.Shift);
        }

        var query = _db.CashShifts.AsNoTracking()
            .Include(s => s.CashRegister)
            .Where(s => s.Status == CashShiftStatus.Open);

        if (cashRegisterId.HasValue)
            query = query.Where(s => s.CashRegisterId == cashRegisterId.Value);

        var shift = await query.OrderByDescending(s => s.OpenedAt).FirstOrDefaultAsync(cancellationToken);
        return shift is null ? null : MapShift(shift);
    }

    public async Task EnsureEmployeeHasOpenShiftAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveOpenShiftAsync(employeeId, forUpdate: false, cancellationToken);
        if (resolved is null)
        {
            var isOwner = await IsOwnerAsync(employeeId, cancellationToken);
            throw new InvalidOperationException(isOwner
                ? "Нет открытой кассовой смены у сотрудников. Кто-то должен открыть смену — владелец сможет проводить продажи в ней."
                : "Нет открытой кассовой смены. Откройте смену на странице «Касса».");
        }
    }

    public async Task<CashOwnerProxyInfo?> GetOwnerCashProxyInfoAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveOpenShiftAsync(employeeId, forUpdate: false, cancellationToken);
        if (resolved is null || !resolved.IsOwnerProxy)
            return null;
        return new CashOwnerProxyInfo(
            resolved.Shift.Id,
            resolved.Shift.Number,
            resolved.CashierId,
            resolved.CashierName,
            resolved.ActorName);
    }

    public async Task<ReceiptDto> CreateSaleAsync(
        CreateSaleRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new InvalidOperationException("Receipt must contain items.");
        if (request.Payments is null || request.Payments.Count == 0)
            throw new InvalidOperationException("Receipt must contain payments.");

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.Receipts.AsNoTracking()
                .Include(r => r.Items)
                .Include(r => r.Payments)
                .FirstOrDefaultAsync(r => r.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
                return await MapReceiptAsync(existing, cancellationToken);
        }

        var resolved = await ResolveOpenShiftAsync(employeeId, forUpdate: true, cancellationToken)
            ?? throw new InvalidOperationException(
                await IsOwnerAsync(employeeId, cancellationToken)
                    ? "Нет открытой кассовой смены у сотрудников. Кто-то должен открыть смену — владелец сможет проводить продажи в ней."
                    : "Нет открытой кассовой смены.");

        var shift = resolved.Shift;

        var subtotal = request.Items.Sum(i => i.Quantity * i.UnitPrice);
        var discount = request.Items.Sum(i => i.DiscountAmount);
        var total = Math.Round(subtotal - discount, 2, MidpointRounding.AwayFromZero);
        var paid = request.Payments.Sum(p => p.Amount);
        if (paid != total)
            throw new InvalidOperationException($"Сумма оплат ({paid}) не равна итогу чека ({total}).");

        var number = await _numbers.NextAsync(shift.BranchId, DocumentSequenceType.Receipt, cancellationToken);

        var comment = request.Comment;
        if (resolved.IsOwnerProxy)
        {
            var proxyNote =
                $"Владелец {resolved.ActorName} · смена {resolved.Shift.Number} ({resolved.CashierName})";
            comment = string.IsNullOrWhiteSpace(comment) ? proxyNote : $"{comment} · {proxyNote}";
        }

        var receipt = new Receipt
        {
            BranchId = shift.BranchId,
            CashShiftId = shift.Id,
            Number = number,
            Status = ReceiptStatus.Paid,
            ComputerId = request.ComputerId,
            GamingSessionId = request.GamingSessionId,
            CustomerId = request.CustomerId,
            CreatedByEmployeeId = employeeId,
            Subtotal = Math.Round(subtotal, 2, MidpointRounding.AwayFromZero),
            DiscountAmount = Math.Round(discount, 2, MidpointRounding.AwayFromZero),
            Total = total,
            PaidTotal = paid,
            Comment = comment,
            IdempotencyKey = request.IdempotencyKey
        };

        decimal depositLines = 0;
        foreach (var item in request.Items)
        {
            var line = Math.Round(item.Quantity * item.UnitPrice - item.DiscountAmount, 2, MidpointRounding.AwayFromZero);
            receipt.Items.Add(new ReceiptItem
            {
                ItemType = item.ItemType,
                Name = item.Name,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                DiscountAmount = item.DiscountAmount,
                LineTotal = line,
                ReferenceId = item.ReferenceId
            });
            if (DepositItemTypes.Contains(item.ItemType))
                depositLines += line;
        }

        foreach (var payment in request.Payments)
        {
            receipt.Payments.Add(new Payment
            {
                Method = payment.Method,
                Amount = payment.Amount
            });
            ApplyPaymentToShift(shift, payment.Method, payment.Amount);
        }

        shift.DepositsTotal += depositLines;

        var auditDetails = resolved.IsOwnerProxy
            ? $"{{\"number\":\"{number}\",\"total\":{total.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"ownerProxy\":true,\"owner\":{JsonStr(resolved.ActorName)},\"shiftNumber\":\"{resolved.Shift.Number}\",\"cashier\":{JsonStr(resolved.CashierName)},\"cashierId\":\"{resolved.CashierId}\"}}"
            : $"{{\"number\":\"{number}\",\"total\":{total.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";

        _db.Receipts.Add(receipt);
        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = shift.BranchId,
            EmployeeId = employeeId,
            Action = resolved.IsOwnerProxy ? "cash.receipt.create.owner_proxy" : "cash.receipt.create",
            EntityType = nameof(Receipt),
            EntityId = receipt.Id.ToString(),
            DetailsJson = auditDetails
        });

        // Лояльность: покупки (время/бар/ключи), не пополнение баланса.
        var spendForLoyalty = Math.Round(total - depositLines, 2, MidpointRounding.AwayFromZero);
        if (request.CustomerId is Guid customerId && spendForLoyalty > 0)
        {
            var customer = await _db.Customers
                .Include(c => c.LoyaltyLevel)
                .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
            if (customer is not null && !customer.IsBlocked)
                await LoyaltyProgress.ApplySpendAsync(_db, customer, spendForLoyalty, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await MapReceiptAsync(receipt, cancellationToken);
    }

    private sealed record ResolvedOpenShift(
        CashShift Shift,
        bool IsOwnerProxy,
        Guid CashierId,
        string CashierName,
        string ActorName);

    private async Task<ResolvedOpenShift?> ResolveOpenShiftAsync(
        Guid employeeId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var shifts = forUpdate ? _db.CashShifts : _db.CashShifts.AsNoTracking();

        var own = await shifts
            .FirstOrDefaultAsync(
                s => s.Status == CashShiftStatus.Open && s.OpenedByEmployeeId == employeeId,
                cancellationToken);
        if (own is not null)
        {
            var selfName = await GetEmployeeDisplayNameAsync(employeeId, cancellationToken);
            return new ResolvedOpenShift(own, false, employeeId, selfName, selfName);
        }

        if (!await IsOwnerAsync(employeeId, cancellationToken))
            return null;

        var any = await shifts
            .Where(s => s.Status == CashShiftStatus.Open)
            .OrderByDescending(s => s.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (any is null)
            return null;

        var ownerName = await GetEmployeeDisplayNameAsync(employeeId, cancellationToken);
        var cashierName = await GetEmployeeDisplayNameAsync(any.OpenedByEmployeeId, cancellationToken);
        return new ResolvedOpenShift(any, true, any.OpenedByEmployeeId, cashierName, ownerName);
    }

    private Task<bool> IsOwnerAsync(Guid employeeId, CancellationToken cancellationToken) =>
        _db.EmployeeRoles.AsNoTracking()
            .AnyAsync(
                er => er.EmployeeId == employeeId && er.Role.Code == "owner",
                cancellationToken);

    private async Task<string> GetEmployeeDisplayNameAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var e = await _db.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => new { x.DisplayName, x.FirstName, x.LastName, x.Login })
            .FirstOrDefaultAsync(cancellationToken);
        if (e is null)
            return employeeId.ToString("N")[..8];
        if (!string.IsNullOrWhiteSpace(e.DisplayName))
            return e.DisplayName.Trim();
        var full = $"{e.FirstName} {e.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? e.Login : full;
    }

    private static string JsonStr(string? value) =>
        System.Text.Json.JsonSerializer.Serialize(value ?? "");

    public async Task<CashShiftDto> OpenShiftAsync(
        OpenCashShiftRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.OpeningCash < 0)
            throw new InvalidOperationException("OpeningCash cannot be negative.");

        var register = await _db.CashRegisters
            .FirstOrDefaultAsync(r => r.Id == request.CashRegisterId && r.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Cash register not found.");

        var hasOpenOnRegister = await _db.CashShifts.AnyAsync(
            s => s.CashRegisterId == register.Id && s.Status == CashShiftStatus.Open,
            cancellationToken);
        if (hasOpenOnRegister)
            throw new InvalidOperationException("На этой кассе уже есть открытая смена.");

        var employeeHasOpen = await _db.CashShifts.AnyAsync(
            s => s.OpenedByEmployeeId == employeeId && s.Status == CashShiftStatus.Open,
            cancellationToken);
        if (employeeHasOpen)
            throw new InvalidOperationException("У сотрудника уже есть открытая смена.");

        var number = await _numbers.NextAsync(register.BranchId, DocumentSequenceType.CashShift, cancellationToken);
        var shift = new CashShift
        {
            CashRegisterId = register.Id,
            BranchId = register.BranchId,
            Number = number,
            Status = CashShiftStatus.Open,
            OpenedByEmployeeId = employeeId,
            OpenedAt = DateTimeOffset.UtcNow,
            OpeningCash = request.OpeningCash,
            OpenComment = request.Comment
        };

        _db.CashShifts.Add(shift);
        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = register.BranchId,
            EmployeeId = employeeId,
            Action = "cash.shift.open",
            EntityType = nameof(CashShift),
            EntityId = shift.Id.ToString(),
            DetailsJson = $"{{\"openingCash\":{request.OpeningCash},\"register\":\"{register.Code}\"}}"
        });

        await _db.SaveChangesAsync(cancellationToken);
        shift.CashRegister = register;
        return MapShift(shift);
    }

    public Task<CashShiftDto> CloseShiftAsync(
        Guid shiftId,
        CloseCashShiftRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default) =>
        CloseShiftCoreAsync(shiftId, request, employeeId, force: false, cancellationToken);

    public Task<CashShiftDto> ForceCloseShiftAsync(
        Guid shiftId,
        CloseCashShiftRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default) =>
        CloseShiftCoreAsync(shiftId, request, employeeId, force: true, cancellationToken);

    private async Task<CashShiftDto> CloseShiftCoreAsync(
        Guid shiftId,
        CloseCashShiftRequest request,
        Guid employeeId,
        bool force,
        CancellationToken cancellationToken)
    {
        if (request.ClosingCashActual < 0)
            throw new InvalidOperationException("ClosingCashActual cannot be negative.");

        var shift = await _db.CashShifts.Include(s => s.CashRegister)
            .FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken)
            ?? throw new KeyNotFoundException("Shift not found.");

        if (shift.Status != CashShiftStatus.Open)
            throw new InvalidOperationException("Shift is not open.");

        if (!force && shift.OpenedByEmployeeId != employeeId)
            throw new InvalidOperationException(
                "Смену может закрыть только открывший её сотрудник. Для чужой смены — принудительное закрытие.");

        var expected = CalculateExpectedCash(shift);
        var discrepancy = request.ClosingCashActual - expected;

        shift.Status = force ? CashShiftStatus.ForceClosed : CashShiftStatus.Closed;
        shift.ClosedAt = DateTimeOffset.UtcNow;
        shift.ClosedByEmployeeId = employeeId;
        shift.ClosingCashActual = request.ClosingCashActual;
        shift.ClosingCashExpected = expected;
        shift.Discrepancy = discrepancy;
        shift.DiscrepancyReason = request.DiscrepancyReason;
        shift.CloseComment = request.Comment;
        shift.UpdatedAt = DateTimeOffset.UtcNow;
        shift.UpdatedBy = employeeId;

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = shift.BranchId,
            EmployeeId = employeeId,
            Action = force ? "cash.shift.force_close" : "cash.shift.close",
            EntityType = nameof(CashShift),
            EntityId = shift.Id.ToString(),
            DetailsJson = $"{{\"expected\":{expected},\"actual\":{request.ClosingCashActual},\"discrepancy\":{discrepancy}}}"
        });

        await _db.SaveChangesAsync(cancellationToken);
        return MapShift(shift);
    }

    public async Task<CashShiftDto> AddMovementAsync(
        Guid shiftId,
        CashMovementRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            throw new InvalidOperationException("Amount must be positive.");

        var shift = await _db.CashShifts.Include(s => s.CashRegister)
            .FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken)
            ?? throw new KeyNotFoundException("Shift not found.");

        if (shift.Status != CashShiftStatus.Open)
            throw new InvalidOperationException("Shift is not open.");
        if (shift.OpenedByEmployeeId != employeeId)
            throw new InvalidOperationException("Движения доступны только открывшему смену.");

        _db.CashMovements.Add(new CashMovement
        {
            CashShiftId = shift.Id,
            Type = request.Type,
            Amount = request.Amount,
            EmployeeId = employeeId,
            Category = request.Category,
            Comment = request.Comment
        });

        switch (request.Type)
        {
            case CashMovementType.CashIn:
                shift.CashInTotal += request.Amount;
                break;
            case CashMovementType.CashOut:
            case CashMovementType.ServiceIssue:
                shift.CashOutTotal += request.Amount;
                break;
            case CashMovementType.Expense:
                shift.ExpenseTotal += request.Amount;
                break;
        }

        shift.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return MapShift(shift);
    }


    public async Task<IReadOnlyList<ReceiptDto>> GetRecentReceiptsAsync(
        Guid shiftId,
        int take = 30,
        CancellationToken cancellationToken = default)
    {
        var list = await _db.Receipts.AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.Payments)
            .Where(r => r.CashShiftId == shiftId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        var computerIds = list
            .Where(r => r.ComputerId.HasValue)
            .Select(r => r.ComputerId!.Value)
            .Distinct()
            .ToList();
        var computers = computerIds.Count == 0
            ? new Dictionary<Guid, (string Name, StationKind Kind)>()
            : (await _db.Computers.AsNoTracking()
                .Where(c => computerIds.Contains(c.Id))
                .Select(c => new { c.Id, c.DisplayName, c.WindowsName, c.StationKind })
                .ToListAsync(cancellationToken))
                .ToDictionary(
                    c => c.Id,
                    c => (
                        Name: string.IsNullOrWhiteSpace(c.DisplayName) ? c.WindowsName : c.DisplayName!,
                        Kind: c.StationKind));

        return list.Select(r =>
        {
            string? name = null;
            StationKind? kind = null;
            if (r.ComputerId is Guid cid && computers.TryGetValue(cid, out var info))
            {
                name = info.Name;
                kind = info.Kind;
            }
            return MapReceipt(r, name, kind);
        }).ToList();
    }

    public async Task<CashZReportDto> GetShiftReportAsync(Guid shiftId, CancellationToken cancellationToken = default)
    {
        var shift = await _db.CashShifts.AsNoTracking()
            .Include(s => s.CashRegister)
            .Include(s => s.Movements)
            .FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken)
            ?? throw new KeyNotFoundException("Смена не найдена.");

        var receipts = await _db.Receipts.AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.Payments)
            .Where(r => r.CashShiftId == shiftId)
            .ToListAsync(cancellationToken);

        var paid = receipts.Where(r => r.Status == ReceiptStatus.Paid).ToList();
        var refunded = receipts.Where(r => r.Status == ReceiptStatus.Refunded).ToList();

        var byPay = paid
            .SelectMany(r => r.Payments)
            .GroupBy(p => p.Method.ToString())
            .Select(g => new MoneyKeyDto(g.Key, g.Sum(x => x.Amount), g.Count()))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var byType = paid
            .SelectMany(r => r.Items)
            .GroupBy(i => i.ItemType.ToString())
            .Select(g => new MoneyKeyDto(g.Key, g.Sum(x => x.LineTotal), (int)g.Sum(x => x.Quantity)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var revenue = paid.SelectMany(r => r.Items)
            .Where(i => RevenueItemTypes.Contains(i.ItemType))
            .Sum(i => i.LineTotal);
        var deposits = paid.SelectMany(r => r.Items)
            .Where(i => DepositItemTypes.Contains(i.ItemType))
            .Sum(i => i.LineTotal);

        var movements = shift.Movements
            .OrderBy(m => m.CreatedAt)
            .Select(m => new CashMovementRowDto(m.Type, m.Amount, m.Category, m.Comment, m.CreatedAt))
            .ToList();

        return new CashZReportDto(
            shift.Id,
            shift.Number,
            shift.CashRegister?.Name ?? "",
            shift.Status,
            shift.OpenedByEmployeeId,
            shift.OpenedAt,
            shift.ClosedAt,
            shift.OpeningCash,
            shift.ClosingCashActual,
            shift.ClosingCashExpected,
            shift.Discrepancy,
            shift.DiscrepancyReason,
            shift.SalesCash,
            shift.SalesCard,
            shift.SalesKaspi,
            shift.SalesTransfer,
            shift.SalesOther,
            shift.RefundsCash,
            shift.RefundsCard,
            shift.RefundsKaspi,
            shift.RefundsTransfer,
            shift.RefundsOther,
            shift.CashInTotal,
            shift.CashOutTotal,
            shift.ExpenseTotal,
            CalculateExpectedCash(shift),
            CollectedNow(shift),
            revenue,
            deposits,
            paid.Count,
            refunded.Count,
            byPay,
            byType,
            movements);
    }

    public async Task<ReceiptDto> RefundReceiptAsync(
        Guid receiptId,
        string? comment,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var receipt = await _db.Receipts
            .Include(r => r.Items)
            .Include(r => r.Payments)
            .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken)
            ?? throw new KeyNotFoundException("Чек не найден.");

        if (receipt.Status == ReceiptStatus.Refunded)
            return await MapReceiptAsync(receipt, cancellationToken);
        if (receipt.Status != ReceiptStatus.Paid)
            throw new InvalidOperationException("Вернуть можно только оплаченный чек.");

        var shift = await _db.CashShifts
            .FirstOrDefaultAsync(s => s.Id == receipt.CashShiftId, cancellationToken)
            ?? throw new KeyNotFoundException("Смена чека не найдена.");

        // Возврат в открытой смене — только кассир этой смены.
        // После закрытия смены — разрешаем (для отмены брони / исправлений), счётчики правятся на той же смене.
        if (shift.Status == CashShiftStatus.Open && shift.OpenedByEmployeeId != employeeId)
            throw new InvalidOperationException("Возврат только в своей открытой смене, где был чек.");

        foreach (var payment in receipt.Payments)
            ApplyRefundToShift(shift, payment.Method, payment.Amount);

        var depositLines = receipt.Items.Where(i => DepositItemTypes.Contains(i.ItemType)).Sum(i => i.LineTotal);
        if (depositLines > 0)
            shift.DepositsTotal = Math.Max(0, shift.DepositsTotal - depositLines);

        await ReverseBalanceTopUpAsync(receipt, comment, employeeId, cancellationToken);
        await RestoreProductStockAsync(receipt, employeeId, cancellationToken);
        await ReverseBookingPrepayAsync(receipt, cancellationToken);

        receipt.Status = ReceiptStatus.Refunded;
        receipt.Comment = string.IsNullOrWhiteSpace(comment)
            ? $"Возврат · {receipt.Comment}"
            : $"Возврат: {comment.Trim()} · {receipt.Comment}";
        receipt.UpdatedAt = DateTimeOffset.UtcNow;
        shift.UpdatedAt = DateTimeOffset.UtcNow;

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = shift.BranchId,
            EmployeeId = employeeId,
            Action = "cash.receipt.refund",
            EntityType = nameof(Receipt),
            EntityId = receipt.Id.ToString(),
            DetailsJson = $"{{\"number\":\"{receipt.Number}\",\"total\":{receipt.Total}}}"
        });

        await _db.SaveChangesAsync(cancellationToken);
        return await MapReceiptAsync(receipt, cancellationToken);
    }

    private async Task ReverseBalanceTopUpAsync(
        Receipt receipt,
        string? comment,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var topUp = receipt.Items.Where(i => i.ItemType == ReceiptItemType.BalanceTopUp).Sum(i => i.LineTotal);
        if (topUp <= 0 || receipt.CustomerId is not Guid customerId)
            return;

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
            return;

        if (customer.Balance < topUp)
            throw new InvalidOperationException(
                $"Нельзя вернуть пополнение: на балансе клиента только {customer.Balance:0} ₸.");

        var before = customer.Balance;
        customer.Balance -= topUp;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        _db.CustomerBalanceTransactions.Add(new CustomerBalanceTransaction
        {
            CustomerId = customer.Id,
            Type = LedgerTransactionType.Refund,
            Direction = LedgerDirection.Debit,
            Amount = topUp,
            BalanceBefore = before,
            BalanceAfter = customer.Balance,
            EmployeeId = employeeId,
            ReceiptId = receipt.Id,
            SourceType = "Refund",
            Comment = comment ?? $"Возврат пополнения по чеку {receipt.Number}"
        });
    }

    private async Task RestoreProductStockAsync(Receipt receipt, Guid employeeId, CancellationToken cancellationToken)
    {
        foreach (var item in receipt.Items.Where(i => i.ItemType == ReceiptItemType.Product && i.ReferenceId.HasValue))
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ReferenceId!.Value, cancellationToken);
            if (product is null)
                continue;

            product.StockQty += item.Quantity;
            product.UpdatedAt = DateTimeOffset.UtcNow;
            product.UpdatedBy = employeeId;

            _db.InventoryMovements.Add(new InventoryMovement
            {
                BranchId = product.BranchId,
                ProductId = product.Id,
                Type = InventoryMovementType.SaleReturn,
                QuantityDelta = item.Quantity,
                StockAfter = product.StockQty,
                UnitCost = product.CostPrice,
                EmployeeId = employeeId,
                ReferenceId = receipt.Id,
                Comment = $"Возврат по чеку {receipt.Number}"
            });
        }
    }

    private async Task ReverseBookingPrepayAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        var hasDeposit = receipt.Items.Any(i => i.ItemType == ReceiptItemType.BookingDeposit);
        if (!hasDeposit)
            return;

        var bookings = await _db.Bookings
            .Where(b => b.PrepayReceiptId == receipt.Id)
            .ToListAsync(cancellationToken);

        foreach (var booking in bookings)
        {
            booking.PrepaidAmount = 0;
            booking.PrepayMethod = null;
            booking.PrepayReceiptId = null;
            booking.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static void ApplyPaymentToShift(CashShift shift, PaymentMethod method, decimal amount)
    {
        switch (method)
        {
            case PaymentMethod.Cash:
                shift.SalesCash += amount;
                break;
            case PaymentMethod.Card:
                shift.SalesCard += amount;
                break;
            case PaymentMethod.KaspiQr:
                shift.SalesKaspi += amount;
                break;
            case PaymentMethod.Transfer:
                shift.SalesTransfer += amount;
                break;
            default:
                shift.SalesOther += amount;
                break;
        }
    }

    private static void ApplyRefundToShift(CashShift shift, PaymentMethod method, decimal amount)
    {
        switch (method)
        {
            case PaymentMethod.Cash:
                shift.RefundsCash += amount;
                break;
            case PaymentMethod.Card:
                shift.RefundsCard += amount;
                shift.SalesCard = Math.Max(0, shift.SalesCard - amount);
                break;
            case PaymentMethod.KaspiQr:
                shift.RefundsKaspi += amount;
                shift.SalesKaspi = Math.Max(0, shift.SalesKaspi - amount);
                break;
            case PaymentMethod.Transfer:
                shift.RefundsTransfer += amount;
                shift.SalesTransfer = Math.Max(0, shift.SalesTransfer - amount);
                break;
            default:
                shift.RefundsOther += amount;
                shift.SalesOther = Math.Max(0, shift.SalesOther - amount);
                break;
        }
    }

    private static decimal CalculateExpectedCash(CashShift shift) =>
        shift.OpeningCash + shift.SalesCash + shift.CashInTotal - shift.CashOutTotal - shift.ExpenseTotal - shift.RefundsCash;

    private static decimal CollectedNow(CashShift shift) =>
        shift.SalesCash + shift.SalesCard + shift.SalesKaspi + shift.SalesTransfer + shift.SalesOther
        - shift.RefundsCash - shift.RefundsCard - shift.RefundsKaspi - shift.RefundsTransfer - shift.RefundsOther;

    private static decimal RevenueNow(CashShift shift) =>
        Math.Max(0, CollectedNow(shift) - shift.DepositsTotal);

    private static CashShiftDto MapShift(CashShift s) => new(
        s.Id,
        s.CashRegisterId,
        s.CashRegister?.Name ?? string.Empty,
        s.BranchId,
        s.Number,
        s.Status,
        s.OpenedByEmployeeId,
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
        s.RefundsCard,
        s.RefundsKaspi,
        s.RefundsTransfer,
        s.RefundsOther,
        s.DepositsTotal,
        s.CashInTotal,
        s.CashOutTotal,
        s.ExpenseTotal,
        CalculateExpectedCash(s),
        CollectedNow(s),
        RevenueNow(s));

    private static ReceiptDto MapReceipt(Receipt r, string? computerName = null, StationKind? stationKind = null) => new(
        r.Id,
        r.Number,
        r.Status,
        r.CashShiftId,
        r.ComputerId,
        r.GamingSessionId,
        r.CustomerId,
        r.Subtotal,
        r.DiscountAmount,
        r.Total,
        r.PaidTotal,
        r.CreatedAt,
        r.Items.Select(i => new ReceiptItemDto(
            i.Id, i.ItemType, i.Name, i.Quantity, i.UnitPrice, i.DiscountAmount, i.LineTotal, i.ReferenceId)).ToList(),
        r.Payments.Select(p => new PaymentPartDto(p.Method, p.Amount)).ToList(),
        computerName,
        stationKind);

    private async Task<ReceiptDto> MapReceiptAsync(Receipt r, CancellationToken cancellationToken)
    {
        string? name = null;
        StationKind? kind = null;
        if (r.ComputerId is Guid cid)
        {
            var c = await _db.Computers.AsNoTracking()
                .Where(x => x.Id == cid)
                .Select(x => new { x.DisplayName, x.WindowsName, x.StationKind })
                .FirstOrDefaultAsync(cancellationToken);
            if (c is not null)
            {
                name = string.IsNullOrWhiteSpace(c.DisplayName) ? c.WindowsName : c.DisplayName;
                kind = c.StationKind;
            }
        }
        return MapReceipt(r, name, kind);
    }
}
