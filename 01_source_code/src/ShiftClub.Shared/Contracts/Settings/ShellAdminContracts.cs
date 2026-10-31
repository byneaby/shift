namespace ShiftClub.Shared.Contracts.Settings;

public sealed record ShellAdminPasswordStatusDto(bool IsConfigured);

public sealed record SetShellAdminPasswordRequest(string Password);

public sealed record VerifyShellAdminPasswordRequest(string Password);

public sealed record LoginBackgroundDto(string? Url);

public sealed record ClientBrandingDto(string? LoginBackgroundUrl, string Name, string? LogoUrl);

public sealed record UpdateClubBrandingRequest(string? Name);
