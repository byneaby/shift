using ShiftClub.Shared.Contracts.Reports;

namespace ShiftClub.Application.Abstractions;

public interface IReportService
{
    Task<OverviewReportDto> GetOverviewAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<SalesReportDto> GetSalesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<ShiftsReportDto> GetShiftsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<LoadReportDto> GetLoadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<BarReportDto> GetBarAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<CustomerReportDto> GetCustomersAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
