using Microsoft.EntityFrameworkCore;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.ErrorCodes;

namespace ShiftClub.Server.Middleware;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ShiftClub.Server.Auth.BranchAccessDeniedException ex)
        {
            // Это не поломка сервера, а отказ в доступе: в список ошибок клуба
            // такие случаи попадать не должны.
            _logger.LogWarning("Branch access denied: {Path}", context.Request.Path);
            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(
                ApiResponse<object>.Fail("branch_forbidden", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled API exception");
            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var baseMessage = ex.GetBaseException().Message;
            var message = ex is DbUpdateConcurrencyException
                ? "Данные изменились параллельно. Обновите страницу и повторите."
                : (baseMessage.Length > 300 ? baseMessage[..300] : baseMessage);

            await context.Response.WriteAsJsonAsync(
                ApiResponse<object>.Fail(CommonErrorCodes.InternalError, message));
        }
    }
}
