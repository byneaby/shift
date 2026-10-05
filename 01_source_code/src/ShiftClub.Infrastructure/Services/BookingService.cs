using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Time;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Infrastructure.SignalR;
using ShiftClub.Shared.Contracts.Bookings;
using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Contracts.Computers;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared.SignalR;

namespace ShiftClub.Infrastructure.Services;

public sealed class BookingService : IBookingService
{
    /// <summary>За сколько минут до начала soft-hold удерживает ПК как Reserved.</summary>
    public const int SoftHoldMinutes = 30;

    private static readonly BookingStatus[] BlockingStatuses =
    [
        BookingStatus.Pending,
        BookingStatus.Confirmed,
        BookingStatus.Arrived,
        BookingStatus.Active
    ];

    private readonly ShiftClubDbContext _db;
    private readonly ICashService _cash;
    private readonly IDocumentNumberService _numbers;
    private readonly ISessionService _sessions;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly ITelegramAlertSink _telegramAlerts;

    public BookingService(
        ShiftClubDbContext db,
        ICashService cash,
        IDocumentNumberService numbers,
        ISessionService sessions,
        IHubContext<StaffHub> staffHub,
        ITelegramAlertSink telegramAlerts)
    {
        _db = db;
        _cash = cash;
        _numbers = numbers;
        _sessions = sessions;
        _staffHub = staffHub;
        _telegramAlerts = telegramAlerts;
    }

    public async Task<IReadOnlyList<BookingDto>> GetForDayAsync(
        DateOnly date,
        Guid? zoneId,
        Guid? computerId,
        CancellationToken cancellationToken = default)
    {
        var (dayStart, dayEnd) = await ResolveLocalDayBoundsAsync(date, cancellationToken);

        var query = _db.Bookings.AsNoTracking()
            .Include(b => b.Zone)
            .Include(b => b.Customer)
            .Include(b => b.Computers).ThenInclude(c => c.Computer).ThenInclude(c => c!.Zone)
            .Where(b => b.StartsAt < dayEnd && b.EndsAt > dayStart);

        if (zoneId.HasValue)
            query = query.Where(b => b.ZoneId == zoneId || b.Computers.Any(c => c.Computer.ZoneId == zoneId));

        if (computerId.HasValue)
            query = query.Where(b => b.Computers.Any(c => c.ComputerId == computerId));

        var list = await query.OrderBy(b => b.StartsAt).ToListAsync(cancellationToken);
        return list.Select(Map).ToList();
    }

    public async Task<BookingDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var booking = await LoadTrackedAsync(id, asNoTracking: true, cancellationToken);
        return booking is null ? null : Map(booking);
    }

    public async Task<IReadOnlyList<AvailableComputerDto>> GetAvailableComputersAsync(
        DateTimeOffset startsAt,
        int durationMinutes,
        Guid? zoneId,
        Guid? branchId,
        CancellationToken cancellationToken = default,
        Guid? excludeBookingId = null)
    {
        if (durationMinutes < 15)
            throw new InvalidOperationException("Минимальная длительность — 15 минут.");

        var start = startsAt.ToUniversalTime();
        var end = start.AddMinutes(durationMinutes);

        var query = _db.Computers.AsNoTracking()
            .Include(c => c.Zone)
            .Where(c => c.IsApproved && !c.IsMaintenance && !c.IsDeleted);

        if (branchId.HasValue)
            query = query.Where(c => c.BranchId == branchId.Value);
        if (zoneId.HasValue)
            query = query.Where(c => c.ZoneId == zoneId.Value);

        var computers = await query.OrderBy(c => c.DisplayName ?? c.WindowsName).ToListAsync(cancellationToken);
        if (computers.Count == 0)
            return Array.Empty<AvailableComputerDto>();

        var computerIds = computers.Select(c => c.Id).ToList();

        var busyBookingIds = await _db.BookingComputers.AsNoTracking()
            .Where(bc => computerIds.Contains(bc.ComputerId)
                         && BlockingStatuses.Contains(bc.Booking.Status)
                         && (!excludeBookingId.HasValue || bc.BookingId != excludeBookingId.Value)
                         && bc.Booking.StartsAt < end
                         && bc.Booking.EndsAt > start)
            .Select(bc => bc.ComputerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var busySessionIds = await _db.GamingSessions.AsNoTracking()
            .Where(s => computerIds.Contains(s.ComputerId)
                        && (s.Status == SessionStatus.Active
                            || s.Status == SessionStatus.Paused
                            || s.Status == SessionStatus.PaymentPending)
                        && s.StartedAt < end
                        && (s.PlannedEndsAt == null || s.PlannedEndsAt > start))
            .Select(s => s.ComputerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var busy = busyBookingIds.Concat(busySessionIds).ToHashSet();

        return computers
            .Where(c => !busy.Contains(c.Id)
                        && c.Status is not (ComputerStatus.Maintenance
                            or ComputerStatus.Updating
                            or ComputerStatus.Error))
            .Select(c => new AvailableComputerDto(
                c.Id,
                c.DisplayName ?? c.WindowsName,
                c.ZoneId,
                c.Zone?.Name,
                c.Status,
                c.Status is not (ComputerStatus.Offline or ComputerStatus.Error)))
            .ToList();
    }

    public async Task<BookingDto> CreateAsync(
        CreateBookingRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (request.DurationMinutes < 15)
            throw new InvalidOperationException("Минимальная длительность брони — 15 минут.");
        if (request.ComputerIds is null || request.ComputerIds.Count == 0)
            throw new InvalidOperationException("Укажите хотя бы один ПК.");
        if (string.IsNullOrWhiteSpace(request.ContactName) || string.IsNullOrWhiteSpace(request.ContactPhone))
            throw new InvalidOperationException("Имя и телефон обязательны.");
        if (request.PrepaidAmount < 0)
            throw new InvalidOperationException("Предоплата не может быть отрицательной.");
        if (request.PrepaidAmount > 0 && request.PrepayMethod is null)
            throw new InvalidOperationException("Укажите способ предоплаты.");

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.Bookings.AsNoTracking()
                .Include(b => b.Zone)
                .Include(b => b.Customer)
                .Include(b => b.Computers).ThenInclude(c => c.Computer).ThenInclude(c => c!.Zone)
                .FirstOrDefaultAsync(b => b.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
                return Map(existing);
        }

        var tzId = await _db.Branches.AsNoTracking().Select(b => b.TimeZoneId).FirstOrDefaultAsync(cancellationToken);
        var startsAt = ResolveStartsAt(request, tzId);
        if (startsAt <= DateTimeOffset.UtcNow.AddMinutes(-5))
            throw new InvalidOperationException("Время начала брони уже прошло.");

        var uniqueComputerIds = request.ComputerIds.Distinct().ToList();
        var endsAt = startsAt.AddMinutes(request.DurationMinutes);

        var computers = await _db.Computers
            .Include(c => c.Zone)
            .Where(c => uniqueComputerIds.Contains(c.Id) && !c.IsDeleted)
            .ToListAsync(cancellationToken);

        if (computers.Count != uniqueComputerIds.Count)
            throw new KeyNotFoundException("Один или несколько ПК не найдены.");

        if (computers.Any(c => !c.IsApproved || c.IsMaintenance))
            throw new InvalidOperationException("Нельзя бронировать незатверждённый или обслуживаемый ПК.");

        var branchId = computers[0].BranchId;
        if (computers.Any(c => c.BranchId != branchId))
            throw new InvalidOperationException("Все ПК брони должны быть из одного филиала.");

        Customer? customer = null;
        if (request.CustomerId.HasValue)
        {
            customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Клиент не найден.");
            if (customer.BranchId != branchId)
                throw new InvalidOperationException("Клиент другого филиала.");
            if (customer.IsBlocked)
                throw new InvalidOperationException("Клиент заблокирован.");
        }

        await EnsureNoConflictsAsync(uniqueComputerIds, startsAt, endsAt, excludeBookingId: null, cancellationToken);

        if (request.PrepaidAmount > 0)
            await _cash.EnsureEmployeeHasOpenShiftAsync(employeeId, cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var number = await _numbers.NextAsync(branchId, DocumentSequenceType.Booking, cancellationToken);
        var phone = new string(request.ContactPhone.Where(char.IsDigit).ToArray());

        var booking = new Booking
        {
            BranchId = branchId,
            ZoneId = request.ZoneId ?? computers[0].ZoneId,
            CustomerId = customer?.Id,
            ContactName = request.ContactName.Trim(),
            ContactPhone = phone,
            Comment = request.Comment,
            // С панели/API кассы бронь сразу подтверждена — отдельный шаг «Подтвердить» не нужен.
            Status = BookingStatus.Confirmed,
            StartsAt = startsAt,
            EndsAt = endsAt,
            DurationMinutes = request.DurationMinutes,
            PrepaidAmount = request.PrepaidAmount,
            TotalEstimated = request.PrepaidAmount,
            PrepayMethod = request.PrepayMethod,
            GraceMinutes = request.GraceMinutes is > 0 ? request.GraceMinutes.Value : 15,
            CreatedByEmployeeId = employeeId,
            IdempotencyKey = request.IdempotencyKey,
            Number = number
        };

        foreach (var computer in computers)
            booking.Computers.Add(new BookingComputer { ComputerId = computer.Id });

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync(cancellationToken);

        if (request.PrepaidAmount > 0)
        {
            var receipt = await _cash.CreateSaleAsync(new CreateSaleRequest(
                null,
                null,
                $"Предоплата брони {number}",
                request.IdempotencyKey is null ? null : $"booking-prepay-{request.IdempotencyKey}",
                [
                    new CreateSaleItemRequest(
                        ReceiptItemType.BookingDeposit,
                        $"Предоплата {number}",
                        1,
                        request.PrepaidAmount,
                        0,
                        booking.Id)
                ],
                [new PaymentPartDto(request.PrepayMethod!.Value, request.PrepaidAmount)],
                customer?.Id), employeeId, cancellationToken);

            booking.PrepayReceiptId = receipt.Id;
            await _db.SaveChangesAsync(cancellationToken);
        }

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            Action = "booking.create",
            EntityType = nameof(Booking),
            EntityId = booking.Id.ToString(),
            DetailsJson = $"{{\"number\":\"{number}\",\"pcs\":{computers.Count}}}"
        });
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Soft-hold сразу, если слот близко
        await ApplySoftHoldIfNeededAsync(booking.Id, employeeId, cancellationToken);

        var dto = await GetByIdAsync(booking.Id, cancellationToken)
                  ?? throw new InvalidOperationException("Booking mapping failed.");

        await _staffHub.Clients.Group(HubGroups.Staff())
            .SendAsync(HubMethods.BookingChanged, dto, cancellationToken);

        var pcs = string.Join(", ", dto.Computers.Select(c => c.ComputerName ?? "?"));
        await _telegramAlerts.PublishAsync(
            new StaffAlertMessage(
                "booking",
                "Новая бронь",
                $"{dto.StartsAt.ToLocalTime():dd.MM HH:mm} · {dto.ContactName} · {pcs}",
                BookingId: dto.Id),
            cancellationToken);

        return dto;
    }

    public async Task<BookingDto> ConfirmAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var booking = await LoadTrackedAsync(id, asNoTracking: false, cancellationToken)
            ?? throw new KeyNotFoundException("Бронь не найдена.");

        if (booking.Status != BookingStatus.Pending)
            throw new InvalidOperationException("Подтвердить можно только Pending.");

        booking.Status = BookingStatus.Confirmed;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        booking.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);

        await ApplySoftHoldIfNeededAsync(id, employeeId, cancellationToken);
        return await PublishAsync(booking.Id, cancellationToken);
    }

    public async Task<BookingDto> MarkArrivedAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var booking = await LoadTrackedAsync(id, asNoTracking: false, cancellationToken)
            ?? throw new KeyNotFoundException("Бронь не найдена.");

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            throw new InvalidOperationException("Отметить прибытие можно для Pending/Confirmed.");

        booking.Status = BookingStatus.Arrived;
        booking.ArrivedAt = DateTimeOffset.UtcNow;
        booking.UpdatedAt = booking.ArrivedAt;
        booking.UpdatedBy = employeeId;

        foreach (var link in booking.Computers)
        {
            var computer = await _db.Computers.FirstOrDefaultAsync(c => c.Id == link.ComputerId, cancellationToken);
            if (computer is null) continue;
            if (computer.Status is ComputerStatus.Free or ComputerStatus.Online or ComputerStatus.Locked or ComputerStatus.Reserved)
            {
                computer.Status = ComputerStatus.Reserved;
                computer.UpdatedAt = DateTimeOffset.UtcNow;
                computer.UpdatedBy = employeeId;
                await PublishComputerStatusAsync(computer, cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await PublishAsync(booking.Id, cancellationToken);
    }

    public async Task<BookingDto> CancelAsync(
        Guid id,
        CancelBookingRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var booking = await LoadTrackedAsync(id, asNoTracking: false, cancellationToken)
            ?? throw new KeyNotFoundException("Бронь не найдена.");

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.NoShow)
            throw new InvalidOperationException("Бронь уже закрыта.");

        // Active: завершаем связанные живые сеансы, освобождаем незапущенные ПК.
        if (booking.Status is BookingStatus.Active)
        {
            foreach (var link in booking.Computers.Where(c => c.GamingSessionId is not null))
            {
                var live = await _db.GamingSessions.AsNoTracking()
                    .Where(s => s.Id == link.GamingSessionId
                                && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused))
                    .Select(s => s.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (live != Guid.Empty)
                {
                    await _sessions.EndAsync(
                        live,
                        new EndSessionRequest(ForceUnpaid: true, Reason: "Отмена брони"),
                        employeeId,
                        cancellationToken);
                }
            }
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = DateTimeOffset.UtcNow;
        booking.CancelledByEmployeeId = employeeId;
        booking.CancelReason = string.IsNullOrWhiteSpace(request.Reason) ? "Отмена сотрудником" : request.Reason.Trim();
        booking.UpdatedAt = booking.CancelledAt;
        booking.UpdatedBy = employeeId;

        if (request.RefundPrepay && booking.PrepayReceiptId is Guid prepayReceiptId)
        {
            try
            {
                await _cash.RefundReceiptAsync(
                    prepayReceiptId,
                    $"Отмена брони {booking.Number}",
                    employeeId,
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Если предоплата уже возвращена / чек не Paid — не блокируем отмену брони.
            }
        }

        await ReleaseReservedComputersAsync(booking, employeeId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return await PublishAsync(booking.Id, cancellationToken);
    }

    public async Task<BookingDto> UpdateAsync(
        Guid id,
        UpdateBookingRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await UpdateCoreAsync(id, request, employeeId, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                    entry.State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("Не удалось обновить бронь из-за параллельного изменения. Повторите.");
    }

    private async Task<BookingDto> UpdateCoreAsync(
        Guid id,
        UpdateBookingRequest request,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var booking = await LoadTrackedAsync(id, asNoTracking: false, cancellationToken)
            ?? throw new KeyNotFoundException("Бронь не найдена.");

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.Arrived))
            throw new InvalidOperationException("Редактировать можно только Pending/Confirmed/Arrived.");

        if (request.DurationMinutes < 15)
            throw new InvalidOperationException("Минимальная длительность брони — 15 минут.");
        if (request.ComputerIds is null || request.ComputerIds.Count == 0)
            throw new InvalidOperationException("Укажите хотя бы один ПК.");
        if (string.IsNullOrWhiteSpace(request.ContactName) || string.IsNullOrWhiteSpace(request.ContactPhone))
            throw new InvalidOperationException("Имя и телефон обязательны.");

        var tzId = await _db.Branches.AsNoTracking().Select(b => b.TimeZoneId).FirstOrDefaultAsync(cancellationToken);
        var startsAt = ResolveStartsAt(
            new CreateBookingRequest(
                request.ZoneId,
                request.CustomerId,
                request.ContactName,
                request.ContactPhone,
                request.Comment,
                request.StartsAt,
                request.LocalDate,
                request.LocalTime,
                request.DurationMinutes,
                request.ComputerIds,
                0,
                null,
                request.GraceMinutes,
                null),
            tzId);
        var endsAt = startsAt.AddMinutes(request.DurationMinutes);
        if (startsAt <= DateTimeOffset.UtcNow.AddMinutes(-5))
            throw new InvalidOperationException("Время начала брони уже прошло.");

        var uniqueComputerIds = request.ComputerIds.Distinct().ToList();
        var computers = await _db.Computers
            .Include(c => c.Zone)
            .Where(c => uniqueComputerIds.Contains(c.Id) && !c.IsDeleted)
            .ToListAsync(cancellationToken);

        if (computers.Count != uniqueComputerIds.Count)
            throw new KeyNotFoundException("Один или несколько ПК не найдены.");
        if (computers.Any(c => !c.IsApproved || c.IsMaintenance))
            throw new InvalidOperationException("Нельзя бронировать незатверждённый или обслуживаемый ПК.");
        if (computers.Any(c => c.BranchId != booking.BranchId))
            throw new InvalidOperationException("Все ПК брони должны быть из одного филиала.");

        await EnsureNoConflictsAsync(uniqueComputerIds, startsAt, endsAt, excludeBookingId: id, cancellationToken);

        var oldComputerIds = booking.Computers.Select(c => c.ComputerId).ToHashSet();
        await ReleaseReservedComputersAsync(booking, employeeId, cancellationToken);

        booking.ContactName = request.ContactName.Trim();
        booking.ContactPhone = new string(request.ContactPhone.Where(char.IsDigit).ToArray());
        booking.Comment = request.Comment;
        booking.ZoneId = request.ZoneId ?? computers[0].ZoneId;
        booking.CustomerId = request.CustomerId ?? booking.CustomerId;
        booking.StartsAt = startsAt;
        booking.EndsAt = endsAt;
        booking.DurationMinutes = request.DurationMinutes;
        if (request.GraceMinutes is > 0)
            booking.GraceMinutes = request.GraceMinutes.Value;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        booking.UpdatedBy = employeeId;

        var existingLinks = booking.Computers.ToList();
        var toRemove = existingLinks.Where(link => !uniqueComputerIds.Contains(link.ComputerId)).ToList();
        if (toRemove.Count > 0)
            _db.BookingComputers.RemoveRange(toRemove);

        var existingIds = existingLinks.Select(link => link.ComputerId).ToHashSet();
        foreach (var computer in computers)
        {
            if (existingIds.Contains(computer.Id)) continue;
            booking.Computers.Add(new BookingComputer { ComputerId = computer.Id });
        }

        // TotalEstimated — ориентир по числу ПК (предоплата отдельно).
        if (booking.PrepaidAmount > 0)
            booking.TotalEstimated = booking.PrepaidAmount;
        else
            booking.TotalEstimated = 0;

        await _db.SaveChangesAsync(cancellationToken);
        await ApplySoftHoldIfNeededAsync(booking.Id, employeeId, cancellationToken);

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = booking.BranchId,
            EmployeeId = employeeId,
            Action = "booking.update",
            EntityType = nameof(Booking),
            EntityId = booking.Id.ToString(),
            DetailsJson = $"{{\"number\":\"{booking.Number}\",\"removedPcs\":{oldComputerIds.Count},\"pcs\":{computers.Count}}}"
        });
        await _db.SaveChangesAsync(cancellationToken);

        return await PublishAsync(booking.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var booking = await LoadTrackedAsync(id, asNoTracking: false, cancellationToken)
            ?? throw new KeyNotFoundException("Бронь не найдена.");

        if (booking.Status is BookingStatus.Active)
            throw new InvalidOperationException("Активную бронь удалить нельзя — завершите связанные сеансы.");

        if (booking.Computers.Any(c => c.GamingSessionId is not null))
            throw new InvalidOperationException("У брони есть связанный сеанс — нельзя удалить.");

        await ReleaseReservedComputersAsync(booking, employeeId, cancellationToken);

        var branchId = booking.BranchId;
        var number = booking.Number;

        _db.BookingComputers.RemoveRange(booking.Computers);
        _db.Bookings.Remove(booking);

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            Action = "booking.delete",
            EntityType = nameof(Booking),
            EntityId = id.ToString(),
            DetailsJson = $"{{\"number\":\"{number}\"}}"
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _staffHub.Clients.Group(HubGroups.Staff())
            .SendAsync(HubMethods.BookingChanged, new { id, deleted = true, number }, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionDto>> StartSessionsAsync(
        Guid bookingId,
        StartBookingSessionsRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var booking = await LoadTrackedAsync(bookingId, asNoTracking: true, cancellationToken)
            ?? throw new KeyNotFoundException("Бронь не найдена.");

        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.Arrived or BookingStatus.Active))
            throw new InvalidOperationException("Запуск возможен только для Pending/Confirmed/Arrived/Active.");

        if (request.DurationMinutes <= 0)
            throw new InvalidOperationException("Укажите длительность сеанса.");

        var targets = booking.Computers
            .Where(c => c.GamingSessionId is null)
            .Where(c => request.ComputerIds is null || request.ComputerIds.Count == 0 || request.ComputerIds.Contains(c.ComputerId))
            .ToList();

        if (targets.Count == 0)
            throw new InvalidOperationException("Нет ПК брони без активного сеанса.");

        // Автоприбытие перед стартом
        if (booking.Status is BookingStatus.Pending or BookingStatus.Confirmed)
            await MarkArrivedAsync(bookingId, employeeId, cancellationToken);

        var results = new List<SessionDto>();
        var index = 0;
        foreach (var link in targets)
        {
            var key = request.IdempotencyKey is null
                ? null
                : $"{request.IdempotencyKey}:{link.ComputerId}";

            var session = await _sessions.StartGuestSessionAsync(
                new StartGuestSessionRequest(
                    link.ComputerId,
                    request.TariffId,
                    request.DurationMinutes,
                    request.PaymentMethod,
                    booking.ContactName,
                    booking.CustomerId,
                    key,
                    request.UseTimeBank,
                    bookingId),
                employeeId,
                cancellationToken);
            results.Add(session);
            index++;
        }

        return results;
    }

    public async Task ProcessNoShowsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await _db.Bookings
            .Include(b => b.Computers)
            .Where(b =>
                ((b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed)
                 && b.StartsAt.AddMinutes(b.GraceMinutes) < now)
                || (b.Status == BookingStatus.Arrived
                    && b.EndsAt.AddMinutes(b.GraceMinutes) < now))
            .ToListAsync(cancellationToken);

        foreach (var booking in candidates)
        {
            booking.Status = BookingStatus.NoShow;
            booking.NoShowAt = now;
            booking.UpdatedAt = now;
            booking.CancelReason = "Автоматическая неявка";

            if (booking.PrepayReceiptId is Guid prepayReceiptId)
            {
                try
                {
                    var refundEmployeeId = await _db.Employees.AsNoTracking()
                        .Where(e => e.BranchId == booking.BranchId && e.IsActive)
                        .OrderBy(e => e.CreatedAt)
                        .Select(e => e.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (refundEmployeeId != Guid.Empty)
                    {
                        await _cash.RefundReceiptAsync(
                            prepayReceiptId,
                            $"Неявка по брони {booking.Number}",
                            refundEmployeeId,
                            cancellationToken);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Чек уже возвращён / смена закрыта — не блокируем NoShow.
                }
            }

            await ReleaseReservedComputersAsync(booking, Guid.Empty, cancellationToken);
        }

        if (candidates.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        foreach (var booking in candidates)
        {
            var dto = await GetByIdAsync(booking.Id, cancellationToken);
            if (dto is not null)
            {
                await _staffHub.Clients.Group(HubGroups.Staff())
                    .SendAsync(HubMethods.BookingChanged, dto, cancellationToken);
            }
        }
    }

    public async Task ProcessSoftHoldsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var windowEnd = now.AddMinutes(SoftHoldMinutes);

        var bookings = await _db.Bookings
            .Include(b => b.Computers)
            .Where(b => (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed)
                        && b.StartsAt <= windowEnd
                        && b.EndsAt > now)
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var booking in bookings)
        {
            foreach (var link in booking.Computers)
            {
                var computer = await _db.Computers.FirstOrDefaultAsync(c => c.Id == link.ComputerId, cancellationToken);
                if (computer is null || computer.CurrentSessionId is not null) continue;
                if (computer.Status is ComputerStatus.Free or ComputerStatus.Online or ComputerStatus.Locked)
                {
                    computer.Status = ComputerStatus.Reserved;
                    computer.UpdatedAt = now;
                    changed = true;
                    await PublishComputerStatusAsync(computer, cancellationToken);
                }
            }
        }

        // Снять «залипший» Reserved: бронь ещё далеко (>30 мин) или уже прошла — ПК снова свободен.
        var reservedPcs = await _db.Computers
            .Where(c => c.Status == ComputerStatus.Reserved && c.CurrentSessionId == null)
            .ToListAsync(cancellationToken);

        foreach (var computer in reservedPcs)
        {
            var stillHeld = await _db.BookingComputers.AsNoTracking()
                .AnyAsync(bc =>
                        bc.ComputerId == computer.Id
                        && BlockingStatuses.Contains(bc.Booking.Status)
                        && bc.GamingSessionId == null
                        && (bc.Booking.Status == BookingStatus.Arrived
                            || bc.Booking.Status == BookingStatus.Active
                            || (bc.Booking.StartsAt <= windowEnd && bc.Booking.EndsAt > now)),
                    cancellationToken);

            if (stillHeld) continue;

            computer.Status = ComputerStatus.Free;
            computer.UpdatedAt = now;
            changed = true;
            await PublishComputerStatusAsync(computer, cancellationToken);
        }

        if (changed)
            await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplySoftHoldIfNeededAsync(Guid bookingId, Guid employeeId, CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.Include(b => b.Computers)
            .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking is null) return;
        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.Arrived))
            return;

        var now = DateTimeOffset.UtcNow;
        var inWindow = booking.Status == BookingStatus.Arrived
                       || booking.StartsAt <= now.AddMinutes(SoftHoldMinutes);

        if (!inWindow) return;

        foreach (var link in booking.Computers)
        {
            var computer = await _db.Computers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == link.ComputerId, cancellationToken);
            if (computer is null || computer.CurrentSessionId is not null) continue;
            if (computer.Status is ComputerStatus.Free or ComputerStatus.Online or ComputerStatus.Locked or ComputerStatus.Reserved)
            {
                var query = _db.Computers.Where(c => c.Id == link.ComputerId && c.CurrentSessionId == null);
                var updated = employeeId != Guid.Empty
                    ? await query.ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Status, ComputerStatus.Reserved)
                        .SetProperty(c => c.UpdatedAt, now)
                        .SetProperty(c => c.UpdatedBy, employeeId)
                        .SetProperty(c => c.RowVersion, Guid.NewGuid()), cancellationToken)
                    : await query.ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Status, ComputerStatus.Reserved)
                        .SetProperty(c => c.UpdatedAt, now)
                        .SetProperty(c => c.RowVersion, Guid.NewGuid()), cancellationToken);
                if (updated > 0)
                {
                    computer.Status = ComputerStatus.Reserved;
                    computer.UpdatedAt = now;
                    if (employeeId != Guid.Empty)
                        computer.UpdatedBy = employeeId;
                    await PublishComputerStatusAsync(computer, cancellationToken);
                }
            }
        }
    }

    private async Task EnsureNoConflictsAsync(
        IReadOnlyList<Guid> computerIds,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        Guid? excludeBookingId,
        CancellationToken cancellationToken)
    {
        var start = startsAt.ToUniversalTime();
        var end = endsAt.ToUniversalTime();

        var conflict = await _db.BookingComputers
            .AsNoTracking()
            .Include(bc => bc.Booking)
            .Where(bc => computerIds.Contains(bc.ComputerId)
                         && BlockingStatuses.Contains(bc.Booking.Status)
                         && (!excludeBookingId.HasValue || bc.BookingId != excludeBookingId.Value)
                         && bc.Booking.StartsAt < end
                         && bc.Booking.EndsAt > start)
            .Select(bc => new { bc.ComputerId, bc.Booking.Number, bc.Booking.StartsAt, bc.Booking.EndsAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (conflict is not null)
        {
            throw new InvalidOperationException(
                $"Конфликт с бронью {conflict.Number} ({conflict.StartsAt:HH:mm}–{conflict.EndsAt:HH:mm}) на этом ПК.");
        }

        var sessionConflict = await _db.GamingSessions.AsNoTracking()
            .Include(s => s.Computer)
            .Where(s => computerIds.Contains(s.ComputerId)
                        && (s.Status == SessionStatus.Active
                            || s.Status == SessionStatus.Paused
                            || s.Status == SessionStatus.PaymentPending)
                        && s.StartedAt < end
                        && (s.PlannedEndsAt == null || s.PlannedEndsAt > start))
            .Select(s => new { s.ComputerId, Name = s.Computer.DisplayName ?? s.Computer.WindowsName, s.StartedAt, s.PlannedEndsAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (sessionConflict is not null)
        {
            throw new InvalidOperationException(
                $"ПК «{sessionConflict.Name}» занят активным сеансом до {sessionConflict.PlannedEndsAt:HH:mm}.");
        }
    }

    private async Task ReleaseReservedComputersAsync(
        Booking booking,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        foreach (var link in booking.Computers)
        {
            var computer = await _db.Computers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == link.ComputerId, cancellationToken);
            if (computer is null || computer.Status != ComputerStatus.Reserved) continue;
            if (computer.CurrentSessionId is not null) continue;

            // Не снимать hold, если другая активная бронь держит ПК
            var otherHold = await _db.BookingComputers.AsNoTracking()
                .AnyAsync(bc => bc.ComputerId == computer.Id
                                && bc.BookingId != booking.Id
                                && BlockingStatuses.Contains(bc.Booking.Status)
                                && (bc.Booking.Status == BookingStatus.Arrived
                                    || bc.Booking.Status == BookingStatus.Active
                                    || bc.Booking.StartsAt <= DateTimeOffset.UtcNow.AddMinutes(SoftHoldMinutes)),
                    cancellationToken);
            if (otherHold) continue;

            var now = DateTimeOffset.UtcNow;
            var query = _db.Computers
                .Where(c => c.Id == computer.Id && c.CurrentSessionId == null && c.Status == ComputerStatus.Reserved);
            var updated = employeeId != Guid.Empty
                ? await query.ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Status, ComputerStatus.Free)
                    .SetProperty(c => c.UpdatedAt, now)
                    .SetProperty(c => c.UpdatedBy, employeeId)
                    .SetProperty(c => c.RowVersion, Guid.NewGuid()), cancellationToken)
                : await query.ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Status, ComputerStatus.Free)
                    .SetProperty(c => c.UpdatedAt, now)
                    .SetProperty(c => c.RowVersion, Guid.NewGuid()), cancellationToken);
            if (updated > 0)
            {
                computer.Status = ComputerStatus.Free;
                computer.UpdatedAt = now;
                if (employeeId != Guid.Empty)
                    computer.UpdatedBy = employeeId;
                await PublishComputerStatusAsync(computer, cancellationToken);
            }
        }
    }

    private async Task PublishComputerStatusAsync(Computer computer, CancellationToken cancellationToken)
    {
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            new ComputerStatusChangedEvent(computer.Id, computer.Status, computer.LastSeenAt, computer.DisplayName),
            cancellationToken);
    }

    private static DateTimeOffset ResolveStartsAt(CreateBookingRequest request, string? tzId)
    {
        if (request.LocalDate is DateOnly localDate && !string.IsNullOrWhiteSpace(request.LocalTime))
        {
            if (!TimeOnly.TryParse(request.LocalTime, out var localTime))
                throw new InvalidOperationException("Некорректное локальное время (ожидается ЧЧ:ММ).");
            return BranchTimeZone.ToUtc(localDate, localTime, tzId);
        }

        if (request.StartsAt is null)
            throw new InvalidOperationException("Укажите время начала (localDate+localTime или startsAt).");

        return BranchTimeZone.EnsureUtc(request.StartsAt.Value);
    }

    private async Task<(DateTimeOffset DayStart, DateTimeOffset DayEnd)> ResolveLocalDayBoundsAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var tzId = await _db.Branches.AsNoTracking().Select(b => b.TimeZoneId).FirstOrDefaultAsync(cancellationToken);
        return BranchTimeZone.DayBoundsUtc(date, tzId);
    }

    private async Task<Booking?> LoadTrackedAsync(Guid id, bool asNoTracking, CancellationToken cancellationToken)
    {
        IQueryable<Booking> query = _db.Bookings
            .Include(b => b.Zone)
            .Include(b => b.Customer)
            .Include(b => b.Computers).ThenInclude(c => c.Computer).ThenInclude(c => c!.Zone);

        if (asNoTracking)
            query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    private async Task<BookingDto> PublishAsync(Guid id, CancellationToken cancellationToken)
    {
        var dto = await GetByIdAsync(id, cancellationToken)
                  ?? throw new InvalidOperationException("Booking mapping failed.");
        await _staffHub.Clients.Group(HubGroups.Staff())
            .SendAsync(HubMethods.BookingChanged, dto, cancellationToken);
        return dto;
    }

    private static BookingDto Map(Booking b) => new(
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
        b.Computers.Select(c => new BookingComputerDto(
            c.ComputerId,
            c.Computer?.DisplayName ?? c.Computer?.WindowsName,
            c.Computer?.ZoneId,
            c.Computer?.Zone?.Name,
            c.GamingSessionId)).ToList());
}
