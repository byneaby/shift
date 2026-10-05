using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.News;
using ShiftClub.Shared.ErrorCodes;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/news")]
public sealed class NewsController : ControllerBase
{
    private readonly IClubNewsService _news;
    private readonly ShiftClubDbContext _db;

    public NewsController(IClubNewsService news, ShiftClubDbContext db)
    {
        _news = news;
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ClubNewsDto>>>> List(
        [FromQuery] bool includeUnpublished = true,
        CancellationToken cancellationToken = default)
    {
        var list = await _news.ListAdminAsync(null, includeUnpublished, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ClubNewsDto>>.Ok(list));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ClubNewsDto>>> Create(
        [FromBody] UpsertClubNewsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var branchId = await _db.Branches.Select(b => b.Id).FirstAsync(cancellationToken);
            var result = await _news.CreateAsync(branchId, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ClubNewsDto>.Ok(result));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return BadRequest(ApiResponse<ClubNewsDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ClubNewsDto>>> Update(
        Guid id,
        [FromBody] UpsertClubNewsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _news.UpdateAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<ClubNewsDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ClubNewsDto>.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClubNewsDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _news.DeleteAsync(id, cancellationToken);
            return Ok(ApiResponse.Ok());
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail(CommonErrorCodes.NotFound, "Не найдено"));
        }
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(raw!);
    }
}
