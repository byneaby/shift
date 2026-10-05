using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace ShiftClub.Client.Shell;

/// <summary>
/// Shared frozen BitmapImage cache. Downloads via HttpClient + StreamSource
/// (WPF UriSource HTTP is unreliable on kiosk PCs and used to permanently cache failures).
/// </summary>
internal static class ShellImageCache
{
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ShiftClub.Shell");
        return c;
    }

    public static BitmapImage? Get(Uri uri, int decodePixelWidth = 420)
    {
        var key = uri.AbsoluteUri;
        if (Cache.TryGetValue(key, out var hit))
            return hit;

        try
        {
            // Sync download on UI thread is acceptable for catalog cards (few dozen, cached).
            using var resp = Http.GetAsync(uri).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
                return null;

            var bytes = resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (bytes.Length == 0)
                return null;

            // WPF cannot decode WebP — skip rather than poison cache.
            if (LooksLikeWebp(bytes))
                return null;

            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodePixelWidth > 0)
                bmp.DecodePixelWidth = decodePixelWidth;
            bmp.EndInit();
            bmp.Freeze();
            Cache[key] = bmp;
            return bmp;
        }
        catch
        {
            // Do not cache failures — next render / catalog reload can retry.
            return null;
        }
    }

    public static void Clear() => Cache.Clear();

    private static bool LooksLikeWebp(byte[] bytes) =>
        bytes.Length >= 12
        && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
        && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
}
