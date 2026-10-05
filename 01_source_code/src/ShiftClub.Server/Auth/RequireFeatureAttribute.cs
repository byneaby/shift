using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts;

namespace ShiftClub.Server.Auth;

/// <summary>
/// Требует, чтобы возможность была включена в лицензии клуба.
/// Ставится только на ручки, которые что-то создают или меняют: читать то,
/// что уже накопилось, клуб должен иметь право всегда — иначе при отключении
/// возможности он потеряет доступ к своей же истории.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireFeatureAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _feature;

    public RequireFeatureAttribute(string feature)
    {
        _feature = feature;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var license = context.HttpContext.RequestServices.GetService<ILicenseService>();
        if (license is null)
            return;

        if (await license.HasFeatureAsync(_feature, context.HttpContext.RequestAborted))
            return;

        context.Result = new ObjectResult(
            ApiResponse<object>.Fail(
                "feature_not_licensed",
                "Эта возможность не входит в лицензию клуба."))
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
