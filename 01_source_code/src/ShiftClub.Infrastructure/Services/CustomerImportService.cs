using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Import;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared;
using ShiftClub.Shared.Contracts.Import;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Services;

/// <summary>
/// Перенос базы клиентов из старой системы.
/// Балансы не записываются в поле напрямую: на каждый перенесённый остаток
/// создаётся проводка в ledger, иначе в отчётах появятся деньги без происхождения
/// и сойтись с кассой будет невозможно.
/// </summary>
public sealed class CustomerImportService : ICustomerImportService
{
    private const int PreviewRowLimit = 200;

    private const string SourceType = "Import";

    private readonly ShiftClubDbContext _db;
    private readonly ILogger<CustomerImportService> _logger;

    public CustomerImportService(ShiftClubDbContext db, ILogger<CustomerImportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CustomerImportPreviewDto> PreviewAsync(
        CustomerImportRequest request,
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var parsed = CustomerCsvParser.Parse(request.Csv);
        var existing = await LoadExistingPhonesAsync(branchId, cancellationToken);
        var alreadyPosted = await LoadPostedKeysAsync(cancellationToken);

        var rows = new List<CustomerImportRowDto>();
        var willCreate = 0;
        var willUpdate = 0;
        var willSkip = 0;
        var balanceToPost = 0m;
        var repeatedBalances = 0;

        foreach (var row in parsed.Rows)
        {
            var action = ResolveAction(row, existing, request.UpdateExisting, out var problem);
            var balancePending = request.ImportBalances
                                 && row.Balance > 0
                                 && !alreadyPosted.Contains(BalanceKey(row.Phone));

            switch (action)
            {
                case CustomerImportActions.Create:
                    willCreate++;
                    break;
                case CustomerImportActions.Update:
                    willUpdate++;
                    break;
                default:
                    willSkip++;
                    break;
            }

            if (action != CustomerImportActions.Skip)
            {
                if (balancePending)
                    balanceToPost += row.Balance;
                else if (request.ImportBalances && row.Balance > 0)
                    repeatedBalances++;
            }

            if (rows.Count < PreviewRowLimit)
            {
                rows.Add(new CustomerImportRowDto(
                    row.LineNumber,
                    row.FirstName,
                    row.LastName,
                    row.Phone,
                    row.Email,
                    balancePending ? row.Balance : 0m,
                    row.BonusBalance,
                    row.Notes,
                    action,
                    problem));
            }
        }

        var warnings = new List<string>(parsed.Warnings);
        if (repeatedBalances > 0)
        {
            warnings.Add(
                $"У {repeatedBalances} клиентов остаток уже переносили раньше — повторно начислен не будет.");
        }
        if (parsed.Rows.Count > PreviewRowLimit)
            warnings.Add($"Показаны первые {PreviewRowLimit} строк из {parsed.Rows.Count}. Импорт обработает все.");
        if (willUpdate > 0 && !request.UpdateExisting)
            warnings.Add("Совпадения по телефону будут пропущены. Включите «обновлять существующих», если нужно иначе.");

        var summary = parsed.Rows.Count == 0
            ? "Нечего импортировать"
            : $"Создать {willCreate}, обновить {willUpdate}, пропустить {willSkip}";

        return new CustomerImportPreviewDto(
            parsed.Rows.Count,
            willCreate,
            willUpdate,
            willSkip,
            balanceToPost,
            parsed.RecognizedColumns,
            warnings,
            rows,
            summary);
    }

    public async Task<CustomerImportResultDto> ImportAsync(
        CustomerImportRequest request,
        Guid branchId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var parsed = CustomerCsvParser.Parse(request.Csv);
        if (parsed.Rows.Count == 0)
            throw new InvalidOperationException("В файле нет строк с данными.");

        var defaultLoyaltyId = await _db.LoyaltyLevels.AsNoTracking()
            .Where(l => l.BranchId == branchId && l.IsActive)
            .OrderBy(l => l.SortOrder)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var existing = await LoadExistingPhonesAsync(branchId, cancellationToken);
        var alreadyPosted = await LoadPostedKeysAsync(cancellationToken);
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var balancePosted = 0m;
        var stamp = DateTimeOffset.UtcNow;

        foreach (var row in parsed.Rows)
        {
            var action = ResolveAction(row, existing, request.UpdateExisting, out _);

            if (action == CustomerImportActions.Skip)
            {
                skipped++;
                continue;
            }

            Customer customer;
            if (action == CustomerImportActions.Update)
            {
                var id = existing[Key(row.Phone)];
                customer = await _db.Customers.FirstAsync(c => c.Id == id, cancellationToken);

                // Имя и заметки обновляем только если в базе пусто: данные из кассы
                // свежее, чем выгрузка из старой системы.
                if (string.IsNullOrWhiteSpace(customer.LastName))
                    customer.LastName = row.LastName;
                if (string.IsNullOrWhiteSpace(customer.Email))
                    customer.Email = row.Email;
                if (customer.BirthDate is null)
                    customer.BirthDate = row.BirthDate;

                updated++;
            }
            else
            {
                customer = new Customer
                {
                    BranchId = branchId,
                    FirstName = row.FirstName,
                    LastName = row.LastName,
                    Phone = row.Phone,
                    Email = row.Email,
                    BirthDate = row.BirthDate,
                    Notes = row.Notes,
                    LoyaltyLevelId = defaultLoyaltyId,
                    IsActive = true,
                    CreatedBy = employeeId
                };

                _db.Customers.Add(customer);
                existing[Key(row.Phone)] = customer.Id;
                created++;
            }

            // Повторный импорт того же файла не должен удваивать деньги: ключ строится
            // по телефону, поэтому остаётся тем же между запусками.
            if (request.ImportBalances && row.Balance > 0 && alreadyPosted.Add(BalanceKey(row.Phone)))
            {
                PostOpeningBalance(customer, row, employeeId, stamp);
                balancePosted += row.Balance;
            }

            if (request.ImportBalances && row.BonusBalance > 0 && alreadyPosted.Add(BonusKey(row.Phone)))
            {
                customer.BonusBalance += row.BonusBalance;
                _db.CustomerBalanceTransactions.Add(new CustomerBalanceTransaction
                {
                    CustomerId = customer.Id,
                    Type = LedgerTransactionType.BonusCredit,
                    Direction = LedgerDirection.Credit,
                    Amount = row.BonusBalance,
                    BalanceBefore = customer.Balance,
                    BalanceAfter = customer.Balance,
                    EmployeeId = employeeId,
                    SourceType = SourceType,
                    IdempotencyKey = BonusKey(row.Phone),
                    Comment = "Бонусы перенесены из старой системы",
                    CreatedBy = employeeId
                });
            }
        }

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            Action = "customers.import",
            EntityType = nameof(Customer),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                created,
                updated,
                skipped,
                balancePosted
            }),
            CreatedBy = employeeId
        });

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Customer import: created {Created}, updated {Updated}, skipped {Skipped}, balance {Balance}",
            created,
            updated,
            skipped,
            balancePosted);

        return new CustomerImportResultDto(
            created,
            updated,
            skipped,
            balancePosted,
            $"Готово: создано {created}, обновлено {updated}, пропущено {skipped}.");
    }

    private void PostOpeningBalance(Customer customer, ParsedCustomerRow row, Guid employeeId, DateTimeOffset stamp)
    {
        var before = customer.Balance;
        customer.Balance = before + row.Balance;

        _db.CustomerBalanceTransactions.Add(new CustomerBalanceTransaction
        {
            CustomerId = customer.Id,
            Type = LedgerTransactionType.ManualAdjustment,
            Direction = LedgerDirection.Credit,
            Amount = row.Balance,
            BalanceBefore = before,
            BalanceAfter = customer.Balance,
            EmployeeId = employeeId,
            SourceType = SourceType,
            SourceId = row.LineNumber.ToString(),
            IdempotencyKey = BalanceKey(row.Phone),
            Comment = "Остаток перенесён из старой системы",
            CreatedBy = employeeId,
            CreatedAt = stamp
        });
    }

    private static string BalanceKey(string phone) => $"import:balance:{Key(phone)}";

    private static string BonusKey(string phone) => $"import:bonus:{Key(phone)}";

    private async Task<HashSet<string>> LoadPostedKeysAsync(CancellationToken cancellationToken)
    {
        var keys = await _db.CustomerBalanceTransactions.AsNoTracking()
            .Where(t => t.SourceType == SourceType && t.IdempotencyKey != null)
            .Select(t => t.IdempotencyKey!)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(keys, StringComparer.Ordinal);
    }

    private static string ResolveAction(
        ParsedCustomerRow row,
        Dictionary<string, Guid> existing,
        bool updateExisting,
        out string? problem)
    {
        if (row.Problem is not null)
        {
            problem = row.Problem;
            return CustomerImportActions.Skip;
        }

        if (existing.ContainsKey(Key(row.Phone)))
        {
            if (!updateExisting)
            {
                problem = "Клиент с таким телефоном уже есть";
                return CustomerImportActions.Skip;
            }

            problem = null;
            return CustomerImportActions.Update;
        }

        problem = null;
        return CustomerImportActions.Create;
    }

    /// <summary>Сверяем по последним 10 цифрам: в выгрузках один и тот же номер пишут по-разному.</summary>
    private static string Key(string phone) => PhoneDigits.Last10(phone);

    private async Task<Dictionary<string, Guid>> LoadExistingPhonesAsync(
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Customers.AsNoTracking()
            .Where(c => c.BranchId == branchId)
            .Select(c => new { c.Id, c.Phone })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var key = Key(r.Phone);
            if (key.Length >= 10)
                map[key] = r.Id;
        }

        return map;
    }
}
