using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts;

namespace ShiftClub.Server.Controllers;

public sealed record ResetFinancesRequest(bool Confirm);

public sealed record ResetDataRequest(bool Confirm, IReadOnlyList<string>? Scopes);

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private static readonly HashSet<string> AllowedScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "cash", "bar", "sessions", "balances", "payroll", "all"
    };

    private readonly ShiftClubDbContext _db;

    public SystemController(ShiftClubDbContext db)
    {
        _db = db;
    }

    [HttpGet("info")]
    public ActionResult<ApiResponse<object>> GetInfo()
    {
        var payload = new
        {
            product = "SHIFT Club Management System",
            version = "0.2.0-hardening",
            timeZone = "Asia/Almaty",
            currency = "KZT",
            utcNow = DateTimeOffset.UtcNow
        };

        return Ok(ApiResponse<object>.Ok(payload));
    }

    /// <summary>
    /// Полный сброс (alias). Предпочтительно: POST reset-data со scopes.
    /// </summary>
    [HttpPost("reset-finances")]
    [Authorize(Roles = "owner")]
    public Task<ActionResult<ApiResponse<object>>> ResetFinances(
        [FromBody] ResetFinancesRequest? request,
        CancellationToken cancellationToken) =>
        ResetData(new ResetDataRequest(request?.Confirm ?? false, ["all"]), cancellationToken);

    /// <summary>
    /// Выборочный сброс операционных данных (только владелец).
    /// scopes: cash | bar | sessions | balances | payroll | all
    /// </summary>
    [HttpPost("reset-data")]
    [Authorize(Roles = "owner")]
    public async Task<ActionResult<ApiResponse<object>>> ResetData(
        [FromBody] ResetDataRequest? request,
        CancellationToken cancellationToken)
    {
        var confirm = request?.Confirm ?? false;
        if (!confirm)
            return BadRequest(ApiResponse<object>.Fail("confirmation_required", "Передайте confirm: true для подтверждения сброса."));

        var raw = request?.Scopes?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList()
                  ?? [];
        if (raw.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("scopes_required", "Укажите хотя бы один scope."));

        foreach (var s in raw)
        {
            if (!AllowedScopes.Contains(s))
                return BadRequest(ApiResponse<object>.Fail("invalid_scope", $"Неизвестный scope: {s}"));
        }

        var all = raw.Any(s => s.Equals("all", StringComparison.OrdinalIgnoreCase));
        var cash = all || raw.Any(s => s.Equals("cash", StringComparison.OrdinalIgnoreCase));
        var bar = all || raw.Any(s => s.Equals("bar", StringComparison.OrdinalIgnoreCase));
        var sessions = all || raw.Any(s => s.Equals("sessions", StringComparison.OrdinalIgnoreCase));
        var balances = all || raw.Any(s => s.Equals("balances", StringComparison.OrdinalIgnoreCase));
        var payroll = all || raw.Any(s => s.Equals("payroll", StringComparison.OrdinalIgnoreCase));

        var applied = new List<string>();
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (cash)
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM payments", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM receipt_items", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM receipts", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM cash_movements", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM cash_shifts", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM document_sequences", cancellationToken);
            applied.Add("cash");
        }

        if (bar)
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM bar_order_items", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM bar_orders", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM inventory_movements", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync(
                """UPDATE products SET "StockQty" = 0""",
                cancellationToken);
            applied.Add("bar");
        }

        if (sessions)
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM session_history", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM gaming_sessions", cancellationToken);
            applied.Add("sessions");
        }

        if (balances)
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM customer_balance_transactions", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM customer_time_bank_transactions", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM customer_zone_time_banks", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM customer_packages", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync(
                """
                UPDATE customers SET
                  "Balance" = 0,
                  "BonusBalance" = 0,
                  "TotalSpent" = 0,
                  "VisitCount" = 0,
                  "TotalMinutesPlayed" = 0,
                  "TimeBankMinutes" = 0
                """,
                cancellationToken);
            applied.Add("balances");
        }

        if (payroll)
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM payroll_accruals", cancellationToken);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM work_shifts", cancellationToken);
            applied.Add("payroll");
        }

        await tx.CommitAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new
        {
            message = "Данные сброшены: " + string.Join(", ", applied),
            scopes = applied
        }));
    }

    [HttpGet("db")]
    public async Task<ActionResult<ApiResponse<object>>> CheckDatabase(CancellationToken cancellationToken)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { canConnect }));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail("db_unavailable", ex.Message));
        }
    }
}
