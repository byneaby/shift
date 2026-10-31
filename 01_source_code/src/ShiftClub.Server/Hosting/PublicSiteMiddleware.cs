using ShiftClub.Shared.Sites;

namespace ShiftClub.Server.Hosting;

/// <summary>
/// Serves the product landing and the owner wiki on their own host names.
/// Those hosts never fall through to the staff panel or the license lock page.
/// </summary>
public sealed class PublicSiteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;

    public PublicSiteMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var kind = PublicSiteFiles.KindForHost(context.Request.Host.Host);
        if (kind == PublicSiteKind.None)
            return _next(context);

        var root = Path.Combine(_env.ContentRootPath, "wwwroot", PublicSiteFiles.FolderName(kind));
        return PublicSiteResponse.WriteAsync(context, root, context.Request.Path.Value);
    }
}

public static class PublicSiteResponse
{
    public static async Task WriteAsync(HttpContext context, string siteRoot, string? requestPath)
    {
        if (!PublicSiteFiles.TryResolve(siteRoot, requestPath, out var file, out var contentType, out var cache))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            var missing = Path.Combine(siteRoot, "404.html");
            if (File.Exists(missing))
                await context.Response.SendFileAsync(missing);
            return;
        }

        context.Response.ContentType = contentType;
        context.Response.Headers.CacheControl = cache;
        await context.Response.SendFileAsync(file);
    }
}
