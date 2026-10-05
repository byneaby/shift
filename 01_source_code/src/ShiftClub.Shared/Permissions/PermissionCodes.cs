namespace ShiftClub.Shared.Permissions;

/// <summary>
/// Стабильные коды разрешений. Проверка всегда на сервере.
/// </summary>
public static class PermissionCodes
{
    public const string BranchesView = "branches.view";
    public const string BranchesManage = "branches.manage";
    public const string ZonesView = "zones.view";
    public const string ZonesManage = "zones.manage";
    public const string EmployeesView = "employees.view";
    public const string EmployeesManage = "employees.manage";
    public const string RolesManage = "roles.manage";
    public const string AuditView = "audit.view";
    public const string SettingsManage = "settings.manage";
    public const string ComputersView = "computers.view";
    public const string ComputersManage = "computers.manage";
    public const string ComputersCommand = "computers.command";
    public const string SessionsView = "sessions.view";
    public const string SessionsStart = "sessions.start";
    public const string SessionsExtend = "sessions.extend";
    public const string SessionsEnd = "sessions.end";
    public const string TariffsManage = "tariffs.manage";
    public const string CashView = "cash.view";
    public const string CashShiftOpen = "cash.shift.open";
    public const string CashShiftClose = "cash.shift.close";
    public const string CashSale = "cash.sale";
    public const string CashMovement = "cash.movement";
    public const string CashRefund = "cash.refund";
    public const string BarView = "bar.view";
    public const string BarSell = "bar.sell";
    public const string BarOrders = "bar.orders";
    public const string InventoryManage = "inventory.manage";
    public const string CustomersView = "customers.view";
    public const string CustomersManage = "customers.manage";
    public const string CustomersDeposit = "customers.deposit";
    public const string CustomersAdjust = "customers.adjust";
    public const string BookingsView = "bookings.view";
    public const string BookingsManage = "bookings.manage";
    public const string BookingsCancel = "bookings.cancel";
    public const string ReportsView = "reports.view";
    public const string ReportsExport = "reports.export";
    public const string PayrollView = "payroll.view";
    public const string PayrollManage = "payroll.manage";
    public const string SchedulesManage = "schedules.manage";
}
