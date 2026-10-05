namespace ShiftClub.Shared.Contracts.Settings;

public sealed record ShellAdminPasswordStatusDto(bool IsConfigured);

public sealed record SetShellAdminPasswordRequest(string Password);

public sealed record VerifyShellAdminPasswordRequest(string Password);

public sealed record LoginBackgroundDto(string? Url);

/// <summary>
/// Оформление для Shell на клиентском ПК. Поле LoginBackgroundUrl остаётся первым
/// и под тем же именем: старые версии Shell читают только его.
/// </summary>
public sealed record ClientBrandingDto(
    string? LoginBackgroundUrl,
    string ClubName,
    string ShortName,
    string? LogoUrl,
    string AccentColor,
    string? SupportContact);
