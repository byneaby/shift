namespace ShiftClub.Shared.Contracts.Auth;

public sealed record LoginRequest(string Login, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    EmployeeDto Employee);

public sealed record EmployeeDto(
    Guid Id,
    string Login,
    string DisplayName,
    Guid? BranchId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record StaffTelegramTicketDto(
    Guid TicketId,
    string Code,
    string DeepLink,
    string QrPngBase64,
    DateTimeOffset ExpiresAt);

public sealed record StaffTelegramStartRequest(string ClientNonce);

public sealed record StaffTelegramTicketStatusDto(
    string Status,
    LoginResponse? Auth,
    string? Message);
