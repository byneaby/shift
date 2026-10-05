using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Auth;
using ShiftClub.Shared.Contracts.ClientLauncher;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared;

namespace ShiftClub.Infrastructure.Services;

public sealed class TelegramAuthService : ITelegramAuthService
{
    private static readonly Regex DeepLinkCode = new(
        @"[?&]start=L?([A-Za-z0-9]{6,16})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string CodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly ConcurrentDictionary<string, List<long>> StaffCreateBucket = new();

    private readonly ShiftClubDbContext _db;
    private readonly IClientLauncherService _launcher;
    private readonly ISessionService _sessions;
    private readonly IClubSettingsService _settings;
    private readonly ITelegramBotRuntime _botRuntime;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuthService _auth;
    private readonly ICaseService _cases;

    public TelegramAuthService(
        ShiftClubDbContext db,
        IClientLauncherService launcher,
        ISessionService sessions,
        IClubSettingsService settings,
        ITelegramBotRuntime botRuntime,
        IPasswordHasher passwordHasher,
        IAuthService auth,
        ICaseService cases)
    {
        _db = db;
        _launcher = launcher;
        _sessions = sessions;
        _settings = settings;
        _botRuntime = botRuntime;
        _passwordHasher = passwordHasher;
        _auth = auth;
        _cases = cases;
    }

    public async Task<ClientTelegramTicketDto> CreateTicketAsync(
        Guid computerId,
        string purpose,
        Guid? sessionId,
        Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        purpose = NormalizePurpose(purpose);
        var computer = await _db.Computers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("ПК не найден");

        if (purpose == "BindSession")
        {
            if (sessionId is null)
                throw new InvalidOperationException("Для привязки сеанса нужен активный сеанс");
            var session = await _db.GamingSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId.Value && s.ComputerId == computerId, cancellationToken)
                ?? throw new InvalidOperationException("Сеанс не найден");
            if (session.Status is not (SessionStatus.Active or SessionStatus.Paused))
                throw new InvalidOperationException("Сеанс не активен");
            if (session.CustomerId is not null)
                throw new InvalidOperationException("Сеанс уже привязан к аккаунту");
        }

        if (purpose is "LinkAccount" or "ChangeTelegram")
        {
            if (customerId is null)
                throw new InvalidOperationException("Войдите в аккаунт, чтобы управлять Telegram");
            var customer = await _db.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == customerId.Value && c.IsActive, cancellationToken)
                ?? throw new InvalidOperationException("Аккаунт не найден");
            if (customer.BranchId != computer.BranchId)
                throw new InvalidOperationException("Аккаунт принадлежит другому филиалу");

            if (purpose == "LinkAccount")
            {
                if (customer.TelegramUserId is not null)
                    throw new InvalidOperationException("Telegram уже привязан. Используйте смену аккаунта.");
            }
            else
            {
                if (customer.TelegramUserId is null)
                    throw new InvalidOperationException("Сначала привяжите Telegram");
                await EnsureTelegramChangeAllowedAsync(customer, cancellationToken);
            }
        }

        var old = await _db.TelegramAuthTickets
            .Where(t => t.ComputerId == computerId
                        && t.Purpose == purpose
                        && (t.Status == "Pending"
                            || t.Status == "Claiming"
                            || t.Status == "AwaitingRegistration"))
            .ToListAsync(cancellationToken);
        foreach (var t in old)
        {
            t.Status = "Cancelled";
            t.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var code = await NewUniqueCodeAsync(cancellationToken);
        var ticket = new TelegramAuthTicket
        {
            BranchId = computer.BranchId,
            Code = code,
            Purpose = purpose,
            ComputerId = computerId,
            SessionId = sessionId,
            CustomerId = purpose is "LinkAccount" or "ChangeTelegram" ? customerId : null,
            Status = "Pending",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            AuthRedeemed = false
        };
        _db.TelegramAuthTickets.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken);

        var deepLink = await BuildDeepLinkAsync(code, cancellationToken);
        return new ClientTelegramTicketDto(
            ticket.Id,
            ticket.Code,
            deepLink,
            MakeQrPngBase64(deepLink),
            ticket.ExpiresAt,
            ticket.Purpose);
    }

    public async Task<StaffTelegramTicketDto> CreateStaffLoginTicketAsync(
        string clientNonce,
        string? clientIp = null,
        CancellationToken cancellationToken = default)
    {
        clientNonce = (clientNonce ?? "").Trim();
        if (clientNonce.Length is < 8 or > 64)
            throw new InvalidOperationException("Некорректный идентификатор сессии браузера.");

        EnforceStaffCreateRate(clientNonce, clientIp);

        var branch = await _db.Branches.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Филиал не настроен");

        // Гасим только свои тикеты с тем же nonce — не чужие вкладки
        var old = await _db.TelegramAuthTickets
            .Where(t => t.Purpose == "StaffLogin"
                        && t.ComputerId == null
                        && t.ClientNonce == clientNonce
                        && (t.Status == "Pending"
                            || t.Status == "Claiming"
                            || t.Status == "AwaitingRegistration"))
            .ToListAsync(cancellationToken);
        foreach (var t in old)
        {
            t.Status = "Cancelled";
            t.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var code = await NewUniqueCodeAsync(cancellationToken);
        var ticket = new TelegramAuthTicket
        {
            BranchId = branch.Id,
            Code = code,
            Purpose = "StaffLogin",
            ComputerId = null,
            ClientNonce = clientNonce,
            Status = "Pending",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            AuthRedeemed = false
        };
        _db.TelegramAuthTickets.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken);

        var deepLink = await BuildDeepLinkAsync(code, cancellationToken);
        return new StaffTelegramTicketDto(
            ticket.Id,
            ticket.Code,
            deepLink,
            MakeQrPngBase64(deepLink),
            ticket.ExpiresAt);
    }

    public async Task<StaffTelegramTicketStatusDto> GetStaffLoginStatusAsync(
        Guid ticketId,
        string? clientNonce = null,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _db.TelegramAuthTickets
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.Purpose == "StaffLogin", cancellationToken)
            ?? throw new KeyNotFoundException("Код не найден");

        if (!string.IsNullOrWhiteSpace(clientNonce)
            && !string.IsNullOrWhiteSpace(ticket.ClientNonce)
            && !string.Equals(ticket.ClientNonce, clientNonce.Trim(), StringComparison.Ordinal))
            throw new KeyNotFoundException("Код не найден");

        await ExpireIfNeededAsync(ticket, cancellationToken);

        if (ticket.Status == "Claiming")
            return new StaffTelegramTicketStatusDto("Pending", null, "Подтверждаем Telegram…");

        if (ticket.Status != "Consumed" || ticket.TelegramUserId is null)
            return new StaffTelegramTicketStatusDto(ticket.Status, null, ticket.ResultMessage);

        if (ticket.AuthRedeemed)
            return new StaffTelegramTicketStatusDto("Consumed", null, ticket.ResultMessage ?? "Вход уже выполнен");

        var auth = await _auth.LoginByTelegramUserIdAsync(ticket.TelegramUserId.Value, cancellationToken);
        if (auth is null)
            return new StaffTelegramTicketStatusDto(
                "Consumed",
                null,
                "Telegram не привязан к сотруднику. Добавьте ID в настройках бота.");

        ticket.AuthRedeemed = true;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return new StaffTelegramTicketStatusDto("Consumed", auth, ticket.ResultMessage);
    }

    public async Task<ClientTelegramTicketStatusDto> GetTicketStatusAsync(
        Guid computerId,
        Guid ticketId,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _db.TelegramAuthTickets
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.ComputerId == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("Код не найден");

        await ExpireIfNeededAsync(ticket, cancellationToken);

        if (ticket.Status == "Claiming")
        {
            return new ClientTelegramTicketStatusDto(
                "Pending",
                null,
                "Подтверждаем Telegram…");
        }

        if (ticket.Status == "AwaitingRegistration")
        {
            return new ClientTelegramTicketStatusDto(
                "AwaitingRegistration",
                null,
                ticket.ResultMessage ?? "Telegram подтверждён. Завершите регистрацию.",
                NeedsRegistration: true,
                TelegramDisplayName: ticket.PendingDisplayName);
        }

        if (ticket.Status != "Consumed")
            return new ClientTelegramTicketStatusDto(ticket.Status, null, ticket.ResultMessage);

        if (ticket.Purpose is "LinkAccount" or "ChangeTelegram")
            return new ClientTelegramTicketStatusDto(ticket.Status, null, ticket.ResultMessage);

        if (ticket.CustomerId is null)
            return new ClientTelegramTicketStatusDto(ticket.Status, null, ticket.ResultMessage);

        if (ticket.AuthRedeemed)
            return new ClientTelegramTicketStatusDto("Consumed", null, ticket.ResultMessage ?? "Вход уже выполнен");

        var auth = await _launcher.LoginByCustomerIdAsync(
            computerId, ticket.CustomerId.Value, cancellationToken);
        ticket.AuthRedeemed = true;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return new ClientTelegramTicketStatusDto("Consumed", auth, ticket.ResultMessage);
    }

    public async Task CancelTicketAsync(
        Guid ticketId,
        string? clientNonce = null,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _db.TelegramAuthTickets.FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken);
        if (ticket is null)
            return;
        if (ticket.Status is not ("Pending" or "Claiming" or "AwaitingRegistration"))
            return;
        if (!string.IsNullOrWhiteSpace(clientNonce)
            && !string.IsNullOrWhiteSpace(ticket.ClientNonce)
            && !string.Equals(ticket.ClientNonce, clientNonce.Trim(), StringComparison.Ordinal))
            return;

        ticket.Status = "Cancelled";
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public string? TryExtractTicketCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        raw = raw.Trim();

        var m = DeepLinkCode.Match(raw);
        if (m.Success)
            return m.Groups[1].Value.ToUpperInvariant();

        if (raw.StartsWith("L", StringComparison.OrdinalIgnoreCase)
            && raw.Length is >= 7 and <= 17
            && raw[1..].All(char.IsLetterOrDigit))
            return raw[1..].ToUpperInvariant();

        var digits = new string(raw.Where(char.IsLetterOrDigit).ToArray());
        if (digits.Length is >= 6 and <= 16)
            return digits.ToUpperInvariant();

        return null;
    }

    public async Task<string> CompleteTicketFromTelegramAsync(
        string code,
        long telegramUserId,
        string? telegramDisplayName,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var extracted = TryExtractTicketCode(code) ?? code.Trim().ToUpperInvariant();
        var ticket = await _db.TelegramAuthTickets
            .FirstOrDefaultAsync(t => t.Code == extracted, cancellationToken)
            ?? throw new InvalidOperationException("Код не найден. Запросите новый QR на экране ПК.");

        if (ticket.Status == "AwaitingRegistration")
        {
            if (ticket.TelegramUserId == telegramUserId)
            {
                await tx.CommitAsync(cancellationToken);
                return "✅ Telegram уже подтверждён. Завершите регистрацию в приложении SHIFT или на ПК.";
            }
            throw new InvalidOperationException("Этот код уже ожидает регистрацию другим Telegram.");
        }

        if (ticket.Status == "Claiming")
        {
            if (ticket.TelegramUserId != telegramUserId)
                throw new InvalidOperationException("Этот код уже используется. Запросите новый QR.");
            // Тот же пользователь — дожимаем прерванный confirm
        }
        else if (ticket.Status != "Pending")
            throw new InvalidOperationException("Этот код уже использован или устарел.");

        if (ticket.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            ticket.Status = "Expired";
            ticket.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            throw new InvalidOperationException("Срок действия кода истёк. Запросите новый на ПК.");
        }

        if (ticket.Status == "Pending")
        {
            // Атомарный захват: Claiming — чужой confirm не перепишет TelegramUserId
            var claimed = await _db.TelegramAuthTickets
                .Where(t => t.Id == ticket.Id && t.Status == "Pending")
                .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.Status, "Claiming")
                        .SetProperty(t => t.TelegramUserId, telegramUserId)
                        .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow),
                    cancellationToken);
            if (claimed != 1)
                throw new InvalidOperationException("Этот код уже используется. Запросите новый QR.");

            await _db.Entry(ticket).ReloadAsync(cancellationToken);
        }

        try
        {
            string result;
            if (ticket.Purpose == "StaffLogin")
                result = await CompleteStaffLoginAsync(ticket, telegramUserId, telegramDisplayName, cancellationToken);
            else
                result = await CompleteClientTicketAsync(ticket, telegramUserId, telegramDisplayName, cancellationToken);

            await tx.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception)
        {
            // Не затираем исходную ошибку вторичным SaveChanges (сеанс/ПК могут быть
            // уже в ChangeTracker с устаревшим RowVersion после AttachCustomer и т.п.).
            try
            {
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                    entry.State = EntityState.Detached;

                await _db.TelegramAuthTickets
                    .Where(t => t.Id == ticket.Id && t.Status == "Claiming")
                    .ExecuteUpdateAsync(s => s
                            .SetProperty(t => t.Status, "Pending")
                            .SetProperty(t => t.TelegramUserId, (long?)null)
                            .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow),
                        cancellationToken);
            }
            catch
            {
                /* best-effort rollback of claim */
            }

            try
            {
                await tx.RollbackAsync(cancellationToken);
            }
            catch
            {
                /* dispose will clean up */
            }

            throw;
        }
    }

    public async Task<ClientTelegramTicketStatusDto> CompleteRegistrationOnPcAsync(
        Guid computerId,
        Guid ticketId,
        ClientTelegramCompleteRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _db.TelegramAuthTickets
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.ComputerId == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("Код не найден");

        await ExpireIfNeededAsync(ticket, cancellationToken);
        if (ticket.Status != "AwaitingRegistration" || ticket.TelegramUserId is null)
            throw new InvalidOperationException("Сначала отсканируйте QR в Telegram.");

        var customer = await UpsertCustomerForRegistrationAsync(
            ticket.BranchId,
            ticket.TelegramUserId.Value,
            request.Phone,
            request.FirstName,
            request.LastName,
            request.Password,
            request.Iin,
            request.LinkExisting,
            cancellationToken);

        if (ticket.Purpose == "BindSession" && ticket.SessionId is Guid sid)
        {
            foreach (var entry in _db.ChangeTracker.Entries().ToList())
                entry.State = EntityState.Detached;
            await _sessions.AttachCustomerAsync(sid, customer.Id, cancellationToken);
            ticket = await _db.TelegramAuthTickets
                .FirstAsync(t => t.Id == ticketId && t.ComputerId == computerId, cancellationToken);
        }

        ticket.Status = "Consumed";
        ticket.ConsumedAt = DateTimeOffset.UtcNow;
        ticket.CustomerId = customer.Id;
        ticket.AuthRedeemed = false;
        ticket.ResultMessage = "Аккаунт готов. Входим на ПК…";
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var auth = await _launcher.LoginByCustomerIdAsync(computerId, customer.Id, cancellationToken);
        ticket.AuthRedeemed = true;
        await _db.SaveChangesAsync(cancellationToken);
        return new ClientTelegramTicketStatusDto("Consumed", auth, ticket.ResultMessage);
    }

    public async Task<(string Message, Guid CustomerId)> CompleteRegistrationFromTelegramAsync(
        string ticketCode,
        long telegramUserId,
        string phone,
        string firstName,
        string lastName,
        string password,
        string? iin,
        bool linkExisting,
        CancellationToken cancellationToken = default)
    {
        var extracted = TryExtractTicketCode(ticketCode) ?? ticketCode.Trim().ToUpperInvariant();
        var ticket = await _db.TelegramAuthTickets
            .FirstOrDefaultAsync(t => t.Code == extracted, cancellationToken)
            ?? throw new InvalidOperationException("Код не найден. Запросите новый QR на ПК.");

        await ExpireIfNeededAsync(ticket, cancellationToken);
        if (ticket.Status != "AwaitingRegistration")
            throw new InvalidOperationException("Сначала отсканируйте QR на экране ПК.");
        if (ticket.TelegramUserId != telegramUserId)
            throw new InvalidOperationException("Этот QR подтверждён другим Telegram.");

        var customer = await UpsertCustomerForRegistrationAsync(
            ticket.BranchId,
            telegramUserId,
            phone,
            firstName,
            lastName,
            password,
            iin,
            linkExisting,
            cancellationToken);

        if (ticket.Purpose == "BindSession" && ticket.SessionId is Guid sid)
        {
            foreach (var entry in _db.ChangeTracker.Entries().ToList())
                entry.State = EntityState.Detached;
            await _sessions.AttachCustomerAsync(sid, customer.Id, cancellationToken);
            ticket = await _db.TelegramAuthTickets.FirstAsync(t => t.Id == ticket.Id, cancellationToken);
        }

        ticket.Status = "Consumed";
        ticket.ConsumedAt = DateTimeOffset.UtcNow;
        ticket.CustomerId = customer.Id;
        ticket.AuthRedeemed = false;
        ticket.ResultMessage = "Аккаунт создан. Вернитесь к ПК — вход выполнится автоматически.";
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return ("✅ " + ticket.ResultMessage, customer.Id);
    }

    private async Task<string> CompleteClientTicketAsync(
        TelegramAuthTicket ticket,
        long telegramUserId,
        string? telegramDisplayName,
        CancellationToken cancellationToken)
    {
        var existingByTg = await _db.Customers
            .FirstOrDefaultAsync(c => c.TelegramUserId == telegramUserId && c.IsActive, cancellationToken);

        Customer customer;
        string okMessage;

        if (ticket.Purpose is "Login")
        {
            if (existingByTg is null)
            {
                ticket.Status = "AwaitingRegistration";
                ticket.TelegramUserId = telegramUserId;
                ticket.PendingDisplayName = TrimDisplay(telegramDisplayName);
                ticket.ResultMessage =
                    "Telegram подтверждён. Создайте аккаунт в приложении SHIFT или заполните форму на ПК.";
                ticket.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return "✅ Telegram подтверждён.\n\n"
                       + "• В приложении SHIFT: создайте аккаунт или привяжите существующий.\n"
                       + "• Или вернитесь к ПК и заполните форму справа.\n"
                       + "После этого вход на ПК выполнится автоматически.";
            }

            if (existingByTg.BranchId != ticket.BranchId)
                throw new InvalidOperationException("Аккаунт относится к другому филиалу.");
            customer = existingByTg;
            okMessage = "Вход подтверждён. Вернитесь к ПК — сеанс откроется автоматически.";
        }
        else if (ticket.Purpose == "BindSession")
        {
            if (existingByTg is not null)
            {
                customer = existingByTg;
                if (ticket.SessionId is Guid sid)
                {
                    // AttachCustomer трогает GamingSession.RowVersion — не смешивать с tracked ticket.
                    var ticketId = ticket.Id;
                    foreach (var entry in _db.ChangeTracker.Entries().ToList())
                        entry.State = EntityState.Detached;
                    await _sessions.AttachCustomerAsync(sid, customer.Id, cancellationToken);
                    ticket = await _db.TelegramAuthTickets.FirstAsync(t => t.Id == ticketId, cancellationToken);
                    customer = await _db.Customers.FirstAsync(c => c.Id == existingByTg.Id, cancellationToken);
                }

                okMessage = $"Готово. Сеанс сохранён в аккаунт {customer.FirstName}.";
            }
            else
            {
                ticket.Status = "AwaitingRegistration";
                ticket.TelegramUserId = telegramUserId;
                ticket.PendingDisplayName = TrimDisplay(telegramDisplayName);
                ticket.ResultMessage =
                    "Telegram подтверждён. Создайте аккаунт, чтобы сохранить сеанс.";
                ticket.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return "✅ Telegram подтверждён. Создайте аккаунт в SHIFT или на ПК — сеанс сохранится автоматически.";
            }
        }
        else if (ticket.Purpose == "LinkAccount")
        {
            if (ticket.CustomerId is null)
                throw new InvalidOperationException("Некорректный запрос привязки.");
            if (existingByTg is not null && existingByTg.Id != ticket.CustomerId)
                throw new InvalidOperationException("Этот Telegram уже привязан к другому аккаунту.");

            customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == ticket.CustomerId.Value, cancellationToken)
                       ?? throw new InvalidOperationException("Аккаунт не найден.");
            if (customer.TelegramUserId is not null && customer.TelegramUserId != telegramUserId)
                throw new InvalidOperationException("К аккаунту уже привязан другой Telegram.");

            customer.TelegramUserId = telegramUserId;
            customer.TelegramLinkedAt = DateTimeOffset.UtcNow;
            okMessage = "Telegram успешно привязан к вашему аккаунту.";
        }
        else if (ticket.Purpose == "ChangeTelegram")
        {
            if (ticket.CustomerId is null)
                throw new InvalidOperationException("Некорректный запрос смены Telegram.");
            if (existingByTg is not null && existingByTg.Id != ticket.CustomerId)
                throw new InvalidOperationException("Этот Telegram уже привязан к другому аккаунту.");

            customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == ticket.CustomerId.Value, cancellationToken)
                       ?? throw new InvalidOperationException("Аккаунт не найден.");
            await EnsureTelegramChangeAllowedAsync(customer, cancellationToken);

            customer.TelegramUserId = telegramUserId;
            customer.TelegramLinkedAt ??= DateTimeOffset.UtcNow;
            customer.TelegramChangedAt = DateTimeOffset.UtcNow;
            okMessage = "Telegram обновлён. Следующая смена будет доступна после истечения лимита.";
        }
        else
            throw new InvalidOperationException("Неизвестная операция.");

        if (ticket.Purpose is not ("LinkAccount" or "ChangeTelegram"))
        {
            if (customer.TelegramUserId is null)
            {
                var taken = await _db.Customers.AnyAsync(
                    c => c.TelegramUserId == telegramUserId && c.Id != customer.Id, cancellationToken);
                if (taken)
                    throw new InvalidOperationException("Этот Telegram уже привязан к другому аккаунту.");
                customer.TelegramUserId = telegramUserId;
                customer.TelegramLinkedAt = DateTimeOffset.UtcNow;
            }
            else if (customer.TelegramUserId != telegramUserId)
                throw new InvalidOperationException("Аккаунт привязан к другому Telegram.");
        }

        ticket.Status = "Consumed";
        ticket.ConsumedAt = DateTimeOffset.UtcNow;
        ticket.CustomerId = customer.Id;
        ticket.TelegramUserId = telegramUserId;
        ticket.AuthRedeemed = false;
        ticket.ResultMessage = okMessage;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return "✅ " + okMessage;
    }

    private async Task<string> CompleteStaffLoginAsync(
        TelegramAuthTicket ticket,
        long telegramUserId,
        string? telegramDisplayName,
        CancellationToken cancellationToken)
    {
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.TelegramUserId == telegramUserId && e.IsActive, cancellationToken);

        if (employee is null)
        {
            var stored = await _settings.GetTelegramBotStoredAsync(cancellationToken);
            var allowed = stored.AllowedUsers.FirstOrDefault(u => u.TelegramUserId == telegramUserId);
            if (allowed?.EmployeeId is { } eid)
            {
                employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == eid && e.IsActive, cancellationToken);
                if (employee is not null && employee.TelegramUserId is null)
                {
                    employee.TelegramUserId = telegramUserId;
                    employee.TelegramLinkedAt = DateTimeOffset.UtcNow;
                    employee.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
        }

        if (employee is null)
            throw new InvalidOperationException(
                "Telegram не привязан к сотруднику. Владелец должен добавить ваш ID в панели → Telegram.");

        var name = string.IsNullOrWhiteSpace(telegramDisplayName) ? employee.DisplayName : telegramDisplayName.Trim();
        ticket.Status = "Consumed";
        ticket.ConsumedAt = DateTimeOffset.UtcNow;
        ticket.TelegramUserId = telegramUserId;
        ticket.AuthRedeemed = false;
        ticket.ResultMessage = $"Вход как {employee.DisplayName} подтверждён. Вернитесь в панель.";
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return $"✅ Вход в панель подтверждён ({name}). Вернитесь в браузер.";
    }

    private async Task<Customer> UpsertCustomerForRegistrationAsync(
        Guid branchId,
        long telegramUserId,
        string phoneRaw,
        string firstName,
        string lastName,
        string password,
        string? iinRaw,
        bool linkExisting,
        CancellationToken cancellationToken)
    {
        var phone = PhoneDigits.Normalize(phoneRaw);
        if (phone.Length < 10)
            throw new InvalidOperationException("Укажите телефон — не менее 10 цифр.");
        var first = (firstName ?? "").Trim();
        var last = (lastName ?? "").Trim();
        if (first.Length < 1)
            throw new InvalidOperationException("Укажите имя.");
        if (last.Length < 1)
            throw new InvalidOperationException("Укажите фамилию.");
        var passwordTrim = (password ?? "").Trim();
        if (passwordTrim.Length < 4)
            throw new InvalidOperationException("Пароль: минимум 4 символа.");
        var iin = NormalizeIin(iinRaw);

        var byTg = await _db.Customers
            .FirstOrDefaultAsync(c => c.TelegramUserId == telegramUserId && c.IsActive, cancellationToken);
        if (byTg is not null)
            return byTg;

        if (iin is not null
            && await _db.Customers.AnyAsync(c => c.BranchId == branchId && c.Iin == iin && c.IsActive, cancellationToken))
            throw new InvalidOperationException("Этот ИИН уже зарегистрирован.");

        var last10 = PhoneDigits.Last10(phone);
        var existing = await _db.Customers
            .FirstOrDefaultAsync(
                c => c.BranchId == branchId && c.IsActive && (c.Phone == phone || c.Phone.EndsWith(last10)),
                cancellationToken);

        if (existing is not null)
        {
            if (existing.TelegramUserId is not null && existing.TelegramUserId != telegramUserId)
                throw new InvalidOperationException("Этот телефон уже привязан к другому Telegram.");
            if (!VerifySecret(existing, passwordTrim))
                throw new InvalidOperationException(
                    linkExisting
                        ? "Неверный пароль или PIN существующего аккаунта."
                        : "Телефон уже занят. Включите «У меня уже есть аккаунт» и введите пароль с ПК.");

            existing.TelegramUserId = telegramUserId;
            existing.TelegramLinkedAt ??= DateTimeOffset.UtcNow;
            existing.FirstName = first;
            existing.LastName = last;
            if (string.IsNullOrWhiteSpace(existing.PasswordHash))
                existing.PasswordHash = _passwordHasher.Hash(passwordTrim);
            if (iin is not null) existing.Iin = iin;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        if (linkExisting)
            throw new InvalidOperationException("Аккаунт с таким телефоном не найден.");

        var loyalty = await _db.LoyaltyLevels
            .Where(l => l.BranchId == branchId && l.IsActive)
            .OrderBy(l => l.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        var customer = new Customer
        {
            BranchId = branchId,
            FirstName = first,
            LastName = last,
            Phone = phone,
            Iin = iin,
            LoyaltyLevelId = loyalty?.Id,
            TelegramUserId = telegramUserId,
            TelegramLinkedAt = DateTimeOffset.UtcNow,
            PasswordHash = _passwordHasher.Hash(passwordTrim),
            IsActive = true,
            AllowNotifications = true
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            await _cases.TryGrantRegistrationKeyAsync(customer.Id, cancellationToken);
        }
        catch
        {
            // welcome key is best-effort
        }

        return customer;
    }

    public async Task LinkTelegramToCustomerAsync(
        Guid customerId,
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        var taken = await _db.Customers.AnyAsync(
            c => c.TelegramUserId == telegramUserId && c.Id != customerId, cancellationToken);
        if (taken)
            throw new InvalidOperationException("Этот Telegram уже привязан к другому аккаунту.");

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
                       ?? throw new KeyNotFoundException("Клиент не найден");
        customer.TelegramUserId = telegramUserId;
        customer.TelegramLinkedAt = DateTimeOffset.UtcNow;
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CustomerTelegramProfile?> FindCustomerByTelegramAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        var c = await _db.Customers.AsNoTracking()
            .Include(x => x.LoyaltyLevel)
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId && x.IsActive, cancellationToken);
        if (c is null)
            return null;
        return new CustomerTelegramProfile(
            c.Id,
            c.BranchId,
            $"{c.FirstName} {c.LastName}".Trim(),
            c.Phone,
            c.Balance,
            c.BonusBalance,
            c.TimeBankMinutes,
            c.VisitStreakDays,
            c.PendingBarRewards,
            c.LoyaltyLevel?.Name,
            c.ComfortHideBalance,
            c.ComfortSoundEnabled,
            c.ComfortLanguage,
            c.ComfortBrightness,
            c.BirthDate);
    }

    private async Task ExpireIfNeededAsync(TelegramAuthTicket ticket, CancellationToken cancellationToken)
    {
        if ((ticket.Status is "Pending" or "Claiming" or "AwaitingRegistration")
            && ticket.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            ticket.Status = "Expired";
            ticket.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task EnsureTelegramChangeAllowedAsync(Customer customer, CancellationToken cancellationToken)
    {
        var eng = await _settings.GetEngagementStoredAsync(cancellationToken);
        var cooldown = Math.Clamp(eng.TelegramChangeCooldownDays, 1, 365);
        var last = customer.TelegramChangedAt ?? customer.TelegramLinkedAt;
        if (last is null)
            return;
        var next = last.Value.AddDays(cooldown);
        if (next > DateTimeOffset.UtcNow)
        {
            var local = next.ToLocalTime();
            throw new InvalidOperationException(
                $"Смена Telegram доступна с {local:dd.MM.yyyy}. Интервал — раз в {cooldown} дн.");
        }
    }

    private bool VerifySecret(Customer customer, string secret)
    {
        if (!string.IsNullOrWhiteSpace(customer.PasswordHash)
            && _passwordHasher.Verify(secret, customer.PasswordHash))
            return true;
        if (!string.IsNullOrWhiteSpace(customer.PinHash)
            && _passwordHasher.Verify(secret, customer.PinHash))
            return true;
        return false;
    }

    private static string DigitsOnly(string? raw) =>
        new string((raw ?? "").Where(char.IsDigit).ToArray());

    private static string? NormalizeIin(string? raw)
    {
        var d = DigitsOnly(raw);
        if (d.Length == 0) return null;
        if (d.Length != 12)
            throw new InvalidOperationException("ИИН должен содержать 12 цифр.");
        return d;
    }

    private static string? TrimDisplay(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        return name.Length > 80 ? name[..80] : name;
    }

    private async Task<string> NewUniqueCodeAsync(CancellationToken ct)
    {
        for (var i = 0; i < 32; i++)
        {
            var code = NewCode(8);
            if (!await _db.TelegramAuthTickets.AnyAsync(t => t.Code == code, ct))
                return code;
        }
        return Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }

    private static string NewCode(int length)
    {
        Span<char> chars = stackalloc char[length];
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < length; i++)
            chars[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        return new string(chars);
    }

    private async Task<string> BuildDeepLinkAsync(string code, CancellationToken cancellationToken)
    {
        var stored = await _settings.GetTelegramBotStoredAsync(cancellationToken);
        var username = (_botRuntime.BotUsername ?? stored.BotUsername ?? "").Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(username))
            return code;
        return $"https://t.me/{username}?start=L{code}";
    }

    private static string MakeQrPngBase64(string payload)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        var pixels = payload.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? 12 : 16;
        var bytes = png.GetGraphic(pixels);
        return Convert.ToBase64String(bytes);
    }

    private static void EnforceStaffCreateRate(string clientNonce, string? clientIp)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        TrimBucket(StaffCreateBucket.GetOrAdd("n:" + clientNonce, _ => new List<long>()), now, max: 8);
        if (!string.IsNullOrWhiteSpace(clientIp))
            TrimBucket(StaffCreateBucket.GetOrAdd("ip:" + clientIp, _ => new List<long>()), now, max: 30);
    }

    private static void TrimBucket(List<long> bucket, long now, int max)
    {
        lock (bucket)
        {
            bucket.RemoveAll(t => now - t > 300);
            if (bucket.Count >= max)
                throw new InvalidOperationException("Слишком много запросов QR. Подождите минуту.");
            bucket.Add(now);
        }
    }

    private static string NormalizePurpose(string purpose) =>
        purpose.Trim() switch
        {
            "BindSession" or "bind" or "bind-session" => "BindSession",
            "LinkAccount" or "link" => "LinkAccount",
            "ChangeTelegram" or "change" or "change-telegram" => "ChangeTelegram",
            "StaffLogin" or "staff" or "staff-login" or "panel" => "StaffLogin",
            _ => "Login"
        };
}
