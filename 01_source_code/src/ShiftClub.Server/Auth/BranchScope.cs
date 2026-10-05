using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Permissions;

namespace ShiftClub.Server.Auth;

/// <summary>
/// Сотрудник запросил данные филиала, к которому не привязан, и не имеет права
/// работать со всеми филиалами.
/// </summary>
public sealed class BranchAccessDeniedException : Exception
{
    public BranchAccessDeniedException()
        : base("Этот филиал вам не доступен.")
    {
    }
}

/// <summary>
/// Определяет, с каким филиалом работает запрос.
///
/// Раньше филиал приходил из строки запроса, а при записи подставлялся «первый
/// по дате создания». В клубе с одним филиалом это незаметно, а в сети это
/// значит, что кассир одного филиала видит и правит чужой. Теперь филиал
/// берётся из токена, а явно указанный другой филиал доступен только тем, у
/// кого есть право branches.manage.
/// </summary>
public static class BranchScope
{
    public const string ClaimName = "branch_id";

    /// <summary>Филиал из токена. null — сотрудник не привязан к филиалу.</summary>
    public static Guid? OwnBranchId(this ControllerBase controller) =>
        Guid.TryParse(controller.User.FindFirstValue(ClaimName), out var id) ? id : null;

    public static bool CanUseAllBranches(this ControllerBase controller) =>
        controller.User.HasClaim("permission", PermissionCodes.BranchesManage);

    /// <summary>
    /// Филиал для операции. requested — то, что попросил клиент (может быть null).
    /// Бросает <see cref="BranchAccessDeniedException"/>, если филиал чужой.
    /// </summary>
    public static async Task<Guid> ResolveBranchIdAsync(
        this ControllerBase controller,
        ShiftClubDbContext db,
        Guid? requested,
        CancellationToken cancellationToken)
    {
        var own = controller.OwnBranchId();

        if (requested is { } asked)
        {
            if (own is { } mine && asked != mine && !controller.CanUseAllBranches())
                throw new BranchAccessDeniedException();

            return asked;
        }

        if (own is { } ownBranch)
            return ownBranch;

        // Сотрудник без филиала (владелец сети, настройка): берём единственный.
        // Если филиалов несколько, выбирать за него нельзя — пусть укажет явно.
        var branches = await db.Branches.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        return branches.Count switch
        {
            0 => throw new InvalidOperationException("В системе нет ни одного филиала."),
            1 => branches[0],
            _ => throw new InvalidOperationException("Филиалов несколько — укажите, с каким работаем (branchId).")
        };
    }

    /// <summary>
    /// Проверяет, что филиал уже найденной записи доступен сотруднику. Нужно там,
    /// где операция идёт по идентификатору записи, а не по филиалу.
    /// </summary>
    public static void EnsureBranchAllowed(this ControllerBase controller, Guid branchId)
    {
        if (controller.OwnBranchId() is { } own && branchId != own && !controller.CanUseAllBranches())
            throw new BranchAccessDeniedException();
    }

    /// <summary>
    /// Филиал для выборок: null означает «все доступные». Привязанный сотрудник
    /// без права branches.manage всегда получает только свой.
    /// </summary>
    public static Guid? ResolveFilter(this ControllerBase controller, Guid? requested)
    {
        var own = controller.OwnBranchId();

        if (own is null)
            return requested;

        if (requested is { } asked && asked != own && !controller.CanUseAllBranches())
            throw new BranchAccessDeniedException();

        return requested ?? own;
    }
}
