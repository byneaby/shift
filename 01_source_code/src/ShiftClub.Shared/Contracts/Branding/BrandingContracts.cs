namespace ShiftClub.Shared.Contracts.Branding;

/// <summary>
/// Как клуб называет себя сам. Раньше название было зашито в код («SHIFT»),
/// из-за чего систему нельзя было поставить другому клубу без правки исходников.
/// </summary>
public sealed record BrandingDto(
    string ClubName,
    string ShortName,
    string? LogoUrl,
    string AccentColor,
    string? LoginBackgroundUrl,
    string? WebsiteUrl,
    string? SupportContact,
    string TelegramSignature);

public sealed record UpdateBrandingRequest(
    string? ClubName,
    string? ShortName,
    string? LogoUrl,
    string? AccentColor,
    string? WebsiteUrl,
    string? SupportContact,
    string? TelegramSignature);
