using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;

namespace ShiftClub.IntegrationTests;

/// <summary>
/// Минимальный клуб для одного теста: свой филиал, касса, кассир и товар.
/// Тесты делят одну базу, поэтому каждый работает со своими строками и не
/// зависит от порядка запуска.
/// </summary>
public sealed class ClubScenario : IDisposable
{
    private readonly IServiceScope _scope;

    private ClubScenario(IServiceScope scope, ShiftClubDbContext db, Branch branch, Employee cashier, CashRegister register)
    {
        _scope = scope;
        Db = db;
        Branch = branch;
        Cashier = cashier;
        Register = register;
    }

    public ShiftClubDbContext Db { get; }
    public Branch Branch { get; }
    public Employee Cashier { get; }
    public CashRegister Register { get; }

    public ICashService Cash => _scope.ServiceProvider.GetRequiredService<ICashService>();
    public ICustomerService Customers => _scope.ServiceProvider.GetRequiredService<ICustomerService>();

    public static async Task<ClubScenario> CreateAsync(ClubDatabaseFixture fixture)
    {
        var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShiftClubDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..8];

        var branch = new Branch
        {
            Name = $"Тестовый клуб {suffix}",
            Code = $"T{suffix}",
            TimeZoneId = "Asia/Almaty",
            CurrencyCode = "KZT",
            Address = ""
        };
        db.Branches.Add(branch);

        var cashier = new Employee
        {
            BranchId = branch.Id,
            Login = $"cashier-{suffix}",
            DisplayName = "Кассир Тестовый",
            FirstName = "Кассир",
            LastName = "Тестовый",
            IsActive = true
        };
        db.Employees.Add(cashier);

        var register = new CashRegister
        {
            BranchId = branch.Id,
            Name = $"Касса {suffix}",
            Code = $"R{suffix}",
            IsActive = true
        };
        db.CashRegisters.Add(register);

        await db.SaveChangesAsync();

        return new ClubScenario(scope, db, branch, cashier, register);
    }

    public async Task<Product> AddProductAsync(string name, decimal price, decimal stock)
    {
        var category = new ProductCategory
        {
            BranchId = Branch.Id,
            Name = "Напитки",
            Code = $"DRINK-{Guid.NewGuid():N}"[..20],
            IsActive = true
        };
        Db.ProductCategories.Add(category);

        var product = new Product
        {
            BranchId = Branch.Id,
            CategoryId = category.Id,
            Name = name,
            Sku = $"SKU-{Guid.NewGuid():N}"[..16],
            SalePrice = price,
            CostPrice = price / 2,
            StockQty = stock,
            IsActive = true
        };
        Db.Products.Add(product);

        await Db.SaveChangesAsync();
        return product;
    }

    public async Task<Customer> AddCustomerAsync(decimal balance = 0)
    {
        var customer = new Customer
        {
            BranchId = Branch.Id,
            FirstName = "Гость",
            LastName = "Тестовый",
            Phone = $"+7700{Random.Shared.Next(1000000, 9999999)}",
            Balance = balance
        };
        Db.Customers.Add(customer);
        await Db.SaveChangesAsync();
        return customer;
    }

    /// <summary>Свежая копия строки из базы: кэш контекста в проверках доверия не заслуживает.</summary>
    public Task<Customer> ReloadCustomerAsync(Guid id) =>
        Db.Customers.AsNoTracking().FirstAsync(c => c.Id == id);

    public Task<CashShift> ReloadShiftAsync(Guid id) =>
        Db.CashShifts.AsNoTracking().FirstAsync(s => s.Id == id);

    public void Dispose() => _scope.Dispose();
}
