using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Application.Tests;

/// <summary>
/// Границы между филиалами: кассир одного филиала не должен добраться до чужого
/// ни запросом, ни подстановкой branchId в адрес.
/// </summary>
public class BranchScopeTests
{
    private static readonly Guid BranchA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BranchB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class FakeController : ControllerBase
    {
    }

    private static FakeController Employee(Guid? branchId, bool allBranches = false)
    {
        var claims = new List<Claim>();
        if (branchId is { } id)
            claims.Add(new Claim(BranchScope.ClaimName, id.ToString()));
        if (allBranches)
            claims.Add(new Claim("permission", PermissionCodes.BranchesManage));

        return new FakeController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
                }
            }
        };
    }

    [Fact]
    public void FilterFallsBackToOwnBranch()
    {
        Assert.Equal(BranchA, Employee(BranchA).ResolveFilter(null));
    }

    [Fact]
    public void FilterKeepsOwnBranchWhenAskedForIt()
    {
        Assert.Equal(BranchA, Employee(BranchA).ResolveFilter(BranchA));
    }

    [Fact]
    public void FilterRefusesForeignBranch()
    {
        Assert.Throws<BranchAccessDeniedException>(() => Employee(BranchA).ResolveFilter(BranchB));
    }

    [Fact]
    public void FilterAllowsForeignBranchForNetworkManager()
    {
        Assert.Equal(BranchB, Employee(BranchA, allBranches: true).ResolveFilter(BranchB));
    }

    [Fact]
    public void FilterStaysEmptyForEmployeeWithoutBranch()
    {
        Assert.Null(Employee(null).ResolveFilter(null));
    }

    [Fact]
    public void FilterTrustsRequestOfEmployeeWithoutBranch()
    {
        Assert.Equal(BranchB, Employee(null).ResolveFilter(BranchB));
    }

    [Fact]
    public void EnsureAllowedRefusesRecordOfForeignBranch()
    {
        Assert.Throws<BranchAccessDeniedException>(() => Employee(BranchA).EnsureBranchAllowed(BranchB));
    }

    [Fact]
    public void EnsureAllowedPassesOwnRecord()
    {
        Employee(BranchA).EnsureBranchAllowed(BranchA);
    }

    [Fact]
    public void EnsureAllowedPassesAnyRecordForNetworkManager()
    {
        Employee(BranchA, allBranches: true).EnsureBranchAllowed(BranchB);
    }

    [Fact]
    public void OwnBranchIsEmptyWhenTokenHasNoBranch()
    {
        Assert.Null(Employee(null).OwnBranchId());
    }
}
