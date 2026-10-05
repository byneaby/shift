using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Branding;
using ShiftClub.Shared.Contracts.Licensing;
using ShiftClub.Shared.Contracts.Setup;

namespace ShiftClub.Infrastructure.Services;

/// <summary>
/// Чек-лист первого запуска в новом клубе.
/// Смысл не в красивом экране, а в том, чтобы клуб не остался работать
/// с чужим названием, паролем из поставки и без копий базы — а чтобы это
/// было видно сразу, а не выяснилось через месяц.
/// </summary>
public sealed class SetupService : ISetupService
{
    public const string CompletedKey = "setup.completed";

    private const int MinOwnerPasswordLength = 10;

    private readonly ShiftClubDbContext _db;
    private readonly IBrandingService _branding;
    private readonly ILicenseService _license;
    private readonly IDatabaseBackupService _backups;
    private readonly IPasswordHasher _hasher;
    private readonly IConfiguration _config;
    private readonly ILogger<SetupService> _logger;

    public SetupService(
        ShiftClubDbContext db,
        IBrandingService branding,
        ILicenseService license,
        IDatabaseBackupService backups,
        IPasswordHasher hasher,
        IConfiguration config,
        ILogger<SetupService> logger)
    {
        _db = db;
        _branding = branding;
        _license = license;
        _backups = backups;
        _hasher = hasher;
        _config = config;
        _logger = logger;
    }

    public async Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var completed = await _db.AppSettings.AsNoTracking()
            .AnyAsync(s => s.Key == CompletedKey && s.Value == "true", cancellationToken);

        var branding = await _branding.GetAsync(cancellationToken);
        var brandingDone = await IsBrandingConfiguredAsync(branding, cancellationToken);

        var ownerPasswordChanged = await IsOwnerPasswordChangedAsync(cancellationToken);

        var license = await _license.GetStatusAsync(cancellationToken);
        var licenseRequired = _config.GetValue("License:Enforce", false);

        var backups = await _backups.GetStatusAsync(cancellationToken);

        var computers = await _db.Computers.AsNoTracking().CountAsync(c => !c.IsDeleted, cancellationToken);
        var tariffs = await _db.Tariffs.AsNoTracking().CountAsync(t => t.IsActive, cancellationToken);

        var steps = new List<SetupStepDto>
        {
            new(
                "branding",
                "Название клуба",
                "Чтобы в панели, на табло и в сообщениях бота было имя вашего клуба",
                brandingDone,
                true),
            new(
                "owner-password",
                "Свой пароль владельца",
                "Пароль из поставки знают все — его нужно сменить до открытия смены",
                ownerPasswordChanged,
                true),
            new(
                "license",
                "Лицензионный ключ",
                "Без действующего ключа не стартуют сеансы и не регистрируются новые ПК",
                // При выключенной проверке (свой клуб, стенд) ключ не нужен,
                // и пункт не должен висеть невыполненным.
                license.State == LicenseState.Active || !licenseRequired,
                licenseRequired),
            new(
                "backups",
                "Копии базы",
                "pg_dump должен находиться, иначе копии не создаются",
                backups.Enabled && backups.ToolAvailable,
                true),
            new(
                "tariffs",
                "Тарифы",
                "Проверьте цены: в поставке лежит пример прайса, а не ваш",
                tariffs > 0,
                false),
            new(
                "computers",
                "Компьютеры подключены",
                "Shell на клиентских ПК должен зарегистрироваться на сервере",
                computers > 0,
                false)
        };

        var doneCount = steps.Count(s => s.Done);
        var required = steps.Any(s => s.Required && !s.Done);

        var summary = required
            ? $"Не готово: {string.Join(", ", steps.Where(s => s.Required && !s.Done).Select(s => s.Title.ToLowerInvariant()))}"
            : completed
                ? "Настройка завершена"
                : "Обязательные пункты выполнены — можно отметить настройку завершённой";

        return new SetupStatusDto(completed, required, doneCount, steps.Count, steps, summary);
    }

    public async Task<ApplySetupResultDto> ApplyAsync(
        ApplySetupRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var applied = new List<string>();
        var problems = new List<string>();

        await ApplyBranchAsync(request, applied, problems, cancellationToken);
        await ApplyBrandingAsync(request, employeeId, applied, problems, cancellationToken);
        await ApplyOwnerPasswordAsync(request, employeeId, applied, problems, cancellationToken);
        await ApplyLicenseAsync(request, employeeId, applied, problems, cancellationToken);

        if (request.MarkCompleted)
        {
            var status = await GetStatusAsync(cancellationToken);
            if (status.Required)
            {
                problems.Add("Остались обязательные пункты — настройка не отмечена завершённой.");
            }
            else
            {
                await MarkCompletedAsync(employeeId, cancellationToken);
                applied.Add("Настройка отмечена завершённой");
            }
        }

        if (applied.Count > 0)
            _logger.LogInformation("Setup applied: {Applied}", string.Join("; ", applied));

        return new ApplySetupResultDto(await GetStatusAsync(cancellationToken), applied, problems);
    }

    private async Task ApplyBranchAsync(
        ApplySetupRequest request,
        List<string> applied,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        var branch = await _db.Branches.OrderBy(b => b.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (branch is null)
            return;

        var changed = false;

        if (!string.IsNullOrWhiteSpace(request.ClubName) && request.ClubName.Trim() != branch.Name)
        {
            branch.Name = request.ClubName.Trim();
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(request.TimeZoneId))
        {
            var tz = request.TimeZoneId.Trim();
            if (TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _))
            {
                branch.TimeZoneId = tz;
                changed = true;
            }
            else
            {
                problems.Add($"Часовой пояс «{tz}» система не знает — оставлен прежний.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.CurrencyCode))
        {
            var currency = request.CurrencyCode.Trim().ToUpperInvariant();
            if (currency.Length == 3 && currency.All(char.IsAsciiLetterUpper))
            {
                branch.CurrencyCode = currency;
                changed = true;
            }
            else
            {
                problems.Add("Код валюты — три латинские буквы, например KZT.");
            }
        }

        if (request.Address is not null && request.Address.Trim() != branch.Address)
        {
            branch.Address = request.Address.Trim();
            changed = true;
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
            applied.Add("Данные клуба сохранены");
        }
    }

    private async Task ApplyBrandingAsync(
        ApplySetupRequest request,
        Guid employeeId,
        List<string> applied,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClubName) && string.IsNullOrWhiteSpace(request.ShortName))
            return;

        try
        {
            await _branding.UpdateAsync(
                new UpdateBrandingRequest(
                    request.ClubName,
                    request.ShortName,
                    null,
                    null,
                    null,
                    null,
                    request.ClubName),
                employeeId,
                cancellationToken);
            applied.Add("Название применено в панели, на табло и в боте");
        }
        catch (InvalidOperationException ex)
        {
            problems.Add(ex.Message);
        }
    }

    private async Task ApplyOwnerPasswordAsync(
        ApplySetupRequest request,
        Guid employeeId,
        List<string> applied,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerPassword))
            return;

        var password = request.OwnerPassword.Trim();
        if (password.Length < MinOwnerPasswordLength)
        {
            problems.Add($"Пароль владельца — минимум {MinOwnerPasswordLength} символов.");
            return;
        }

        var credential = await _db.EmployeeCredentials
            .FirstOrDefaultAsync(c => c.EmployeeId == employeeId, cancellationToken);

        if (credential is null)
        {
            problems.Add("Не нашли учётную запись для смены пароля.");
            return;
        }

        if (_hasher.Verify(password, credential.PasswordHash))
        {
            problems.Add("Это тот же пароль, что и сейчас. Придумайте другой.");
            return;
        }

        credential.PasswordHash = _hasher.Hash(password);
        credential.PasswordChangedAt = DateTimeOffset.UtcNow;
        credential.AccessFailedCount = 0;
        credential.LockoutEnd = null;
        credential.UpdatedBy = employeeId;
        credential.UpdatedAt = DateTimeOffset.UtcNow;

        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employeeId,
            Action = "setup.owner_password_changed",
            EntityType = nameof(Employee),
            EntityId = employeeId.ToString(),
            CreatedBy = employeeId
        });

        await _db.SaveChangesAsync(cancellationToken);
        applied.Add("Пароль владельца изменён");
    }

    private async Task ApplyLicenseAsync(
        ApplySetupRequest request,
        Guid employeeId,
        List<string> applied,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey))
            return;

        try
        {
            var status = await _license.SetKeyAsync(request.LicenseKey.Trim(), employeeId, cancellationToken);
            applied.Add($"Лицензия установлена: {status.ClubName ?? "—"}");
        }
        catch (InvalidOperationException ex)
        {
            problems.Add(ex.Message);
        }
    }

    private async Task MarkCompletedAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == CompletedKey && s.BranchId == null, cancellationToken);

        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = CompletedKey,
                Value = "true",
                Description = "Мастер первого запуска пройден",
                CreatedBy = employeeId,
                UpdatedBy = employeeId,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            setting.Value = "true";
            setting.UpdatedBy = employeeId;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Пункт выполнен, если у клуба есть своё название — сохранённое в панели
    /// или заданное установщиком (Seed:ClubName). Название из поставки
    /// выполненным пунктом не считается.
    /// </summary>
    private async Task<bool> IsBrandingConfiguredAsync(BrandingDto branding, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branding.ClubName)
            || branding.ClubName == BrandingService.FallbackClubName)
        {
            return false;
        }

        if (await _db.AppSettings.AsNoTracking()
                .AnyAsync(s => s.Key == BrandingService.SettingKey, cancellationToken))
        {
            return true;
        }

        return await _db.Branches.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(cancellationToken) == branding.ClubName;
    }

    /// <summary>
    /// Пароль из поставки узнаём по тому, что его ни разу не меняли:
    /// PasswordChangedAt у свежесозданной учётки совпадает с датой создания.
    /// </summary>
    private async Task<bool> IsOwnerPasswordChangedAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Employees.AsNoTracking()
            .Where(e => e.IsActive && e.EmployeeRoles.Any(r => r.Role!.Code == "owner"))
            .Select(e => new { e.CreatedAt, e.Credential!.PasswordChangedAt })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return false;

        return rows.Any(r =>
            r.PasswordChangedAt is { } changed
            && changed - r.CreatedAt > TimeSpan.FromMinutes(1));
    }
}
