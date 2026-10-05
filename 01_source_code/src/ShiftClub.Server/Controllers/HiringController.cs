using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShiftClub.Server.Hiring;
using ShiftClub.Shared.Contracts;

namespace ShiftClub.Server.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/hiring")]
public sealed class HiringController : ControllerBase
{
    private readonly HiringService _hiring;
    private readonly HiringOptions _opt;

    public HiringController(HiringService hiring, IOptions<HiringOptions> opt)
    {
        _hiring = hiring;
        _opt = opt.Value;
    }

    [HttpGet("schema")]
    public ActionResult<ApiResponse<object>> Schema() =>
        Ok(ApiResponse<object>.Ok(HiringFormSchema.Build()));

    [HttpPost("apply")]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<ApiResponse<object>>> Apply(
        [FromForm] string answersJson,
        IFormFile? photo,
        CancellationToken ct)
    {
        Dictionary<string, JsonElement> answers;
        try
        {
            answers = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(answersJson)
                      ?? throw new InvalidOperationException("Пустая анкета.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            return BadRequest(ApiResponse<object>.Fail("bad_json", "Некорректный JSON анкеты."));
        }

        string? photoPath = null;
        if (photo is { Length: > 0 })
        {
            await using var stream = photo.OpenReadStream();
            photoPath = _hiring.SavePhoto(0, stream, photo.FileName);
        }

        try
        {
            var id = _hiring.CreateCandidate(answers, photoPath);
            return Ok(ApiResponse<object>.Ok(new
            {
                id,
                message = "Спасибо! Ваша анкета сохранена. Представитель SHIFT Cyber Club продолжит собеседование лично."
            }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("validation", ex.Message));
        }
    }

    [HttpPost("login")]
    public ActionResult<ApiResponse<object>> Login([FromBody] HiringLoginRequest body)
    {
        if (!_hiring.ValidatePin(body.Pin))
            return Unauthorized(ApiResponse<object>.Fail("bad_pin", "Неверный PIN."));
        var token = _hiring.CreateSession();
        return Ok(ApiResponse<object>.Ok(new { token, expiresHours = _opt.SessionHours }));
    }

    [HttpGet("dashboard")]
    public ActionResult<ApiResponse<object>> Dashboard()
    {
        if (!ManagerOk()) return Unauthorized(ApiResponse<object>.Fail("auth", "Нужен вход руководителя."));
        return Ok(ApiResponse<object>.Ok(_hiring.Dashboard()));
    }

    [HttpGet("candidates")]
    public ActionResult<ApiResponse<object>> List(
        [FromQuery] string? status,
        [FromQuery] string? q,
        [FromQuery] bool archived = false)
    {
        if (!ManagerOk()) return Unauthorized(ApiResponse<object>.Fail("auth", "Нужен вход руководителя."));
        return Ok(ApiResponse<object>.Ok(_hiring.ListCandidates(status, q, archived)));
    }

    [HttpGet("candidates/{id:long}")]
    public ActionResult<ApiResponse<object>> Get(long id)
    {
        if (!ManagerOk()) return Unauthorized(ApiResponse<object>.Fail("auth", "Нужен вход руководителя."));
        var c = _hiring.GetCandidate(id);
        if (c is null) return NotFound(ApiResponse<object>.Fail("not_found", "Не найден."));
        return Ok(ApiResponse<object>.Ok(c));
    }

    [HttpPatch("candidates/{id:long}")]
    public ActionResult<ApiResponse<object>> Patch(long id, [FromBody] JsonElement patch)
    {
        if (!ManagerOk()) return Unauthorized(ApiResponse<object>.Fail("auth", "Нужен вход руководителя."));
        try
        {
            _hiring.UpdateCandidate(id, patch, "manager");
            return Ok(ApiResponse<object>.Ok(_hiring.GetCandidate(id)!));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("update", ex.Message));
        }
    }

    [HttpPost("candidates/{id:long}/archive")]
    public ActionResult<ApiResponse<object>> Archive(long id, [FromBody] HiringArchiveRequest body)
    {
        if (!ManagerOk()) return Unauthorized(ApiResponse<object>.Fail("auth", "Нужен вход руководителя."));
        _hiring.SetArchived(id, body.Archived, "manager");
        return Ok(ApiResponse<object>.Ok(new { id, archived = body.Archived }));
    }

    [HttpGet("photos/{fileName}")]
    public IActionResult Photo(string fileName)
    {
        fileName = Path.GetFileName(fileName);
        var path = Path.Combine(_hiring.PhotosAbsolutePath, fileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        var content = fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
            : fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
            : "image/jpeg";
        return PhysicalFile(path, content);
    }

    private bool ManagerOk()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return _hiring.ValidateSession(auth["Bearer ".Length..].Trim());
        if (Request.Headers.TryGetValue("X-Hiring-Token", out var t))
            return _hiring.ValidateSession(t.ToString());
        return false;
    }
}

public sealed record HiringLoginRequest(string Pin);
public sealed record HiringArchiveRequest(bool Archived);
