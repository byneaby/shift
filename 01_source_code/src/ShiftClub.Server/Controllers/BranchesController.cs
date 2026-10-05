using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Branches;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class BranchesController : ControllerBase
{
    private readonly IBranchService _branchService;

    public BranchesController(IBranchService branchService)
    {
        _branchService = branchService;
    }

    [HttpGet]
    [RequirePermission(
        PermissionCodes.BranchesView,
        PermissionCodes.ComputersView,
        PermissionCodes.SessionsView,
        PermissionCodes.CustomersView)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BranchDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var branches = await _branchService.GetBranchesAsync(cancellationToken);

        // Панель берёт «свой» филиал из начала этого списка, поэтому привязанному
        // сотруднику нельзя показывать сеть целиком — иначе он начнёт заводить
        // тарифы и зоны в чужом филиале.
        if (this.OwnBranchId() is { } own && !this.CanUseAllBranches())
            branches = branches.Where(b => b.Id == own).ToList();

        return Ok(ApiResponse<IReadOnlyList<BranchDto>>.Ok(branches));
    }
}
