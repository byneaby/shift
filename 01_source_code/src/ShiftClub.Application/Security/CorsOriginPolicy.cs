using System.Net;
using System.Net.Sockets;

namespace ShiftClub.Application.Security;

/// <summary>
/// Какие источники пускать в API.
/// Клуб работает в локальной сети и обращается к серверу по IP (192.168.x.x),
/// поэтому «локальную сеть» разрешаем всегда — иначе панель перестанет открываться
/// с кассы. А вот произвольный сайт из интернета обращаться к API клуба не должен:
/// иначе чужая страница в браузере кассира может дёргать ручки от его имени.
/// </summary>
public static class CorsOriginPolicy
{
    public const string AllowedOriginsKey = "Cors:AllowedOrigins";

    /// <summary>Значение в списке, которое означает «пускать кого угодно» (осознанный отказ от защиты).</summary>
    public const string AllowAll = "*";

    public static bool AllowsEverything(IReadOnlyCollection<string>? allowed) =>
        allowed is not null && allowed.Any(a => a.Trim() == AllowAll);

    public static bool IsAllowed(string? origin, IReadOnlyCollection<string>? allowed)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return false;

        if (AllowsEverything(allowed))
            return true;

        if (!Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        if (IsLocalNetwork(uri.Host))
            return true;

        if (allowed is null)
            return false;

        foreach (var raw in allowed)
        {
            var entry = raw?.Trim();
            if (string.IsNullOrEmpty(entry))
                continue;

            if (Matches(uri, entry))
                return true;
        }

        return false;
    }

    private static bool Matches(Uri origin, string entry)
    {
        // Запись вида https://panel.club.kz — сверяем схему, хост и порт целиком.
        if (Uri.TryCreate(entry, UriKind.Absolute, out var entryUri) && entryUri.Scheme is "http" or "https")
        {
            return string.Equals(origin.Scheme, entryUri.Scheme, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(origin.Host, entryUri.Host, StringComparison.OrdinalIgnoreCase)
                   && origin.Port == entryUri.Port;
        }

        // Запись вида *.club.kz — любой поддомен, но не сам club.kz и не чужой clubs.kz.
        if (entry.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = entry[1..];
            return origin.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                   && origin.Host.Length > suffix.Length;
        }

        // Запись вида panel.club.kz — только хост, схема и порт любые.
        return string.Equals(origin.Host, entry, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Локальная сеть клуба и сам сервер.</summary>
    public static bool IsLocalNetwork(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        // Имена вида shift-server.local раздаёт роутер в локальной сети.
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return true;

        var bare = host.Trim('[', ']');
        if (!IPAddress.TryParse(bare, out var ip))
            return false;

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] switch
            {
                10 => true,
                127 => true,
                169 when b[1] == 254 => true,
                172 when b[1] >= 16 && b[1] <= 31 => true,
                192 when b[1] == 168 => true,
                _ => false
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;

            // fc00::/7 — локальные адреса IPv6.
            return (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}
