using Microsoft.EntityFrameworkCore;
using ShiftClub.Domain.Entities;

namespace ShiftClub.Infrastructure.Persistence;

public class ShiftClubDbContext : DbContext
{
    public ShiftClubDbContext(DbContextOptions<ShiftClubDbContext> options) : base(options)
    {
    }

    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeCredential> EmployeeCredentials => Set<EmployeeCredential>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<EmployeeRole> EmployeeRoles => Set<EmployeeRole>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<ComputerHeartbeat> ComputerHeartbeats => Set<ComputerHeartbeat>();
    public DbSet<ComputerCommand> ComputerCommands => Set<ComputerCommand>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<GamingSession> GamingSessions => Set<GamingSession>();
    public DbSet<SessionHistoryEntry> SessionHistory => Set<SessionHistoryEntry>();
    public DbSet<CashRegister> CashRegisters => Set<CashRegister>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptItem> ReceiptItems => Set<ReceiptItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<BarOrder> BarOrders => Set<BarOrder>();
    public DbSet<BarOrderItem> BarOrderItems => Set<BarOrderItem>();
    public DbSet<LoyaltyLevel> LoyaltyLevels => Set<LoyaltyLevel>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<TelegramAuthTicket> TelegramAuthTickets => Set<TelegramAuthTicket>();
    public DbSet<CustomerBalanceTransaction> CustomerBalanceTransactions => Set<CustomerBalanceTransaction>();
    public DbSet<CustomerTimeBankTransaction> CustomerTimeBankTransactions => Set<CustomerTimeBankTransaction>();
    public DbSet<CustomerZoneTimeBank> CustomerZoneTimeBanks => Set<CustomerZoneTimeBank>();
    public DbSet<CustomerPackage> CustomerPackages => Set<CustomerPackage>();
    public DbSet<CustomerTelegramOutreach> CustomerTelegramOutreach => Set<CustomerTelegramOutreach>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingComputer> BookingComputers => Set<BookingComputer>();
    public DbSet<WorkShift> WorkShifts => Set<WorkShift>();
    public DbSet<WorkShiftNote> WorkShiftNotes => Set<WorkShiftNote>();
    public DbSet<PayrollAccrual> PayrollAccruals => Set<PayrollAccrual>();
    public DbSet<SoftwareApp> SoftwareApps => Set<SoftwareApp>();
    public DbSet<ClubNewsPost> ClubNewsPosts => Set<ClubNewsPost>();
    public DbSet<StaffWikiPage> StaffWikiPages => Set<StaffWikiPage>();
    public DbSet<FloorMapElement> FloorMapElements => Set<FloorMapElement>();
    public DbSet<CaseDefinition> CaseDefinitions => Set<CaseDefinition>();
    public DbSet<CasePrize> CasePrizes => Set<CasePrize>();
    public DbSet<CaseKeyLedger> CaseKeyLedgers => Set<CaseKeyLedger>();
    public DbSet<CaseOpening> CaseOpenings => Set<CaseOpening>();
    public DbSet<CaseUserReward> CaseUserRewards => Set<CaseUserReward>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShiftClubDbContext).Assembly);
    }
}
