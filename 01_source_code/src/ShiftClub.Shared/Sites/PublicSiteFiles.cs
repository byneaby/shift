namespace ShiftClub.Shared.Sites;

public enum PublicSiteKind
{
    None = 0,
    Product = 1,
    Wiki = 2
}

/// <summary>
/// Public product landing and owner wiki. Served by host name so DNS A-records
/// can point at the same machine without opening the staff panel.
/// </summary>
public static class PublicSiteFiles
{
    public const string ProductHost = "product.shift-club.kz";
    public const string WikiHost = "wiki.shift-club.kz";

    public static PublicSiteKind KindForHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return PublicSiteKind.None;

        host = host.Trim().TrimEnd('.');
        if (IsHost(host, ProductHost))
            return PublicSiteKind.Product;
        if (IsHost(host, WikiHost))
            return PublicSiteKind.Wiki;
        return PublicSiteKind.None;
    }

    public static string FolderName(PublicSiteKind kind) => kind switch
    {
        PublicSiteKind.Product => "product",
        PublicSiteKind.Wiki => "guide",
        _ => ""
    };

    public static bool TryResolve(
        string siteRoot,
        string? requestPath,
        out string absoluteFile,
        out string contentType,
        out string cacheControl)
    {
        absoluteFile = "";
        contentType = "application/octet-stream";
        cacheControl = "no-store";

        if (string.IsNullOrWhiteSpace(siteRoot) || !Directory.Exists(siteRoot))
            return false;

        var root = Path.GetFullPath(siteRoot);
        var relative = (requestPath ?? "/").Replace('\\', '/');
        if (relative.Contains("..", StringComparison.Ordinal) || relative.Contains('\0'))
            return false;
        if (!relative.StartsWith('/'))
            relative = "/" + relative;

        var trimmed = relative.Trim('/');
        var candidate = root;
        if (trimmed.Length > 0)
        {
            foreach (var part in trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part is "." or ".." || part.StartsWith('.'))
                    return false;
                candidate = Path.Combine(candidate, part);
            }
        }
        else
        {
            candidate = Path.Combine(root, "index.html");
        }

        var full = Path.GetFullPath(candidate);
        if (!IsInside(root, full))
            return false;

        if (Directory.Exists(full))
            full = Path.Combine(full, "index.html");

        if (!File.Exists(full) && !Path.HasExtension(full))
        {
            var html = full + ".html";
            if (File.Exists(html))
                full = html;
        }

        full = Path.GetFullPath(full);
        if (!IsInside(root, full) || !File.Exists(full))
            return false;

        absoluteFile = full;
        var extension = Path.GetExtension(full);
        contentType = ContentType(extension);
        cacheControl = extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            ? "no-cache, no-store, must-revalidate"
            : "public, max-age=300";
        return true;
    }

    public static string ContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".txt" => "text/plain; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream"
    };

    private static bool IsHost(string host, string name) =>
        host.Equals(name, StringComparison.OrdinalIgnoreCase)
        || host.Equals("www." + name, StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string root, string full)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
               || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
