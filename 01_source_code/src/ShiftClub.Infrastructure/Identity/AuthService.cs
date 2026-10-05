using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Auth;

namespace ShiftClub.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private static readonly Regex DigitsOnly = new(@"\D+", RegexOptions.Compiled);

    private readonly ShiftClubDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;

    public AuthService(
        ShiftClubDbContext db,
        IPasswordHasher passwordHasher,
        IConfiguration configuration)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var login = request.Login.Trim();
        var phoneDigits = NormalizePhone(login);

        var query = _db.Employees
            .Include(e => e.Credential)
            .Include(e => e.EmployeeRoles)
                .ThenInclude(er => er.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .Where(e => e.IsActive);

        var employee = await query.FirstOrDefaultAsync(
            e => e.Login == login || (e.Phone != null && e.Phone == login),
            cancellationToken);

        if (employee is null && phoneDigits.Length >= 10)
        {
            var withPhone = await query.Where(e => e.Phone != null).ToListAsync(cancellationToken);
            employee = withPhone.FirstOrDefault(e => NormalizePhone(e.Phone!) == phoneDigits);
        }

        if (employee?.Credential is null)
            return null;

        if (employee.Credential.LockoutEnd is { } lockoutEnd && lockoutEnd > DateTimeOffset.UtcNow)
        {
            var mins = Math.Max(1, (int)Math.Ceiling((lockoutEnd - DateTimeOffset.UtcNow).TotalMinutes));
            throw new InvalidOperationException($"Аккаунт временно заблокирован. Повторите через {mins} мин.");
        }

        if (!_passwordHasher.Verify(request.Password, employee.Credential.PasswordHash))
        {
            employee.Credential.AccessFailedCount++;
            if (employee.Credential.AccessFailedCount >= 5)
                employee.Credential.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);

            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        employee.Credential.AccessFailedCount = 0;
        employee.Credential.LockoutEnd = null;
        return await IssueLoginAsync(employee, "auth.login", cancellationToken);
    }

    public async Task<LoginResponse?> LoginByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var employee = await LoadEmployeeAsync(employeeId, cancellationToken);
        if (employee is null || !employee.IsActive)
            return null;
        return await IssueLoginAsync(employee, "auth.login.telegram", cancellationToken);
    }

    public async Task ChangePasswordAsync(
        Guid employeeId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            throw new InvalidOperationException("Введите текущий пароль.");

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            throw new InvalidOperationException("Новый пароль должен быть не короче 6 символов.");

        if (request.CurrentPassword == request.NewPassword)
            throw new InvalidOperationException("Новый пароль должен отличаться от текущего.");

        var employee = await LoadEmployeeAsync(employeeId, cancellationToken)
                       ?? throw new KeyNotFoundException("Сотрудник не найден.");

        if (!employee.IsActive)
            throw new InvalidOperationException("Аккаунт отключён.");

        if (employee.Credential is null)
            throw new InvalidOperationException("У аккаунта нет пароля. Обратитесь к администратору.");

        if (!_passwordHasher.Verify(request.CurrentPassword, employee.Credential.PasswordHash))
            throw new InvalidOperationException("Неверный текущий пароль.");

        employee.Credential.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        employee.Credential.PasswordChangedAt = DateTimeOffset.UtcNow;
        employee.Credential.AccessFailedCount = 0;
        employee.Credential.LockoutEnd = null;
        employee.UpdatedAt = DateTimeOffset.UtcNow;

        _db.AuditLogs.Add(new Domain.Entities.AuditLog
        {
            BranchId = employee.BranchId,
            EmployeeId = employee.Id,
            Action = "auth.password.change",
            EntityType = nameof(Domain.Entities.Employee),
            EntityId = employee.Id.ToString(),
            DetailsJson = """{"self":true}"""
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LoginResponse?> LoginByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var employee = await _db.Employees
            .Include(e => e.Credential)
            .Include(e => e.EmployeeRoles)
                .ThenInclude(er => er.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(e => e.TelegramUserId == telegramUserId && e.IsActive, cancellationToken);

        if (employee is null)
        {
            // Allow-list fallback: Telegram ID привязан к сотруднику в настройках бота
            var stored = await _db.AppSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "telegram.settings", cancellationToken);
            if (stored?.Value is { } json)
            {
                try
                {
                    var cfg = System.Text.Json.JsonSerializer.Deserialize<Shared.Contracts.Settings.TelegramBotStoredSettings>(json);
                    var allowed = cfg?.AllowedUsers?.FirstOrDefault(u => u.TelegramUserId == telegramUserId && u.EmployeeId is not null);
                    if (allowed?.EmployeeId is { } eid)
                        employee = await LoadEmployeeAsync(eid, cancellationToken);
                }
                catch
                {
                    /* ignore */
                }
            }
        }

        if (employee is null || !employee.IsActive)
            return null;

        if (employee.TelegramUserId is null)
        {
            employee.TelegramUserId = telegramUserId;
            employee.TelegramLinkedAt = DateTimeOffset.UtcNow;
            employee.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return await IssueLoginAsync(employee, "auth.login.telegram", cancellationToken);
    }

    private async Task<Domain.Entities.Employee?> LoadEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        return await _db.Employees
            .Include(e => e.Credential)
            .Include(e => e.EmployeeRoles)
                .ThenInclude(er => er.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
    }

    private async Task<LoginResponse> IssueLoginAsync(
        Domain.Entities.Employee employee,
        string auditAction,
        CancellationToken cancellationToken)
    {
        employee.LastLoginAt = DateTimeOffset.UtcNow;

        _db.AuditLogs.Add(new Domain.Entities.AuditLog
        {
            BranchId = employee.BranchId,
            EmployeeId = employee.Id,
            Action = auditAction,
            EntityType = nameof(Domain.Entities.Employee),
            EntityId = employee.Id.ToString(),
            DetailsJson = """{"success":true}"""
        });

        await _db.SaveChangesAsync(cancellationToken);

        var roles = employee.EmployeeRoles.Select(x => x.Role.Code).Distinct().ToList();
        var permissions = employee.EmployeeRoles
            .SelectMany(x => x.Role.RolePermissions)
            .Select(x => x.Permission.Code)
            .Distinct()
            .ToList();

        // Staff panel JWT lifetime (PC/client guest tokens are separate, see ClientLauncherService).
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(
            _configuration.GetValue("Jwt:ExpirationMinutes", 720));

        var token = CreateToken(employee.Id, employee.Login, roles, permissions, expiresAt);

        return new LoginResponse(
            token,
            expiresAt,
            new EmployeeDto(employee.Id, employee.Login, employee.DisplayName, employee.BranchId, roles, permissions));
    }

    private string CreateToken(
        Guid employeeId,
        string login,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        DateTimeOffset expiresAt)
    {
        var key = _configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured. Set it via user-secrets.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, employeeId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, login),
            new("employee_id", employeeId.ToString())
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim("permission", p)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "ShiftClub",
            audience: _configuration["Jwt:Audience"] ?? "ShiftClub.Web",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static string NormalizePhone(string raw)
    {
        var digits = DigitsOnly.Replace(raw ?? "", "");
        if (digits.Length >= 10 && digits.StartsWith('8'))
            digits = "7" + digits[1..];
        return digits;
    }
}
