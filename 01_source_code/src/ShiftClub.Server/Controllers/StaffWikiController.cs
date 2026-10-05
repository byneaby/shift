using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.StaffWiki;
using ShiftClub.Shared.ErrorCodes;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/staff-wiki")]
public sealed class StaffWikiController : ControllerBase
{
    private readonly IStaffWikiService _wiki;
    private readonly IWebHostEnvironment _env;

    public StaffWikiController(IStaffWikiService wiki, IWebHostEnvironment env)
    {
        _wiki = wiki;
        _env = env;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StaffWikiPageListItemDto>>>> List(
        CancellationToken cancellationToken)
    {
        var list = await _wiki.ListAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<StaffWikiPageListItemDto>>.Ok(list));
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<ApiResponse<StaffWikiPageDto>>> Get(
        string slug,
        CancellationToken cancellationToken)
    {
        var page = await _wiki.GetBySlugAsync(slug, cancellationToken);
        if (page is null)
            return NotFound(ApiResponse<StaffWikiPageDto>.Fail(CommonErrorCodes.NotFound, "Статья не найдена"));
        return Ok(ApiResponse<StaffWikiPageDto>.Ok(page));
    }

    [HttpPost]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<StaffWikiPageDto>>> Create(
        [FromBody] UpsertStaffWikiPageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _wiki.CreateAsync(request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<StaffWikiPageDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<StaffWikiPageDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<StaffWikiPageDto>>> Update(
        Guid id,
        [FromBody] UpsertStaffWikiPageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _wiki.UpdateAsync(id, request, GetEmployeeId(), cancellationToken);
            return Ok(ApiResponse<StaffWikiPageDto>.Ok(dto));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<StaffWikiPageDto>.Fail(CommonErrorCodes.NotFound, "Статья не найдена"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<StaffWikiPageDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _wiki.DeleteAsync(id, cancellationToken);
            return Ok(ApiResponse.Ok());
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail(CommonErrorCodes.NotFound, "Статья не найдена"));
        }
    }

    [HttpPost("upload")]
    [Authorize(Roles = "owner")]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<ApiResponse<StaffWikiUploadDto>>> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<StaffWikiUploadDto>.Fail(CommonErrorCodes.ValidationFailed, "Файл пустой"));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif"))
            return BadRequest(ApiResponse<StaffWikiUploadDto>.Fail(
                CommonErrorCodes.ValidationFailed, "Нужен jpg/png/webp/gif"));

        var dir = Path.Combine(_env.ContentRootPath, "data", "wiki");
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{ext}";
        var path = Path.Combine(dir, name);
        await using (var stream = System.IO.File.Create(path))
            await file.CopyToAsync(stream, cancellationToken);

        var url = $"/media/wiki/{name}?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        return Ok(ApiResponse<StaffWikiUploadDto>.Ok(new StaffWikiUploadDto(url)));
    }

    private Guid GetEmployeeId()
    {
        var raw = User.FindFirstValue("employee_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
