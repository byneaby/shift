using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Contracts.Customers;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared;

namespace ShiftClub.Infrastructure.Services;

public sealed class CustomerService : ICustomerService
{
    private readonly ShiftClubDbContext _db;
    private readonly ICashService _cash;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IClubSettingsService _settings;
    private readonly ICaseService _cases;
    private readonly ICustomerEngagementService _engagement;

    public CustomerService(
        ShiftClubDbContext db,
        ICashService cash,
        IPasswordHasher passwordHasher,
        IClubSettingsService settings,
        ICaseService cases,
        ICustomerEngagementService engagement)
    {
        _db = db;
        _cash = cash;
        _passwordHasher = passwordHasher;
        _settings = settings;
        _cases = cases;
        _engagement = engagement;
    }

    public async Task<IReadOnlyList<CustomerDto>> SearchAsync(
        string? query,
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var q = _db.Customers.AsNoTracking()
            .Include(c => c.LoyaltyLevel)
            .Include(c => c.ZoneTimeBanks).ThenInclude(z => z.Zone)
            .AsQueryable();
        if (branchId.HasValue)
            q = q.Where(c => c.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLower();
            var digits = PhoneDigits.Normalize(query);
            var last10 = PhoneDigits.Last10(query);
            q = q.Where(c =>
                c.Phone.Contains(term) ||
                (digits.Length >= 4 && c.Phone.Contains(digits)) ||
                (last10.Length >= 10 && c.Phone.EndsWith(last10)) ||
                c.FirstName.ToLower().Contains(term) ||
                c.LastName.ToLower().Contains(term) ||
                ((c.FirstName + " " + c.LastName).ToLower().Contains(term)) ||
                (c.Login != null && c.Login.ToLower().Contains(term)));
        }

        var list = await q.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).Take(100).ToListAsync(cancellationToken);
        return list.Select(Map).ToList();
    }

    public async Task<CustomerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _engagement.ReconcileVisitStatsFromSessionsAsync(id, cancellationToken);

        var customer = await _db.Customers.AsNoTracking()
            .Include(c => c.LoyaltyLevel)
            .Include(c => c.ZoneTimeBanks).ThenInclude(z => z.Zone)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return customer is null ? null : Map(customer);
    }

    public async Task<CustomerDto> CreateAsync(
        CreateCustomerRequest request,
        Guid branchId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var phone = PhoneDigits.Normalize(request.Phone);
        if (string.IsNullOrWhiteSpace(request.FirstName))
            throw new InvalidOperationException("Имя обязательно.");
        if (phone.Length < 10)
            throw new InvalidOperationException("Телефон: не менее 10 цифр.");

        var last10 = PhoneDigits.Last10(phone);
        var exists = await _db.Customers.AnyAsync(
            c => c.BranchId == branchId && (c.Phone == phone || c.Phone.EndsWith(last10)),
            cancellationToken);
        if (exists)
            throw new InvalidOperationException("Клиент с таким телефоном уже есть.");

        var defaultLoyalty = await _db.LoyaltyLevels
            .Where(l => l.BranchId == branchId && l.IsActive)
            .OrderBy(l => l.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Login))
        {
            var login = request.Login.Trim();
            var loginTaken = await _db.Customers.AnyAsync(
                c => c.BranchId == branchId && c.Login == login, cancellationToken);
            if (loginTaken)
                throw new InvalidOperationException("Логин уже занят.");
        }

        if (!string.IsNullOrWhiteSpace(request.Pin))
        {
            var pin = request.Pin.Trim();
            if (pin.Length < 4 || !pin.All(char.IsDigit))
                throw new InvalidOperationException("ПИН: 4+ цифр.");
        }

        if (!string.IsNullOrWhiteSpace(request.Password) && request.Password.Trim().Length < 4)
            throw new InvalidOperationException("Пароль: минимум 4 символа.");

        var customer = new Customer
        {
            BranchId = branchId,
            FirstName = request.FirstName.Trim(),
            LastName = (request.LastName ?? "").Trim(),
            Phone = phone,
            Email = request.Email?.Trim(),
            Notes = request.Notes,
            Login = string.IsNullOrWhiteSpace(request.Login) ? null : request.Login.Trim(),
            PasswordHash = string.IsNullOrWhiteSpace(request.Password)
                ? null
                : _passwordHasher.Hash(request.Password.Trim()),
            PinHash = string.IsNullOrWhiteSpace(request.Pin) ? null : _passwordHasher.Hash(request.Pin.Trim()),
            LoyaltyLevelId = defaultLoyalty?.Id,
            IsActive = true
        };

        _db.Customers.Add(customer);
        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            Action = "customer.create",
            EntityType = nameof(Customer),
            EntityId = customer.Id.ToString(),
            DetailsJson = $"{{\"phone\":\"{phone}\"}}"
        });

        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            await _cases.TryGrantRegistrationKeyAsync(customer.Id, cancellationToken);
        }
        catch { /* best-effort */ }

        customer.LoyaltyLevel = defaultLoyalty;
        return Map(customer);
    }

    public async Task<CustomerDto> UpdateAsync(
        Guid id,
        UpdateCustomerRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден.");

        var phone = PhoneDigits.Normalize(request.Phone);
        if (phone.Length < 10)
            throw new InvalidOperationException("Телефон: не менее 10 цифр.");
        var last10 = PhoneDigits.Last10(phone);
        var phoneTaken = await _db.Customers.AnyAsync(
            c => c.BranchId == customer.BranchId
                 && c.Id != id
                 && (c.Phone == phone || c.Phone.EndsWith(last10)),
            cancellationToken);
        if (phoneTaken)
            throw new InvalidOperationException("Телефон уже используется другим клиентом.");

        if (string.IsNullOrWhiteSpace(request.FirstName))
            throw new InvalidOperationException("Имя обязательно.");
        customer.FirstName = request.FirstName.Trim();
        customer.LastName = (request.LastName ?? "").Trim();
        customer.Phone = phone;
        customer.Email = request.Email?.Trim();
        customer.Notes = request.Notes;
        customer.IsBlocked = request.IsBlocked;
        customer.BlockReason = request.IsBlocked ? request.BlockReason : null;

        if (request.LoyaltyLevelLocked.HasValue)
            customer.LoyaltyLevelLocked = request.LoyaltyLevelLocked.Value;

        if (request.LoyaltyLevelId.HasValue)
        {
            var levelId = request.LoyaltyLevelId.Value;
            if (levelId == Guid.Empty)
            {
                customer.LoyaltyLevelId = null;
                customer.LoyaltyLevel = null;
            }
            else
            {
                var level = await _db.LoyaltyLevels.FirstOrDefaultAsync(
                    l => l.Id == levelId && l.BranchId == customer.BranchId, cancellationToken)
                    ?? throw new InvalidOperationException("Уровень лояльности не найден.");
                customer.LoyaltyLevelId = level.Id;
                customer.LoyaltyLevel = level;
            }
        }

        customer.UpdatedAt = DateTimeOffset.UtcNow;
        customer.UpdatedBy = employeeId;

        await _db.SaveChangesAsync(cancellationToken);
        return Map(customer);
    }

    public async Task<CustomerDto> SetCredentialsAsync(
        Guid id,
        SetCustomerCredentialsRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _db.Customers.Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден.");

        if (!string.IsNullOrWhiteSpace(request.Login))
        {
            var login = request.Login.Trim();
            var taken = await _db.Customers.AnyAsync(
                c => c.BranchId == customer.BranchId && c.Login == login && c.Id != id,
                cancellationToken);
            if (taken)
                throw new InvalidOperationException("Логин уже занят.");
            customer.Login = login;
        }

        if (request.ClearPassword)
            customer.PasswordHash = null;
        else if (!string.IsNullOrWhiteSpace(request.Password))
        {
            if (request.Password.Trim().Length < 4)
                throw new InvalidOperationException("Пароль: минимум 4 символа.");
            customer.PasswordHash = _passwordHasher.Hash(request.Password.Trim());
        }

        if (request.ClearPin)
            customer.PinHash = null;
        else if (!string.IsNullOrWhiteSpace(request.Pin))
        {
            var pin = request.Pin.Trim();
            if (pin.Length < 4 || !pin.All(char.IsDigit))
                throw new InvalidOperationException("ПИН: 4+ цифр.");
            customer.PinHash = _passwordHasher.Hash(pin);
        }

        customer.UpdatedAt = DateTimeOffset.UtcNow;
        customer.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(customer);
    }

    public async Task<CustomerDepositQuoteDto> QuoteDepositAsync(
        Guid id,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Сумма пополнения должна быть больше 0.");

        var customer = await _db.Customers.AsNoTracking()
            .Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден.");

        if (customer.IsBlocked)
            throw new InvalidOperationException("Клиент заблокирован.");

        var eng = await _settings.GetEngagementStoredAsync(cancellationToken);
        return BuildDepositQuote(customer, amount, eng);
    }

    public async Task<CustomerDepositResultDto> DepositAsync(
        Guid id,
        DepositCustomerRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            throw new InvalidOperationException("Сумма пополнения должна быть больше 0.");

        await _cash.EnsureEmployeeHasOpenShiftAsync(employeeId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.CustomerBalanceTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                var c = await GetByIdAsync(id, cancellationToken)
                        ?? throw new KeyNotFoundException("Клиент не найден.");
                var engExisting = await _settings.GetEngagementStoredAsync(cancellationToken);
                var customerSnap = await _db.Customers.AsNoTracking()
                    .Include(x => x.LoyaltyLevel)
                    .FirstAsync(x => x.Id == id, cancellationToken);
                // Балансы уже включают это пополнение — не прибавляем сумму повторно.
                var q = BuildDepositQuote(customerSnap, request.Amount, engExisting) with
                {
                    BalanceAfterDeposit = c.Balance,
                    BonusBalanceAfterDeposit = c.BonusBalance
                };
                return new CustomerDepositResultDto(c, q);
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var customer = await _db.Customers.Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден.");

        if (customer.IsBlocked)
            throw new InvalidOperationException("Клиент заблокирован.");

        var eng = await _settings.GetEngagementStoredAsync(cancellationToken);
        var quote = BuildDepositQuote(customer, request.Amount, eng);

        var receipt = await _cash.CreateSaleAsync(new CreateSaleRequest(
            null,
            null,
            $"Пополнение баланса {customer.FirstName} {customer.LastName}",
            request.IdempotencyKey is null ? null : $"deposit-{request.IdempotencyKey}",
            [
                new CreateSaleItemRequest(
                    ReceiptItemType.BalanceTopUp,
                    "Пополнение баланса",
                    1,
                    request.Amount,
                    0,
                    customer.Id)
            ],
            [new PaymentPartDto(request.PaymentMethod, request.Amount)],
            customer.Id), employeeId, cancellationToken);

        await PostLedgerAsync(
            customer,
            LedgerTransactionType.Deposit,
            LedgerDirection.Credit,
            request.Amount,
            employeeId,
            receipt.Id,
            null,
            "Deposit",
            receipt.Id.ToString(),
            request.IdempotencyKey,
            request.Comment ?? "Пополнение баланса",
            cancellationToken);

        if (quote.LoyaltyBonusAmount > 0)
        {
            customer.BonusBalance += quote.LoyaltyBonusAmount;
            _db.CustomerBalanceTransactions.Add(new CustomerBalanceTransaction
            {
                CustomerId = customer.Id,
                Type = LedgerTransactionType.BonusCredit,
                Direction = LedgerDirection.Credit,
                Amount = quote.LoyaltyBonusAmount,
                BalanceBefore = customer.Balance,
                BalanceAfter = customer.Balance,
                EmployeeId = employeeId,
                ReceiptId = receipt.Id,
                SourceType = "Bonus",
                Comment = $"Бонус {quote.LoyaltyBonusPercent:0.##}%"
            });
        }

        if (quote.TierBonusAmount > 0)
        {
            customer.BonusBalance += quote.TierBonusAmount;
            _db.CustomerBalanceTransactions.Add(new CustomerBalanceTransaction
            {
                CustomerId = customer.Id,
                Type = LedgerTransactionType.BonusCredit,
                Direction = LedgerDirection.Credit,
                Amount = quote.TierBonusAmount,
                BalanceBefore = customer.Balance,
                BalanceAfter = customer.Balance,
                EmployeeId = employeeId,
                ReceiptId = receipt.Id,
                SourceType = "DepositTier",
                Comment = quote.TierMinAmount > 0
                    ? $"Бонус за пополнение от {quote.TierMinAmount:0} ₸"
                    : "Бонус за пополнение"
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        try
        {
            await _cases.TryGrantDepositKeysAsync(customer.Id, request.Amount, receipt.Id, cancellationToken);
        }
        catch
        {
            // deposit already committed
        }

        var mapped = Map(customer);
        var finalQuote = quote with
        {
            BalanceAfterDeposit = mapped.Balance,
            BonusBalanceAfterDeposit = mapped.BonusBalance
        };
        return new CustomerDepositResultDto(mapped, finalQuote);
    }

    private static CustomerDepositQuoteDto BuildDepositQuote(
        Customer customer,
        decimal amount,
        EngagementStoredSettings eng)
    {
        var loyaltyPct = customer.LoyaltyLevel?.BonusPercent ?? 0m;
        var applyLoyalty = loyaltyPct > 0
            && (!eng.DepositBonusEnabled || eng.DepositBonusStackWithLoyaltyPercent);

        var loyaltyBonus = 0m;
        if (applyLoyalty)
            loyaltyBonus = Math.Round(amount * loyaltyPct / 100m, 2, MidpointRounding.AwayFromZero);

        var tierMin = 0m;
        var tierBonus = 0m;
        if (eng.DepositBonusEnabled)
        {
            var tier = (eng.DepositBonusTiers ?? [])
                .Where(t => t.MinAmount > 0 && t.BonusAmount > 0 && amount >= t.MinAmount)
                .OrderByDescending(t => t.MinAmount)
                .FirstOrDefault();
            if (tier is not null)
            {
                tierMin = tier.MinAmount;
                tierBonus = Math.Round(tier.BonusAmount, 2, MidpointRounding.AwayFromZero);
            }
        }

        var totalBonus = loyaltyBonus + tierBonus;
        return new CustomerDepositQuoteDto(
            amount,
            applyLoyalty ? loyaltyPct : 0m,
            loyaltyBonus,
            tierMin,
            tierBonus,
            totalBonus,
            eng.DepositBonusEnabled,
            eng.DepositBonusStackWithLoyaltyPercent,
            customer.Balance + amount,
            customer.BonusBalance + totalBonus);
    }

    public async Task<CustomerDto> AdjustAsync(
        Guid id,
        AdjustCustomerBalanceRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            throw new InvalidOperationException("Сумма должна быть больше 0.");
        if (string.IsNullOrWhiteSpace(request.Comment))
            throw new InvalidOperationException("Комментарий обязателен для корректировки.");

        var customer = await _db.Customers.Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден.");

        if (request.Direction == LedgerDirection.Debit && customer.Balance < request.Amount)
            throw new InvalidOperationException("Недостаточно средств на балансе.");

        await PostLedgerAsync(
            customer,
            LedgerTransactionType.ManualAdjustment,
            request.Direction,
            request.Amount,
            employeeId,
            null,
            null,
            "ManualAdjustment",
            null,
            request.IdempotencyKey,
            request.Comment,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(customer);
    }

    public async Task ChargeForSessionAsync(
        Guid customerId,
        decimal amount,
        Guid sessionId,
        Guid? receiptId,
        Guid employeeId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            return;

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
                       ?? throw new KeyNotFoundException("Клиент не найден.");

        if (customer.IsBlocked)
            throw new InvalidOperationException("Клиент заблокирован.");

        await ChargeFromWalletAsync(
            customerId,
            amount,
            LedgerTransactionType.SessionCharge,
            sessionId,
            receiptId,
            employeeId,
            idempotencyKey,
            "Списание за игровой сеанс",
            cancellationToken);
    }

    public async Task ChargeFromWalletAsync(
        Guid customerId,
        decimal amount,
        LedgerTransactionType type,
        Guid? sessionId,
        Guid? receiptId,
        Guid employeeId,
        string? idempotencyKey,
        string comment,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            return;

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var exists = await _db.CustomerBalanceTransactions
                .AnyAsync(t => t.IdempotencyKey == idempotencyKey, cancellationToken);
            if (exists)
                return;
        }

        var customer = await _db.Customers.Include(c => c.LoyaltyLevel)
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден.");

        if (customer.IsBlocked)
            throw new InvalidOperationException("Клиент заблокирован.");

        var wallet = customer.Balance + customer.BonusBalance;
        if (wallet < amount)
            throw new InvalidOperationException(
                $"Недостаточно средств (баланс {customer.Balance:0} ₸ + бонусы {customer.BonusBalance:0} ₸).");

        var fromBonus = Math.Min(customer.BonusBalance, amount);
        var fromBalance = amount - fromBonus;

        if (fromBonus > 0)
        {
            var beforeBonus = customer.BonusBalance;
            customer.BonusBalance -= fromBonus;
            _db.CustomerBalanceTransactions.Add(new CustomerBalanceTransaction
            {
                CustomerId = customer.Id,
                Type = LedgerTransactionType.BonusDebit,
                Direction = LedgerDirection.Debit,
                Amount = fromBonus,
                BalanceBefore = customer.Balance,
                BalanceAfter = customer.Balance,
                EmployeeId = employeeId,
                ReceiptId = receiptId,
                GamingSessionId = sessionId,
                SourceType = type == LedgerTransactionType.SessionCharge ? "GamingSession" : "Product",
                SourceId = sessionId?.ToString() ?? receiptId?.ToString(),
                IdempotencyKey = idempotencyKey is null ? null : $"{idempotencyKey}-bonus",
                Comment = $"{comment} · бонусы (−{fromBonus:0} ₸, было {beforeBonus:0})"
            });
        }

        if (fromBalance > 0)
        {
            await PostLedgerAsync(
                customer,
                type,
                LedgerDirection.Debit,
                fromBalance,
                employeeId,
                receiptId,
                sessionId,
                type == LedgerTransactionType.SessionCharge ? "GamingSession" : "Product",
                sessionId?.ToString() ?? receiptId?.ToString(),
                fromBonus > 0 && idempotencyKey is not null ? $"{idempotencyKey}-balance" : idempotencyKey,
                fromBonus > 0 ? $"{comment} · баланс (−{fromBalance:0} ₸)" : comment,
                cancellationToken);
        }

        // Lifetime spend / лояльность: без чека (оплата сеанса с баланса).
        // С чеком — учитывает CashService.CreateSale, здесь не дублируем.
        if (receiptId is null)
            await LoyaltyProgress.ApplySpendAsync(_db, customer, amount, cancellationToken);
        else
            customer.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshLoyaltyLevelAsync(Customer customer, CancellationToken cancellationToken)
    {
        await LoyaltyProgress.RefreshUpgradeOnlyAsync(_db, customer, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerBalanceTransactionDto>> GetTransactionsAsync(
        Guid customerId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);
        var list = await _db.CustomerBalanceTransactions.AsNoTracking()
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return list.Select(t => new CustomerBalanceTransactionDto(
            t.Id, t.Type, t.Direction, t.Amount, t.BalanceBefore, t.BalanceAfter,
            t.Comment, t.CreatedAt, t.ReceiptId, t.GamingSessionId)).ToList();
    }

    public async Task<CustomerDto> AdjustTimeBankAsync(
        Guid id,
        AdjustCustomerTimeBankRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.Minutes <= 0)
            throw new InvalidOperationException("Количество минут должно быть больше 0.");
        if (request.ZoneId == Guid.Empty)
            throw new InvalidOperationException("Укажите зону для банка времени.");
        if (string.IsNullOrWhiteSpace(request.Comment))
            throw new InvalidOperationException("Комментарий обязателен для корректировки банка времени.");

        var reason = request.Direction == LedgerDirection.Credit
            ? TimeBankReason.ManualCredit
            : TimeBankReason.ManualDebit;

        await ApplyTimeBankChangeAsync(
            id,
            request.ZoneId,
            request.Minutes,
            request.Direction,
            reason,
            employeeId,
            null,
            request.Comment.Trim(),
            request.IdempotencyKey,
            cancellationToken);

        return await GetByIdAsync(id, cancellationToken)
               ?? throw new KeyNotFoundException("Клиент не найден.");
    }

    public async Task<IReadOnlyList<CustomerTimeBankTransactionDto>> GetTimeBankTransactionsAsync(
        Guid customerId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);
        var list = await _db.CustomerTimeBankTransactions.AsNoTracking()
            .Include(t => t.Zone)
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return list.Select(t => new CustomerTimeBankTransactionDto(
            t.Id, t.ZoneId, t.Zone?.Name, t.Reason, t.Direction, t.Minutes, t.BalanceBefore, t.BalanceAfter,
            t.Comment, t.CreatedAt, t.GamingSessionId, t.EmployeeId)).ToList();
    }

    public async Task<int> GetZoneTimeBankMinutesAsync(
        Guid customerId,
        Guid zoneId,
        CancellationToken cancellationToken = default)
    {
        return await _db.CustomerZoneTimeBanks.AsNoTracking()
            .Where(b => b.CustomerId == customerId && b.ZoneId == zoneId)
            .Select(b => b.Minutes)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task ApplyTimeBankChangeAsync(
        Guid customerId,
        Guid zoneId,
        int minutes,
        LedgerDirection direction,
        TimeBankReason reason,
        Guid? employeeId,
        Guid? sessionId,
        string? comment,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (minutes <= 0)
            return;
        if (zoneId == Guid.Empty)
            throw new InvalidOperationException("Зона банка времени обязательна.");

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var exists = await _db.CustomerTimeBankTransactions
                .AnyAsync(t => t.IdempotencyKey == idempotencyKey, cancellationToken);
            if (exists)
                return;
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден.");

        var zone = await _db.Zones.AsNoTracking()
            .FirstOrDefaultAsync(z => z.Id == zoneId && z.BranchId == customer.BranchId, cancellationToken)
            ?? throw new InvalidOperationException("Зона не найдена в филиале клиента.");

        var bank = await _db.CustomerZoneTimeBanks
            .FirstOrDefaultAsync(b => b.CustomerId == customerId && b.ZoneId == zoneId, cancellationToken);

        if (bank is null)
        {
            bank = new CustomerZoneTimeBank
            {
                CustomerId = customerId,
                ZoneId = zoneId,
                Minutes = 0
            };
            _db.CustomerZoneTimeBanks.Add(bank);
        }

        var before = bank.Minutes;
        var after = direction == LedgerDirection.Credit ? before + minutes : before - minutes;
        if (after < 0)
            throw new InvalidOperationException(
                $"В банке зоны «{zone.Name}» недостаточно минут (есть {before} мин).");

        bank.Minutes = after;
        bank.UpdatedAt = DateTimeOffset.UtcNow;

        // Суммарный кэш на клиенте
        var otherSum = await _db.CustomerZoneTimeBanks
            .Where(b => b.CustomerId == customerId && b.ZoneId != zoneId)
            .SumAsync(b => (int?)b.Minutes, cancellationToken) ?? 0;
        customer.TimeBankMinutes = otherSum + after;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        if (employeeId.HasValue && employeeId.Value != Guid.Empty)
            customer.UpdatedBy = employeeId;

        _db.CustomerTimeBankTransactions.Add(new CustomerTimeBankTransaction
        {
            CustomerId = customer.Id,
            ZoneId = zoneId,
            Reason = reason,
            Direction = direction,
            Minutes = minutes,
            BalanceBefore = before,
            BalanceAfter = after,
            EmployeeId = employeeId is Guid e && e != Guid.Empty ? e : null,
            GamingSessionId = sessionId,
            IdempotencyKey = idempotencyKey,
            Comment = comment
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task PostLedgerAsync(
        Customer customer,
        LedgerTransactionType type,
        LedgerDirection direction,
        decimal amount,
        Guid employeeId,
        Guid? receiptId,
        Guid? sessionId,
        string? sourceType,
        string? sourceId,
        string? idempotencyKey,
        string? comment,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var exists = await _db.CustomerBalanceTransactions
                .AnyAsync(t => t.IdempotencyKey == idempotencyKey, cancellationToken);
            if (exists)
                return;
        }

        var before = customer.Balance;
        var after = direction == LedgerDirection.Credit ? before + amount : before - amount;
        if (after < 0)
            throw new InvalidOperationException("Баланс не может стать отрицательным.");

        customer.Balance = after;
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        _db.CustomerBalanceTransactions.Add(new CustomerBalanceTransaction
        {
            CustomerId = customer.Id,
            Type = type,
            Direction = direction,
            Amount = amount,
            BalanceBefore = before,
            BalanceAfter = after,
            EmployeeId = employeeId,
            ReceiptId = receiptId,
            GamingSessionId = sessionId,
            SourceType = sourceType,
            SourceId = sourceId,
            IdempotencyKey = idempotencyKey,
            Comment = comment
        });

        await Task.CompletedTask;
    }


    private static CustomerDto Map(Customer c)
    {
        var banks = (c.ZoneTimeBanks ?? [])
            .Where(b => b.Minutes > 0)
            .OrderBy(b => b.Zone?.SortOrder ?? 0)
            .ThenBy(b => b.Zone?.Name)
            .Select(b => new CustomerZoneTimeBankDto(b.ZoneId, b.Zone?.Name ?? "Зона", b.Minutes))
            .ToList();

        return new(
            c.Id,
            c.FirstName,
            c.LastName,
            $"{c.FirstName} {c.LastName}".Trim(),
            c.Phone,
            c.Email,
            c.Balance,
            c.BonusBalance,
            banks.Sum(b => b.Minutes) > 0 ? banks.Sum(b => b.Minutes) : c.TimeBankMinutes,
            c.LoyaltyLevel?.Name,
            c.VisitCount,
            c.TotalSpent,
            c.IsBlocked,
            c.BlockReason,
            c.IsActive,
            c.CreatedAt,
            banks,
            c.Notes,
            c.Login,
            !string.IsNullOrEmpty(c.PasswordHash),
            !string.IsNullOrEmpty(c.PinHash),
            c.LoyaltyLevelId,
            c.LoyaltyLevel?.BonusPercent ?? 0,
            c.LoyaltyLevel?.TimeDiscountPercent ?? 0,
            c.LoyaltyLevelLocked,
            c.CaseKeysBalance);
    }
}
