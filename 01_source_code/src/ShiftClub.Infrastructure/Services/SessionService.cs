using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Computers;
using ShiftClub.Application.Sessions;
using ShiftClub.Application.Time;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Infrastructure.SignalR;
using ShiftClub.Shared.Contracts.Computers;
using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared.SignalR;

namespace ShiftClub.Infrastructure.Services;

public sealed class SessionService : ISessionService
{
    private readonly ShiftClubDbContext _db;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly IHubContext<ComputerHub> _computerHub;
    private readonly IComputerService _computers;
    private readonly ICashService _cash;
    private readonly ICustomerService _customers;
    private readonly ITelegramAlertSink _telegramAlerts;
    private readonly ICustomerTelegramNotifySink _customerNotify;
    private readonly IClubSettingsService _settings;
    private readonly ICaseService _cases;
    private readonly ICustomerEngagementService _engagement;
    private readonly ILicenseService _license;

    public SessionService(
        ShiftClubDbContext db,
        IHubContext<StaffHub> staffHub,
        IHubContext<ComputerHub> computerHub,
        IComputerService computers,
        ICashService cash,
        ICustomerService customers,
        ITelegramAlertSink telegramAlerts,
        ICustomerTelegramNotifySink customerNotify,
        IClubSettingsService settings,
        ICaseService cases,
        ICustomerEngagementService engagement,
        ILicenseService license)
    {
        _db = db;
        _staffHub = staffHub;
        _computerHub = computerHub;
        _computers = computers;
        _cash = cash;
        _customers = customers;
        _telegramAlerts = telegramAlerts;
        _customerNotify = customerNotify;
        _settings = settings;
        _cases = cases;
        _engagement = engagement;
        _license = license;
    }

    public async Task<IReadOnlyList<TariffDto>> GetTariffsAsync(
        Guid? branchId,
        bool includeInactive = false,
        bool onlyAvailableNow = false,
        Guid? zoneId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Tariffs.AsNoTracking().Include(t => t.Zone).AsQueryable();
        if (!includeInactive)
            query = query.Where(t => t.IsActive);
        if (branchId.HasValue)
            query = query.Where(t => t.BranchId == branchId.Value);
        if (zoneId.HasValue)
            query = query.Where(t => t.ZoneId == null || t.ZoneId == zoneId.Value);

        var list = await query.OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(cancellationToken);
        var tz = await ResolveBranchTimeZoneAsync(branchId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var promo = await _settings.GetMarketingPromoStoredAsync(cancellationToken);

        IEnumerable<Tariff> filtered = list;
        if (onlyAvailableNow)
            filtered = list.Where(t => TariffAvailability.IsAvailableNow(t, now, tz));

        return filtered.Select(t => MapTariff(t, TariffAvailability.IsAvailableNow(t, now, tz), tz, promo)).ToList();
    }

    public async Task<TariffDto> CreateTariffAsync(UpsertTariffRequest request, CancellationToken cancellationToken = default)
    {
        var branchId = request.BranchId
                       ?? await _db.Branches.Select(b => b.Id).FirstAsync(cancellationToken);
        ValidateTariffRequest(request);

        var tariff = new Tariff { BranchId = branchId };
        ApplyTariff(tariff, request);
        _db.Tariffs.Add(tariff);
        await _db.SaveChangesAsync(cancellationToken);
        await _db.Entry(tariff).Reference(t => t.Zone).LoadAsync(cancellationToken);
        var tz = await ResolveBranchTimeZoneAsync(branchId, cancellationToken);
        var promo = await _settings.GetMarketingPromoStoredAsync(cancellationToken);
        return MapTariff(tariff, TariffAvailability.IsAvailableNow(tariff, DateTimeOffset.UtcNow, tz), tz, promo);
    }

    public async Task<TariffDto> UpdateTariffAsync(Guid id, UpsertTariffRequest request, CancellationToken cancellationToken = default)
    {
        ValidateTariffRequest(request);
        var tariff = await _db.Tariffs.Include(t => t.Zone).FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Тариф не найден");
        ApplyTariff(tariff, request);
        tariff.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        var tz = await ResolveBranchTimeZoneAsync(tariff.BranchId, cancellationToken);
        var promo = await _settings.GetMarketingPromoStoredAsync(cancellationToken);
        return MapTariff(tariff, TariffAvailability.IsAvailableNow(tariff, DateTimeOffset.UtcNow, tz), tz, promo);
    }

    public async Task DeactivateTariffAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tariff = await _db.Tariffs.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                     ?? throw new KeyNotFoundException("Тариф не найден");
        tariff.IsActive = false;
        tariff.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateTariffRequest(UpsertTariffRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Укажите название тарифа");
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new InvalidOperationException("Укажите код тарифа");
        if (request.DaysOfWeekMask is < 1 or > 127)
            throw new InvalidOperationException("Выберите хотя бы один день недели");

        if (request.DurationMode == TariffDurationMode.TimeWindow)
        {
            if (string.IsNullOrWhiteSpace(request.AvailableFrom) || string.IsNullOrWhiteSpace(request.AvailableTo))
                throw new InvalidOperationException("Для режима «Временной интервал» укажите время начала и окончания");
            if (request.FixedPrice is null or < 0)
                throw new InvalidOperationException("Для режима «Временной интервал» укажите стоимость");
            return;
        }

        if (request.Kind == TariffKind.Package && (request.FixedDurationMinutes is null or <= 0 || request.FixedPrice is null))
            throw new InvalidOperationException("Для пакета укажите длительность и фиксированную цену");
    }

    private static void ApplyTariff(Tariff tariff, UpsertTariffRequest request)
    {
        tariff.ZoneId = request.ZoneId;
        tariff.Name = request.Name.Trim();
        tariff.Code = request.Code.Trim().ToUpperInvariant();
        tariff.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        tariff.Kind = request.Kind;
        tariff.BillingMode = request.BillingMode;
        tariff.DurationMode = request.DurationMode;
        tariff.PricePerHour = request.PricePerHour;
        tariff.MinCharge = request.MinCharge;
        tariff.FixedDurationMinutes = request.DurationMode == TariffDurationMode.TimeWindow
            ? null
            : request.FixedDurationMinutes;
        tariff.FixedPrice = request.FixedPrice;
        tariff.MinDurationMinutes = request.MinDurationMinutes;
        tariff.MaxDurationMinutes = request.MaxDurationMinutes;
        tariff.DaysOfWeekMask = request.DaysOfWeekMask <= 0 ? TariffAvailability.AllDays : request.DaysOfWeekMask;
        tariff.AvailableFrom = ParseTime(request.AvailableFrom);
        tariff.AvailableTo = ParseTime(request.AvailableTo);
        tariff.AllowPause = request.AllowPause;
        tariff.IsActive = request.IsActive;
        tariff.SortOrder = request.SortOrder;
        tariff.ColorHex = string.IsNullOrWhiteSpace(request.ColorHex) ? null : request.ColorHex.Trim();

        if (request.DurationMode == TariffDurationMode.TimeWindow && request.Kind == TariffKind.Hourly)
            tariff.Kind = TariffKind.Package;
    }

    private static TimeSpan? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (TimeSpan.TryParse(value, out var ts))
            return ts;
        if (TimeOnly.TryParse(value, out var t))
            return t.ToTimeSpan();
        throw new InvalidOperationException($"Некорректное время: {value} (ожидается ЧЧ:ММ)");
    }

    private async Task<string?> ResolveBranchTimeZoneAsync(Guid? branchId, CancellationToken ct)
    {
        if (branchId.HasValue)
            return await _db.Branches.AsNoTracking().Where(b => b.Id == branchId).Select(b => b.TimeZoneId).FirstOrDefaultAsync(ct);
        return await _db.Branches.AsNoTracking().Select(b => b.TimeZoneId).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Серия посещений: только при старте сеанса с привязанным аккаунтом (не при логине на Shell).
    /// </summary>
    private async Task RecordVisitStreakForSessionAsync(
        Guid customerId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
            return;

        var tzId = await _db.Branches.AsNoTracking()
            .Where(b => b.Id == branchId)
            .Select(b => b.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        var tz = BranchTimeZone.Resolve(tzId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        await _engagement.RecordVisitAsync(customer, today, cancellationToken);
    }

    public async Task<SessionDto> StartGuestSessionAsync(
        StartGuestSessionRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.GamingSessions.AsNoTracking()
                .Include(s => s.Computer)
                .Include(s => s.Zone)
                .Include(s => s.Tariff)
                .FirstOrDefaultAsync(s => s.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
                return MapSession(existing);
        }

        await _license.EnsureCanStartSessionAsync(cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var computer = await _db.Computers
            .FirstOrDefaultAsync(c => c.Id == request.ComputerId, cancellationToken)
            ?? throw new KeyNotFoundException("Computer not found.");

        if (!computer.IsApproved)
            throw new InvalidOperationException("ПК не подтверждён.");
        if (computer.ZoneId is null)
            throw new InvalidOperationException("У ПК не задана зона.");
        if (computer.CurrentSessionId is not null || computer.Status == ComputerStatus.InSession)
            throw new InvalidOperationException("На этом ПК уже есть активный сеанс.");

        var liveExists = await _db.GamingSessions.AsNoTracking().AnyAsync(
            s => s.ComputerId == computer.Id
                 && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused),
            cancellationToken);
        if (liveExists)
            throw new InvalidOperationException("На этом ПК уже есть активный сеанс.");
        if (computer.IsMaintenance || computer.Status == ComputerStatus.Maintenance)
            throw new InvalidOperationException("ПК на техобслуживании.");
        if (computer.StationKind != StationKind.Console)
        {
            if (computer.Status == ComputerStatus.Updating)
                throw new InvalidOperationException("ПК обновляется.");
            if (computer.Status == ComputerStatus.Error)
                throw new InvalidOperationException("ПК в состоянии ошибки — сначала восстановите клиент.");
        }

        Booking? booking = null;
        BookingComputer? bookingLink = null;
        if (request.BookingId is Guid bookingId)
        {
            booking = await _db.Bookings
                .Include(b => b.Computers)
                .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
                ?? throw new KeyNotFoundException("Бронь не найдена.");

            if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.Arrived or BookingStatus.Active))
                throw new InvalidOperationException("По этой брони нельзя запустить сеанс.");

            bookingLink = booking.Computers.FirstOrDefault(c => c.ComputerId == computer.Id)
                ?? throw new InvalidOperationException("Этот ПК не входит в бронь.");

            if (bookingLink.GamingSessionId is not null)
                throw new InvalidOperationException("На этом ПК брони сеанс уже запущен.");

            if (computer.Status == ComputerStatus.Reserved || booking.Status is BookingStatus.Arrived or BookingStatus.Confirmed)
            {
                // Разрешаем старт с Reserved под своей бронью
            }
        }
        else if (computer.Status == ComputerStatus.Reserved)
        {
            throw new InvalidOperationException(
                "ПК зарезервирован под бронь (за 30 мин до начала). Запустите сеанс из брони или отмените её.");
        }

        var tariff = await _db.Tariffs.FirstOrDefaultAsync(t => t.Id == request.TariffId && t.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Tariff not found.");

        if (tariff.ZoneId.HasValue && tariff.ZoneId != computer.ZoneId)
            throw new InvalidOperationException("Тариф недоступен для этой зоны.");

        var tz = await ResolveBranchTimeZoneAsync(computer.BranchId, cancellationToken);
        if (!TariffAvailability.IsAvailableNow(tariff, DateTimeOffset.UtcNow, tz))
            throw new InvalidOperationException("Тариф сейчас недоступен (день недели или время).");

        Customer? customer = null;
        var customerId = request.CustomerId ?? booking?.CustomerId;
        if (customerId.HasValue)
        {
            customer = await _db.Customers
                .Include(c => c.LoyaltyLevel)
                .FirstOrDefaultAsync(c => c.Id == customerId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Клиент не найден.");
            if (customer.BranchId != computer.BranchId)
                throw new InvalidOperationException("Клиент принадлежит другому филиалу.");
            if (customer.IsBlocked)
                throw new InvalidOperationException("Клиент заблокирован.");
            if (!customer.IsActive)
                throw new InvalidOperationException("Клиент неактивен.");

            var otherLive = await _db.GamingSessions.AsNoTracking()
                .Include(s => s.Computer)
                .Where(s => s.CustomerId == customer.Id
                            && s.ComputerId != computer.Id
                            && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused))
                .Select(s => s.Computer.DisplayName ?? s.Computer.WindowsName ?? "другом ПК")
                .FirstOrDefaultAsync(cancellationToken);
            if (otherLive is not null)
                throw new InvalidOperationException(
                    $"Клиент уже играет на «{otherLive}». Одновременно только на одном ПК.");
        }

        int durationMinutes;
        decimal price;
        var spendTimeBank = 0;
        DateTimeOffset? windowStart = null;
        DateTimeOffset? windowEnd = null;
        var durationMode = tariff.DurationMode;

        if (request.UseTimeBank)
        {
            if (customer is null)
                throw new InvalidOperationException("Списание сохранённого времени требует клиента.");
            if (computer.ZoneId is null)
                throw new InvalidOperationException("У ПК не задана зона — банк времени привязан к зоне.");

            var zoneBank = await _customers.GetZoneTimeBankMinutesAsync(
                customer.Id, computer.ZoneId.Value, cancellationToken);
            if (zoneBank <= 0)
                throw new InvalidOperationException(
                    "Нет минут в банке для этой зоны. Минуты другой зоны сюда не переносятся.");

            spendTimeBank = Math.Min(
                zoneBank,
                request.DurationMinutes > 0 ? request.DurationMinutes : zoneBank);
            durationMinutes = spendTimeBank;
            price = 0;
            durationMode = TariffDurationMode.FixedDuration;
        }
        else if (tariff.DurationMode == TariffDurationMode.TimeWindow)
        {
            if (!TariffAvailability.TryResolveTimeWindow(
                    tariff, DateTimeOffset.UtcNow, tz,
                    out var periodStart, out var periodEnd, out var remaining))
            {
                throw new InvalidOperationException(
                    "Тариф с временным интервалом сейчас недоступен или до окончания осталось меньше минуты.");
            }

            // Касса может взять не всё окно, а N минут — считаем по ставке ₸/ч (или пропорции фикс. цены).
            if (request.DurationMinutes > 0 && request.DurationMinutes < remaining)
            {
                durationMinutes = request.DurationMinutes;
                durationMode = TariffDurationMode.FixedDuration;
                windowStart = null;
                windowEnd = null;
                if (tariff.PricePerHour > 0)
                    price = SessionPricing.CalculateHourlyPrice(tariff, durationMinutes);
                else if (tariff.FixedPrice is > 0)
                    price = Math.Round(
                        tariff.FixedPrice.Value * durationMinutes / remaining,
                        2,
                        MidpointRounding.AwayFromZero);
                else
                    throw new InvalidOperationException(
                        "У дневного/ночного тарифа нет ставки для частичного времени. Укажите цену за час.");
            }
            else
            {
                durationMinutes = remaining;
                windowStart = periodStart;
                windowEnd = periodEnd;
                price = SessionPricing.Calculate(tariff, durationMinutes);
            }
        }
        else
        {
            if (request.DurationMinutes <= 0)
                throw new InvalidOperationException("DurationMinutes must be positive.");
            durationMinutes = TariffAvailability.ResolveDurationMinutes(tariff, request.DurationMinutes);
            if (durationMinutes <= 0)
                throw new InvalidOperationException("Некорректная длительность.");
            price = SessionPricing.Calculate(tariff, durationMinutes);
        }

        var nowProbe = DateTimeOffset.UtcNow;
        var endsProbe = windowEnd ?? nowProbe.AddMinutes(durationMinutes);
        await EnsureNoBookingConflictForSessionAsync(
            computer.Id,
            nowProbe,
            endsProbe,
            request.BookingId,
            cancellationToken);

        // Маркетинговая акция только на 2+1 / 3+2 / день / ночь; затем лояльность, бронь
        var listPrice = price;
        var promo = await _settings.GetMarketingPromoStoredAsync(cancellationToken);
        var marketingDiscount = MarketingPromoTariff.DiscountAmount(
            tariff, listPrice, promo, DateTimeOffset.UtcNow);

        var priceAfterPromo = Math.Max(0, listPrice - marketingDiscount);
        var loyaltyDiscount = LoyaltyTimeDiscount(priceAfterPromo, customer?.LoyaltyLevel?.TimeDiscountPercent);
        var priceAfterLoyalty = Math.Max(0, priceAfterPromo - loyaltyDiscount);

        decimal caseDiscount = 0;
        Guid? caseDiscountRewardId = null;
        if (customer is not null && !request.UseTimeBank && priceAfterLoyalty > 0)
        {
            var (casePct, rewardId) = await _cases.PeekPendingDiscountAsync(customer.Id, cancellationToken);
            if (casePct > 0 && rewardId is Guid rid)
            {
                caseDiscount = Math.Round(priceAfterLoyalty * casePct / 100m, 2, MidpointRounding.AwayFromZero);
                caseDiscountRewardId = rid;
            }
        }

        var priceAfterCase = Math.Max(0, priceAfterLoyalty - caseDiscount);

        decimal prepaidDiscount = 0;
        if (booking is not null && booking.PrepaidAmount > 0 && priceAfterCase > 0)
        {
            var usedDiscount = await SumPrepaidAppliedAsync(booking.Id, cancellationToken);
            var remainingPrepaid = Math.Max(0, booking.PrepaidAmount - usedDiscount);
            prepaidDiscount = Math.Min(remainingPrepaid, priceAfterCase);
        }

        var totalDiscount = marketingDiscount + loyaltyDiscount + caseDiscount + prepaidDiscount;
        var chargePrice = Math.Max(0, listPrice - totalDiscount);

        if (request.PaymentMethod == PaymentMethod.Postpay)
            throw new InvalidOperationException("Постоплата отключена. Оплатите сеанс сразу (нал / карта / Kaspi / баланс).");

        if (!request.UseTimeBank && request.PaymentMethod == PaymentMethod.Free && chargePrice > 0)
            throw new InvalidOperationException("Бесплатный старт недоступен для платного тарифа. Выберите способ оплаты.");

        var requiresPaymentNow = !request.UseTimeBank
                                 && request.PaymentMethod is not PaymentMethod.Free
                                 && chargePrice > 0;

        if (request.PaymentMethod == PaymentMethod.Balance && !request.UseTimeBank)
        {
            if (customer is null)
                throw new InvalidOperationException("Для оплаты с баланса укажите клиента.");
            if (requiresPaymentNow && customer.Balance + customer.BonusBalance < chargePrice)
                throw new InvalidOperationException(
                    $"Недостаточно средств (баланс {customer.Balance:0} ₸ + бонусы {customer.BonusBalance:0} ₸).");
        }
        else if (requiresPaymentNow)
        {
            if (request.PaymentMethod == PaymentMethod.Mixed)
                _ = PaymentParts.Resolve(request.PaymentMethod, chargePrice, request.Payments);
            await _cash.EnsureEmployeeHasOpenShiftAsync(employeeId, cancellationToken);
        }

        var ownerProxy = await _cash.GetOwnerCashProxyInfoAsync(employeeId, cancellationToken);

        var guestName = !string.IsNullOrWhiteSpace(request.GuestName)
            ? request.GuestName.Trim()
            : customer is not null
                ? $"{customer.FirstName} {customer.LastName}".Trim()
                : booking?.ContactName ?? "Гость";

        var now = DateTimeOffset.UtcNow;
        var endsAt = windowEnd ?? now.AddMinutes(durationMinutes);

        var session = new GamingSession
        {
            BranchId = computer.BranchId,
            ComputerId = computer.Id,
            ZoneId = computer.ZoneId.Value,
            TariffId = tariff.Id,
            DurationMode = durationMode,
            WindowPeriodStartsAt = windowStart,
            WindowPeriodEndsAt = windowEnd,
            CustomerId = customer?.Id,
            GuestName = guestName,
            PaymentMethod = request.UseTimeBank ? PaymentMethod.Free : request.PaymentMethod,
            Status = SessionStatus.Active,
            StartedAt = now,
            PlannedEndsAt = endsAt,
            DurationMinutes = durationMinutes,
            BasePrice = listPrice,
            DiscountAmount = totalDiscount,
            PrepaidAppliedAmount = prepaidDiscount,
            TotalPrice = chargePrice,
            PaidAmount = request.UseTimeBank ? 0 : chargePrice,
            DebtAmount = 0,
            StartedByEmployeeId = employeeId,
            IdempotencyKey = request.IdempotencyKey
        };

        session.History.Add(new SessionHistoryEntry
        {
            Action = SessionHistoryAction.Started,
            EmployeeId = employeeId,
            PriceAfter = session.TotalPrice,
            PlannedEndsAtAfter = endsAt,
            DetailsJson = AppendOwnerProxyJson(
                $"{{\"durationMinutes\":{durationMinutes},\"durationMode\":\"{durationMode}\",\"payment\":\"{session.PaymentMethod}\",\"timeBank\":{spendTimeBank},\"windowEnd\":{(windowEnd.HasValue ? $"\"{windowEnd:O}\"" : "null")}}}",
                ownerProxy)
        });

        _db.GamingSessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);

        if (caseDiscountRewardId is Guid caseRid)
            await _cases.ConsumeDiscountRewardAsync(caseRid, session.Id, cancellationToken);

        if (customer is not null)
        {
            try
            {
                await _cases.TryGrantNightPackageKeyAsync(customer.Id, tariff.Code, session.Id, cancellationToken);
            }
            catch
            {
                // key grant must not break session start
            }

            try
            {
                await RecordVisitStreakForSessionAsync(customer.Id, computer.BranchId, cancellationToken);
            }
            catch
            {
                // streak must not break session start
            }
        }

        if (spendTimeBank > 0 && customer is not null)
        {
            await _customers.ApplyTimeBankChangeAsync(
                customer.Id,
                computer.ZoneId!.Value,
                spendTimeBank,
                LedgerDirection.Debit,
                TimeBankReason.SessionSpent,
                employeeId,
                session.Id,
                $"Списание на сеанс: {FormatDuration(durationMinutes)} · зона ПК",
                $"timebank-spend-{session.Id}",
                cancellationToken);
        }

        computer.CurrentSessionId = session.Id;
        computer.Status = ComputerStatus.InSession;
        computer.UpdatedAt = now;
        computer.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);

        if (requiresPaymentNow)
        {
            if (request.PaymentMethod == PaymentMethod.Balance)
            {
                await _customers.ChargeForSessionAsync(
                    customer!.Id,
                    chargePrice,
                    session.Id,
                    null,
                    employeeId,
                    request.IdempotencyKey is null ? null : $"session-balance-{request.IdempotencyKey}",
                    cancellationToken);
            }
            else
            {
                var paymentParts = PaymentParts.Resolve(
                    request.PaymentMethod,
                    chargePrice,
                    request.Payments);
                await _cash.CreateSaleAsync(new CreateSaleRequest(
                    computer.Id,
                    session.Id,
                    $"Сеанс {session.GuestName}",
                    request.IdempotencyKey is null ? null : $"session-start-{request.IdempotencyKey}",
                    [
                        new CreateSaleItemRequest(
                            ReceiptItemType.GamingTime,
                            FormatSessionSaleLabel(tariff.Name, durationMinutes, loyaltyDiscount, prepaidDiscount),
                            1,
                            chargePrice,
                            0,
                            session.Id)
                    ],
                    paymentParts,
                    customer?.Id), employeeId, cancellationToken);
            }
        }

        if (booking is not null && bookingLink is not null)
        {
            // Перезагрузка tracked-сущностей после SaveChanges
            var link = await _db.BookingComputers
                .FirstAsync(bc => bc.BookingId == booking.Id && bc.ComputerId == computer.Id, cancellationToken);
            link.GamingSessionId = session.Id;

            var trackedBooking = await _db.Bookings.FirstAsync(b => b.Id == booking.Id, cancellationToken);
            if (trackedBooking.Status is BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.Arrived)
                trackedBooking.Status = BookingStatus.Active;
            trackedBooking.UpdatedAt = now;
            trackedBooking.UpdatedBy = employeeId;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = computer.BranchId,
            EmployeeId = employeeId,
            Action = ownerProxy is null ? "session.start" : "session.start.owner_proxy",
            EntityType = nameof(GamingSession),
            EntityId = session.Id.ToString(),
            DetailsJson = AppendOwnerProxyJson(
                $"{{\"computerId\":\"{computer.Id}\",\"total\":{session.TotalPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"bookingId\":{(request.BookingId.HasValue ? $"\"{request.BookingId}\"" : "null")}}}",
                ownerProxy)
        });
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Unlock PC for session
        await _computers.SendCommandAsync(
            computer.Id,
            new SendComputerCommandRequest(ComputerCommandType.Unlock, null, $"session-start-{session.Id}"),
            employeeId,
            cancellationToken);

        var dto = await GetByIdAsync(session.Id, cancellationToken)
                  ?? throw new InvalidOperationException("Session mapping failed.");

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(HubMethods.SessionStarted, new SessionStartedEvent(dto), cancellationToken);
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            new ComputerStatusChangedEvent(computer.Id, computer.Status, computer.LastSeenAt, computer.DisplayName),
            cancellationToken);
        await _computerHub.Clients.Group(HubGroups.Computer(computer.Id))
            .SendAsync(HubMethods.SessionStarted, dto, cancellationToken);

        if (request.BookingId is Guid startedBookingId)
        {
            var bookingDto = await _db.Bookings.AsNoTracking()
                .Include(b => b.Zone)
                .Include(b => b.Customer)
                .Include(b => b.Computers).ThenInclude(c => c.Computer).ThenInclude(c => c!.Zone)
                .FirstOrDefaultAsync(b => b.Id == startedBookingId, cancellationToken);
            if (bookingDto is not null)
            {
                await _staffHub.Clients.Group(HubGroups.Staff())
                    .SendAsync(HubMethods.BookingChanged, MapBookingDto(bookingDto), cancellationToken);
            }
        }

        return dto;
    }

    public async Task<SessionDto> StartComplimentaryAsync(
        Guid computerId,
        int durationMinutes,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (durationMinutes <= 0)
            durationMinutes = ShiftClub.Shared.Contracts.Public.FreeHoursPromo.DurationMinutes;

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _db.GamingSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                await tx.CommitAsync(cancellationToken);
                return await GetByIdAsync(existing.Id, cancellationToken)
                       ?? throw new InvalidOperationException("Session mapping failed.");
            }
        }

        var computer = await _db.Computers
            .Include(c => c.Zone)
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("ПК не найден.");

        if (!computer.IsApproved)
            throw new InvalidOperationException("ПК ещё не подтверждён.");
        if (computer.ZoneId is null)
            throw new InvalidOperationException("У ПК не задана зона.");
        if (computer.IsMaintenance)
            throw new InvalidOperationException("ПК на техработах.");
        if (computer.CurrentSessionId is not null
            || computer.Status == ComputerStatus.InSession)
            throw new InvalidOperationException("На этом ПК уже идёт сеанс.");

        var live = await _db.GamingSessions.AsNoTracking()
            .AnyAsync(
                s => s.ComputerId == computer.Id
                     && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused),
                cancellationToken);
        if (live)
            throw new InvalidOperationException("На этом ПК уже идёт сеанс.");

        var tariff = await _db.Tariffs.AsNoTracking()
            .Where(t => t.IsActive
                        && t.BranchId == computer.BranchId
                        && (!t.ZoneId.HasValue || t.ZoneId == computer.ZoneId)
                        && t.DurationMode == TariffDurationMode.FixedDuration)
            .OrderBy(t => t.Kind == TariffKind.Hourly ? 0 : 1)
            .ThenBy(t => t.SortOrder)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Нет тарифа для зоны этого ПК.");

        var starterId = await _db.Employees.AsNoTracking()
            .Where(e => e.IsActive && (e.BranchId == null || e.BranchId == computer.BranchId))
            .OrderBy(e => e.CreatedAt)
            .Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (starterId == Guid.Empty)
            throw new InvalidOperationException("Нет сотрудника для служебного старта.");

        var now = DateTimeOffset.UtcNow;
        var endsAt = now.AddMinutes(durationMinutes);
        await EnsureNoBookingConflictForSessionAsync(computer.Id, now, endsAt, null, cancellationToken);

        var session = new GamingSession
        {
            BranchId = computer.BranchId,
            ComputerId = computer.Id,
            ZoneId = computer.ZoneId.Value,
            TariffId = tariff.Id,
            DurationMode = TariffDurationMode.FixedDuration,
            GuestName = ShiftClub.Shared.Contracts.Public.FreeHoursPromo.GuestName,
            PaymentMethod = PaymentMethod.Free,
            Status = SessionStatus.Active,
            StartedAt = now,
            PlannedEndsAt = endsAt,
            DurationMinutes = durationMinutes,
            BasePrice = 0,
            DiscountAmount = 0,
            TotalPrice = 0,
            PaidAmount = 0,
            DebtAmount = 0,
            StartedByEmployeeId = starterId,
            IdempotencyKey = idempotencyKey,
            IsComplimentary = true
        };

        session.History.Add(new SessionHistoryEntry
        {
            Action = SessionHistoryAction.Started,
            EmployeeId = starterId,
            PriceAfter = 0,
            PlannedEndsAtAfter = endsAt,
            DetailsJson = $"{{\"complimentary\":true,\"durationMinutes\":{durationMinutes},\"source\":\"public-free\"}}"
        });

        _db.GamingSessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);

        computer.CurrentSessionId = session.Id;
        computer.Status = ComputerStatus.InSession;
        computer.UpdatedAt = now;
        computer.UpdatedBy = starterId;

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = computer.BranchId,
            EmployeeId = starterId,
            Action = "session.complimentary",
            EntityType = nameof(GamingSession),
            EntityId = session.Id.ToString(),
            DetailsJson = $"{{\"computerId\":\"{computer.Id}\",\"minutes\":{durationMinutes}}}"
        });
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        await _computers.SendCommandAsync(
            computer.Id,
            new SendComputerCommandRequest(ComputerCommandType.Unlock, null, $"free-start-{session.Id}"),
            starterId,
            cancellationToken);

        var dto = await GetByIdAsync(session.Id, cancellationToken)
                  ?? throw new InvalidOperationException("Session mapping failed.");

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(HubMethods.SessionStarted, new SessionStartedEvent(dto), cancellationToken);
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            new ComputerStatusChangedEvent(computer.Id, computer.Status, computer.LastSeenAt, computer.DisplayName),
            cancellationToken);
        await _computerHub.Clients.Group(HubGroups.Computer(computer.Id))
            .SendAsync(HubMethods.SessionStarted, dto, cancellationToken);

        return dto;
    }

    private static ShiftClub.Shared.Contracts.Bookings.BookingDto MapBookingDto(Booking b) => new(
        b.Id,
        b.Number,
        b.BranchId,
        b.ZoneId,
        b.Zone?.Name,
        b.CustomerId,
        b.Customer is null ? null : $"{b.Customer.FirstName} {b.Customer.LastName}".Trim(),
        b.ContactName,
        b.ContactPhone,
        b.Comment,
        b.Status,
        b.StartsAt,
        b.EndsAt,
        b.DurationMinutes,
        b.PrepaidAmount,
        b.TotalEstimated,
        b.PrepayMethod,
        b.GraceMinutes,
        b.CreatedAt,
        b.Computers.Select(c => new ShiftClub.Shared.Contracts.Bookings.BookingComputerDto(
            c.ComputerId,
            c.Computer?.DisplayName ?? c.Computer?.WindowsName,
            c.Computer?.ZoneId,
            c.Computer?.Zone?.Name,
            c.GamingSessionId)).ToList());

    private async Task EnsureNoBookingConflictForSessionAsync(
        Guid computerId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        Guid? allowBookingId,
        CancellationToken cancellationToken)
    {
        var conflict = await _db.BookingComputers.AsNoTracking()
            .Where(bc => bc.ComputerId == computerId
                         && (bc.Booking.Status == BookingStatus.Pending
                             || bc.Booking.Status == BookingStatus.Confirmed
                             || bc.Booking.Status == BookingStatus.Arrived
                             || bc.Booking.Status == BookingStatus.Active)
                         && (!allowBookingId.HasValue || bc.BookingId != allowBookingId.Value)
                         && bc.Booking.StartsAt < endsAt
                         && bc.Booking.EndsAt > startsAt)
            .Select(bc => new { bc.Booking.Number, bc.Booking.StartsAt, bc.Booking.EndsAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (conflict is not null)
        {
            var branchId = await _db.Computers.AsNoTracking()
                .Where(c => c.Id == computerId)
                .Select(c => c.BranchId)
                .FirstOrDefaultAsync(cancellationToken);
            var tzId = await _db.Branches.AsNoTracking()
                .Where(b => b.Id == branchId)
                .Select(b => b.TimeZoneId)
                .FirstOrDefaultAsync(cancellationToken);
            var tz = BranchTimeZone.Resolve(tzId);
            var from = TimeZoneInfo.ConvertTime(conflict.StartsAt, tz);
            var to = TimeZoneInfo.ConvertTime(conflict.EndsAt, tz);
            var softHoldStarts = conflict.StartsAt.AddMinutes(-30);
            if (DateTimeOffset.UtcNow >= softHoldStarts && DateTimeOffset.UtcNow < conflict.StartsAt)
            {
                throw new InvalidOperationException(
                    $"ПК зарезервирован под бронь {conflict.Number} с {from:HH:mm} (hold за 30 мин до начала). Запустите сеанс из брони или отмените её.");
            }

            throw new InvalidOperationException(
                $"Сеанс не успеет закончиться: на ПК бронь {conflict.Number} с {from:HH:mm} до {to:HH:mm}. Выберите меньшее время или другой ПК.");
        }
    }

    private async Task<decimal> SumPrepaidAppliedAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var sessionIds = await _db.BookingComputers.AsNoTracking()
            .Where(bc => bc.BookingId == bookingId && bc.GamingSessionId != null)
            .Select(bc => bc.GamingSessionId!.Value)
            .ToListAsync(cancellationToken);
        if (sessionIds.Count == 0)
            return 0;

        return await _db.GamingSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .SumAsync(s => s.PrepaidAppliedAmount, cancellationToken);
    }

    public async Task<StartGuestSessionsBatchResult> StartGuestSessionsBatchAsync(
        StartGuestSessionsBatchRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.ComputerIds is null || request.ComputerIds.Count == 0)
            throw new InvalidOperationException("Укажите хотя бы один ПК.");
        if (request.UseTimeBank && request.ComputerIds.Distinct().Count() > 1)
            throw new InvalidOperationException("Списание банка времени доступно только для одного ПК за раз.");
        if (request.CustomerId is not null && request.ComputerIds.Distinct().Count() > 1)
            throw new InvalidOperationException(
                "Аккаунт клиента — только на один ПК. Для компании стартуйте гостями или по одному.");

        var unique = request.ComputerIds.Distinct().ToList();
        var results = new List<SessionDto>();
        var errors = new List<string>();

        foreach (var computerId in unique)
        {
            try
            {
                var key = request.IdempotencyKey is null
                    ? null
                    : $"{request.IdempotencyKey}:{computerId}";
                var session = await StartGuestSessionAsync(
                    new StartGuestSessionRequest(
                        computerId,
                        request.TariffId,
                        request.DurationMinutes,
                        request.PaymentMethod,
                        request.GuestName,
                        request.CustomerId,
                        key,
                        request.UseTimeBank,
                        BookingId: null,
                        Payments: request.Payments),
                    employeeId,
                    cancellationToken);
                results.Add(session);
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
            {
                var name = await _db.Computers.AsNoTracking()
                    .Where(c => c.Id == computerId)
                    .Select(c => c.DisplayName ?? c.WindowsName)
                    .FirstOrDefaultAsync(cancellationToken) ?? computerId.ToString("N")[..8];
                errors.Add($"{name}: {ex.Message}");
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException(
                errors.Count > 0 ? string.Join("; ", errors) : "Не удалось запустить сеансы.");

        return new StartGuestSessionsBatchResult(results, errors);
    }

    public async Task<SessionDto> ExtendAsync(
        Guid sessionId,
        ExtendSessionRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.AdditionalMinutes <= 0 && request.TariffId is null)
            throw new InvalidOperationException("Укажите минуты продления (> 0) или тариф.");

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await ExtendCoreAsync(sessionId, request, employeeId, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                    entry.State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("Не удалось продлить сеанс из‑за параллельного обновления. Повторите.");
    }

    public async Task<ExtendSessionQuoteDto> QuoteExtendAsync(
        Guid sessionId,
        int additionalMinutes,
        CancellationToken cancellationToken = default,
        Guid? tariffId = null)
    {
        var session = await _db.GamingSessions.AsNoTracking()
            .Include(s => s.Tariff)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Session not found.");

        if (session.Status is not (SessionStatus.Active or SessionStatus.Paused))
            throw new InvalidOperationException("Продлить можно только активный или на паузе сеанс.");

        var quote = await BuildExtendQuoteAsync(session, additionalMinutes, tariffId, cancellationToken);
        var perHour = quote.Tariff.PricePerHour;
        var promo = await _settings.GetMarketingPromoStoredAsync(cancellationToken);
        if (quote.MarketingDiscount > 0 && MarketingPromoMath.IsActive(promo, DateTimeOffset.UtcNow))
            perHour = MarketingPromoMath.Apply(perHour, promo.Percent);
        var perMinute = perHour > 0 ? perHour / 60m : 0m;
        var afterPromo = Math.Max(0, quote.ListPrice - quote.MarketingDiscount);
        return new ExtendSessionQuoteDto(
            quote.Minutes,
            quote.ChargePrice,
            perHour,
            perMinute,
            quote.Tariff.BillingMode.ToString(),
            quote.Tariff.Name,
            quote.ListPrice,
            quote.DiscountAmount,
            quote.Tariff.Id,
            quote.DurationMode.ToString(),
            quote.MarketingDiscount,
            quote.LoyaltyDiscount,
            quote.CaseDiscount,
            quote.PrepaidApplied,
            afterPromo);
    }

    private async Task<SessionDto> ExtendCoreAsync(
        Guid sessionId,
        ExtendSessionRequest request,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        if (request.AdditionalMinutes <= 0 && request.TariffId is null)
            throw new InvalidOperationException("Укажите минуты продления (> 0) или тариф.");

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.SessionHistory
                .AsNoTracking()
                .Where(h => h.SessionId == sessionId && h.Action == SessionHistoryAction.Extended && h.DetailsJson != null && h.DetailsJson.Contains(request.IdempotencyKey))
                .Select(h => h.SessionId)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing != Guid.Empty)
            {
                var mapped = await GetByIdAsync(sessionId, cancellationToken);
                if (mapped is not null)
                    return mapped;
            }
        }

        var session = await _db.GamingSessions.AsNoTracking()
            .Include(s => s.Tariff)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Session not found.");

        if (session.Status is not (SessionStatus.Active or SessionStatus.Paused))
            throw new InvalidOperationException("Продлить можно только активный или на паузе сеанс.");

        var quote = await BuildExtendQuoteAsync(session, request.AdditionalMinutes, request.TariffId, cancellationToken);
        var expected = quote.ChargePrice;
        var paymentMethod = request.PaymentMethod;

        if (expected > 0 && paymentMethod is PaymentMethod.Free)
            throw new InvalidOperationException("Для платного продления выберите способ оплаты.");

        if (paymentMethod == PaymentMethod.Postpay)
            throw new InvalidOperationException("Постоплата отключена. Выберите способ оплаты.");

        if (paymentMethod == PaymentMethod.Balance)
        {
            var customerId = request.CustomerId ?? session.CustomerId;
            if (customerId is null)
                throw new InvalidOperationException("Для оплаты с баланса выберите клиента.");
        }

        if (expected > 0
            && paymentMethod is PaymentMethod.Cash or PaymentMethod.Card or PaymentMethod.KaspiQr or PaymentMethod.Transfer or PaymentMethod.Mixed)
        {
            if (paymentMethod == PaymentMethod.Mixed)
            {
                // Проверка частей — в Resolve; tendered = итог (сдача по mixed не считается).
                _ = PaymentParts.Resolve(paymentMethod, expected, request.Payments);
                if (request.AmountTendered is null)
                    throw new InvalidOperationException("Укажите, сколько клиент заплатил.");
                if (request.AmountTendered.Value + 0.009m < expected)
                    throw new InvalidOperationException(
                        $"Недостаточно: к оплате {expected:0.##} ₸, передано {request.AmountTendered:0.##} ₸.");
            }
            else
            {
                if (request.AmountTendered is null)
                    throw new InvalidOperationException("Укажите, сколько клиент заплатил.");
                if (request.AmountTendered.Value + 0.009m < expected)
                    throw new InvalidOperationException(
                        $"Недостаточно: к оплате {expected:0.##} ₸, передано {request.AmountTendered:0.##} ₸.");
            }
        }

        var beforeEnds = session.PlannedEndsAt;
        var beforePrice = session.TotalPrice;
        var beforeBase = session.BasePrice;
        var beforeDiscount = session.DiscountAmount;
        var beforePrepaid = session.PrepaidAppliedAmount;
        var now = DateTimeOffset.UtcNow;
        var baseEnds = session.Status == SessionStatus.Paused && session.PausedAt is DateTimeOffset pausedAt
            ? pausedAt.AddSeconds(session.RemainingSecondsAtPause ?? 0)
            : (session.PlannedEndsAt ?? now);
        if (baseEnds < now) baseEnds = now;
        var newEnds = quote.WindowEnd ?? baseEnds.AddMinutes(quote.Minutes);
        var newDuration = session.DurationMinutes + quote.Minutes;

        var linkedBookingId = await _db.BookingComputers.AsNoTracking()
            .Where(bc => bc.GamingSessionId == sessionId)
            .Select(bc => (Guid?)bc.BookingId)
            .FirstOrDefaultAsync(cancellationToken);
        await EnsureNoBookingConflictForSessionAsync(
            session.ComputerId,
            now,
            newEnds,
            linkedBookingId,
            cancellationToken);

        var newPrice = beforePrice + expected;
        var newBase = beforeBase + quote.ListPrice;
        var newDiscount = beforeDiscount + quote.DiscountAmount;
        var newPrepaid = beforePrepaid + quote.PrepaidApplied;
        var newPaid = session.PaidAmount + expected;
        var newRowVersion = Guid.NewGuid();
        var change = request.AmountTendered is { } tendered && tendered > expected
            ? tendered - expected
            : 0m;

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (expected > 0 && paymentMethod is not (PaymentMethod.Postpay or PaymentMethod.Free))
            {
                if (paymentMethod == PaymentMethod.Balance)
                {
                    var customerId = request.CustomerId ?? session.CustomerId
                        ?? throw new InvalidOperationException("Сеанс с оплатой балансом без клиента.");
                    await _customers.ChargeForSessionAsync(
                        customerId,
                        expected,
                        sessionId,
                        null,
                        employeeId,
                        request.IdempotencyKey is null ? null : $"session-extend-balance-{request.IdempotencyKey}",
                        cancellationToken);
                }
                else
                {
                    await _cash.EnsureEmployeeHasOpenShiftAsync(employeeId, cancellationToken);
                    var ownerProxy = await _cash.GetOwnerCashProxyInfoAsync(employeeId, cancellationToken);
                    var comment = change > 0
                        ? $"Продление на {FormatDuration(quote.Minutes)} · принято {request.AmountTendered:0.##} ₸ · сдача {change:0.##} ₸"
                        : $"Продление сеанса · {quote.Tariff.Name} · {FormatDuration(quote.Minutes)}";
                    if (ownerProxy is not null)
                        comment += $" · владелец {ownerProxy.OwnerName} · смена {ownerProxy.ShiftNumber} ({ownerProxy.CashierName})";
                    await _cash.CreateSaleAsync(new CreateSaleRequest(
                        session.ComputerId,
                        sessionId,
                        comment,
                        request.IdempotencyKey is null ? null : $"session-extend-{request.IdempotencyKey}",
                        [
                            new CreateSaleItemRequest(
                                ReceiptItemType.GamingTime,
                                FormatSessionSaleLabel(quote.Tariff.Name, quote.Minutes, quote.LoyaltyDiscount, quote.PrepaidApplied),
                                1,
                                expected,
                                0,
                                sessionId)
                        ],
                        PaymentParts.Resolve(paymentMethod, expected, request.Payments),
                        request.CustomerId ?? session.CustomerId), employeeId, cancellationToken);
                }
            }

            if (quote.CaseDiscountRewardId is Guid caseRid)
                await _cases.ConsumeDiscountRewardAsync(caseRid, sessionId, cancellationToken);

            var affected = await _db.GamingSessions
                .Where(s => s.Id == sessionId && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused))
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.DurationMinutes, newDuration)
                        .SetProperty(s => s.PlannedEndsAt, newEnds)
                        .SetProperty(s => s.BasePrice, newBase)
                        .SetProperty(s => s.DiscountAmount, newDiscount)
                        .SetProperty(s => s.PrepaidAppliedAmount, newPrepaid)
                        .SetProperty(s => s.TotalPrice, newPrice)
                        .SetProperty(s => s.PaidAmount, newPaid)
                        .SetProperty(s => s.DebtAmount, 0m)
                        .SetProperty(s => s.PaymentMethod, paymentMethod)
                        .SetProperty(s => s.TariffId, quote.Tariff.Id)
                        .SetProperty(s => s.DurationMode, quote.DurationMode)
                        .SetProperty(s => s.WindowPeriodStartsAt, quote.WindowStart ?? session.WindowPeriodStartsAt)
                        .SetProperty(s => s.WindowPeriodEndsAt, quote.WindowEnd ?? session.WindowPeriodEndsAt)
                        .SetProperty(s => s.Warning15Sent, false)
                        .SetProperty(s => s.Warning10Sent, false)
                        .SetProperty(s => s.Warning5Sent, false)
                        .SetProperty(s => s.Warning1Sent, false)
                        .SetProperty(s => s.UpdatedAt, now)
                        .SetProperty(s => s.UpdatedBy, employeeId)
                        .SetProperty(s => s.RowVersion, newRowVersion),
                    cancellationToken);

            if (affected == 0)
                throw new InvalidOperationException("Сеанс уже изменён или не активен. Обновите карту и повторите.");

            // Если был на паузе — обновляем остаток под новый конец
            if (session.Status == SessionStatus.Paused)
            {
                var remainSec = Math.Max(0, (int)(newEnds - (session.PausedAt ?? now)).TotalSeconds);
                await _db.GamingSessions
                    .Where(s => s.Id == sessionId && s.Status == SessionStatus.Paused)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.RemainingSecondsAtPause, remainSec), cancellationToken);
            }

            var ownerProxyNote = await _cash.GetOwnerCashProxyInfoAsync(employeeId, cancellationToken);
            _db.SessionHistory.Add(new SessionHistoryEntry
            {
                SessionId = sessionId,
                Action = SessionHistoryAction.Extended,
                EmployeeId = employeeId,
                PriceBefore = beforePrice,
                PriceAfter = newPrice,
                PlannedEndsAtBefore = beforeEnds,
                PlannedEndsAtAfter = newEnds,
                DetailsJson = AppendOwnerProxyJson(
                    $"{{\"additionalMinutes\":{quote.Minutes},\"list\":{quote.ListPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"expected\":{expected.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"discount\":{quote.DiscountAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"prepaidApplied\":{quote.PrepaidApplied.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"tariffId\":\"{quote.Tariff.Id}\",\"tendered\":{request.AmountTendered?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"},\"change\":{change.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"paymentMethod\":\"{paymentMethod}\",\"idempotencyKey\":\"{request.IdempotencyKey}\"}}",
                    ownerProxyNote)
            });
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        var dto = await GetByIdAsync(sessionId, cancellationToken)
                  ?? throw new InvalidOperationException("Session mapping failed after extend.");
        await PublishUpdatedAsync(dto, cancellationToken);
        return dto;
    }

    private sealed record ExtendQuoteResult(
        Tariff Tariff,
        int Minutes,
        decimal ListPrice,
        decimal ChargePrice,
        decimal DiscountAmount,
        decimal MarketingDiscount,
        decimal LoyaltyDiscount,
        decimal CaseDiscount,
        decimal PrepaidApplied,
        Guid? CaseDiscountRewardId,
        TariffDurationMode DurationMode,
        DateTimeOffset? WindowStart,
        DateTimeOffset? WindowEnd);

    /// <summary>
    /// Расчёт продления: либо доплата минут по текущему тарифу (+ пакеты 2+1/3+2),
    /// либо покупка другого тарифа зоны (час / пакет / день / ночь).
    /// </summary>
    private async Task<ExtendQuoteResult> BuildExtendQuoteAsync(
        GamingSession session,
        int additionalMinutes,
        Guid? offerTariffId,
        CancellationToken cancellationToken)
    {
        var tz = await _db.Branches.AsNoTracking()
            .Where(b => b.Id == session.BranchId)
            .Select(b => b.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);

        Tariff tariff;
        int minutes;
        decimal listPrice;
        TariffDurationMode durationMode;
        DateTimeOffset? windowStart = null;
        DateTimeOffset? windowEnd = null;
        var minuteOnlyExtension = false;

        if (offerTariffId is Guid tid)
        {
            tariff = await _db.Tariffs.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tid && t.BranchId == session.BranchId, cancellationToken)
                ?? throw new KeyNotFoundException("Тариф не найден.");

            if (!tariff.IsActive)
                throw new InvalidOperationException("Тариф неактивен.");
            if (!TariffAvailability.IsAvailableNow(tariff, DateTimeOffset.UtcNow, tz))
                throw new InvalidOperationException("Тариф сейчас недоступен (день/время).");

            var zoneId = session.ZoneId;
            if (tariff.ZoneId is Guid zid && zid != zoneId)
                throw new InvalidOperationException("Тариф другой зоны — выберите тариф этой зоны.");

            durationMode = tariff.DurationMode;
            if (tariff.DurationMode == TariffDurationMode.TimeWindow)
            {
                if (!TariffAvailability.TryResolveTimeWindow(
                        tariff, DateTimeOffset.UtcNow, tz,
                        out var periodStart, out var periodEnd, out minutes))
                {
                    throw new InvalidOperationException(
                        "Тариф с временным интервалом сейчас недоступен или до окончания осталось меньше минуты.");
                }

                windowStart = periodStart;
                windowEnd = periodEnd;
                listPrice = SessionPricing.Calculate(tariff, minutes);
            }
            else
            {
                var requested = additionalMinutes > 0
                    ? additionalMinutes
                    : tariff.FixedDurationMinutes is > 0
                        ? tariff.FixedDurationMinutes.Value
                        : tariff.MinDurationMinutes is > 0 ? tariff.MinDurationMinutes.Value : 60;
                minutes = TariffAvailability.ResolveDurationMinutes(tariff, requested);
                if (minutes <= 0)
                    throw new InvalidOperationException("Некорректная длительность продления.");
                listPrice = SessionPricing.Calculate(tariff, minutes);
            }
        }
        else
        {
            tariff = session.Tariff
                ?? throw new InvalidOperationException("У сеанса нет тарифа.");

            if (session.DurationMode == TariffDurationMode.TimeWindow
                || tariff.DurationMode == TariffDurationMode.TimeWindow)
            {
                // День/ночь: доп. минуты по почасовой ставке этого же тарифа (PricePerHour).
                if (additionalMinutes <= 0)
                    throw new InvalidOperationException("Укажите минуты продления (> 0).");
                if (tariff.PricePerHour <= 0)
                    throw new InvalidOperationException(
                        "У дневного/ночного тарифа нет ставки ₸/ч для минутных продлений. Выберите почасовой тариф.");

                minutes = additionalMinutes;
                durationMode = TariffDurationMode.FixedDuration;
                listPrice = SessionPricing.PriceForAdditionalMinutes(tariff, additionalMinutes);
                minuteOnlyExtension = true;
            }
            else
            {
                if (additionalMinutes <= 0)
                    throw new InvalidOperationException("Укажите минуты продления (> 0).");

                minutes = additionalMinutes;
                durationMode = tariff.DurationMode;

                if (tariff.Kind == TariffKind.Package
                    && tariff.FixedPrice is > 0
                    && tariff.FixedDurationMinutes == additionalMinutes)
                {
                    listPrice = tariff.FixedPrice.Value;
                    minuteOnlyExtension = false;
                }
                else
                {
                    var zoneId = tariff.ZoneId ?? (session.ZoneId == Guid.Empty ? null : session.ZoneId);
                    var promoPack = await _db.Tariffs.AsNoTracking()
                        .Where(t =>
                            t.BranchId == session.BranchId
                            && t.IsActive
                            && t.Kind == TariffKind.Package
                            && t.FixedPrice != null
                            && t.FixedDurationMinutes == additionalMinutes
                            && (zoneId == null || t.ZoneId == null || t.ZoneId == zoneId)
                            && (t.Code.Contains("2P1") || t.Code.Contains("3P2")
                                || t.Name == "2+1" || t.Name == "3+2"))
                        .OrderBy(t => t.FixedPrice)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (promoPack?.FixedPrice is > 0)
                    {
                        listPrice = promoPack.FixedPrice.Value;
                        tariff = promoPack;
                        minuteOnlyExtension = false;
                    }
                    else
                    {
                        listPrice = SessionPricing.PriceForAdditionalMinutes(tariff, additionalMinutes);
                        minuteOnlyExtension = true;
                    }
                }
            }
        }

        var promo = await _settings.GetMarketingPromoStoredAsync(cancellationToken);
        var marketingDiscount = minuteOnlyExtension
            ? 0m
            : MarketingPromoTariff.DiscountAmount(tariff, listPrice, promo, DateTimeOffset.UtcNow);

        var afterPromo = Math.Max(0, listPrice - marketingDiscount);

        decimal loyaltyPct = 0;
        if (session.CustomerId is Guid cid)
        {
            loyaltyPct = await _db.Customers.AsNoTracking()
                .Where(c => c.Id == cid)
                .Select(c => c.LoyaltyLevel != null ? c.LoyaltyLevel.TimeDiscountPercent : 0m)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var loyaltyDiscount = LoyaltyTimeDiscount(afterPromo, loyaltyPct);
        var afterLoyalty = Math.Max(0, afterPromo - loyaltyDiscount);

        decimal caseDiscount = 0;
        Guid? caseRewardId = null;
        if (session.CustomerId is Guid customerId && afterLoyalty > 0)
        {
            var (casePct, rewardId) = await _cases.PeekPendingDiscountAsync(customerId, cancellationToken);
            if (casePct > 0 && rewardId is Guid rid)
            {
                caseDiscount = Math.Round(afterLoyalty * casePct / 100m, 2, MidpointRounding.AwayFromZero);
                caseRewardId = rid;
            }
        }

        var afterCase = Math.Max(0, afterLoyalty - caseDiscount);

        decimal prepaidApplied = 0;
        var bookingId = await _db.BookingComputers.AsNoTracking()
            .Where(bc => bc.GamingSessionId == session.Id)
            .Select(bc => (Guid?)bc.BookingId)
            .FirstOrDefaultAsync(cancellationToken);
        if (bookingId is Guid bid && afterCase > 0)
        {
            var prepaid = await _db.Bookings.AsNoTracking()
                .Where(b => b.Id == bid)
                .Select(b => b.PrepaidAmount)
                .FirstOrDefaultAsync(cancellationToken);
            if (prepaid > 0)
            {
                var used = await SumPrepaidAppliedAsync(bid, cancellationToken);
                var remaining = Math.Max(0, prepaid - used);
                prepaidApplied = Math.Min(remaining, afterCase);
            }
        }

        var discount = marketingDiscount + loyaltyDiscount + caseDiscount + prepaidApplied;
        var charge = Math.Max(0, listPrice - discount);

        return new ExtendQuoteResult(
            tariff,
            minutes,
            listPrice,
            charge,
            discount,
            marketingDiscount,
            loyaltyDiscount,
            caseDiscount,
            prepaidApplied,
            caseRewardId,
            durationMode,
            windowStart,
            windowEnd);
    }

    private static decimal LoyaltyTimeDiscount(decimal price, decimal? timeDiscountPercent)
    {
        if (price <= 0 || timeDiscountPercent is not > 0)
            return 0;
        return Math.Round(price * timeDiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
    }

    public async Task<SessionDto> EndAsync(
        Guid sessionId,
        EndSessionRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await EndCoreAsync(sessionId, request, employeeId, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                    entry.State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("Не удалось завершить сеанс из‑за параллельного обновления. Повторите.");
    }

    public async Task<SessionDto> TransferAsync(
        Guid sessionId,
        TransferSessionRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await TransferCoreAsync(sessionId, request, employeeId, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                DetachAllTracked();
            }
        }

        throw new InvalidOperationException(
            "Не удалось перенести сеанс из‑за параллельного обновления. Обновите карту и повторите.");
    }

    private async Task<SessionDto> TransferCoreAsync(
        Guid sessionId,
        TransferSessionRequest request,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        if (request.TargetComputerId == Guid.Empty)
            throw new InvalidOperationException("Укажите целевой ПК.");

        DetachAllTracked();

        var session = await _db.GamingSessions.AsNoTracking()
            .Include(s => s.Tariff)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Сеанс не найден.");

        if (session.Status is not (SessionStatus.Active or SessionStatus.Paused))
            throw new InvalidOperationException("Перенести можно только активный или паузированный сеанс.");

        if (session.ComputerId == request.TargetComputerId)
            throw new InvalidOperationException("Сеанс уже на этом ПК.");

        var source = await _db.Computers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == session.ComputerId, cancellationToken)
            ?? throw new KeyNotFoundException("Исходный ПК не найден.");

        var target = await _db.Computers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.TargetComputerId, cancellationToken)
            ?? throw new KeyNotFoundException("Целевой ПК не найден.");

        if (!target.IsApproved)
            throw new InvalidOperationException("Целевой ПК не подтверждён.");
        if (target.ZoneId is null)
            throw new InvalidOperationException("У целевого ПК нет зоны.");
        if (source.ZoneId is null)
            throw new InvalidOperationException("У исходного ПК нет зоны.");
        if (target.BranchId != session.BranchId)
            throw new InvalidOperationException("ПК другого филиала.");

        // Strict same-zone: Standard → VIP and vice versa are not allowed.
        if (source.ZoneId != target.ZoneId || session.ZoneId != target.ZoneId.Value)
            throw new InvalidOperationException(
                "Перенос только в ту же зону (например Standard→Standard). На другую зону переносить нельзя.");

        if (session.Tariff.ZoneId.HasValue && session.Tariff.ZoneId != target.ZoneId)
            throw new InvalidOperationException("Тариф сеанса привязан к другой зоне — выберите ПК этой зоны.");

        if (target.CurrentSessionId is not null || target.Status == ComputerStatus.InSession)
            throw new InvalidOperationException("Целевой ПК занят.");
        if (target.IsMaintenance || target.Status == ComputerStatus.Maintenance)
            throw new InvalidOperationException("Целевой ПК на техобслуживании.");
        if (target.Status == ComputerStatus.Updating)
            throw new InvalidOperationException("Целевой ПК обновляется.");
        if (target.Status == ComputerStatus.Error)
            throw new InvalidOperationException("Целевой ПК в ошибке.");
        if (target.Status == ComputerStatus.Reserved)
            throw new InvalidOperationException("Целевой ПК зарезервирован под бронь.");

        var fromComputerId = session.ComputerId;
        var fromZoneId = session.ZoneId;
        var toComputerId = target.Id;
        var toZoneId = target.ZoneId.Value;
        var now = DateTimeOffset.UtcNow;
        var sessionEnd = session.PlannedEndsAt ?? now.AddHours(1);

        var linkedBookingId = await _db.BookingComputers.AsNoTracking()
            .Where(bc => bc.GamingSessionId == sessionId)
            .Select(bc => (Guid?)bc.BookingId)
            .FirstOrDefaultAsync(cancellationToken);

        await EnsureNoBookingConflictForSessionAsync(
            toComputerId,
            now,
            sessionEnd,
            linkedBookingId,
            cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // IX_computers_CurrentSessionId is UNIQUE — never set target to sessionId
            // while source still holds the same CurrentSessionId.

            // 1) Free source first (must still own this session).
            var freed = await _db.Computers
                .Where(c => c.Id == fromComputerId && c.CurrentSessionId == sessionId)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.CurrentSessionId, (Guid?)null)
                        .SetProperty(c => c.Status, ComputerStatus.Free)
                        .SetProperty(c => c.UpdatedAt, now)
                        .SetProperty(c => c.UpdatedBy, employeeId)
                        .SetProperty(c => c.RowVersion, Guid.NewGuid()),
                    cancellationToken);
            if (freed == 0)
                throw new InvalidOperationException(
                    "Сеанс уже изменён или перенесён. Обновите карту и повторите.");

            // 2) Claim target only if still free.
            var claimed = await _db.Computers
                .Where(c => c.Id == toComputerId
                            && c.CurrentSessionId == null
                            && !c.IsMaintenance
                            && c.Status != ComputerStatus.InSession
                            && c.Status != ComputerStatus.Maintenance
                            && c.Status != ComputerStatus.Updating
                            && c.Status != ComputerStatus.Error
                            && c.Status != ComputerStatus.Reserved
                            && c.ZoneId == toZoneId)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.CurrentSessionId, sessionId)
                        .SetProperty(c => c.Status, ComputerStatus.InSession)
                        .SetProperty(c => c.UpdatedAt, now)
                        .SetProperty(c => c.UpdatedBy, employeeId)
                        .SetProperty(c => c.RowVersion, Guid.NewGuid()),
                    cancellationToken);
            if (claimed == 0)
                throw new InvalidOperationException("Целевой ПК уже занят или недоступен. Обновите карту.");

            // 3) Move session only if it is still on the source PC.
            var moved = await _db.GamingSessions
                .Where(s => s.Id == sessionId
                            && s.ComputerId == fromComputerId
                            && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused))
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.ComputerId, toComputerId)
                        .SetProperty(s => s.ZoneId, toZoneId)
                        .SetProperty(s => s.UpdatedAt, now)
                        .SetProperty(s => s.UpdatedBy, employeeId)
                        .SetProperty(s => s.RowVersion, Guid.NewGuid()),
                    cancellationToken);
            if (moved == 0)
                throw new InvalidOperationException(
                    "Сеанс уже изменён или перенесён. Обновите карту и повторите.");

            if (linkedBookingId.HasValue)
            {
                var bookingPc = await _db.BookingComputers
                    .FirstOrDefaultAsync(bc => bc.GamingSessionId == sessionId, cancellationToken);
                if (bookingPc is not null)
                {
                    var dup = await _db.BookingComputers.AnyAsync(
                        bc => bc.BookingId == bookingPc.BookingId
                              && bc.ComputerId == toComputerId
                              && bc.Id != bookingPc.Id,
                        cancellationToken);
                    if (dup)
                        throw new InvalidOperationException("Целевой ПК уже в этой брони на другом слоте.");

                    bookingPc.ComputerId = toComputerId;
                }
            }

            _db.SessionHistory.Add(new SessionHistoryEntry
            {
                SessionId = sessionId,
                Action = SessionHistoryAction.Moved,
                EmployeeId = employeeId,
                PlannedEndsAtBefore = session.PlannedEndsAt,
                PlannedEndsAtAfter = session.PlannedEndsAt,
                PriceBefore = session.TotalPrice,
                PriceAfter = session.TotalPrice,
                DetailsJson =
                    $"{{\"fromComputerId\":\"{fromComputerId}\",\"toComputerId\":\"{toComputerId}\",\"fromZoneId\":\"{fromZoneId}\",\"toZoneId\":\"{toZoneId}\",\"idempotency\":\"{request.IdempotencyKey}\"}}"
            });

            _db.AuditLogs.Add(new AuditLog
            {
                BranchId = session.BranchId,
                EmployeeId = employeeId,
                Action = "session.transfer",
                EntityType = nameof(GamingSession),
                EntityId = sessionId.ToString(),
                DetailsJson = $"{{\"from\":\"{fromComputerId}\",\"to\":\"{toComputerId}\"}}"
            });

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            DetachAllTracked();
            throw;
        }

        var dto = MapSession(await ReloadSessionGraphAsync(sessionId, cancellationToken));
        var sourceNow = await _db.Computers.AsNoTracking().FirstAsync(c => c.Id == fromComputerId, cancellationToken);
        var targetNow = await _db.Computers.AsNoTracking().FirstAsync(c => c.Id == toComputerId, cancellationToken);

        if (ShouldWakeForSessionTransfer(target))
        {
            try
            {
                await _computers.WakeAsync(toComputerId, employeeId, cancellationToken);
                await Task.Delay(1200, cancellationToken);
            }
            catch
            {
                // WOL не должен отменять перенос — касса может включить ПК вручную.
            }
        }

        // Source shell: завершить сеанс и вернуть ПК в «свободен» (не Lock — иначе на карте «заблокирован»).
        await _computerHub.Clients.Group(HubGroups.Computer(fromComputerId))
            .SendAsync(HubMethods.SessionEnded, dto, cancellationToken);
        await _computers.SendCommandAsync(
            fromComputerId,
            new SendComputerCommandRequest(ComputerCommandType.Unlock, null, $"session-move-from-{sessionId}"),
            employeeId,
            cancellationToken);

        // Target shell: unlock + start
        await _computers.SendCommandAsync(
            toComputerId,
            new SendComputerCommandRequest(ComputerCommandType.Unlock, null, $"session-move-to-{sessionId}"),
            employeeId,
            cancellationToken);
        await _computerHub.Clients.Group(HubGroups.Computer(toComputerId))
            .SendAsync(HubMethods.SessionStarted, dto, cancellationToken);

        await _staffHub.Clients.Group(HubGroups.Staff())
            .SendAsync(HubMethods.SessionUpdated, new SessionUpdatedEvent(dto), cancellationToken);
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            new ComputerStatusChangedEvent(fromComputerId, sourceNow.Status, sourceNow.LastSeenAt, sourceNow.DisplayName),
            cancellationToken);
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            new ComputerStatusChangedEvent(toComputerId, targetNow.Status, targetNow.LastSeenAt, targetNow.DisplayName),
            cancellationToken);

        return dto;
    }

    public async Task<SessionDto> PauseAsync(Guid sessionId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var session = await _db.GamingSessions
            .Include(s => s.Tariff)
            .Include(s => s.Computer)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Session not found.");

        if (session.Status != SessionStatus.Active)
            throw new InvalidOperationException("Пауза доступна только для активного сеанса.");
        if (session.Tariff is { AllowPause: false })
            throw new InvalidOperationException("Тариф не разрешает паузу.");
        if (session.PlannedEndsAt is null)
            throw new InvalidOperationException("У сеанса нет планового окончания.");

        var now = DateTimeOffset.UtcNow;
        var remaining = Math.Max(0, (int)(session.PlannedEndsAt.Value - now).TotalSeconds);
        if (remaining < 30)
            throw new InvalidOperationException("Слишком мало оставшегося времени для паузы.");

        session.Status = SessionStatus.Paused;
        session.PausedAt = now;
        session.RemainingSecondsAtPause = remaining;
        session.UpdatedAt = now;
        session.UpdatedBy = employeeId;
        session.RowVersion = Guid.NewGuid();
        session.History.Add(new SessionHistoryEntry
        {
            Action = SessionHistoryAction.Paused,
            EmployeeId = employeeId,
            PlannedEndsAtBefore = session.PlannedEndsAt,
            DetailsJson = $"{{\"remainingSeconds\":{remaining}}}"
        });

        if (session.Computer is not null)
        {
            session.Computer.Status = ComputerStatus.Locked;
            session.Computer.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _computers.SendCommandAsync(
            session.ComputerId,
            new SendComputerCommandRequest(ComputerCommandType.Lock, null, $"session-pause-{sessionId}"),
            employeeId,
            cancellationToken);

        var dto = MapSession(await ReloadSessionGraphAsync(sessionId, cancellationToken));
        await PublishUpdatedAsync(dto, cancellationToken);
        return dto;
    }

    public async Task<SessionDto> ResumeAsync(Guid sessionId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var session = await _db.GamingSessions
            .Include(s => s.Computer)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Session not found.");

        if (session.Status != SessionStatus.Paused)
            throw new InvalidOperationException("Возобновить можно только сеанс на паузе.");
        if (session.RemainingSecondsAtPause is null or <= 0)
            throw new InvalidOperationException("Нет сохранённого остатка времени.");

        var now = DateTimeOffset.UtcNow;
        var remaining = session.RemainingSecondsAtPause.Value;
        var pausedFor = session.PausedAt.HasValue
            ? Math.Max(0, (int)(now - session.PausedAt.Value).TotalSeconds)
            : 0;

        session.Status = SessionStatus.Active;
        session.PlannedEndsAt = now.AddSeconds(remaining);
        session.TotalPausedSeconds += pausedFor;
        session.PausedAt = null;
        session.RemainingSecondsAtPause = null;
        session.Warning15Sent = false;
        session.Warning10Sent = false;
        session.Warning5Sent = false;
        session.Warning1Sent = false;
        session.UpdatedAt = now;
        session.UpdatedBy = employeeId;
        session.RowVersion = Guid.NewGuid();
        session.History.Add(new SessionHistoryEntry
        {
            Action = SessionHistoryAction.Resumed,
            EmployeeId = employeeId,
            PlannedEndsAtAfter = session.PlannedEndsAt,
            DetailsJson = $"{{\"remainingSeconds\":{remaining},\"pausedSeconds\":{pausedFor}}}"
        });

        if (session.Computer is not null)
        {
            session.Computer.Status = ComputerStatus.InSession;
            session.Computer.CurrentSessionId = session.Id;
            session.Computer.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _computers.SendCommandAsync(
            session.ComputerId,
            new SendComputerCommandRequest(ComputerCommandType.Unlock, null, $"session-resume-{sessionId}"),
            employeeId,
            cancellationToken);

        var dto = MapSession(await ReloadSessionGraphAsync(sessionId, cancellationToken));
        await PublishUpdatedAsync(dto, cancellationToken);
        return dto;
    }

    private async Task<SessionDto> EndCoreAsync(
        Guid sessionId,
        EndSessionRequest request,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var session = await _db.GamingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Session not found.");

        if (session.Status is SessionStatus.Completed or SessionStatus.Cancelled)
        {
            // Сеанс уже закрыт, но ПК мог остаться с CurrentSessionId / InSession (ручной SQL, сбой и т.п.).
            await _db.Computers
                .Where(c => c.Id == session.ComputerId
                            && (c.CurrentSessionId == session.Id || c.Status == ComputerStatus.InSession))
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.CurrentSessionId, (Guid?)null)
                        .SetProperty(c => c.Status, ComputerStatus.Free)
                        .SetProperty(c => c.UpdatedAt, DateTimeOffset.UtcNow),
                    cancellationToken);
            return MapSession(await ReloadSessionGraphAsync(session.Id, cancellationToken));
        }

        if (session.Status is not (SessionStatus.Active or SessionStatus.PaymentPending or SessionStatus.Paused))
            throw new InvalidOperationException($"Cannot end session in status {session.Status}.");

        if (session.DebtAmount > 0 && !request.ForceUnpaid)
        {
            await _db.GamingSessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.Status, SessionStatus.PaymentPending)
                        .SetProperty(s => s.UpdatedAt, DateTimeOffset.UtcNow),
                    cancellationToken);
            throw new InvalidOperationException("Session has unpaid debt. Confirm with ForceUnpaid.");
        }

        var now = DateTimeOffset.UtcNow;
        var computerId = session.ComputerId;
        var endedBy = employeeId == Guid.Empty ? (Guid?)null : employeeId;

        var remainingSeconds = session.Status == SessionStatus.Paused
            ? session.RemainingSecondsAtPause ?? 0
            : session.PlannedEndsAt.HasValue
                ? Math.Max(0, (int)(session.PlannedEndsAt.Value - now).TotalSeconds)
                : 0;

        // День/ночь (окно) — остаток до конца периода в банк не переносим.
        var saveToTimeBank = request.SaveRemainingToTimeBank
                             && session.DurationMode != TariffDurationMode.TimeWindow;

        if (saveToTimeBank)
        {
            if (session.CustomerId is null)
                throw new InvalidOperationException(
                    "Нельзя сохранить минуты: сеанс не привязан к аккаунту. Сначала привяжите клиента.");
            if (remainingSeconds < 60)
                throw new InvalidOperationException("Осталось меньше минуты — сохранять нечего.");
        }

        var affected = await _db.GamingSessions
            .Where(s => s.Id == sessionId
                        && (s.Status == SessionStatus.Active
                            || s.Status == SessionStatus.PaymentPending
                            || s.Status == SessionStatus.Paused))
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Status, SessionStatus.Completed)
                    .SetProperty(s => s.ActualEndedAt, now)
                    .SetProperty(s => s.EndedByEmployeeId, endedBy)
                    .SetProperty(s => s.CancelReason, request.Reason)
                    .SetProperty(s => s.PausedAt, (DateTimeOffset?)null)
                    .SetProperty(s => s.RemainingSecondsAtPause, (int?)null)
                    .SetProperty(s => s.UpdatedAt, now)
                    .SetProperty(s => s.UpdatedBy, endedBy)
                    .SetProperty(s => s.RowVersion, Guid.NewGuid()),
                cancellationToken);

        if (affected == 0)
            return MapSession(await ReloadSessionGraphAsync(sessionId, cancellationToken));

        var savedBankMinutes = 0;
        if (saveToTimeBank && session.CustomerId is Guid saveCustomerId)
        {
            savedBankMinutes = remainingSeconds / 60;
            await _customers.ApplyTimeBankChangeAsync(
                saveCustomerId,
                session.ZoneId,
                savedBankMinutes,
                LedgerDirection.Credit,
                TimeBankReason.SessionSaved,
                endedBy,
                sessionId,
                $"Сохранено при завершении сеанса · зона сеанса ({remainingSeconds} сек)",
                $"timebank-save-{sessionId}",
                cancellationToken);
        }

        _db.SessionHistory.Add(new SessionHistoryEntry
        {
            SessionId = sessionId,
            Action = employeeId == Guid.Empty ? SessionHistoryAction.AutoEnded : SessionHistoryAction.Ended,
            EmployeeId = endedBy,
            PriceBefore = session.TotalPrice,
            PriceAfter = session.TotalPrice,
            PlannedEndsAtBefore = session.PlannedEndsAt,
            PlannedEndsAtAfter = session.PlannedEndsAt,
            DetailsJson = $"{{\"reason\":\"{request.Reason}\",\"savedSeconds\":{(saveToTimeBank ? remainingSeconds : 0)},\"savedMinutes\":{savedBankMinutes}}}"
        });

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = session.BranchId,
            EmployeeId = endedBy,
            Action = "session.end",
            EntityType = nameof(GamingSession),
            EntityId = sessionId.ToString()
        });

        await _db.SaveChangesAsync(cancellationToken);

        await _db.Computers
            .Where(c => c.Id == computerId)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.CurrentSessionId, (Guid?)null)
                    .SetProperty(c => c.Status, ComputerStatus.Free)
                    .SetProperty(c => c.UpdatedAt, now),
                cancellationToken);

        if (session.CustomerId is Guid playCustomerId && !session.IsComplimentary)
        {
            var playedMinutes = Math.Max(0, (int)(now - session.StartedAt).TotalMinutes);
            try
            {
                await _cases.TryGrantPlayHoursKeyAsync(playCustomerId, playedMinutes, sessionId, cancellationToken);
            }
            catch
            {
                // ignore key grant failures
            }
        }

        // Unlock → login screen (do not lock the PC after session end).
        if (employeeId != Guid.Empty)
        {
            await _computers.SendCommandAsync(
                computerId,
                new SendComputerCommandRequest(ComputerCommandType.Unlock, null, $"session-end-{sessionId}"),
                employeeId,
                cancellationToken);
        }
        else
        {
            await _computers.SendCommandAsync(
                computerId,
                new SendComputerCommandRequest(ComputerCommandType.Unlock, null, $"session-auto-end-{sessionId}"),
                Guid.Empty,
                cancellationToken);
        }

        var dto = MapSession(await ReloadSessionGraphAsync(sessionId, cancellationToken));
        var computer = await _db.Computers.AsNoTracking().FirstAsync(c => c.Id == computerId, cancellationToken);

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(HubMethods.SessionEnded, new SessionEndedEvent(dto), cancellationToken);
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            new ComputerStatusChangedEvent(computer.Id, computer.Status, computer.LastSeenAt, computer.DisplayName),
            cancellationToken);
        await _computerHub.Clients.Group(HubGroups.Computer(computerId))
            .SendAsync(HubMethods.SessionEnded, dto, cancellationToken);

        await TryCompleteBookingAfterSessionEndAsync(sessionId, cancellationToken);
        return dto;
    }

    private async Task TryCompleteBookingAfterSessionEndAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var link = await _db.BookingComputers
            .Include(bc => bc.Booking)
            .ThenInclude(b => b.Computers)
            .FirstOrDefaultAsync(bc => bc.GamingSessionId == sessionId, cancellationToken);
        if (link?.Booking is null) return;
        if (link.Booking.Status != BookingStatus.Active) return;

        var sessionIds = link.Booking.Computers
            .Where(c => c.GamingSessionId.HasValue)
            .Select(c => c.GamingSessionId!.Value)
            .ToList();

        // Ждём, пока по всем ПК брони будут сеансы и все они закрыты —
        // если часть ПК так и не стартанула, бронь остаётся Active (персонал отменит вручную не выйдет — нужен Cancel только до Active).
        // Для незапущенных ПК: если бронь уже Active и EndsAt прошла — завершаем.
        var allLinksHaveSession = link.Booking.Computers.All(c => c.GamingSessionId.HasValue);
        var now = DateTimeOffset.UtcNow;

        if (!allLinksHaveSession && link.Booking.EndsAt.AddMinutes(link.Booking.GraceMinutes) > now)
            return;

        if (sessionIds.Count > 0)
        {
            var anyAlive = await _db.GamingSessions.AsNoTracking()
                .AnyAsync(s => sessionIds.Contains(s.Id)
                               && (s.Status == SessionStatus.Active
                                   || s.Status == SessionStatus.Paused
                                   || s.Status == SessionStatus.PaymentPending),
                    cancellationToken);
            if (anyAlive) return;
        }

        link.Booking.Status = BookingStatus.Completed;
        link.Booking.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        var dto = MapBookingDto(await _db.Bookings.AsNoTracking()
            .Include(b => b.Zone)
            .Include(b => b.Customer)
            .Include(b => b.Computers).ThenInclude(c => c.Computer).ThenInclude(c => c!.Zone)
            .FirstAsync(b => b.Id == link.BookingId, cancellationToken));

        await _staffHub.Clients.Group(HubGroups.Staff())
            .SendAsync(HubMethods.BookingChanged, dto, cancellationToken);
    }

    private async Task<GamingSession> ReloadSessionGraphAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        return await _db.GamingSessions.AsNoTracking()
            .Include(s => s.Computer)
            .Include(s => s.Zone)
            .Include(s => s.Tariff)
            .FirstAsync(s => s.Id == sessionId, cancellationToken);
    }

    public async Task<SessionDto?> GetActiveByComputerAsync(Guid computerId, CancellationToken cancellationToken = default)
    {
        var session = await _db.GamingSessions.AsNoTracking()
            .Include(s => s.Computer)
            .Include(s => s.Zone)
            .Include(s => s.Tariff)
            .Where(s => s.ComputerId == computerId && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return session is null ? null : MapSession(session);
    }

    public async Task<SessionDto?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _db.GamingSessions.AsNoTracking()
            .Include(s => s.Computer)
            .Include(s => s.Zone)
            .Include(s => s.Tariff)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        return session is null ? null : MapSession(session);
    }

    public async Task<SessionDto> AttachCustomerAsync(
        Guid sessionId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await AttachCustomerCoreAsync(sessionId, customerId, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                    entry.State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException(
            "Не удалось привязать аккаунт из‑за параллельного обновления сеанса. Повторите QR.");
    }

    private async Task<SessionDto> AttachCustomerCoreAsync(
        Guid sessionId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var session = await _db.GamingSessions
            .Include(s => s.Computer)
            .Include(s => s.Zone)
            .Include(s => s.Tariff)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Session not found.");

        if (session.Status is not (SessionStatus.Active or SessionStatus.Paused))
            throw new InvalidOperationException("Сеанс не активен.");
        if (session.CustomerId is not null && session.CustomerId != customerId)
            throw new InvalidOperationException("Сеанс уже привязан к другому клиенту.");

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw new KeyNotFoundException("Клиент не найден.");
        if (customer.BranchId != session.BranchId)
            throw new InvalidOperationException("Клиент другого филиала.");
        if (customer.IsBlocked || !customer.IsActive)
            throw new InvalidOperationException("Клиент недоступен.");

        var otherLive = await _db.GamingSessions.AsNoTracking()
            .AnyAsync(s => s.CustomerId == customerId
                           && s.Id != sessionId
                           && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused),
                cancellationToken);
        if (otherLive)
            throw new InvalidOperationException("Клиент уже играет на другом ПК.");

        if (session.CustomerId == customerId)
            return MapSession(session);

        var guestName = $"{customer.FirstName} {customer.LastName}".Trim();
        var now = DateTimeOffset.UtcNow;
        var newRv = Guid.NewGuid();
        var affected = await _db.GamingSessions
            .Where(s => s.Id == sessionId
                        && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused)
                        && (s.CustomerId == null || s.CustomerId == customerId)
                        && s.RowVersion == session.RowVersion)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.CustomerId, customerId)
                    .SetProperty(s => s.GuestName, guestName)
                    .SetProperty(s => s.UpdatedAt, now)
                    .SetProperty(s => s.RowVersion, newRv),
                cancellationToken);

        if (affected == 0)
            throw new DbUpdateConcurrencyException("Session attach raced with another update.");

        _db.SessionHistory.Add(new SessionHistoryEntry
        {
            SessionId = sessionId,
            Action = SessionHistoryAction.Recovered,
            DetailsJson = $"{{\"attachCustomer\":\"{customerId}\"}}",
            PriceAfter = session.TotalPrice,
            PlannedEndsAtAfter = session.PlannedEndsAt
        });
        await _db.SaveChangesAsync(cancellationToken);

        var dto = await GetByIdAsync(sessionId, cancellationToken)
                  ?? throw new InvalidOperationException("Session mapping failed after attach.");
        await PublishUpdatedAsync(dto, cancellationToken);
        return dto;
    }

    public async Task ProcessDueSessionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Warnings: 7 / 5 / 3 / 1 мин до конца (флаги БД: 15→7, 10→3, 5→5, 1→1).
        await ClaimAndSendWarningAsync(now, minutesLeft: 7, lowerExclusiveMinutes: 5, cancellationToken);
        await ClaimAndSendWarningAsync(now, minutesLeft: 5, lowerExclusiveMinutes: 3, cancellationToken);
        await ClaimAndSendWarningAsync(now, minutesLeft: 3, lowerExclusiveMinutes: 1, cancellationToken);
        await ClaimAndSendWarningAsync(now, minutesLeft: 1, lowerExclusiveMinutes: 0, cancellationToken);

        var dueIds = await _db.GamingSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Active
                        && s.PlannedEndsAt != null
                        && s.PlannedEndsAt <= now)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        foreach (var sessionId in dueIds)
        {
            try
            {
                DetachAllTracked();
                await EndAsync(
                    sessionId,
                    new EndSessionRequest(ForceUnpaid: true, Reason: "auto-end"),
                    Guid.Empty,
                    cancellationToken);
            }
            catch
            {
                // Next tick retries; never fail the whole worker loop on one PC.
                DetachAllTracked();
            }
        }
    }

    private void DetachAllTracked()
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    /// <summary>
    /// Atomically marks a warning flag via ExecuteUpdate, then notifies hubs.
    /// Band: remaining ∈ (lowerExclusive, minutesLeft] minutes.
    /// </summary>
    private async Task ClaimAndSendWarningAsync(
        DateTimeOffset now,
        int minutesLeft,
        int lowerExclusiveMinutes,
        CancellationToken cancellationToken)
    {
        var upper = now.AddMinutes(minutesLeft);
        var lower = now.AddMinutes(lowerExclusiveMinutes);

        var candidates = await _db.GamingSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Active && s.PlannedEndsAt != null)
            .Where(s => s.PlannedEndsAt <= upper && s.PlannedEndsAt > lower)
            .Where(s =>
                (minutesLeft == 7 && !s.Warning15Sent)
                || (minutesLeft == 5 && !s.Warning5Sent)
                || (minutesLeft == 3 && !s.Warning10Sent)
                || (minutesLeft == 1 && !s.Warning1Sent))
            .Select(s => new { s.Id, s.ComputerId, s.CustomerId })
            .ToListAsync(cancellationToken);

        foreach (var row in candidates)
        {
            var affected = minutesLeft switch
            {
                7 => await _db.GamingSessions
                    .Where(s => s.Id == row.Id && s.Status == SessionStatus.Active && !s.Warning15Sent)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.Warning15Sent, true)
                        .SetProperty(s => s.UpdatedAt, now), cancellationToken),
                5 => await _db.GamingSessions
                    .Where(s => s.Id == row.Id && s.Status == SessionStatus.Active && !s.Warning5Sent)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.Warning5Sent, true)
                        .SetProperty(s => s.UpdatedAt, now), cancellationToken),
                3 => await _db.GamingSessions
                    .Where(s => s.Id == row.Id && s.Status == SessionStatus.Active && !s.Warning10Sent)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.Warning10Sent, true)
                        .SetProperty(s => s.UpdatedAt, now), cancellationToken),
                _ => await _db.GamingSessions
                    .Where(s => s.Id == row.Id && s.Status == SessionStatus.Active && !s.Warning1Sent)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.Warning1Sent, true)
                        .SetProperty(s => s.UpdatedAt, now), cancellationToken)
            };

            if (affected == 0)
                continue;

            var action = minutesLeft switch
            {
                7 => SessionHistoryAction.Warning15,
                5 => SessionHistoryAction.Warning5,
                3 => SessionHistoryAction.Warning10,
                _ => SessionHistoryAction.Warning1
            };

            _db.SessionHistory.Add(new SessionHistoryEntry
            {
                SessionId = row.Id,
                Action = action,
                DetailsJson = $"{{\"minutesLeft\":{minutesLeft}}}"
            });
            await _db.SaveChangesAsync(cancellationToken);
            DetachAllTracked();

            var evt = new SessionWarningEvent(row.Id, row.ComputerId, minutesLeft);
            await _staffHub.Clients.Group(HubGroups.Staff())
                .SendAsync(HubMethods.SessionWarning, evt, cancellationToken);
            await _computerHub.Clients.Group(HubGroups.Computer(row.ComputerId))
                .SendAsync(HubMethods.SessionWarning, evt, cancellationToken);

            var pcName = await _db.Computers.AsNoTracking()
                .Where(c => c.Id == row.ComputerId)
                .Select(c => c.DisplayName ?? c.WindowsName)
                .FirstOrDefaultAsync(cancellationToken) ?? "ПК";
            await _telegramAlerts.PublishAsync(
                new StaffAlertMessage(
                    "session_warning",
                    $"Мало времени · {FormatDuration(minutesLeft)}",
                    $"{pcName}: осталось {FormatDuration(minutesLeft)}",
                    row.ComputerId,
                    pcName,
                    SessionId: row.Id,
                    MinutesLeft: minutesLeft),
                cancellationToken);

            if (row.CustomerId is Guid cid)
            {
                var tg = await _db.Customers.AsNoTracking()
                    .Where(c => c.Id == cid && c.AllowNotifications && c.TelegramUserId != null)
                    .Select(c => c.TelegramUserId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (tg is long tgId)
                {
                    await _customerNotify.PublishAsync(
                        new CustomerTelegramNotice(
                            tgId,
                            $"⏱ На <b>{System.Net.WebUtility.HtmlEncode(pcName)}</b> осталось <b>{FormatDuration(minutesLeft)}</b>.\nПродлите на кассе или в клубе."),
                        cancellationToken);
                }
            }
        }
    }

    private static string FormatSessionSaleLabel(
        string tariffName,
        int durationMinutes,
        decimal loyaltyDiscount,
        decimal prepaidDiscount)
    {
        var label = $"Игровое время · {tariffName} · {FormatDuration(durationMinutes)}";
        var parts = new List<string>();
        if (loyaltyDiscount > 0)
            parts.Add($"лояльность −{loyaltyDiscount:0.##} ₸");
        if (prepaidDiscount > 0)
            parts.Add($"предоплата −{prepaidDiscount:0.##} ₸");
        return parts.Count == 0 ? label : $"{label} ({string.Join(", ", parts)})";
    }

    private async Task PublishUpdatedAsync(SessionDto dto, CancellationToken cancellationToken)
    {
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(HubMethods.SessionUpdated, new SessionUpdatedEvent(dto), cancellationToken);
        await _computerHub.Clients.Group(HubGroups.Computer(dto.ComputerId)).SendAsync(HubMethods.SessionUpdated, dto, cancellationToken);
    }

    private static TariffDto MapTariff(
        Tariff t,
        bool availableNow,
        string? timeZoneId = null,
        MarketingPromoStoredSettings? promo = null)
    {
        string? windowEndsLocal = null;
        int? remainingInWindow = null;
        string? salePreview = null;
        var promoActive = MarketingPromoMath.IsActive(promo, DateTimeOffset.UtcNow);
        var tariffPromoEligible = MarketingPromoTariff.Qualifies(t);
        var promoPercent = promoActive && tariffPromoEligible
            ? MarketingPromoMath.ClampPercent(promo!.Percent)
            : 0m;
        var promoLabel = promoActive && tariffPromoEligible ? promo!.Label : null;

        decimal DisplayFixed(decimal? fixedPrice) =>
            fixedPrice is null
                ? 0
                : promoActive && tariffPromoEligible
                    ? MarketingPromoMath.Apply(fixedPrice.Value, promoPercent)
                    : fixedPrice.Value;

        if (t.DurationMode == TariffDurationMode.TimeWindow
            && availableNow
            && TariffAvailability.TryResolveTimeWindow(
                t, DateTimeOffset.UtcNow, timeZoneId,
                out _, out var periodEnd, out var remainingMins))
        {
            remainingInWindow = remainingMins;
            var tz = TariffAvailability.ResolveTz(timeZoneId);
            var localEnd = TimeZoneInfo.ConvertTime(periodEnd, tz);
            windowEndsLocal = localEnd.ToString("HH:mm");
            var list = t.FixedPrice ?? 0;
            var price = DisplayFixed(t.FixedPrice);
            salePreview =
                $"Тариф «{t.Name}» действует до {windowEndsLocal}. " +
                $"Осталось {TariffAvailability.FormatRemaining(remainingMins)}. " +
                (promoActive && tariffPromoEligible && list > price
                    ? $"Стоимость: {price:0.##} ₸ (было {list:0.##} ₸, {promoLabel})"
                    : $"Стоимость: {price:0.##} ₸");
        }
        else if (promoActive && tariffPromoEligible && !string.IsNullOrWhiteSpace(promoLabel))
        {
            salePreview = promoLabel;
        }

        // Для продажи/клиента отдаём цены уже со скидкой; в панели тарифов редактируют через PUT с этими же полями —
        // поэтому сюда кладём каталожные цены, а скидку помечаем PromoPercent (UI применяет при показе).
        return new TariffDto(
            t.Id,
            t.BranchId,
            t.ZoneId,
            t.Zone?.Name,
            t.Name,
            t.Code,
            t.Description,
            t.Kind,
            t.BillingMode,
            t.DurationMode,
            t.PricePerHour,
            t.MinCharge,
            t.FixedDurationMinutes,
            t.FixedPrice,
            t.MinDurationMinutes,
            t.MaxDurationMinutes,
            t.DaysOfWeekMask,
            FormatTime(t.AvailableFrom),
            FormatTime(t.AvailableTo),
            t.AllowPause,
            t.IsActive,
            t.SortOrder,
            t.ColorHex,
            availableNow,
            windowEndsLocal,
            remainingInWindow,
            salePreview,
            promoPercent,
            promoLabel);
    }

    private static string AppendOwnerProxyJson(string jsonObject, CashOwnerProxyInfo? proxy)
    {
        if (proxy is null || string.IsNullOrWhiteSpace(jsonObject) || jsonObject[^1] != '}')
            return jsonObject;
        var owner = System.Text.Json.JsonSerializer.Serialize(proxy.OwnerName);
        var cashier = System.Text.Json.JsonSerializer.Serialize(proxy.CashierName);
        var fragment =
            $",\"ownerProxy\":true,\"owner\":{owner},\"shiftNumber\":{System.Text.Json.JsonSerializer.Serialize(proxy.ShiftNumber)},\"cashier\":{cashier},\"cashierId\":\"{proxy.CashierEmployeeId}\"";
        return jsonObject[..^1] + fragment + "}";
    }

    private static string FormatDuration(int minutes)
    {
        if (minutes < 60)
            return $"{minutes} мин";

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} ч" : $"{hours} ч {rest} мин";
    }

    private static string? FormatTime(TimeSpan? t) =>
        t is null ? null : $"{(int)t.Value.TotalHours:00}:{t.Value.Minutes:00}";

    private static SessionDto MapSession(GamingSession s)
    {
        int? remaining = null;
        if (s.Status == SessionStatus.Paused)
            remaining = s.RemainingSecondsAtPause;
        else if (s.Status == SessionStatus.Active && s.PlannedEndsAt.HasValue)
            remaining = Math.Max(0, (int)(s.PlannedEndsAt.Value - DateTimeOffset.UtcNow).TotalSeconds);

        return new SessionDto(
            s.Id,
            s.BranchId,
            s.ComputerId,
            s.Computer?.DisplayName ?? s.Computer?.WindowsName,
            s.ZoneId,
            s.Zone?.Name,
            s.TariffId,
            s.Tariff?.Name,
            s.CustomerId,
            s.GuestName,
            s.PaymentMethod,
            s.Status,
            s.StartedAt,
            s.PlannedEndsAt,
            s.ActualEndedAt,
            s.DurationMinutes,
            s.BasePrice,
            s.DiscountAmount,
            s.TotalPrice,
            s.PaidAmount,
            s.DebtAmount,
            remaining,
            s.PausedAt,
            s.Tariff?.AllowPause ?? true,
            s.DurationMode,
            s.WindowPeriodStartsAt,
            s.WindowPeriodEndsAt);
    }

    /// <summary>Целевой ПК выключен / Shell офлайн — перед Unlock нужен WOL (как при старте с кассы).</summary>
    private static bool ShouldWakeForSessionTransfer(Computer computer)
    {
        if (computer.StationKind == StationKind.Console)
            return false;
        if (string.IsNullOrWhiteSpace(computer.MacAddress))
            return false;
        if (computer.Status is ComputerStatus.Offline or ComputerStatus.Error)
            return true;
        var (occupancy, detail) = ComputerOccupancy.Resolve(computer);
        if (!string.Equals(occupancy, "Free", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(detail))
            return false;
        return detail.Contains("офлайн", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("offline", StringComparison.OrdinalIgnoreCase);
    }
}
