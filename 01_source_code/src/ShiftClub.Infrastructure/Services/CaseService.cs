using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Cases;
using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Services;

public sealed class CaseService : ICaseService
{
    /// <summary>Ключи/кейс считаются с 15.09.2026 00:00 Asia/Almaty.</summary>
    private static readonly DateTimeOffset CaseKeysLaunchUtc =
        new DateTimeOffset(2026, 9, 14, 19, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo ClubTz = ResolveClubTz();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ShiftClubDbContext _db;
    private readonly ICashService _cash;
    private readonly IHostEnvironment _env;
    private readonly ILogger<CaseService> _logger;

    public CaseService(
        ShiftClubDbContext db,
        ICashService cash,
        IHostEnvironment env,
        ILogger<CaseService> logger)
    {
        _db = db;
        _cash = cash;
        _env = env;
        _logger = logger;
    }

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _db.CaseDefinitions
            .Include(c => c.Prizes)
            .FirstOrDefaultAsync(c => c.Code == "SHIFT_CASE", cancellationToken);

        var lootPath = ResolveLootPath();
        if (lootPath is null)
        {
            _logger.LogWarning("SHIFT CASE loot JSON not found; skip seed");
            return;
        }

        await using var stream = File.OpenRead(lootPath);
        var loot = await JsonSerializer.DeserializeAsync<LootFile>(stream, JsonOpts, cancellationToken)
                   ?? throw new InvalidOperationException("Invalid shift-case-loot.json");

        if (existing is null)
        {
            existing = new CaseDefinition
            {
                Code = loot.CaseCode ?? "SHIFT_CASE",
                Title = loot.Title ?? "SHIFT CASE",
                Description = loot.Description,
                EconomicsNote = loot.EconomicsNote,
                ExpectedCostKzt = loot.ExpectedCostKzt,
                LimitsJson = loot.Limits is null ? null : JsonSerializer.Serialize(loot.Limits, JsonOpts),
                ShowProbabilitiesToUsers = loot.ShowProbabilitiesToUsers,
                IsEnabled = true,
                KeyCost = 1
            };
            _db.CaseDefinitions.Add(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            existing.Title = loot.Title ?? existing.Title;
            existing.Description = loot.Description;
            existing.EconomicsNote = loot.EconomicsNote;
            existing.ExpectedCostKzt = loot.ExpectedCostKzt;
            existing.LimitsJson = loot.Limits is null ? null : JsonSerializer.Serialize(loot.Limits, JsonOpts);
            existing.ShowProbabilitiesToUsers = loot.ShowProbabilitiesToUsers;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var order = 0;
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in loot.Prizes ?? [])
        {
            order++;
            var code = p.Id?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                continue;
            seenCodes.Add(code);

            var prize = existing.Prizes.FirstOrDefault(x => x.PrizeCode == code);
            var isNew = prize is null;
            if (isNew)
            {
                prize = new CasePrize { CaseDefinitionId = existing.Id, PrizeCode = code };
                existing.Prizes.Add(prize);
                _db.CasePrizes.Add(prize);
            }

            // Каталог из loot: имя/тип/пейлоад. IsActive и Weight (шанс) для уже
            // существующих призов не трогаем — иначе кнопка «Выкл» и правки % откатываются
            // при каждом GetAdmin / EnsureSeeded.
            prize!.Name = p.Name ?? code;
            prize.Description = p.Description;
            prize.PrizeType = ParsePrizeType(p.Type);
            prize.Rarity = ParseRarity(p.Rarity);
            prize.CostEstimateKzt = p.CostEstimateKzt;
            prize.PayloadJson = p.Payload.HasValue
                ? p.Payload.Value.GetRawText()
                : "{}";
            if (isNew || string.IsNullOrWhiteSpace(prize.ImageUrl))
                prize.ImageUrl = p.Image;
            prize.DailyLimit = p.DailyLimit;
            prize.TotalLimit = p.TotalLimit;
            prize.RequiresClaim = p.RequiresClaim
                                 || prize.PrizeType is CasePrizeType.BarItem or CasePrizeType.Discount
                                 || (prize.PrizeType == CasePrizeType.Service
                                     && prize.PayloadJson.Contains("\"tariffKind\"", StringComparison.OrdinalIgnoreCase));
            prize.SortOrder = order;
            if (isNew)
            {
                prize.Weight = Math.Max(0, p.Weight);
                prize.IsActive = true;
            }

            prize.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // Старые призы, которых нет в актуальном loot — выключаем (шансы не мешают).
        foreach (var old in existing.Prizes.Where(p => !seenCodes.Contains(p.PrizeCode)))
        {
            if (!old.IsActive)
                continue;
            old.IsActive = false;
            old.Weight = 0;
            old.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("SHIFT CASE seeded/updated with {Count} loot prizes", seenCodes.Count);
    }

    public async Task<CasePublicHomeDto> GetPublicHomeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        var catalog = await BuildCatalogAsync("SHIFT_CASE", cancellationToken);
        var recent = await LoadDemoRecentAsync(cancellationToken);
        return new CasePublicHomeDto(catalog, recent);
    }

    public async Task<CasePublicSpinResultDto> SpinPublicAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);

        var definition = await _db.CaseDefinitions
            .Include(c => c.Prizes.Where(p => p.IsActive && p.Weight > 0))
            .FirstOrDefaultAsync(c => c.Code == "SHIFT_CASE", cancellationToken)
            ?? throw new InvalidOperationException("Кейс не найден");

        if (!definition.IsEnabled)
            throw new InvalidOperationException("Рулетка сейчас недоступна");

        var eligible = definition.Prizes.Where(p => p.IsActive && p.Weight > 0).OrderBy(p => p.SortOrder).ToList();
        if (eligible.Count == 0)
            throw new InvalidOperationException("Нет активных призов");

        var weightTotal = eligible.Sum(p => p.Weight);
        var roll = RandomNumberGenerator.GetInt32(0, weightTotal);
        var cursor = 0;
        CasePrize? won = null;
        foreach (var p in eligible)
        {
            cursor += p.Weight;
            if (roll < cursor)
            {
                won = p;
                break;
            }
        }

        won ??= eligible[^1];

        var result = new CasePublicSpinResultDto(
            won.PrizeCode,
            won.Name,
            won.PrizeType,
            won.Rarity,
            won.ImageUrl,
            won.Description);

        await PushDemoRecentAsync(result, cancellationToken);
        return result;
    }

    public async Task<CaseAdminDto> GetAdminAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        var def = await _db.CaseDefinitions.AsNoTracking()
            .Include(c => c.Prizes)
            .FirstOrDefaultAsync(c => c.Code == "SHIFT_CASE", cancellationToken)
            ?? throw new InvalidOperationException("Кейс не найден");

        return MapAdmin(def);
    }

    public async Task<CaseAdminDto> UpdateAdminAsync(
        UpdateCaseAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        var def = await _db.CaseDefinitions
            .Include(c => c.Prizes)
            .FirstOrDefaultAsync(c => c.Code == "SHIFT_CASE", cancellationToken)
            ?? throw new InvalidOperationException("Кейс не найден");

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("Название обязательно");

        def.Title = request.Title.Trim();
        def.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var wasEnabled = def.IsEnabled;
        def.IsEnabled = request.IsEnabled;
        def.ShowProbabilitiesToUsers = request.ShowProbabilitiesToUsers;
        def.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        if (!wasEnabled && request.IsEnabled)
            await GrantWelcomeKeysToAllEligibleAsync(cancellationToken);

        return MapAdmin(def);
    }

    public async Task<CaseAdminPrizeDto> UpsertPrizeAsync(
        Guid? prizeId,
        UpsertCasePrizeRequest request,
        CancellationToken cancellationToken = default)
    {
        var def = await _db.CaseDefinitions
            .Include(c => c.Prizes)
            .FirstOrDefaultAsync(c => c.Code == "SHIFT_CASE", cancellationToken)
            ?? throw new InvalidOperationException("Кейс не найден");

        if (string.IsNullOrWhiteSpace(request.PrizeCode))
            throw new InvalidOperationException("Код приза обязателен");
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Название приза обязательно");
        if (request.DropPercent < 0 || request.DropPercent > 100)
            throw new InvalidOperationException("Процент выпадения: от 0 до 100");

        var code = request.PrizeCode.Trim();
        CasePrize prize;
        if (prizeId is Guid id)
        {
            prize = def.Prizes.FirstOrDefault(p => p.Id == id)
                    ?? throw new KeyNotFoundException("Приз не найден");
            if (def.Prizes.Any(p => p.Id != id && p.PrizeCode == code))
                throw new InvalidOperationException("Такой код приза уже есть");
        }
        else
        {
            if (def.Prizes.Any(p => p.PrizeCode == code))
                throw new InvalidOperationException("Такой код приза уже есть");
            prize = new CasePrize { CaseDefinitionId = def.Id, PrizeCode = code };
            _db.CasePrizes.Add(prize);
            def.Prizes.Add(prize);
        }

        prize.PrizeCode = code;
        prize.Name = request.Name.Trim();
        prize.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        prize.PrizeType = request.PrizeType;
        prize.Rarity = CasePrizeRarity.Common;
        // 1% → weight 100; сохраняем точность до 0.01%
        prize.Weight = Math.Max(0, (int)Math.Round(request.DropPercent * 100m, MidpointRounding.AwayFromZero));
        prize.CostEstimateKzt = request.CostEstimateKzt;
        prize.PayloadJson = string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson.Trim();
        prize.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
        prize.DailyLimit = request.DailyLimit;
        prize.TotalLimit = request.TotalLimit;
        prize.RequiresClaim = request.RequiresClaim;
        prize.SortOrder = request.SortOrder;
        prize.IsActive = request.IsActive;
        prize.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapAdminPrize(prize);
    }

    public async Task SetPrizeActiveAsync(Guid prizeId, bool isActive, CancellationToken cancellationToken = default)
    {
        var prize = await _db.CasePrizes.FirstOrDefaultAsync(p => p.Id == prizeId, cancellationToken)
                    ?? throw new KeyNotFoundException("Приз не найден");
        prize.IsActive = isActive;
        prize.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CaseAdminPrizeDto> SetPrizeImageUrlAsync(
        Guid prizeId,
        string? imageUrl,
        CancellationToken cancellationToken = default)
    {
        var prize = await _db.CasePrizes.FirstOrDefaultAsync(p => p.Id == prizeId, cancellationToken)
                    ?? throw new KeyNotFoundException("Приз не найден");
        prize.ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        prize.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return MapAdminPrize(prize);
    }

    private const string DemoRecentKey = "case.demo.recent";

    private async Task<IReadOnlyList<CaseRecentWinDto>> LoadDemoRecentAsync(CancellationToken cancellationToken)
    {
        var raw = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == DemoRecentKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            var list = JsonSerializer.Deserialize<List<DemoWinStored>>(raw, JsonOpts) ?? [];
            return list
                .OrderByDescending(x => x.At)
                .Take(24)
                .Select(x => new CaseRecentWinDto(x.Name, x.Type, x.Image, x.Guest, x.At))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private async Task PushDemoRecentAsync(CasePublicSpinResultDto spin, CancellationToken cancellationToken)
    {
        var list = (await LoadDemoRecentAsync(cancellationToken)).ToList();
        list.Insert(0, new CaseRecentWinDto(
            spin.PrizeName,
            spin.PrizeType,
            spin.ImageUrl,
            "Гость",
            DateTimeOffset.UtcNow));
        list = list.Take(40).ToList();

        var payload = JsonSerializer.Serialize(
            list.Select(x => new DemoWinStored(x.PrizeName, x.PrizeType, x.ImageUrl, x.CustomerDisplay, x.OpenedAt)).ToList(),
            JsonOpts);

        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == DemoRecentKey, cancellationToken);
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = DemoRecentKey,
                Value = payload,
                Description = "Последние спины публичной рулетки SHIFT CASE"
            });
        }
        else
        {
            setting.Value = payload;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static CaseAdminDto MapAdmin(CaseDefinition def)
    {
        var prizes = def.Prizes.OrderBy(p => p.SortOrder).Select(MapAdminPrize).ToList();
        var sum = prizes.Where(p => p.IsActive).Sum(p => p.DropPercent);
        return new CaseAdminDto(
            def.Id,
            def.Code,
            def.Title,
            def.Description,
            def.IsEnabled,
            def.ShowProbabilitiesToUsers,
            def.KeyCost,
            prizes,
            sum);
    }

    private static CaseAdminPrizeDto MapAdminPrize(CasePrize p) => new(
        p.Id,
        p.PrizeCode,
        p.Name,
        p.Description,
        p.PrizeType,
        Math.Round(p.Weight / 100m, 2),
        p.CostEstimateKzt,
        p.PayloadJson,
        p.ImageUrl,
        p.DailyLimit,
        p.TotalLimit,
        p.RequiresClaim,
        p.SortOrder,
        p.IsActive);

    private sealed record DemoWinStored(
        string Name,
        CasePrizeType Type,
        string? Image,
        string Guest,
        DateTimeOffset At);

    public async Task<CaseMyStateDto> GetMyStateAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден");

        var catalog = await BuildCatalogAsync("SHIFT_CASE", cancellationToken);

        var pending = await _db.CaseUserRewards.AsNoTracking()
            .Where(r => r.CustomerId == customerId && r.Status == CaseRewardStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        var openings = await _db.CaseOpenings.AsNoTracking()
            .Include(o => o.Reward)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(30)
            .ToListAsync(cancellationToken);

        return new CaseMyStateDto(
            customer.CaseKeysBalance,
            catalog,
            pending.Select(MapReward).ToList(),
            openings.Select(MapOpening).ToList());
    }

    public async Task<CaseOpenResultDto> OpenAsync(
        Guid customerId,
        CaseOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);

        var caseCode = string.IsNullOrWhiteSpace(request.CaseCode) ? "SHIFT_CASE" : request.CaseCode.Trim();
        var idem = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim();

        if (idem is not null)
        {
            var prior = await _db.CaseOpenings.AsNoTracking()
                .Include(o => o.Reward)
                .FirstOrDefaultAsync(o => o.IdempotencyKey == idem, cancellationToken);
            if (prior is not null)
            {
                var keys = await _db.Customers.AsNoTracking()
                    .Where(c => c.Id == customerId)
                    .Select(c => c.CaseKeysBalance)
                    .FirstAsync(cancellationToken);
                return MapOpenResult(prior, prior.Reward!, keys, await StripCodesAsync(caseCode, cancellationToken), null);
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var definition = await _db.CaseDefinitions
            .Include(c => c.Prizes.Where(p => p.IsActive && p.Weight > 0))
            .FirstOrDefaultAsync(c => c.Code == caseCode, cancellationToken)
            ?? throw new InvalidOperationException("Кейс не найден");

        if (!definition.IsEnabled)
            throw new InvalidOperationException("Кейс временно недоступен");

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
                       ?? throw new KeyNotFoundException("Клиент не найден");

        if (customer.IsBlocked)
            throw new InvalidOperationException("Клиент заблокирован");

        var keyCost = Math.Max(1, definition.KeyCost);
        if (customer.CaseKeysBalance < keyCost)
            throw new InvalidOperationException("Недостаточно ключей для открытия кейса");

        var clubDay = ClubDayKey(DateTimeOffset.UtcNow);
        var eligible = await FilterByLimitsAsync(definition, clubDay, cancellationToken);
        if (eligible.Count == 0)
            throw new InvalidOperationException("Сейчас нет доступных призов (лимиты). Попробуйте позже.");

        var weightTotal = eligible.Sum(p => p.Weight);
        var roll = RandomNumberGenerator.GetInt32(0, weightTotal);
        var cursor = 0;
        CasePrize? won = null;
        foreach (var p in eligible)
        {
            cursor += p.Weight;
            if (roll < cursor)
            {
                won = p;
                break;
            }
        }

        won ??= eligible[^1];

        customer.CaseKeysBalance -= keyCost;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        _db.CaseKeyLedgers.Add(new CaseKeyLedger
        {
            CustomerId = customer.Id,
            Delta = -keyCost,
            BalanceAfter = customer.CaseKeysBalance,
            Reason = CaseKeyReason.OpenSpend,
            IdempotencyKey = idem is null ? null : $"open-spend:{idem}",
            Comment = $"Открытие {definition.Code}"
        });

        var opening = new CaseOpening
        {
            CustomerId = customer.Id,
            CaseDefinitionId = definition.Id,
            CasePrizeId = won.Id,
            PrizeCodeSnapshot = won.PrizeCode,
            PrizeNameSnapshot = won.Name,
            RaritySnapshot = won.Rarity,
            PrizeTypeSnapshot = won.PrizeType,
            PayloadJsonSnapshot = won.PayloadJson,
            ImageUrlSnapshot = won.ImageUrl,
            KeyCost = keyCost,
            WeightRoll = roll,
            WeightTotal = weightTotal,
            ClubDayKey = clubDay,
            IdempotencyKey = idem
        };
        _db.CaseOpenings.Add(opening);

        var reward = new CaseUserReward
        {
            CustomerId = customer.Id,
            CaseOpening = opening,
            CasePrizeId = won.Id,
            PrizeCode = won.PrizeCode,
            Name = won.Name,
            PrizeType = won.PrizeType,
            PayloadJson = won.PayloadJson,
            ImageUrl = won.ImageUrl,
            Status = CaseRewardStatus.Pending
        };
        _db.CaseUserRewards.Add(reward);

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var strip = await StripCodesAsync(caseCode, cancellationToken);
        return MapOpenResult(opening, reward, customer.CaseKeysBalance, strip, BuildPendingInstruction(reward));
    }

    public async Task GrantKeysAsync(
        Guid customerId,
        int keys,
        CaseKeyReason reason,
        string? comment,
        string? idempotencyKey,
        Guid? employeeId = null,
        Guid? relatedEntityId = null,
        CancellationToken cancellationToken = default)
    {
        if (keys == 0)
            return;

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var exists = await _db.CaseKeyLedgers.AnyAsync(l => l.IdempotencyKey == idempotencyKey, cancellationToken);
            if (exists)
                return;
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
                       ?? throw new KeyNotFoundException("Клиент не найден");

        if (keys < 0 && customer.CaseKeysBalance + keys < 0)
            throw new InvalidOperationException("Недостаточно ключей");

        customer.CaseKeysBalance += keys;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        _db.CaseKeyLedgers.Add(new CaseKeyLedger
        {
            CustomerId = customerId,
            Delta = keys,
            BalanceAfter = customer.CaseKeysBalance,
            Reason = reason,
            IdempotencyKey = idempotencyKey,
            Comment = comment,
            EmployeeId = employeeId,
            RelatedEntityId = relatedEntityId
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<BuyCaseKeysResultDto> BuyKeysAsync(
        BuyCaseKeysRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0 || request.Quantity > 50)
            throw new InvalidOperationException("Количество ключей: от 1 до 50.");
        if (request.UnitPrice < 100)
            throw new InvalidOperationException("Цена ключа: минимум 100 ₸.");
        if (request.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.Card or PaymentMethod.KaspiQr or PaymentMethod.Transfer or PaymentMethod.Mixed))
            throw new InvalidOperationException("Оплата: наличные, карта, Kaspi, перевод или смешанная.");

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден.");
        if (customer.IsBlocked)
            throw new InvalidOperationException("Клиент заблокирован.");

        var total = Math.Round(request.Quantity * request.UnitPrice, 2, MidpointRounding.AwayFromZero);
        var idem = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"case-keys-buy:{request.CustomerId:N}:{request.Quantity}:{total}:{DateTimeOffset.UtcNow:yyyyMMddHHmm}"
            : request.IdempotencyKey.Trim();

        var receipt = await _cash.CreateSaleAsync(
            new CreateSaleRequest(
                ComputerId: null,
                GamingSessionId: null,
                Comment: request.Comment ?? $"Ключи SHIFT CASE ×{request.Quantity}",
                IdempotencyKey: idem,
                Items:
                [
                    new CreateSaleItemRequest(
                        ReceiptItemType.CaseKey,
                        $"Ключ SHIFT CASE ×{request.Quantity}",
                        request.Quantity,
                        request.UnitPrice,
                        0,
                        request.CustomerId)
                ],
                Payments: PaymentParts.Resolve(request.PaymentMethod, total, request.Payments),
                CustomerId: request.CustomerId),
            employeeId,
            cancellationToken);

        await GrantKeysAsync(
            request.CustomerId,
            request.Quantity,
            CaseKeyReason.Purchase,
            $"Покупка ключей · чек {receipt.Number}",
            $"case-key:purchase:{receipt.Id:N}",
            employeeId,
            receipt.Id,
            cancellationToken);

        var keysBalance = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == request.CustomerId)
            .Select(c => c.CaseKeysBalance)
            .FirstAsync(cancellationToken);

        return new BuyCaseKeysResultDto(
            receipt.Id,
            receipt.Number,
            request.Quantity,
            keysBalance,
            receipt.Total);
    }

    public async Task TryGrantRegistrationKeyAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var hasWelcome = await _db.CaseKeyLedgers.AsNoTracking()
            .AnyAsync(l => l.CustomerId == customerId && l.Reason == CaseKeyReason.Registration, cancellationToken);
        if (hasWelcome)
            return;

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
            return;

        await GrantKeysAsync(
            customerId,
            1,
            CaseKeyReason.Registration,
            "Welcome — регистрация",
            $"case-key:registration:{customerId}",
            cancellationToken: cancellationToken);

        customer.CasePlayBaselineMinutes = customer.TotalMinutesPlayed;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task TryGrantDepositKeysAsync(
        Guid customerId,
        decimal depositAmount,
        Guid? receiptId,
        CancellationToken cancellationToken = default)
    {
        if (depositAmount <= 0)
            return;

        if (receiptId is Guid rid)
        {
            var receiptAt = await _db.Receipts.AsNoTracking()
                .Where(r => r.Id == rid)
                .Select(r => (DateTimeOffset?)r.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (receiptAt is null || receiptAt < CaseKeysLaunchUtc)
                return;
        }
        else if (DateTimeOffset.UtcNow < CaseKeysLaunchUtc)
        {
            return;
        }

        var priorDeposits = await _db.CustomerBalanceTransactions.AsNoTracking()
            .CountAsync(t => t.CustomerId == customerId
                             && t.Type == LedgerTransactionType.Deposit
                             && t.Direction == LedgerDirection.Credit
                             && t.CreatedAt >= CaseKeysLaunchUtc, cancellationToken);

        // текущий депозит уже в ledger → first if count == 1 (только после даты перезапуска)
        if (depositAmount >= 2000 && priorDeposits <= 1)
        {
            await GrantKeysAsync(
                customerId,
                1,
                CaseKeyReason.FirstDepositGe2000,
                "Первое пополнение от 2000₸",
                $"case-key:first-deposit:{customerId}",
                relatedEntityId: receiptId,
                cancellationToken: cancellationToken);
        }

        if (depositAmount < 5000)
            return;

        var keysFromAmount = Math.Min(2, (int)(depositAmount / 5000m));
        if (keysFromAmount <= 0)
            return;

        var day = ClubDayKey(DateTimeOffset.UtcNow);
        var grantedToday = await _db.CaseKeyLedgers.AsNoTracking()
            .Where(l => l.CustomerId == customerId
                        && l.Reason == CaseKeyReason.DepositGe5000
                        && l.CreatedAt >= ClubDayStartUtc(day)
                        && l.CreatedAt >= CaseKeysLaunchUtc)
            .SumAsync(l => (int?)l.Delta, cancellationToken) ?? 0;

        var room = Math.Max(0, 2 - grantedToday);
        var grant = Math.Min(room, keysFromAmount);
        if (grant <= 0)
            return;

        var idem = receiptId.HasValue
            ? $"case-key:deposit5k:{receiptId}"
            : $"case-key:deposit5k:{customerId}:{day}:{depositAmount:0}";

        await GrantKeysAsync(
            customerId,
            grant,
            CaseKeyReason.DepositGe5000,
            $"Пополнение {depositAmount:0}₸",
            idem,
            relatedEntityId: receiptId,
            cancellationToken: cancellationToken);
    }

    public Task TryGrantBirthdayKeyAsync(Guid customerId, int year, CancellationToken cancellationToken = default)
        => GrantKeysAsync(
            customerId,
            1,
            CaseKeyReason.Birthday,
            $"День рождения {year}",
            $"case-key:birthday:{customerId}:{year}",
            cancellationToken: cancellationToken);

    public async Task TryGrantPlayHoursKeyAsync(
        Guid customerId,
        int sessionMinutes,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionMinutes < 1)
            return;

        var sessionStarted = await _db.GamingSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (DateTimeOffset?)s.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (sessionStarted is null || sessionStarted < CaseKeysLaunchUtc)
            return;

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
            return;

        var totalBefore = customer.TotalMinutesPlayed;
        customer.TotalMinutesPlayed = totalBefore + sessionMinutes;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        // Только минуты после welcome-ключа; не более 1 ключа за завершение сеанса.
        var baseline = customer.CasePlayBaselineMinutes;
        var effectiveBefore = Math.Max(0, totalBefore - baseline);
        var effectiveAfter = Math.Max(0, customer.TotalMinutesPlayed - baseline);
        var bucketsBefore = effectiveBefore / (15 * 60);
        var bucketsAfter = effectiveAfter / (15 * 60);
        if (bucketsAfter <= bucketsBefore)
            return;

        await GrantKeysAsync(
            customerId,
            1,
            CaseKeyReason.PlayHours15,
            $"15 часов игры (порог #{bucketsAfter})",
            $"case-key:play15:{customerId}:{bucketsAfter}",
            relatedEntityId: sessionId,
            cancellationToken: cancellationToken);
    }

    /// <summary>Welcome-ключ (1 шт.) всем, у кого его ещё не было — при включении кейса.</summary>
    private async Task GrantWelcomeKeysToAllEligibleAsync(CancellationToken cancellationToken)
    {
        var customers = await _db.Customers
            .Where(c => c.IsActive && !c.IsBlocked)
            .ToListAsync(cancellationToken);

        var granted = 0;
        foreach (var customer in customers)
        {
            var hasWelcome = await _db.CaseKeyLedgers.AsNoTracking()
                .AnyAsync(l => l.CustomerId == customer.Id && l.Reason == CaseKeyReason.Registration, cancellationToken);
            if (hasWelcome)
            {
                if (customer.CasePlayBaselineMinutes == 0 && customer.TotalMinutesPlayed > 0)
                {
                    customer.CasePlayBaselineMinutes = customer.TotalMinutesPlayed;
                    customer.UpdatedAt = DateTimeOffset.UtcNow;
                }
                continue;
            }

            await GrantKeysAsync(
                customer.Id,
                1,
                CaseKeyReason.Registration,
                "Welcome — запуск SHIFT CASE",
                $"case-key:registration:{customer.Id}",
                cancellationToken: cancellationToken);

            customer.CasePlayBaselineMinutes = customer.TotalMinutesPlayed;
            customer.UpdatedAt = DateTimeOffset.UtcNow;
            granted++;
        }

        if (granted > 0)
            await _db.SaveChangesAsync(cancellationToken);

        await ReconcileCaseKeyBalancesAsync(cancellationToken);
        _logger.LogInformation("SHIFT CASE enabled: granted welcome keys to {Count} customers", granted);
    }

    private async Task ReconcileCaseKeyBalancesAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.CaseKeyLedgers.AsNoTracking()
            .GroupBy(l => l.CustomerId)
            .Select(g => new { CustomerId = g.Key, Balance = g.Sum(x => x.Delta) })
            .ToListAsync(cancellationToken);

        var map = rows.ToDictionary(x => x.CustomerId, x => x.Balance);
        var customers = await _db.Customers.ToListAsync(cancellationToken);
        var changed = false;
        foreach (var c in customers)
        {
            var expected = map.GetValueOrDefault(c.Id, 0);
            if (c.CaseKeysBalance == expected)
                continue;
            c.CaseKeysBalance = expected;
            c.UpdatedAt = DateTimeOffset.UtcNow;
            changed = true;
        }

        if (changed)
            await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task TryGrantNightPackageKeyAsync(
        Guid customerId,
        string? tariffCode,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tariffCode))
            return;
        if (!tariffCode.Contains("NIGHT", StringComparison.OrdinalIgnoreCase)
            && !tariffCode.Contains("НОЧ", StringComparison.OrdinalIgnoreCase))
            return;

        var sessionStarted = await _db.GamingSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (DateTimeOffset?)s.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (sessionStarted is null || sessionStarted < CaseKeysLaunchUtc)
            return;

        await GrantKeysAsync(
            customerId,
            1,
            CaseKeyReason.BuyNightPackage,
            $"Ночной пакет ({tariffCode})",
            $"case-key:night:{sessionId}",
            relatedEntityId: sessionId,
            cancellationToken: cancellationToken);
    }

    public async Task<CaseUserRewardDto> ClaimRewardAsync(
        Guid rewardId,
        Guid employeeId,
        CaseClaimRewardRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await ApplyRewardAsync(
            rewardId,
            employeeId,
            new ApplyCaseRewardRequest(null, request.Note),
            cancellationToken);
        var reward = await _db.CaseUserRewards.AsNoTracking()
            .FirstAsync(r => r.Id == result.RewardId, cancellationToken);
        return MapReward(reward);
    }

    public async Task<ApplyCaseRewardResultDto> ApplyRewardAsync(
        Guid rewardId,
        Guid employeeId,
        ApplyCaseRewardRequest request,
        CancellationToken cancellationToken = default)
    {
        var reward = await _db.CaseUserRewards
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == rewardId, cancellationToken)
            ?? throw new KeyNotFoundException("Награда не найдена");

        if (reward.Status is CaseRewardStatus.Claimed or CaseRewardStatus.Applied)
        {
            return new ApplyCaseRewardResultDto(
                reward.Id,
                reward.Status,
                reward.Status == CaseRewardStatus.Applied
                    ? "Награда уже зачислена"
                    : "Награда уже выдана");
        }

        if (reward.Status != CaseRewardStatus.Pending)
            throw new InvalidOperationException($"Нельзя зачислить награду в статусе {reward.Status}");

        var customer = reward.Customer
                       ?? await _db.Customers.FirstAsync(c => c.Id == reward.CustomerId, cancellationToken);
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        string message;

        switch (reward.PrizeType)
        {
            case CasePrizeType.Time:
            case CasePrizeType.Service when !PayloadHasTariffKind(reward.PayloadJson):
                if (request.ZoneId is null || request.ZoneId == Guid.Empty)
                    throw new InvalidOperationException("Выберите зал для начисления времени");
                message = await ApplyTimeRewardToZoneAsync(
                    customer, reward, request.ZoneId.Value, employeeId, cancellationToken);
                break;

            case CasePrizeType.Balance:
                message = await ApplyBalanceRewardAsync(customer, reward, employeeId, cancellationToken);
                break;

            case CasePrizeType.Discount:
                reward.Status = CaseRewardStatus.Applied;
                reward.AppliedAt = DateTimeOffset.UtcNow;
                reward.ClaimedByEmployeeId = employeeId;
                reward.ClaimNote = note;
                reward.UpdatedAt = DateTimeOffset.UtcNow;
                message = "Скидка активирована — применится к следующему платному сеансу";
                break;

            case CasePrizeType.BarItem:
                reward.Status = CaseRewardStatus.Claimed;
                reward.ClaimedAt = DateTimeOffset.UtcNow;
                reward.ClaimedByEmployeeId = employeeId;
                reward.ClaimNote = note;
                reward.UpdatedAt = DateTimeOffset.UtcNow;
                message = "Отмечено: выдано на баре";
                break;

            case CasePrizeType.Service:
                reward.Status = CaseRewardStatus.Claimed;
                reward.ClaimedAt = DateTimeOffset.UtcNow;
                reward.ClaimedByEmployeeId = employeeId;
                reward.ClaimNote = note;
                reward.UpdatedAt = DateTimeOffset.UtcNow;
                message = "Отмечено: услуга выдана";
                break;

            default:
                throw new InvalidOperationException("Этот тип приза нельзя зачислить автоматически");
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new ApplyCaseRewardResultDto(reward.Id, reward.Status, message);
    }

    public async Task<IReadOnlyList<CasePendingClaimDto>> ListPendingClaimsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.CaseUserRewards.AsNoTracking()
            .Include(r => r.Customer)
            .Where(r => r.Status == CaseRewardStatus.Pending
                        && (r.PrizeType == CasePrizeType.BarItem
                            || r.PrizeType == CasePrizeType.Discount
                            || (r.PrizeType == CasePrizeType.Service
                                && r.PayloadJson.Contains("tariffKind"))))
            .OrderBy(r => r.CreatedAt)
            .Take(200)
            .Select(r => new CasePendingClaimDto(
                r.Id,
                r.CustomerId,
                (r.Customer.FirstName + " " + r.Customer.LastName).Trim(),
                r.Customer.Phone,
                r.PrizeCode,
                r.Name,
                r.PrizeType,
                r.ImageUrl,
                r.PayloadJson,
                r.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<CaseStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var day = ClubDayKey(DateTimeOffset.UtcNow);
        var dayStart = ClubDayStartUtc(day);

        var openingsToday = await _db.CaseOpenings.CountAsync(o => o.CreatedAt >= dayStart, cancellationToken);
        var openingsTotal = await _db.CaseOpenings.CountAsync(cancellationToken);
        var keysGranted = await _db.CaseKeyLedgers
            .Where(l => l.Delta > 0)
            .SumAsync(l => (int?)l.Delta, cancellationToken) ?? 0;
        var pending = await _db.CaseUserRewards.CountAsync(r => r.Status == CaseRewardStatus.Pending, cancellationToken);

        // Count с условием внутри группы EF в SQL не переводит — считаем через Sum(... ? 1 : 0),
        // иначе ручка статистики падает с 500.
        var byPrizeRaw = await _db.CaseOpenings.AsNoTracking()
            .GroupBy(o => new { o.PrizeCodeSnapshot, o.PrizeNameSnapshot, o.RaritySnapshot })
            .Select(g => new
            {
                g.Key.PrizeCodeSnapshot,
                g.Key.PrizeNameSnapshot,
                g.Key.RaritySnapshot,
                WinsTotal = g.Count(),
                WinsToday = g.Sum(x => x.CreatedAt >= dayStart ? 1 : 0)
            })
            .OrderByDescending(x => x.WinsTotal)
            .ToListAsync(cancellationToken);

        var byPrize = byPrizeRaw
            .Select(x => new CasePrizeStatDto(
                x.PrizeCodeSnapshot,
                x.PrizeNameSnapshot,
                x.RaritySnapshot,
                x.WinsTotal,
                x.WinsToday))
            .ToList();

        return new CaseStatsDto(openingsToday, openingsTotal, keysGranted, pending, byPrize);
    }

    public async Task<(decimal Percent, Guid? RewardId)> PeekPendingDiscountAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var reward = await _db.CaseUserRewards.AsNoTracking()
            .Where(r => r.CustomerId == customerId
                        && r.Status == CaseRewardStatus.Applied
                        && r.PrizeType == CasePrizeType.Discount)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (reward is null)
            return (0, null);

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(reward.PayloadJson) ? "{}" : reward.PayloadJson);
        var percent = 0m;
        if (doc.RootElement.TryGetProperty("percent", out var p) && p.TryGetDecimal(out var d))
            percent = d;

        percent = Math.Clamp(percent, 0, 50);
        return percent > 0 ? (percent, reward.Id) : (0, null);
    }

    public async Task ConsumeDiscountRewardAsync(
        Guid rewardId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var reward = await _db.CaseUserRewards
            .FirstOrDefaultAsync(r => r.Id == rewardId && r.Status == CaseRewardStatus.Applied, cancellationToken);
        if (reward is null)
            return;

        reward.Status = CaseRewardStatus.Claimed;
        reward.ClaimedAt = DateTimeOffset.UtcNow;
        reward.ClaimNote = $"Автоприменение к сеансу {sessionId:N}";
        reward.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string BuildPendingInstruction(CaseUserReward reward)
    {
        return reward.PrizeType switch
        {
            CasePrizeType.Time => "Кассир: выберите зал ниже и нажмите «Зачислить время».",
            CasePrizeType.Balance => "Кассир: проверьте приз и нажмите «Зачислить на баланс».",
            CasePrizeType.BarItem => "Кассир: выдайте напиток на баре и нажмите «Выдано на баре».",
            CasePrizeType.Discount => "Кассир: нажмите «Активировать скидку» — она применится к следующему платному сеансу.",
            CasePrizeType.Service when PayloadHasTariffKind(reward.PayloadJson) =>
                "Кассир: активируйте услугу (ночь/пакет) и нажмите «Выдано».",
            CasePrizeType.Service => "Кассир: выберите зал и нажмите «Зачислить время».",
            _ => "Кассир: зачислите приз вручную."
        };
    }

    private static bool PayloadHasTariffKind(string? payloadJson)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
        return doc.RootElement.TryGetProperty("tariffKind", out _);
    }

    private async Task<string> ApplyTimeRewardToZoneAsync(
        Customer customer,
        CaseUserReward reward,
        Guid zoneId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(reward.PayloadJson) ? "{}" : reward.PayloadJson);
        var minutes = doc.RootElement.TryGetProperty("minutes", out var m) && m.TryGetInt32(out var mi) ? mi : 0;
        if (minutes <= 0)
            throw new InvalidOperationException("В призе не указаны минуты");

        var zone = await _db.Zones.AsNoTracking()
            .FirstOrDefaultAsync(z => z.Id == zoneId && z.BranchId == customer.BranchId && z.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Зал не найден или неактивен");

        var exists = await _db.CustomerTimeBankTransactions
            .AnyAsync(t => t.IdempotencyKey == $"case-reward-time:{reward.Id}", cancellationToken);
        if (exists)
        {
            reward.Status = CaseRewardStatus.Applied;
            return $"+{minutes} мин уже начислены ранее";
        }

        var bank = await _db.CustomerZoneTimeBanks
            .FirstOrDefaultAsync(b => b.CustomerId == customer.Id && b.ZoneId == zoneId, cancellationToken);
        if (bank is null)
        {
            bank = new CustomerZoneTimeBank { CustomerId = customer.Id, ZoneId = zoneId, Minutes = 0 };
            _db.CustomerZoneTimeBanks.Add(bank);
        }

        var before = bank.Minutes;
        bank.Minutes += minutes;
        bank.UpdatedAt = DateTimeOffset.UtcNow;

        _db.CustomerTimeBankTransactions.Add(new CustomerTimeBankTransaction
        {
            CustomerId = customer.Id,
            ZoneId = zoneId,
            Reason = TimeBankReason.ManualCredit,
            Direction = LedgerDirection.Credit,
            Minutes = minutes,
            BalanceBefore = before,
            BalanceAfter = bank.Minutes,
            IdempotencyKey = $"case-reward-time:{reward.Id}",
            Comment = $"SHIFT CASE: {reward.Name}"
        });

        var otherSum = await _db.CustomerZoneTimeBanks
            .Where(b => b.CustomerId == customer.Id && b.ZoneId != zoneId)
            .SumAsync(b => (int?)b.Minutes, cancellationToken) ?? 0;
        customer.TimeBankMinutes = otherSum + bank.Minutes;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        customer.UpdatedBy = employeeId;

        reward.Status = CaseRewardStatus.Applied;
        reward.AppliedAt = DateTimeOffset.UtcNow;
        reward.ClaimedByEmployeeId = employeeId;
        reward.UpdatedAt = DateTimeOffset.UtcNow;

        return $"+{minutes} мин начислено в зал «{zone.Name}»";
    }

    private async Task<string> ApplyBalanceRewardAsync(
        Customer customer,
        CaseUserReward reward,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(reward.PayloadJson) ? "{}" : reward.PayloadJson);
        var amount = doc.RootElement.TryGetProperty("amount", out var a) && a.TryGetDecimal(out var am) ? am : 0;
        if (amount <= 0)
            throw new InvalidOperationException("В призе не указана сумма");

        var wallet = doc.RootElement.TryGetProperty("wallet", out var w) ? w.GetString() : "balance";
        var toBonus = string.Equals(wallet, "bonus", StringComparison.OrdinalIgnoreCase);

        if (toBonus)
        {
            var exists = await _db.CustomerBalanceTransactions
                .AnyAsync(t => t.IdempotencyKey == $"case-reward-bonus:{reward.Id}", cancellationToken);
            if (exists)
            {
                reward.Status = CaseRewardStatus.Applied;
                return $"+{amount:0} ₸ на бонусы уже начислены ранее";
            }

            customer.BonusBalance += amount;
            _db.CustomerBalanceTransactions.Add(new CustomerBalanceTransaction
            {
                CustomerId = customer.Id,
                Type = LedgerTransactionType.BonusCredit,
                Direction = LedgerDirection.Credit,
                Amount = amount,
                BalanceBefore = customer.Balance,
                BalanceAfter = customer.Balance,
                SourceType = "ShiftCase",
                IdempotencyKey = $"case-reward-bonus:{reward.Id}",
                Comment = $"SHIFT CASE: {reward.Name}"
            });
            reward.Status = CaseRewardStatus.Applied;
            reward.AppliedAt = DateTimeOffset.UtcNow;
            reward.ClaimedByEmployeeId = employeeId;
            reward.UpdatedAt = DateTimeOffset.UtcNow;
            customer.UpdatedAt = DateTimeOffset.UtcNow;
            customer.UpdatedBy = employeeId;
            return $"+{amount:0} ₸ на бонусный баланс";
        }

        var balanceExists = await _db.CustomerBalanceTransactions
            .AnyAsync(t => t.IdempotencyKey == $"case-reward-balance:{reward.Id}", cancellationToken);
        if (balanceExists)
        {
            reward.Status = CaseRewardStatus.Applied;
            return $"+{amount:0} ₸ на баланс уже начислены ранее";
        }

        var before = customer.Balance;
        customer.Balance += amount;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        customer.UpdatedBy = employeeId;
        _db.CustomerBalanceTransactions.Add(new CustomerBalanceTransaction
        {
            CustomerId = customer.Id,
            Type = LedgerTransactionType.ManualAdjustment,
            Direction = LedgerDirection.Credit,
            Amount = amount,
            BalanceBefore = before,
            BalanceAfter = customer.Balance,
            SourceType = "ShiftCase",
            IdempotencyKey = $"case-reward-balance:{reward.Id}",
            Comment = $"SHIFT CASE: {reward.Name}"
        });
        reward.Status = CaseRewardStatus.Applied;
        reward.AppliedAt = DateTimeOffset.UtcNow;
        reward.ClaimedByEmployeeId = employeeId;
        reward.UpdatedAt = DateTimeOffset.UtcNow;
        return $"+{amount:0} ₸ на баланс";
    }

    private async Task<List<CasePrize>> FilterByLimitsAsync(
        CaseDefinition definition,
        string clubDay,
        CancellationToken cancellationToken)
    {
        var prizes = definition.Prizes.Where(p => p.IsActive && p.Weight > 0).OrderBy(p => p.SortOrder).ToList();
        var result = new List<CasePrize>();

        foreach (var prize in prizes)
        {
            if (prize.DailyLimit is int daily)
            {
                var used = await _db.CaseOpenings.CountAsync(
                    o => o.CaseDefinitionId == definition.Id
                         && o.CasePrizeId == prize.Id
                         && o.ClubDayKey == clubDay,
                    cancellationToken);
                if (used >= daily)
                    continue;
            }

            if (prize.TotalLimit is int total)
            {
                var used = await _db.CaseOpenings.CountAsync(
                    o => o.CaseDefinitionId == definition.Id && o.CasePrizeId == prize.Id,
                    cancellationToken);
                if (used >= total)
                    continue;
            }

            result.Add(prize);
        }

        return result;
    }

    private async Task<CaseCatalogDto> BuildCatalogAsync(string caseCode, CancellationToken cancellationToken)
    {
        var def = await _db.CaseDefinitions.AsNoTracking()
            .Include(c => c.Prizes.Where(p => p.IsActive))
            .FirstOrDefaultAsync(c => c.Code == caseCode, cancellationToken)
            ?? throw new InvalidOperationException("Кейс не найден");

        var weightSum = def.Prizes.Sum(p => p.Weight);
        var prizes = def.Prizes
            .OrderBy(p => p.SortOrder)
            .Select(p => new CasePrizePublicDto(
                p.PrizeCode,
                p.Name,
                p.Description,
                p.PrizeType,
                p.Rarity,
                p.ImageUrl,
                p.RequiresClaim,
                def.ShowProbabilitiesToUsers && weightSum > 0
                    ? Math.Round(100m * p.Weight / weightSum, 2)
                    : null))
            .ToList();

        return new CaseCatalogDto(
            def.Code,
            def.Title,
            def.Description,
            null,
            def.IsEnabled,
            def.KeyCost,
            def.ShowProbabilitiesToUsers,
            prizes,
            Array.Empty<CaseKeyRuleDto>());
    }

    private async Task<IReadOnlyList<string>> StripCodesAsync(string caseCode, CancellationToken cancellationToken)
    {
        return await _db.CasePrizes.AsNoTracking()
            .Where(p => p.CaseDefinition.Code == caseCode && p.IsActive)
            .OrderBy(p => p.SortOrder)
            .Select(p => p.PrizeCode)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> ResolveTimePrizeZoneIdsAsync(
        Guid branchId,
        string? zoneRaw,
        CancellationToken cancellationToken)
    {
        var key = (zoneRaw ?? "Any").Trim().ToLowerInvariant();
        var anyZone = key is "any" or "all" or "любой" or "*" or "";

        var zones = await _db.Zones.AsNoTracking()
            .Where(z => z.BranchId == branchId && z.IsActive)
            .Select(z => new { z.Id, z.Code, z.Name, z.Kind })
            .ToListAsync(cancellationToken);

        if (zones.Count == 0)
            return Array.Empty<Guid>();

        if (anyZone)
        {
            // Игровые зоны зала — одни и те же минуты начисляются в каждую, чтобы тратить в любом зале.
            var gaming = zones
                .Where(z =>
                {
                    if (string.Equals(z.Kind, "Hall", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(z.Kind, "Gaming", StringComparison.OrdinalIgnoreCase))
                        return true;
                    var code = z.Code.ToUpperInvariant();
                    var name = z.Name.ToUpperInvariant();
                    return code.Contains("STD") || code.Contains("STANDARD") || code.Contains("VIP")
                           || code.Contains("BOOT") || name.Contains("СТАНДАРТ") || name.Contains("VIP")
                           || name.Contains("BOOT") || name.Contains("БУТ");
                })
                .Select(z => z.Id)
                .Distinct()
                .ToList();
            return gaming.Count > 0 ? gaming : zones.Select(z => z.Id).ToList();
        }

        var one = await ResolveZoneIdAsync(branchId, zoneRaw, cancellationToken);
        return one is Guid id ? new[] { id } : Array.Empty<Guid>();
    }

    private async Task<Guid?> ResolveZoneIdAsync(Guid branchId, string? zoneRaw, CancellationToken cancellationToken)
    {
        var key = (zoneRaw ?? "Standard").Trim().ToLowerInvariant();
        var aliases = key switch
        {
            "standard" or "std" or "стандарт" => new[] { "STD", "Standard", "STANDARD", "Стандарт" },
            "vip" => new[] { "VIP", "Vip" },
            "bootcamp" or "boot" or "буткемп" => new[] { "BOOT", "Bootcamp", "BOOTCAMP", "Boot" },
            _ => new[] { zoneRaw ?? "STD" }
        };

        var zones = await _db.Zones.AsNoTracking()
            .Where(z => z.BranchId == branchId && z.IsActive)
            .Select(z => new { z.Id, z.Code, z.Name })
            .ToListAsync(cancellationToken);

        foreach (var a in aliases)
        {
            var hit = zones.FirstOrDefault(z =>
                z.Code.Equals(a, StringComparison.OrdinalIgnoreCase)
                || z.Name.Equals(a, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit.Id;
        }

        return zones.OrderBy(z => z.Code).Select(z => (Guid?)z.Id).FirstOrDefault();
    }

    private string? ResolveLootPath()
    {
        var candidates = new[]
        {
            Path.Combine(_env.ContentRootPath, "wwwroot", "site", "case", "shift-case-loot.json"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "site", "case", "shift-case-loot.json"),
            Path.Combine(_env.ContentRootPath, "..", "ShiftClub.Server", "wwwroot", "site", "case", "shift-case-loot.json")
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private static CaseOpenResultDto MapOpenResult(
        CaseOpening opening,
        CaseUserReward reward,
        int keys,
        IReadOnlyList<string> strip,
        string? applyMessage)
        => new(
            opening.Id,
            reward.Id,
            opening.PrizeCodeSnapshot,
            opening.PrizeNameSnapshot,
            opening.PrizeTypeSnapshot,
            opening.RaritySnapshot,
            opening.ImageUrlSnapshot,
            opening.PayloadJsonSnapshot,
            reward.Status,
            applyMessage
            ?? (reward.Status == CaseRewardStatus.Applied
                ? "Награда зачислена"
                : reward.Status == CaseRewardStatus.Claimed
                    ? "Награда выдана"
                    : BuildPendingInstruction(reward)),
            keys,
            strip);

    private static CaseOpeningDto MapOpening(CaseOpening o)
        => new(
            o.Id,
            o.PrizeCodeSnapshot,
            o.PrizeNameSnapshot,
            o.PrizeTypeSnapshot,
            o.RaritySnapshot,
            o.ImageUrlSnapshot,
            o.PayloadJsonSnapshot,
            o.Reward?.Status ?? CaseRewardStatus.Pending,
            o.CreatedAt);

    private static CaseUserRewardDto MapReward(CaseUserReward r)
        => new(
            r.Id,
            r.CaseOpeningId,
            r.PrizeCode,
            r.Name,
            r.PrizeType,
            null,
            r.ImageUrl,
            r.PayloadJson,
            r.Status,
            r.CreatedAt,
            r.ClaimedAt);

    private static string MaskName(string first, string last)
    {
        var f = string.IsNullOrWhiteSpace(first) ? "Игрок" : first.Trim();
        var initial = string.IsNullOrWhiteSpace(last) ? "" : $" {char.ToUpperInvariant(last.Trim()[0])}.";
        return f.Length <= 12 ? f + initial : f[..12] + "…" + initial;
    }

    private static CasePrizeType ParsePrizeType(string? raw) => (raw ?? "").Trim().ToUpperInvariant() switch
    {
        "TIME" => CasePrizeType.Time,
        "BALANCE" => CasePrizeType.Balance,
        "BAR_ITEM" => CasePrizeType.BarItem,
        "DISCOUNT" => CasePrizeType.Discount,
        "SERVICE" => CasePrizeType.Service,
        _ => CasePrizeType.Custom
    };

    private static CasePrizeRarity ParseRarity(string? raw) => (raw ?? "").Trim().ToUpperInvariant() switch
    {
        "COMMON" => CasePrizeRarity.Common,
        "UNCOMMON" => CasePrizeRarity.Uncommon,
        "RARE" => CasePrizeRarity.Rare,
        "EPIC" => CasePrizeRarity.Epic,
        "LEGENDARY" => CasePrizeRarity.Legendary,
        _ => CasePrizeRarity.Common
    };

    private static string ClubDayKey(DateTimeOffset utc)
        => TimeZoneInfo.ConvertTime(utc, ClubTz).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset ClubDayStartUtc(string dayKey)
    {
        var localDate = DateOnly.ParseExact(dayKey, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var local = new DateTimeOffset(localDate.ToDateTime(TimeOnly.MinValue), ClubTz.GetUtcOffset(localDate.ToDateTime(TimeOnly.MinValue)));
        return local.ToUniversalTime();
    }

    private static TimeZoneInfo ResolveClubTz()
    {
        foreach (var id in new[] { "Asia/Almaty", "West Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* next */ }
        }

        return TimeZoneInfo.CreateCustomTimeZone("ShiftClub+5", TimeSpan.FromHours(5), "SHIFT", "SHIFT");
    }

    private sealed class LootFile
    {
        public string? CaseCode { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? EconomicsNote { get; set; }
        public decimal ExpectedCostKzt { get; set; }
        public bool ShowProbabilitiesToUsers { get; set; }
        public LootLimits? Limits { get; set; }
        public List<LootPrize>? Prizes { get; set; }
    }

    private sealed class LootLimits
    {
        public int LegendaryNightPerDay { get; set; }
        public int Vip2hPerDay { get; set; }
        public int BarItemPerDayGlobal { get; set; }
    }

    private sealed class LootPrize
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }
        public string? Rarity { get; set; }
        public int Weight { get; set; }
        public decimal CostEstimateKzt { get; set; }
        public JsonElement? Payload { get; set; }
        public string? Image { get; set; }
        public int? DailyLimit { get; set; }
        public int? TotalLimit { get; set; }
        public bool RequiresClaim { get; set; }
    }
}
