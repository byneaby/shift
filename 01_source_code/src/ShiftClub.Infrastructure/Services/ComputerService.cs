using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Computers;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Infrastructure.SignalR;
using ShiftClub.Shared.Contracts.Computers;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared.SignalR;

namespace ShiftClub.Infrastructure.Services;

public sealed class ComputerService : IComputerService
{
    private readonly ShiftClubDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly IHubContext<ComputerHub> _computerHub;
    private readonly ILicenseService _license;

    public ComputerService(
        ShiftClubDbContext db,
        IPasswordHasher hasher,
        IHubContext<StaffHub> staffHub,
        IHubContext<ComputerHub> computerHub,
        ILicenseService license)
    {
        _db = db;
        _hasher = hasher;
        _staffHub = staffHub;
        _computerHub = computerHub;
        _license = license;
    }

    public async Task<RegisterComputerResponse> RegisterAsync(
        RegisterComputerRequest request,
        Guid? defaultBranchId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.InstallationId))
            throw new InvalidOperationException("InstallationId is required.");

        var mac = NormalizeMac(request.MacAddress)
            ?? throw new InvalidOperationException("MAC-Ð°Ð´Ñ€ÐµÑ Ð¾Ð±ÑÐ·Ð°Ñ‚ÐµÐ»ÐµÐ½ Ð´Ð»Ñ Ñ€ÐµÐ³Ð¸ÑÑ‚Ñ€Ð°Ñ†Ð¸Ð¸ ÐŸÐš.");

        var branchId = defaultBranchId
            ?? await _db.Branches.Select(b => (Guid?)b.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No branch configured.");

        var installationId = request.InstallationId.Trim();

        // Ð“Ð»Ð°Ð²Ð½Ñ‹Ð¹ ÐºÐ»ÑŽÑ‡ â€” MAC (ÐºÐ°Ðº CCboot). InstallationId Ñ‚Ð¾Ð»ÑŒÐºÐ¾ Ð²ÑÐ¿Ð¾Ð¼Ð¾Ð³Ð°Ñ‚ÐµÐ»ÑŒÐ½Ñ‹Ð¹.
        var byMac = await _db.Computers
            .FirstOrDefaultAsync(c => c.MacAddress == mac && !c.IsDeleted, cancellationToken);

        if (byMac is null)
        {
            byMac = await _db.Computers
                .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
                .FirstOrDefaultAsync(c => c.MacAddress == mac && c.IsDeleted, cancellationToken);
            if (byMac is not null)
                byMac.IsDeleted = false;
        }

        var byInstall = await _db.Computers
            .FirstOrDefaultAsync(c => c.InstallationId == installationId && !c.IsDeleted, cancellationToken);

        Computer? existing;
        if (byMac is not null && byInstall is not null && byMac.Id != byInstall.Id)
        {
            // MAC Ð¿Ð¾Ð±ÐµÐ¶Ð´Ð°ÐµÑ‚. Ð¡Ñ‚Ð°Ñ€ÑƒÑŽ Ð·Ð°Ð¿Ð¸ÑÑŒ Ñ Ñ‡ÑƒÐ¶Ð¸Ð¼ InstallationId Ð½Ðµ Ñ‚Ñ€Ð¾Ð³Ð°ÐµÐ¼ ÐºÐ°Ðº Â«Ð¶Ð¸Ð²Ð¾Ð¹Â» ÐŸÐš â€”
            // Ñ‚Ð¾Ð»ÑŒÐºÐ¾ Ð¾ÑÐ²Ð¾Ð±Ð¾Ð¶Ð´Ð°ÐµÐ¼ ÑƒÐ½Ð¸ÐºÐ°Ð»ÑŒÐ½Ñ‹Ð¹ InstallationId, ÐµÑÐ»Ð¸ Ð¾Ð½ ÑÐ¾Ð²Ð¿Ð°Ð».
            if (string.Equals(byInstall.MacAddress, mac, StringComparison.OrdinalIgnoreCase))
            {
                // same MAC somehow on two rows â€” remove duplicate install row
                if (await CanHardRemoveAsync(byInstall.Id, cancellationToken))
                {
                    await RemoveComputerDependenciesAsync(byInstall.Id, cancellationToken);
                    _db.Computers.Remove(byInstall);
                }
                else
                {
                    byInstall.IsDeleted = true;
                    byInstall.InstallationId = $"deleted-{byInstall.Id:N}";
                }
            }
            else
            {
                // Shared InstallationId from a cloned CCBoot image: keep both seats, free the id on the other.
                byInstall.InstallationId = $"moved-{byInstall.Id:N}";
            }

            existing = byMac;
        }
        else if (byMac is not null)
        {
            existing = byMac;
        }
        else if (byInstall is not null)
        {
            var installMac = NormalizeMac(byInstall.MacAddress);
            // Reuse InstallationId row ONLY when MAC matches (or old row has no MAC yet).
            // Never let PC-B with a new MAC overwrite PC-A just because they share state.json.
            if (installMac is null || string.Equals(installMac, mac, StringComparison.OrdinalIgnoreCase))
            {
                existing = byInstall;
            }
            else
            {
                byInstall.InstallationId = $"moved-{byInstall.Id:N}";
                existing = null;
            }
        }
        else
        {
            existing = null;
        }

        if (existing is null)
        {
            // Лимит ПК и срок лицензии проверяем только для нового места.
            // Уже зарегистрированный ПК переподключается всегда — иначе зал встанет из-за просроченного ключа.
            await _license.EnsureCanRegisterComputerAsync(cancellationToken);

            existing = new Computer
            {
                BranchId = branchId,
                InstallationId = installationId,
                MacAddress = mac,
                Status = ComputerStatus.PendingApproval,
                IsApproved = false
            };
            _db.Computers.Add(existing);
        }

        // InstallationId ÑƒÐ½Ð¸ÐºÐ°Ð»ÐµÐ½: ÐµÑÐ»Ð¸ Ð·Ð°Ð½ÑÑ‚ Ð´Ñ€ÑƒÐ³Ð¾Ð¹ Ð·Ð°Ð¿Ð¸ÑÑŒÑŽ â€” Ð¾ÑÐ²Ð¾Ð±Ð¾Ð¶Ð´Ð°ÐµÐ¼.
        var installClash = await _db.Computers
            .FirstOrDefaultAsync(
                c => c.InstallationId == installationId && c.Id != existing.Id && !c.IsDeleted,
                cancellationToken);
        if (installClash is not null)
            installClash.InstallationId = $"moved-{installClash.Id:N}";

        existing.InstallationId = installationId;
        existing.WindowsName = string.IsNullOrWhiteSpace(request.WindowsName)
            ? existing.WindowsName
            : request.WindowsName.Trim();
        existing.IpAddress = request.IpAddress;
        existing.MacAddress = mac;
        existing.ClientVersion = request.ClientVersion;
        existing.WindowsVersion = request.WindowsVersion;
        existing.CpuName = request.CpuName;
        existing.GpuName = request.GpuName;
        existing.RamMb = request.RamMb;
        existing.ScreenResolution = request.ScreenResolution;
        existing.RegistrationCode = null;
        existing.RegistrationCodeExpiresAt = null;
        existing.LastSeenAt = DateTimeOffset.UtcNow;

        string? deviceToken = null;
        if (existing.IsApproved)
        {
            // Ð£Ð¶Ðµ Ð¿Ð¾Ð´Ñ‚Ð²ÐµÑ€Ð¶Ð´Ñ‘Ð½ Ð¿Ð¾ MAC â€” ÑÑ€Ð°Ð·Ñƒ Ð²Ñ‹Ð´Ð°Ñ‘Ð¼ Ñ‚Ð¾ÐºÐµÐ½ (ÑƒÐ´Ð¾Ð±Ð½Ð¾ Ð´Ð»Ñ CCboot / wipe Ð¾Ð±Ñ€Ð°Ð·Ð°).
            deviceToken = IssueDeviceToken(existing);
            existing.Status = ComputerStatus.Offline;
        }
        else
        {
            existing.IsApproved = false;
            existing.Status = ComputerStatus.PendingApproval;
            existing.DeviceTokenHash = null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            ToStatusEvent(existing),
            cancellationToken);

        return new RegisterComputerResponse(
            existing.Id,
            existing.IsApproved,
            deviceToken,
            existing.DisplayName ?? existing.WindowsName,
            existing.MacAddress);
    }

    public async Task<ApproveComputerResponse> ApproveAsync(
        Guid computerId,
        ApproveComputerRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("Computer not found.");

        if (string.IsNullOrWhiteSpace(computer.MacAddress))
            throw new InvalidOperationException("Ð£ ÐŸÐš Ð½ÐµÑ‚ MAC-Ð°Ð´Ñ€ÐµÑÐ°. ÐŸÐµÑ€ÐµÐ·Ð°Ð¿ÑƒÑÑ‚Ð¸Ñ‚Ðµ ÐºÐ»Ð¸ÐµÐ½Ñ‚.");

        var zone = await _db.Zones.FirstOrDefaultAsync(
            z => z.Id == request.ZoneId && z.BranchId == computer.BranchId,
            cancellationToken)
            ?? throw new InvalidOperationException("Zone not found in computer branch.");

        computer.DisplayName = request.DisplayName.Trim();
        computer.ZoneId = zone.Id;
        computer.MapX = request.MapX ?? computer.MapX ?? Random.Shared.Next(40, 700);
        computer.MapY = request.MapY ?? computer.MapY ?? Random.Shared.Next(40, 420);
        computer.IsApproved = true;
        computer.Status = ComputerStatus.Offline;
        computer.RegistrationCode = null;
        computer.RegistrationCodeExpiresAt = null;
        computer.UpdatedAt = DateTimeOffset.UtcNow;
        computer.UpdatedBy = employeeId;

        // Ð¡Ñ€Ð°Ð·Ñƒ Ð²Ñ‹Ð´Ð°Ñ‘Ð¼ Ñ‚Ð¾ÐºÐµÐ½ â€” ÐºÐ»Ð¸ÐµÐ½Ñ‚ Ð¿Ð¾Ð´Ñ…Ð²Ð°Ñ‚Ð¸Ñ‚ Ð¿Ñ€Ð¸ ÑÐ»ÐµÐ´ÑƒÑŽÑ‰ÐµÐ¼ register Ð¿Ð¾ MAC (ÐºÐ°Ð¶Ð´Ñ‹Ðµ ~2 Ñ).
        var deviceToken = IssueDeviceToken(computer);

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = computer.BranchId,
            EmployeeId = employeeId,
            Action = "computer.approve",
            EntityType = nameof(Computer),
            EntityId = computer.Id.ToString(),
            DetailsJson = $"{{\"displayName\":\"{computer.DisplayName}\",\"mac\":\"{computer.MacAddress}\"}}"
        });

        await _db.SaveChangesAsync(cancellationToken);

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerApproved,
            Map(computer),
            cancellationToken);

        return new ApproveComputerResponse(computer.Id, computer.DisplayName!, deviceToken);
    }

    public async Task<ComputerDto> CreateManualStationAsync(
        CreateManualStationRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var name = (request.DisplayName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Укажите название станции (например PS5-1).");

        var kind = request.StationKind == default ? StationKind.Console : request.StationKind;
        if (kind == StationKind.Pc)
            throw new InvalidOperationException("Ручное создание только для консолей. ПК регистрируются через Shell.");

        await _license.EnsureCanRegisterComputerAsync(cancellationToken);

        var branchId = request.BranchId
            ?? await _db.Branches.AsNoTracking().Select(b => b.Id).FirstOrDefaultAsync(cancellationToken);
        if (branchId == Guid.Empty)
            throw new InvalidOperationException("Филиал не найден.");

        var zone = await _db.Zones.FirstOrDefaultAsync(
            z => z.Id == request.ZoneId && z.BranchId == branchId && z.IsActive,
            cancellationToken)
            ?? throw new InvalidOperationException("Зона не найдена в филиале.");

        var installId = $"manual-{kind.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}";
        var computer = new Computer
        {
            BranchId = branchId,
            ZoneId = zone.Id,
            DisplayName = name,
            WindowsName = name,
            InstallationId = installId,
            StationKind = kind,
            IsApproved = true,
            Status = ComputerStatus.Free,
            GridCol = request.GridCol,
            GridRow = request.GridRow,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? "Ручная станция · только учёт времени" : request.Notes.Trim(),
            LastSeenAt = DateTimeOffset.UtcNow,
            CreatedBy = employeeId,
            UpdatedBy = employeeId,
        };

        if (computer.GridCol.HasValue)
            computer.MapX = computer.GridCol.Value * 100.0 + 40;
        if (computer.GridRow.HasValue)
            computer.MapY = computer.GridRow.Value * 100.0 + 40;

        _db.Computers.Add(computer);
        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            Action = "computer.create_manual",
            EntityType = nameof(Computer),
            EntityId = computer.Id.ToString(),
            DetailsJson = $"{{\"displayName\":\"{computer.DisplayName}\",\"stationKind\":\"{kind}\",\"zoneId\":\"{zone.Id}\"}}"
        });

        await _db.SaveChangesAsync(cancellationToken);
        await _db.Entry(computer).Reference(c => c.Zone).LoadAsync(cancellationToken);

        var dto = Map(computer);
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            ToStatusEvent(computer),
            cancellationToken);
        return dto;
    }

    public async Task<IReadOnlyList<ComputerDto>> GetComputersAsync(
        Guid? branchId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Computers.AsNoTracking()
            .Include(c => c.Zone)
            .Include(c => c.CurrentSession)
            .Where(c => !c.IsDeleted)
            .AsQueryable();
        if (branchId.HasValue)
            query = query.Where(c => c.BranchId == branchId.Value);

        var list = await query
            .OrderBy(c => c.IsApproved ? 0 : 1)
            .ThenBy(c => c.DisplayName ?? c.WindowsName)
            .ToListAsync(cancellationToken);

        return list.Select(c => Map(c)).ToList();
    }

    public async Task<ComputerDto?> GetByIdAsync(Guid computerId, CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.AsNoTracking()
            .Include(c => c.Zone)
            .Include(c => c.CurrentSession)
            .FirstOrDefaultAsync(c => c.Id == computerId && !c.IsDeleted, cancellationToken);

        return computer is null ? null : Map(computer);
    }

    public async Task<ComputerDto> HeartbeatAsync(
        Guid computerId,
        ComputerHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.Include(c => c.Zone)
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("Computer not found.");

        if (!computer.IsApproved)
            throw new InvalidOperationException("Computer is not approved.");

        var statusBefore = computer.Status;
        var now = DateTimeOffset.UtcNow;
        computer.LastHeartbeatAt = now;
        computer.LastSeenAt = now;
        if (!string.IsNullOrWhiteSpace(request.IpAddress))
            computer.IpAddress = request.IpAddress;
        if (!string.IsNullOrWhiteSpace(request.ClientVersion))
            computer.ClientVersion = request.ClientVersion;

        if (computer.CurrentSessionId is not null)
        {
            var sessionStatus = await _db.GamingSessions.AsNoTracking()
                .Where(s => s.Id == computer.CurrentSessionId)
                .Select(s => (SessionStatus?)s.Status)
                .FirstOrDefaultAsync(cancellationToken);
            computer.Status = sessionStatus == SessionStatus.Paused
                ? ComputerStatus.Locked
                : ComputerStatus.InSession;
        }
        else if (computer.Status is ComputerStatus.Offline
                 or ComputerStatus.PendingApproval
                 or ComputerStatus.Error
                 or ComputerStatus.Updating)
            computer.Status = computer.IsMaintenance ? ComputerStatus.Maintenance : ComputerStatus.Free;

        _db.ComputerHeartbeats.Add(new ComputerHeartbeat
        {
            ComputerId = computer.Id,
            ReceivedAt = now,
            CpuLoadPercent = request.CpuLoadPercent,
            RamUsedPercent = request.RamUsedPercent,
            FreeDiskMb = request.FreeDiskMb,
            UptimeSeconds = request.UptimeSeconds,
            IpAddress = request.IpAddress,
            ClientVersion = request.ClientVersion,
            ShellRunning = request.ShellRunning,
            StatusNote = request.StatusNote
        });

        // keep only recent heartbeats
        var cutoff = now.AddHours(-24);
        var old = await _db.ComputerHeartbeats
            .Where(h => h.ComputerId == computer.Id && h.ReceivedAt < cutoff)
            .Take(200)
            .ToListAsync(cancellationToken);
        if (old.Count > 0)
            _db.ComputerHeartbeats.RemoveRange(old);

        await _db.SaveChangesAsync(cancellationToken);

        var dto = Map(computer);
        // Не спамим панель: heartbeat каждые N сек с каждого ПК → иначе карта зала постоянно refetch.
        if (computer.Status != statusBefore)
        {
            await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
                HubMethods.ComputerStatusChanged,
                ToStatusEvent(computer),
                cancellationToken);
        }

        return dto;
    }

    public async Task<ComputerDto> UpdateLayoutAsync(
        Guid computerId,
        UpdateComputerLayoutRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.Include(c => c.Zone)
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("Computer not found.");

        computer.MapX = request.MapX ?? computer.MapX;
        computer.MapY = request.MapY ?? computer.MapY;
        if (request.GridCol.HasValue || request.GridRow.HasValue
            || request.GridColSpan.HasValue || request.GridRowSpan.HasValue)
        {
            var branch = await _db.Branches.AsNoTracking()
                .Where(b => b.Id == computer.BranchId)
                .Select(b => new { b.FloorGridCols, b.FloorGridRows })
                .FirstOrDefaultAsync(cancellationToken);
            var cols = branch is null || branch.FloorGridCols <= 0 ? 24 : branch.FloorGridCols;
            var rows = branch is null || branch.FloorGridRows <= 0 ? 16 : branch.FloorGridRows;
            if (request.GridCol.HasValue)
                computer.GridCol = Math.Clamp(request.GridCol.Value, 0, cols - 1);
            if (request.GridRow.HasValue)
                computer.GridRow = Math.Clamp(request.GridRow.Value, 0, rows - 1);
            if (request.GridColSpan.HasValue)
                computer.GridColSpan = Math.Clamp(request.GridColSpan.Value, 1, cols);
            if (request.GridRowSpan.HasValue)
                computer.GridRowSpan = Math.Clamp(request.GridRowSpan.Value, 1, rows);
            // Keep span inside grid bounds from top-left.
            if (computer.GridCol.HasValue)
                computer.GridColSpan = Math.Min(computer.GridColSpan, cols - computer.GridCol.Value);
            if (computer.GridRow.HasValue)
                computer.GridRowSpan = Math.Min(computer.GridRowSpan, rows - computer.GridRow.Value);
            computer.GridColSpan = Math.Max(1, computer.GridColSpan);
            computer.GridRowSpan = Math.Max(1, computer.GridRowSpan);
            if (computer.GridCol.HasValue)
                computer.MapX = computer.GridCol.Value * 100.0 + 40;
            if (computer.GridRow.HasValue)
                computer.MapY = computer.GridRow.Value * 100.0 + 40;
        }
        computer.UpdatedAt = DateTimeOffset.UtcNow;
        computer.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);

        var dto = Map(computer);
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            ToStatusEvent(computer),
            cancellationToken);
        return dto;
    }

    public async Task<ComputerCommandDto> SendCommandAsync(
        Guid computerId,
        SendComputerCommandRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("Computer not found.");

        if (!computer.IsApproved)
            throw new InvalidOperationException("Computer is not approved.");

        // Консоли без агента: сеансовый Unlock/Lock и т.п. просто игнорируем.
        if (computer.StationKind == StationKind.Console)
        {
            return new ComputerCommandDto(
                Guid.Empty,
                computer.Id,
                request.Type,
                ComputerCommandStatus.Completed,
                request.PayloadJson,
                DateTimeOffset.UtcNow,
                null,
                null,
                "Консоль: команда не отправлялась (только ручной учёт)");
        }

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.ComputerCommands
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
                return MapCommand(existing);
        }

        var command = new ComputerCommand
        {
            ComputerId = computer.Id,
            Type = request.Type,
            Status = ComputerCommandStatus.Created,
            InitiatedByEmployeeId = employeeId == Guid.Empty ? null : employeeId,
            PayloadJson = request.PayloadJson,
            IdempotencyKey = request.IdempotencyKey,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };

        _db.ComputerCommands.Add(command);
        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = computer.BranchId,
            EmployeeId = employeeId == Guid.Empty ? null : employeeId,
            Action = "computer.command",
            EntityType = nameof(ComputerCommand),
            EntityId = command.Id.ToString(),
            DetailsJson = $"{{\"type\":\"{request.Type}\",\"computerId\":\"{computer.Id}\"}}"
        });

        await _db.SaveChangesAsync(cancellationToken);

        command.Status = ComputerCommandStatus.Sent;
        command.SentAt = DateTimeOffset.UtcNow;
        command.AttemptCount = 1;
        await _db.SaveChangesAsync(cancellationToken);

        var dto = MapCommand(command);
        await _computerHub.Clients.Group(HubGroups.Computer(computer.Id))
            .SendAsync(HubMethods.ComputerCommand, dto, cancellationToken);

        if (request.Type == ComputerCommandType.Lock)
        {
            if (computer.CurrentSessionId is null)
                computer.Status = ComputerStatus.Locked;
        }
        else if (request.Type == ComputerCommandType.Unlock)
        {
            if (computer.CurrentSessionId is null)
                computer.Status = ComputerStatus.Free;
        }
        else if (request.Type == ComputerCommandType.EnterMaintenance)
        {
            computer.IsMaintenance = true;
            computer.Status = ComputerStatus.Maintenance;
        }
        else if (request.Type == ComputerCommandType.UpdateClient)
        {
            computer.Status = ComputerStatus.Updating;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            ToStatusEvent(computer),
            cancellationToken);

        return dto;
    }

    public async Task<IReadOnlyList<ComputerCommandDto>> GetPendingCommandsAsync(
        Guid computerId,
        CancellationToken cancellationToken = default)
    {
        var commands = await _db.ComputerCommands.AsNoTracking()
            .Where(c => c.ComputerId == computerId
                        && (c.Status == ComputerCommandStatus.Created || c.Status == ComputerCommandStatus.Sent)
                        && (c.ExpiresAt == null || c.ExpiresAt > DateTimeOffset.UtcNow))
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return commands.Select(MapCommand).ToList();
    }

    public async Task<ComputerCommandDto?> GetCommandAsync(
        Guid computerId,
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        var command = await _db.ComputerCommands.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == commandId && c.ComputerId == computerId, cancellationToken);
        return command is null ? null : MapCommand(command);
    }

    public async Task MarkCommandStatusAsync(
        Guid commandId,
        Guid computerId,
        ComputerCommandStatus status,
        string? error,
        string? resultJson = null,
        CancellationToken cancellationToken = default)
    {
        var command = await _db.ComputerCommands
            .FirstOrDefaultAsync(c => c.Id == commandId && c.ComputerId == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("Command not found.");

        command.Status = status;
        command.ErrorMessage = error;
        if (resultJson is not null)
            command.ResultJson = resultJson;
        if (status == ComputerCommandStatus.Received)
            command.ReceivedAt = DateTimeOffset.UtcNow;
        if (status == ComputerCommandStatus.Executing)
            command.ExecutedAt = DateTimeOffset.UtcNow;
        if (status is ComputerCommandStatus.Completed or ComputerCommandStatus.Failed)
            command.CompletedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        if (status is ComputerCommandStatus.Completed or ComputerCommandStatus.Failed)
        {
            await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
                HubMethods.ComputerCommandUpdated,
                MapCommand(command),
                cancellationToken);
        }
    }

    public async Task<Guid?> ResolveComputerIdByDeviceTokenAsync(
        string deviceToken,
        CancellationToken cancellationToken = default)
    {
        var computers = await _db.Computers.AsNoTracking()
            .Where(c => c.IsApproved && c.DeviceTokenHash != null)
            .Select(c => new { c.Id, c.DeviceTokenHash })
            .ToListAsync(cancellationToken);

        foreach (var computer in computers)
        {
            if (computer.DeviceTokenHash is not null && _hasher.Verify(deviceToken, computer.DeviceTokenHash))
                return computer.Id;
        }

        return null;
    }

    public async Task MarkStaleComputersOfflineAsync(CancellationToken cancellationToken = default)
    {
        var threshold = DateTimeOffset.UtcNow.AddSeconds(-45);
        var stale = await _db.Computers
            .Where(c => c.IsApproved
                        && c.StationKind == StationKind.Pc
                        && c.Status != ComputerStatus.Offline
                        && c.Status != ComputerStatus.Maintenance
                        && c.Status != ComputerStatus.Reserved
                        && (c.LastHeartbeatAt == null || c.LastHeartbeatAt < threshold))
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
            return;

        foreach (var computer in stale)
        {
            // Ð—Ð°Ð½ÑÑ‚Ð¾ÑÑ‚ÑŒ ÑÐµÐ°Ð½ÑÐ° Ð²Ð°Ð¶Ð½ÐµÐµ Ð¾Ñ‚ÑÑƒÑ‚ÑÑ‚Ð²Ð¸Ñ heartbeat: Ð½Ðµ ÑÐ±Ñ€Ð°ÑÑ‹Ð²Ð°ÐµÐ¼ InSession/Locked Ð² Offline.
            if (computer.CurrentSessionId is not null)
                continue;

            computer.Status = ComputerStatus.Offline;
            await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
                HubMethods.ComputerStatusChanged,
                ToStatusEvent(computer),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid computerId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.FirstOrDefaultAsync(c => c.Id == computerId && !c.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException("Computer not found.");

        if (computer.CurrentSessionId is not null)
        {
            var session = await _db.GamingSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == computer.CurrentSessionId, cancellationToken);
            if (session is { Status: SessionStatus.Active or SessionStatus.Paused })
                throw new InvalidOperationException("Ð¡Ð½Ð°Ñ‡Ð°Ð»Ð° Ð·Ð°Ð²ÐµÑ€ÑˆÐ¸Ñ‚Ðµ Ð°ÐºÑ‚Ð¸Ð²Ð½Ñ‹Ð¹ ÑÐµÐ°Ð½Ñ Ð½Ð° ÑÑ‚Ð¾Ð¼ ÐŸÐš.");
        }

        var activeBooking = await _db.BookingComputers
            .AnyAsync(
                bc => bc.ComputerId == computerId
                      && (bc.Booking.Status == BookingStatus.Pending
                          || bc.Booking.Status == BookingStatus.Confirmed
                          || bc.Booking.Status == BookingStatus.Arrived
                          || bc.Booking.Status == BookingStatus.Active),
                cancellationToken);
        if (activeBooking)
            throw new InvalidOperationException("ÐŸÐš ÑƒÑ‡Ð°ÑÑ‚Ð²ÑƒÐµÑ‚ Ð² Ð°ÐºÑ‚Ð¸Ð²Ð½Ð¾Ð¹ Ð±Ñ€Ð¾Ð½Ð¸. Ð¡Ð½Ð°Ñ‡Ð°Ð»Ð° Ð¾Ñ‚Ð¼ÐµÐ½Ð¸Ñ‚Ðµ Ð±Ñ€Ð¾Ð½ÑŒ.");

        var heartbeats = await _db.ComputerHeartbeats.Where(h => h.ComputerId == computerId).ToListAsync(cancellationToken);
        _db.ComputerHeartbeats.RemoveRange(heartbeats);
        var commands = await _db.ComputerCommands.Where(c => c.ComputerId == computerId).ToListAsync(cancellationToken);
        _db.ComputerCommands.RemoveRange(commands);

        computer.IsDeleted = true;
        computer.IsApproved = false;
        computer.DeviceTokenHash = null;
        computer.RegistrationCode = null;
        computer.RegistrationCodeExpiresAt = null;
        computer.CurrentSessionId = null;
        computer.Status = ComputerStatus.Offline;
        computer.UpdatedAt = DateTimeOffset.UtcNow;
        computer.UpdatedBy = employeeId;

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = computer.BranchId,
            EmployeeId = employeeId,
            Action = "computer.delete",
            EntityType = nameof(Computer),
            EntityId = computer.Id.ToString(),
            DetailsJson = $"{{\"windowsName\":\"{computer.WindowsName}\",\"mac\":\"{computer.MacAddress}\"}}"
        });

        await _db.SaveChangesAsync(cancellationToken);

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            new ComputerStatusChangedEvent(computerId, ComputerStatus.Offline, DateTimeOffset.UtcNow, computer.DisplayName),
            cancellationToken);
    }

    public async Task<ComputerDto> RevokeAsync(Guid computerId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.Include(c => c.Zone)
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("Computer not found.");

        if (computer.CurrentSessionId is not null)
        {
            var session = await _db.GamingSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == computer.CurrentSessionId, cancellationToken);
            if (session is { Status: SessionStatus.Active or SessionStatus.Paused })
                throw new InvalidOperationException("Ð¡Ð½Ð°Ñ‡Ð°Ð»Ð° Ð·Ð°Ð²ÐµÑ€ÑˆÐ¸Ñ‚Ðµ Ð°ÐºÑ‚Ð¸Ð²Ð½Ñ‹Ð¹ ÑÐµÐ°Ð½Ñ Ð½Ð° ÑÑ‚Ð¾Ð¼ ÐŸÐš.");
        }

        computer.DeviceTokenHash = null;
        computer.RegistrationCode = null;
        computer.RegistrationCodeExpiresAt = null;
        computer.IsApproved = false;
        computer.Status = ComputerStatus.PendingApproval;
        computer.CurrentSessionId = null;
        computer.UpdatedAt = DateTimeOffset.UtcNow;
        computer.UpdatedBy = employeeId;

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = computer.BranchId,
            EmployeeId = employeeId,
            Action = "computer.revoke",
            EntityType = nameof(Computer),
            EntityId = computer.Id.ToString(),
            DetailsJson = null
        });

        await _db.SaveChangesAsync(cancellationToken);

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            ToStatusEvent(computer),
            cancellationToken);

        return Map(computer);
    }

    public async Task<ComputerDto> UpdateAsync(
        Guid computerId,
        UpdateComputerRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.Include(c => c.Zone)
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("Computer not found.");

        if (request.DisplayName is not null)
            computer.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();

        if (request.ZoneId is not null)
        {
            var zone = await _db.Zones.FirstOrDefaultAsync(
                z => z.Id == request.ZoneId && z.BranchId == computer.BranchId,
                cancellationToken)
                ?? throw new InvalidOperationException("Zone not found in computer branch.");
            computer.ZoneId = zone.Id;
            computer.Zone = zone;
        }

        if (request.IsMaintenance is not null)
        {
            computer.IsMaintenance = request.IsMaintenance.Value;
            if (computer.IsMaintenance)
                computer.Status = ComputerStatus.Maintenance;
            else if (computer.IsApproved && computer.Status == ComputerStatus.Maintenance)
                computer.Status = ComputerStatus.Offline;
        }

        if (request.Notes is not null)
            computer.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        computer.UpdatedAt = DateTimeOffset.UtcNow;
        computer.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerStatusChanged,
            ToStatusEvent(computer),
            cancellationToken);

        return Map(computer);
    }

    public async Task<ComputerDto> WakeAsync(
        Guid computerId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var computer = await _db.Computers.Include(c => c.Zone)
            .FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken)
            ?? throw new KeyNotFoundException("Computer not found.");

        if (!computer.IsApproved)
            throw new InvalidOperationException("ПК не подтверждён.");

        if (computer.StationKind == StationKind.Console)
            throw new InvalidOperationException("Консоль (PS5) нельзя разбудить удалённо — только учёт времени с кассы.");

        // Разрешаем WOL даже при активном сеансе — полезно для переноса на выключенный ПК.

        var mac = NormalizeMac(computer.MacAddress)
            ?? throw new InvalidOperationException("Нет MAC-адреса у этого ПК. Перерегистрируйте клиент.");

        await Networking.WakeOnLanSender.SendAsync(mac, computer.IpAddress, cancellationToken);
        var ccbootName = await Networking.CcBootWake.TryWakeAsync(
            computer.DisplayName,
            computer.WindowsName,
            cancellationToken);

        _db.AuditLogs.Add(new AuditLog
        {
            BranchId = computer.BranchId,
            EmployeeId = employeeId == Guid.Empty ? null : employeeId,
            Action = "computer.wake",
            EntityType = nameof(Computer),
            EntityId = computer.Id.ToString(),
            DetailsJson =
                $"{{\"mac\":\"{mac}\",\"ip\":\"{computer.IpAddress}\",\"ccboot\":{(ccbootName is null ? "null" : $"\"{ccbootName}\"")}}}"
        });
        await _db.SaveChangesAsync(cancellationToken);

        return Map(computer);
    }

    private async Task<bool> CanHardRemoveAsync(Guid computerId, CancellationToken cancellationToken)
    {
        var hasSessions = await _db.GamingSessions.AsNoTracking()
            .AnyAsync(s => s.ComputerId == computerId, cancellationToken);
        return !hasSessions;
    }

    private async Task RemoveComputerDependenciesAsync(Guid computerId, CancellationToken cancellationToken)
    {
        var heartbeats = await _db.ComputerHeartbeats.Where(h => h.ComputerId == computerId).ToListAsync(cancellationToken);
        _db.ComputerHeartbeats.RemoveRange(heartbeats);

        var commands = await _db.ComputerCommands.Where(c => c.ComputerId == computerId).ToListAsync(cancellationToken);
        _db.ComputerCommands.RemoveRange(commands);

        var bookingLinks = await _db.BookingComputers.Where(b => b.ComputerId == computerId).ToListAsync(cancellationToken);
        _db.BookingComputers.RemoveRange(bookingLinks);

        await _db.BarOrders.Where(o => o.ComputerId == computerId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ComputerId, (Guid?)null), cancellationToken);

        await _db.Receipts.Where(r => r.ComputerId == computerId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ComputerId, (Guid?)null), cancellationToken);

        var computer = await _db.Computers.FirstOrDefaultAsync(c => c.Id == computerId, cancellationToken);
        if (computer is not null)
            computer.CurrentSessionId = null;
    }

    private string IssueDeviceToken(Computer computer)
    {
        var deviceToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        computer.DeviceTokenHash = _hasher.Hash(deviceToken);
        computer.UpdatedAt = DateTimeOffset.UtcNow;
        return deviceToken;
    }

    private static string? NormalizeMac(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var hex = new string(raw.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (hex.Length != 12)
            return null;

        return string.Join(':', Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    private static ComputerDto Map(Computer c)
    {
        int? remaining = null;
        if (c.CurrentSession is { Status: Shared.Enums.SessionStatus.Active, PlannedEndsAt: not null } session)
            remaining = Math.Max(0, (int)(session.PlannedEndsAt.Value - DateTimeOffset.UtcNow).TotalSeconds);
        else if (c.CurrentSession is { Status: Shared.Enums.SessionStatus.Paused } paused)
            remaining = paused.RemainingSecondsAtPause;

        var (occupancy, detail) = ComputerOccupancy.Resolve(c);

        return new ComputerDto(
            c.Id,
            c.BranchId,
            c.ZoneId,
            c.Zone?.Name,
            c.Zone?.ColorHex,
            c.DisplayName,
            c.WindowsName,
            c.IpAddress,
            c.MacAddress,
            c.MapX,
            c.MapY,
            c.Status,
            c.LastSeenAt,
            c.LastHeartbeatAt,
            c.IsApproved,
            c.RegistrationCode,
            c.ClientVersion,
            c.IsMaintenance,
            c.Notes,
            c.CurrentSessionId,
            remaining,
            c.CurrentSession?.TotalPrice,
            c.CurrentSession?.GuestName,
            c.CurrentSession?.Status,
            c.CurrentSession?.PaymentMethod,
            c.CurrentSession?.CustomerId,
            occupancy,
            detail,
            c.GridCol,
            c.GridRow,
            c.Zone?.Kind,
            c.Zone?.GridColumns,
            c.Zone?.GridRows,
            c.StationKind,
            Math.Max(1, c.GridColSpan),
            Math.Max(1, c.GridRowSpan));
    }

    private static ComputerCommandDto MapCommand(ComputerCommand c) => new(
        c.Id,
        c.ComputerId,
        c.Type,
        c.Status,
        c.PayloadJson,
        c.CreatedAt,
        c.ExpiresAt,
        c.ResultJson,
        c.ErrorMessage);

    private static ComputerStatusChangedEvent ToStatusEvent(Computer c) => new(
        c.Id,
        c.Status,
        c.LastSeenAt,
        c.DisplayName);
}
