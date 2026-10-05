using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.ErrorCodes;

namespace ShiftClub.Server.Auth;

/// <summary>Требует хотя бы одно из указанных permission-claim в JWT.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _permissions;

    public RequirePermissionAttribute(params string[] permissions)
    {
        _permissions = permissions ?? Array.Empty<string>();
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedObjectResult(
                ApiResponse<object>.Fail(CommonErrorCodes.Unauthorized, "Требуется вход"));
            return;
        }

        if (_permissions.Length == 0)
            return;

        var has = _permissions.Any(p => user.HasClaim("permission", p));
        if (has)
            return;

        // Owner role retains full access even if claims lag after seed updates.
        if (user.IsInRole("owner"))
            return;

        context.Result = new ObjectResult(
            ApiResponse<object>.Fail(CommonErrorCodes.Forbidden, "Недостаточно прав"))
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
