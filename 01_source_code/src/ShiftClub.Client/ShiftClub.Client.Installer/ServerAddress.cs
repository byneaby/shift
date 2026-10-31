namespace ShiftClub.Client.Installer;

/// <summary>
/// Accepts a domain, an IP, or a full http/https URL and stores an origin for Shell.
/// </summary>
internal static class ServerAddress
{
    public static bool TryNormalize(string? raw, out string url, out string error)
    {
        url = "";
        var text = (raw ?? "").Trim();
        if (text.Length == 0)
        {
            error = "Укажите домен или IP сервера. Например: shift-club.kz или 195.49.210.17";
            return false;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            if (text.Count(static c => c == ':') > 1 && !text.StartsWith('['))
                text = "[" + text + "]";
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "Не получилось разобрать адрес. Напишите домен или IP: shift-club.kz или 195.49.210.17";
            return false;
        }

        url = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        error = "";
        return true;
    }
}
