using ShiftClub.Shared.Contracts.Auth;

namespace ShiftClub.Application.Abstractions;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<LoginResponse?> LoginByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<LoginResponse?> LoginByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default);

    Task ChangePasswordAsync(Guid employeeId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
}
