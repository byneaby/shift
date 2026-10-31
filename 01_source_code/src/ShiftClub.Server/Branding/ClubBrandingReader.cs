using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Branding;
using ShiftClub.Shared.Contracts.Settings;

namespace ShiftClub.Server.Branding;

/// <summary>
/// One branding read is reused for a minute. PCs and the panel ask once and cache the picture themselves.
/// </summary>
internal static class ClubBrandingReader
{
    private static readonly object Gate = new();
    private static ClientBrandingDto? Cached;
    private static long ExpiresAt;

    public static void Clear()
    {
        lock (Gate)
        {
            Cached = null;
            ExpiresAt = 0;
        }
    }

    public static async Task<ClientBrandingDto> GetAsync(
        string contentRoot,
        IClubSettingsService settings,
        CancellationToken cancellationToken)
    {
        var now = Environment.TickCount64;
        lock (Gate)
        {
            if (Cached is not null && ExpiresAt > now)
                return Cached;
        }

        var background = await settings.GetLoginBackgroundAsync(cancellationToken);
        var dto = ClubBrandingFiles.ToDto(contentRoot, background.Url);
        lock (Gate)
        {
            Cached = dto;
            ExpiresAt = now + 60_000;
        }

        return dto;
    }
}
