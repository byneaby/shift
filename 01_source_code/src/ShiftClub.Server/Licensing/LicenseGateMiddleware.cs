using System.Text.Json;
using ShiftClub.Shared.Contracts;

namespace ShiftClub.Server.Licensing;

public sealed class LicenseGateMiddleware
{
    private readonly RequestDelegate _next;

    public LicenseGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, LicenseState state, IWebHostEnvironment env)
    {
        var snap = state.Current;
        if (!snap.Enforce || snap.Operational)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        if (IsOpen(path, context.Request.Method))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        if (WantsHtml(context, path))
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            var club = ShiftClub.Shared.Branding.ClubBrandingFiles.Read(env.ContentRootPath).Name;
            await context.Response.WriteAsync(LicenseLockPage.Html(snap.Message, club));
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(
            ApiResponse.Fail("license_inactive", snap.Message),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static bool IsOpen(string path, string method)
    {
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Equals("/api/license/status", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(method))
            return true;
        if (path.Equals("/api/license/activate", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(method))
            return true;
        if (path.Equals("/api/client/branding", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(method))
            return true;
        if (path.StartsWith("/media/branding/", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(method))
            return true;
        return false;
    }

    private static bool WantsHtml(HttpContext context, string path)
    {
        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase))
            return false;

        var accept = context.Request.Headers.Accept.ToString();
        return accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) || HttpMethods.IsGet(context.Request.Method);
    }
}
