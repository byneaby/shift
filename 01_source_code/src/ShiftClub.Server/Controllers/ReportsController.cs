using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftClub.Application.Abstractions;
using ShiftClub.Application.Time;
using ShiftClub.Server.Auth;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Reports;
using ShiftClub.Shared.Permissions;
using System.Text;

namespace ShiftClub.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports)
    {
        _reports = reports;
    }

    [HttpGet("overview")]
    [RequirePermission(PermissionCodes.ReportsView)]
    public async Task<ActionResult<ApiResponse<OverviewReportDto>>> Overview(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetOverviewAsync(f, t, cancellationToken);
        return Ok(ApiResponse<OverviewReportDto>.Ok(data));
    }

    [HttpGet("sales")]
    [RequirePermission(PermissionCodes.ReportsView)]
    public async Task<ActionResult<ApiResponse<SalesReportDto>>> Sales(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetSalesAsync(f, t, cancellationToken);
        return Ok(ApiResponse<SalesReportDto>.Ok(data));
    }

    [HttpGet("shifts")]
    [RequirePermission(PermissionCodes.ReportsView)]
    public async Task<ActionResult<ApiResponse<ShiftsReportDto>>> Shifts(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetShiftsAsync(f, t, cancellationToken);
        return Ok(ApiResponse<ShiftsReportDto>.Ok(data));
    }

    [HttpGet("load")]
    [RequirePermission(PermissionCodes.ReportsView)]
    public async Task<ActionResult<ApiResponse<LoadReportDto>>> Load(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetLoadAsync(f, t, cancellationToken);
        return Ok(ApiResponse<LoadReportDto>.Ok(data));
    }

    [HttpGet("bar")]
    [RequirePermission(PermissionCodes.ReportsView)]
    public async Task<ActionResult<ApiResponse<BarReportDto>>> Bar(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetBarAsync(f, t, cancellationToken);
        return Ok(ApiResponse<BarReportDto>.Ok(data));
    }

    [HttpGet("customers")]
    [RequirePermission(PermissionCodes.ReportsView)]
    public async Task<ActionResult<ApiResponse<CustomerReportDto>>> Customers(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetCustomersAsync(f, t, cancellationToken);
        return Ok(ApiResponse<CustomerReportDto>.Ok(data));
    }

    [HttpGet("sales/export")]
    [RequirePermission(PermissionCodes.ReportsExport)]
    public async Task<IActionResult> ExportSales(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetSalesAsync(f, t, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("metric,amount");
        sb.AppendLine($"revenue,{data.RevenueTotal}");
        sb.AppendLine($"collected,{data.CollectedTotal}");
        sb.AppendLine($"deposits,{data.DepositsTotal}");
        sb.AppendLine($"wallet_redeemed,{data.WalletRedeemedTotal}");
        sb.AppendLine();
        sb.AppendLine("day,amount,count");
        foreach (var row in data.ByDay)
            sb.AppendLine($"{Csv(row.Key)},{row.Amount},{row.Count}");
        sb.AppendLine();
        sb.AppendLine("payment_method,amount,count");
        foreach (var row in data.ByPaymentMethod)
            sb.AppendLine($"{Csv(row.Key)},{row.Amount},{row.Count}");
        sb.AppendLine();
        sb.AppendLine("item_type,amount,count");
        foreach (var row in data.ByItemType)
            sb.AppendLine($"{Csv(row.Key)},{row.Amount},{row.Count}");

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv", $"sales_{f:yyyyMMdd}_{t:yyyyMMdd}.csv");
    }

    [HttpGet("shifts/export")]
    [RequirePermission(PermissionCodes.ReportsExport)]
    public async Task<IActionResult> ExportShifts(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetShiftsAsync(f, t, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("number,register,status,opened_at,closed_at,sales_cash,sales_card,sales_kaspi,sales_transfer,sales_other,deposits,discrepancy,receipts_total,receipt_count");
        foreach (var s in data.Shifts)
        {
            sb.AppendLine(string.Join(',',
                Csv(s.Number),
                Csv(s.CashRegisterName),
                s.Status,
                s.OpenedAt.ToString("o"),
                s.ClosedAt?.ToString("o") ?? "",
                s.SalesCash,
                s.SalesCard,
                s.SalesKaspi,
                s.SalesTransfer,
                s.SalesOther,
                s.DepositsTotal,
                s.Discrepancy ?? 0,
                s.ReceiptsTotal,
                s.ReceiptCount));
        }

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv", $"shifts_{f:yyyyMMdd}_{t:yyyyMMdd}.csv");
    }

    [HttpGet("load/export")]
    [RequirePermission(PermissionCodes.ReportsExport)]
    public async Task<IActionResult> ExportLoad(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetLoadAsync(f, t, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("computer,zone,sessions,minutes,revenue,utilization_percent");
        foreach (var row in data.ByComputer)
        {
            sb.AppendLine(string.Join(',',
                Csv(row.ComputerName),
                Csv(row.ZoneName ?? ""),
                row.SessionCount,
                row.MinutesPlayed,
                row.Revenue,
                row.UtilizationPercent));
        }

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv", $"load_{f:yyyyMMdd}_{t:yyyyMMdd}.csv");
    }

    [HttpGet("bar/export")]
    [RequirePermission(PermissionCodes.ReportsExport)]
    public async Task<IActionResult> ExportBar(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetBarAsync(f, t, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("product,category,qty,revenue,cost,profit");
        foreach (var row in data.TopProducts)
        {
            sb.AppendLine(string.Join(',',
                Csv(row.Name),
                Csv(row.CategoryName),
                row.QtySold,
                row.Revenue,
                row.Cost,
                row.Profit));
        }
        sb.AppendLine();
        sb.AppendLine($"wallet_bar,{data.WalletBarRevenue}");

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv", $"bar_{f:yyyyMMdd}_{t:yyyyMMdd}.csv");
    }

    [HttpGet("overview/export")]
    [RequirePermission(PermissionCodes.ReportsExport)]
    public async Task<IActionResult> ExportOverview(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetOverviewAsync(f, t, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("показатель,значение");
        sb.AppendLine($"период,{f:yyyy-MM-dd} — {t:yyyy-MM-dd}");
        sb.AppendLine($"выручка,{data.RevenueTotal}");
        sb.AppendLine($"собрано,{data.CollectedTotal}");
        sb.AppendLine($"депозиты,{data.DepositsTotal}");
        sb.AppendLine($"с_баланса,{data.WalletRedeemedTotal}");
        sb.AppendLine($"игры,{data.GamesRevenue}");
        sb.AppendLine($"бар,{data.BarRevenue}");
        sb.AppendLine($"пакеты,{data.PackagesRevenue}");
        sb.AppendLine($"чеков,{data.ReceiptCount}");
        sb.AppendLine($"сеансов,{data.SessionCount}");
        sb.AppendLine($"броней,{data.BookingCount}");
        sb.AppendLine($"новые_клиенты,{data.NewCustomers}");
        sb.AppendLine($"балансы_клиентов,{data.LiabilityBalances}");
        sb.AppendLine($"бонусы,{data.LiabilityBonuses}");
        sb.AppendLine($"выручка_пред_период,{data.PrevRevenueTotal}");
        sb.AppendLine($"дельта_процент,{(data.RevenueDeltaPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "")}");

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv", $"overview_{f:yyyyMMdd}_{t:yyyyMMdd}.csv");
    }

    [HttpGet("customers/export")]
    [RequirePermission(PermissionCodes.ReportsExport)]
    public async Task<IActionResult> ExportCustomers(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (f, t) = ResolvePeriod(from, to);
        var data = await _reports.GetCustomersAsync(f, t, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("показатель,значение");
        sb.AppendLine($"всего_клиентов,{data.TotalCustomers}");
        sb.AppendLine($"новые,{data.NewCustomers}");
        sb.AppendLine($"активные,{data.ActiveCustomers}");
        sb.AppendLine($"ср_баланс,{data.AverageBalance}");
        sb.AppendLine($"сумма_балансов,{data.TotalBalances}");
        sb.AppendLine($"бонусы,{data.TotalBonuses}");
        sb.AppendLine($"банк_времени_мин,{data.TotalTimeBankMinutes}");
        sb.AppendLine();
        sb.AppendLine("клиент,списано,визиты");
        foreach (var row in data.TopSpenders)
            sb.AppendLine($"{Csv(row.Key)},{row.Amount},{row.Count}");

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv", $"customers_{f:yyyyMMdd}_{t:yyyyMMdd}.csv");
    }

    private static (DateOnly From, DateOnly To) ResolvePeriod(DateOnly? from, DateOnly? to)
    {
        var today = BranchTimeZone.TodayLocal(BranchTimeZone.DefaultId);
        var f = from ?? today.AddDays(-6);
        var t = to ?? today;
        return (f, t);
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
