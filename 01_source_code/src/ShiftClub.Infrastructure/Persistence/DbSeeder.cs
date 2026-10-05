using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Persistence;

public static class DbSeeder
{
	public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = services.CreateScope();
		ShiftClubDbContext db = scope.ServiceProvider.GetRequiredService<ShiftClubDbContext>();
		IPasswordHasher hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
		IConfiguration config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
		ILogger logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");
		await db.Database.MigrateAsync(cancellationToken);
		await EnsurePermissionsAsync(db, logger, cancellationToken);
		try
		{
			await scope.ServiceProvider.GetRequiredService<ICaseService>().EnsureSeededAsync(cancellationToken);
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "SHIFT CASE seed skipped");
		}
		try
		{
			await scope.ServiceProvider.GetRequiredService<IStaffWikiService>().EnsureSeededAsync(cancellationToken);
		}
		catch (Exception exception2)
		{
			logger.LogWarning(exception2, "Staff wiki seed skipped");
		}
		Role ownerRole = await db.Roles.Include((Role r) => r.RolePermissions).FirstOrDefaultAsync((Role r) => r.Code == "owner", cancellationToken);
		if (ownerRole == null)
		{
			ownerRole = new Role
			{
				Code = "owner",
				Name = "Владелец",
				Description = "Полный доступ",
				IsSystem = true
			};
			db.Roles.Add(ownerRole);
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Seeded owner role");
		}
		await SyncOwnerPermissionsAsync(db, ownerRole.Id, cancellationToken);
		await EnsureCashierRoleAsync(db, logger, cancellationToken);
		if (!(await db.Branches.AnyAsync(cancellationToken)))
		{
			Branch branch = new Branch
			{
				Name = "SHIFT Club",
				Code = "MAIN",
				TimeZoneId = "Asia/Almaty",
				CurrencyCode = "KZT",
				Address = "г. Алматы, ул. Масанчи 86а"
			};
			db.Branches.Add(branch);
			await db.SaveChangesAsync(cancellationToken);
			db.Zones.AddRange(new Zone
			{
				BranchId = branch.Id,
				Name = "Стандарт",
				Code = "STD",
				ColorHex = "#3B82F6",
				SortOrder = 1,
				MinSessionMinutes = 5
			}, new Zone
			{
				BranchId = branch.Id,
				Name = "VIP",
				Code = "VIP",
				ColorHex = "#F59E0B",
				SortOrder = 2,
				MinSessionMinutes = 5
			}, new Zone
			{
				BranchId = branch.Id,
				Name = "Bootcamp",
				Code = "BOOT",
				ColorHex = "#10B981",
				SortOrder = 3,
				MinSessionMinutes = 5
			}, new Zone
			{
				BranchId = branch.Id,
				Name = "PlayStation 5",
				Code = "PS5",
				ColorHex = "#2e75ff",
				SortOrder = 4,
				MinSessionMinutes = 5,
				Kind = "Console"
			});
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Seeded default branch and zones");
		}
		else
		{
			foreach (Zone item in await db.Zones.ToListAsync(cancellationToken))
			{
				if (item.Code == "STD")
				{
					item.Name = "Стандарт";
					Zone zone = item;
					int? minSessionMinutes = zone.MinSessionMinutes;
					minSessionMinutes.GetValueOrDefault();
					if (!minSessionMinutes.HasValue)
					{
						int value = 5;
						zone.MinSessionMinutes = value;
					}
					item.SortOrder = 1;
					item.IsActive = true;
				}
				if (item.Code == "VIP")
				{
					item.Name = "VIP";
					Zone zone = item;
					int? minSessionMinutes = zone.MinSessionMinutes;
					minSessionMinutes.GetValueOrDefault();
					if (!minSessionMinutes.HasValue)
					{
						int value = 5;
						zone.MinSessionMinutes = value;
					}
					item.SortOrder = 2;
					item.IsActive = true;
				}
				string code = item.Code;
				if ((code == "BOOT" || code == "BOOTCAMP") ? true : false)
				{
					item.Code = "BOOT";
					item.Name = "Bootcamp";
					Zone zone = item;
					int? minSessionMinutes = zone.MinSessionMinutes;
					minSessionMinutes.GetValueOrDefault();
					if (!minSessionMinutes.HasValue)
					{
						int value = 5;
						zone.MinSessionMinutes = value;
					}
					item.SortOrder = 3;
					item.IsActive = true;
				}
			}
			await db.SaveChangesAsync(cancellationToken);
		}
		Guid branchId = await db.Branches.Select((Branch b) => b.Id).FirstAsync(cancellationToken);
		await EnsureZone("STD", "Стандарт", "#3B82F6", 1);
		await EnsureZone("VIP", "VIP", "#F59E0B", 2);
		await EnsureZone("BOOT", "Bootcamp", "#10B981", 3);
		await EnsureZone("PS5", "PlayStation 5", "#2e75ff", 4, "Console");
		await db.SaveChangesAsync(cancellationToken);
		string ownerLogin = config["Seed:OwnerLogin"] ?? "owner";
		if (!(await db.Employees.AnyAsync((Employee e) => e.Login == ownerLogin, cancellationToken)))
		{
			Guid value2 = await db.Branches.Select((Branch b) => b.Id).FirstAsync(cancellationToken);
			string password = config["Seed:OwnerPassword"] ?? "Owner123!";
			Employee employee = new Employee
			{
				BranchId = value2,
				Login = ownerLogin,
				DisplayName = "Владелец",
				FirstName = "Владелец",
				LastName = "",
				IsActive = true,
				Credential = new EmployeeCredential
				{
					PasswordHash = hasher.Hash(password),
					PasswordChangedAt = DateTimeOffset.UtcNow
				}
			};
			db.Employees.Add(employee);
			await db.SaveChangesAsync(cancellationToken);
			db.EmployeeRoles.Add(new EmployeeRole
			{
				EmployeeId = employee.Id,
				RoleId = ownerRole.Id
			});
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Seeded owner employee '{Login}'", ownerLogin);
		}
		if (!(await db.Tariffs.AnyAsync(cancellationToken)))
		{
			logger.LogInformation("Tariffs empty — will seed club price list");
		}
		await UpsertClubTariffsAsync(db, logger, cancellationToken);
		await UpsertClubHookahAsync(db, logger, cancellationToken);
		if (!(await db.CashRegisters.AnyAsync(cancellationToken)))
		{
			Guid branchId2 = await db.Branches.Select((Branch b) => b.Id).FirstAsync(cancellationToken);
			db.CashRegisters.Add(new CashRegister
			{
				BranchId = branchId2,
				Name = "Основная касса",
				Code = "MAIN",
				IsActive = true
			});
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Seeded default cash register");
		}
		if (!(await db.ProductCategories.AnyAsync(cancellationToken)))
		{
			Guid branchId3 = await db.Branches.Select((Branch b) => b.Id).FirstAsync(cancellationToken);
			ProductCategory drinks = new ProductCategory
			{
				BranchId = branchId3,
				Name = "Напитки",
				Code = "DRINKS",
				SortOrder = 1
			};
			ProductCategory energy = new ProductCategory
			{
				BranchId = branchId3,
				Name = "Энергетики",
				Code = "ENERGY",
				SortOrder = 2
			};
			ProductCategory snacks = new ProductCategory
			{
				BranchId = branchId3,
				Name = "Снеки",
				Code = "SNACKS",
				SortOrder = 3
			};
			db.ProductCategories.AddRange(drinks, energy, snacks);
			await db.SaveChangesAsync(cancellationToken);
			db.Products.AddRange(new Product
			{
				BranchId = branchId3,
				CategoryId = drinks.Id,
				Name = "Вода 0.5л",
				Sku = "WATER05",
				Unit = "шт",
				CostPrice = 60m,
				SalePrice = 150m,
				StockQty = 50m,
				MinStockQty = 10m
			}, new Product
			{
				BranchId = branchId3,
				CategoryId = drinks.Id,
				Name = "Cola 0.33л",
				Sku = "COLA033",
				Unit = "шт",
				CostPrice = 120m,
				SalePrice = 350m,
				StockQty = 40m,
				MinStockQty = 8m
			}, new Product
			{
				BranchId = branchId3,
				CategoryId = energy.Id,
				Name = "Red Bull",
				Sku = "RBULL",
				Unit = "шт",
				CostPrice = 280m,
				SalePrice = 700m,
				StockQty = 24m,
				MinStockQty = 6m
			}, new Product
			{
				BranchId = branchId3,
				CategoryId = snacks.Id,
				Name = "Чипсы",
				Sku = "CHIPS",
				Unit = "шт",
				CostPrice = 180m,
				SalePrice = 450m,
				StockQty = 30m,
				MinStockQty = 5m
			});
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Seeded bar catalog");
		}
		if (!(await db.LoyaltyLevels.AnyAsync(cancellationToken)))
		{
			Guid branchId3 = await db.Branches.Select((Branch b) => b.Id).FirstAsync(cancellationToken);
			LoyaltyLevel bronze = new LoyaltyLevel
			{
				BranchId = branchId3,
				Name = "Бронза",
				Code = "BRONZE",
				MinSpent = 0,
				BonusPercent = 0m,
				TimeDiscountPercent = 0m,
				SortOrder = 1
			};
			LoyaltyLevel loyaltyLevel = new LoyaltyLevel
			{
				BranchId = branchId3,
				Name = "Серебро",
				Code = "SILVER",
				MinSpent = 20000,
				BonusPercent = 3m,
				TimeDiscountPercent = 3m,
				SortOrder = 2
			};
			LoyaltyLevel loyaltyLevel2 = new LoyaltyLevel
			{
				BranchId = branchId3,
				Name = "Золото",
				Code = "GOLD",
				MinSpent = 50000,
				BonusPercent = 5m,
				TimeDiscountPercent = 5m,
				SortOrder = 3
			};
			db.LoyaltyLevels.AddRange(bronze, loyaltyLevel, loyaltyLevel2);
			await db.SaveChangesAsync(cancellationToken);
			db.Customers.Add(new Customer
			{
				BranchId = branchId3,
				FirstName = "Иван",
				LastName = "Тестов",
				Phone = "77001234567",
				LoyaltyLevelId = bronze.Id,
				Balance = 5000m,
				PinHash = null,
				IsActive = true
			});
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Seeded loyalty levels and sample customer");
		}
		Customer customer = await db.Customers.FirstOrDefaultAsync((Customer c) => c.Phone == "77001234567", cancellationToken);
		if (customer != null && customer.Balance < 1000m)
		{
			customer.Balance = 5000m;
			await db.SaveChangesAsync(cancellationToken);
		}
		if (!(await db.SoftwareApps.AnyAsync(cancellationToken)))
		{
			Guid branchId4 = await db.Branches.Select((Branch b) => b.Id).FirstAsync(cancellationToken);
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
			string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.System);
			db.SoftwareApps.AddRange(new SoftwareApp
			{
				BranchId = branchId4,
				Name = "Блокнот",
				Category = "Утилиты",
				ExePath = Path.Combine(folderPath2, "notepad.exe"),
				SortOrder = 10
			}, new SoftwareApp
			{
				BranchId = branchId4,
				Name = "Калькулятор",
				Category = "Утилиты",
				ExePath = Path.Combine(folderPath2, "calc.exe"),
				SortOrder = 20
			}, new SoftwareApp
			{
				BranchId = branchId4,
				Name = "Paint",
				Category = "Утилиты",
				ExePath = Path.Combine(folderPath2, "mspaint.exe"),
				SortOrder = 30
			}, new SoftwareApp
			{
				BranchId = branchId4,
				Name = "Проводник",
				Category = "Система",
				ExePath = Path.Combine(folderPath, "explorer.exe"),
				SortOrder = 40
			});
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Seeded software apps catalog");
		}
		await EnsureClubNewsAsync(db, logger, cancellationToken);
		if (!(await db.AppSettings.AnyAsync((AppSetting s) => s.Key == "shell.admin_password_hash", cancellationToken)))
		{
			string password2 = config["Seed:OwnerPassword"] ?? "Owner123!";
			db.AppSettings.Add(new AppSetting
			{
				Key = "shell.admin_password_hash",
				Value = hasher.Hash(password2),
				Description = "Пароль админ-режима Shell (настройка образа / superclient)"
			});
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Seeded Shell admin password from Seed:OwnerPassword");
		}
		async Task EnsureZone(string text, string name, string color, int order, string? kind = null)
		{
			Zone zone2 = await db.Zones.FirstOrDefaultAsync((Zone x) => x.BranchId == branchId && x.Code == text, cancellationToken);
			if (zone2 == null)
			{
				db.Zones.Add(new Zone
				{
					BranchId = branchId,
					Code = text,
					Name = name,
					ColorHex = color,
					SortOrder = order,
					MinSessionMinutes = 5,
					IsActive = true,
					Kind = (kind ?? "Hall")
				});
			}
			else
			{
				zone2.Name = name;
				zone2.ColorHex = color;
				zone2.SortOrder = order;
				Zone zone3 = zone2;
				int? minSessionMinutes2 = zone3.MinSessionMinutes;
				minSessionMinutes2.GetValueOrDefault();
				if (!minSessionMinutes2.HasValue)
				{
					int value3 = 5;
					zone3.MinSessionMinutes = value3;
				}
				zone2.IsActive = true;
				if (!string.IsNullOrWhiteSpace(kind))
				{
					zone2.Kind = kind;
				}
			}
		}
	}

	private static async Task EnsurePermissionsAsync(ShiftClubDbContext db, ILogger logger, CancellationToken cancellationToken)
	{
		(string Code, string Name, string Group)[] definitions = new(string, string, string)[39]
		{
			("branches.view", "Просмотр филиалов", "Branches"),
			("branches.manage", "Управление филиалами", "Branches"),
			("zones.view", "Просмотр зон", "Zones"),
			("zones.manage", "Управление зонами", "Zones"),
			("employees.view", "Просмотр сотрудников", "Employees"),
			("employees.manage", "Управление сотрудниками", "Employees"),
			("roles.manage", "Управление ролями", "Employees"),
			("audit.view", "Просмотр аудита", "Security"),
			("settings.manage", "Настройки", "Settings"),
			("computers.view", "Просмотр ПК", "Computers"),
			("computers.manage", "Управление ПК", "Computers"),
			("computers.command", "Команды ПК", "Computers"),
			("sessions.view", "Просмотр сеансов", "Sessions"),
			("sessions.start", "Запуск сеансов", "Sessions"),
			("sessions.extend", "Продление сеансов", "Sessions"),
			("sessions.end", "Завершение сеансов", "Sessions"),
			("tariffs.manage", "Управление тарифами", "Tariffs"),
			("cash.view", "Просмотр кассы", "Cash"),
			("cash.shift.open", "Открытие смены", "Cash"),
			("cash.shift.close", "Закрытие смены", "Cash"),
			("cash.sale", "Продажи на кассе", "Cash"),
			("cash.movement", "Внесения и изъятия", "Cash"),
			("cash.refund", "Возвраты чеков", "Cash"),
			("bar.view", "Просмотр бара", "Bar"),
			("bar.sell", "Продажа бара", "Bar"),
			("bar.orders", "Заказы бара", "Bar"),
			("inventory.manage", "Склад", "Inventory"),
			("customers.view", "Просмотр клиентов", "Customers"),
			("customers.manage", "Управление клиентами", "Customers"),
			("customers.deposit", "Пополнение баланса", "Customers"),
			("customers.adjust", "Корректировка баланса", "Customers"),
			("bookings.view", "Просмотр броней", "Bookings"),
			("bookings.manage", "Управление бронями", "Bookings"),
			("bookings.cancel", "Отмена броней", "Bookings"),
			("reports.view", "Просмотр отчётов", "Reports"),
			("reports.export", "Экспорт отчётов", "Reports"),
			("payroll.view", "Просмотр зарплат", "Employees"),
			("payroll.manage", "Управление зарплатами", "Employees"),
			("schedules.manage", "Графики смен", "Employees")
		};
		List<string> existing = await db.Permissions.Select((Permission p) => p.Code).ToListAsync(cancellationToken);
		List<(string Code, string Name, string Group)> missing = definitions.Where(((string Code, string Name, string Group) d) => !existing.Contains(d.Code)).ToList();
		if (missing.Count != 0)
		{
			db.Permissions.AddRange(missing.Select(delegate((string Code, string Name, string Group) m)
			{
				Permission permission = new Permission();
				(permission.Code, permission.Name, permission.GroupName) = m;
				return permission;
			}));
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Seeded {Count} permissions", missing.Count);
		}
	}

	private static async Task SyncOwnerPermissionsAsync(ShiftClubDbContext db, Guid ownerRoleId, CancellationToken cancellationToken)
	{
		List<Guid> list = (await db.Permissions.Select((Permission p) => p.Id).ToListAsync(cancellationToken)).Except(await (from rp in db.RolePermissions
			where rp.RoleId == ownerRoleId
			select rp.PermissionId).ToListAsync(cancellationToken)).ToList();
		if (list.Count != 0)
		{
			db.RolePermissions.AddRange(list.Select((Guid pid) => new RolePermission
			{
				RoleId = ownerRoleId,
				PermissionId = pid
			}));
			await db.SaveChangesAsync(cancellationToken);
		}
	}

	private static async Task EnsureCashierRoleAsync(ShiftClubDbContext db, ILogger logger, CancellationToken cancellationToken)
	{
		Role role = await db.Roles.Include((Role r) => r.RolePermissions).FirstOrDefaultAsync((Role r) => r.Code == "cashier", cancellationToken);
		if (role == null)
		{
			role = new Role
			{
				Code = "cashier",
				Name = "Кассир",
				Description = "Касса, сеансы, бар, клиенты, брони — без правок ПК/цен/склада",
				IsSystem = true
			};
			db.Roles.Add(role);
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Seeded cashier role");
		}
		else if (role.Description == null || !role.Description.Contains("без правок"))
		{
			role.Description = "Касса, сеансы, бар, клиенты, брони — без правок ПК/цен/склада";
		}
		string[] codes = new string[23]
		{
			"computers.view", "computers.command", "sessions.view", "sessions.start", "sessions.extend", "sessions.end", "cash.view", "cash.shift.open", "cash.shift.close", "cash.sale",
			"cash.movement", "cash.refund", "bar.view", "bar.sell", "bar.orders", "customers.view", "customers.manage", "customers.deposit", "customers.adjust", "bookings.view", "bookings.manage",
			"reports.view", "reports.export"
		};
		HashSet<Guid> allowedIds = (await (from p in db.Permissions
			where codes.Contains(p.Code)
			select new { p.Id, p.Code }).ToListAsync(cancellationToken)).Select(p => p.Id).ToHashSet();
		List<RolePermission> source = await db.RolePermissions.Where((RolePermission rp) => rp.RoleId == role.Id).ToListAsync(cancellationToken);
		List<RolePermission> list = source.Where((RolePermission rp) => !allowedIds.Contains(rp.PermissionId)).ToList();
		if (list.Count > 0)
		{
			db.RolePermissions.RemoveRange(list);
			logger.LogInformation("Cashier role: removed {Count} extra permissions", list.Count);
		}
		HashSet<Guid> second = source.Select((RolePermission rp) => rp.PermissionId).ToHashSet();
		List<Guid> list2 = allowedIds.Except(second).ToList();
		if (list2.Count > 0)
		{
			db.RolePermissions.AddRange(list2.Select((Guid pid) => new RolePermission
			{
				RoleId = role.Id,
				PermissionId = pid
			}));
			logger.LogInformation("Cashier role: added {Count} permissions", list2.Count);
		}
		if (list.Count > 0 || list2.Count > 0)
		{
			await db.SaveChangesAsync(cancellationToken);
		}
	}

	private static async Task UpsertClubTariffsAsync(ShiftClubDbContext db, ILogger logger, CancellationToken cancellationToken)
	{
		await db.Tariffs.Where((Tariff t) => t.DaysOfWeekMask == 0).ExecuteUpdateAsync((SetPropertyCalls<Tariff> s) => s.SetProperty((Tariff t) => t.DaysOfWeekMask, 127), cancellationToken);
		Guid branchId = await db.Branches.Select((Branch b) => b.Id).FirstAsync(cancellationToken);
		List<Zone> source = await db.Zones.Where((Zone z) => z.BranchId == branchId).ToListAsync(cancellationToken);
		Zone zone = source.FirstOrDefault((Zone z) => z.Code == "STD") ?? throw new InvalidOperationException("Зона STD не найдена");
		Zone zone2 = source.FirstOrDefault((Zone z) => z.Code == "VIP") ?? throw new InvalidOperationException("Зона VIP не найдена");
		Zone zone3 = source.FirstOrDefault((Zone z) => z.Code == "BOOT") ?? throw new InvalidOperationException("Зона BOOT не найдена");
		Zone zone4 = source.FirstOrDefault((Zone z) => z.Code == "PS5") ?? throw new InvalidOperationException("Зона PS5 не найдена");
		TimeSpan value = new TimeSpan(12, 0, 0);
		TimeSpan value2 = new TimeSpan(18, 0, 0);
		TimeSpan value3 = new TimeSpan(23, 0, 0);
		TimeSpan value4 = new TimeSpan(8, 0, 0);
		List<Tariff> catalog = new List<Tariff>
		{
			Hourly(branchId, zone.Id, "STD_HOUR", "1 час", 500m, 1),
			Pack(branchId, zone.Id, "STD_2P1", "2+1", 180, 1000m, 500m, 2),
			Pack(branchId, zone.Id, "STD_3P2", "3+2", 300, 1500m, 500m, 3),
			Pack(branchId, zone.Id, "STD_DAY", "день", 0, 1500m, 500m, 4, value, value2, TariffDurationMode.TimeWindow),
			Pack(branchId, zone.Id, "STD_NIGHT", "ночь", 0, 2000m, 500m, 5, value3, value4, TariffDurationMode.TimeWindow),
			Hourly(branchId, zone2.Id, "VIP_HOUR", "1 час", 750m, 11),
			Pack(branchId, zone2.Id, "VIP_2P1", "2+1", 180, 1500m, 750m, 12),
			Pack(branchId, zone2.Id, "VIP_3P2", "3+2", 300, 2000m, 750m, 13),
			Pack(branchId, zone2.Id, "VIP_DAY", "день", 0, 2000m, 750m, 14, value, value2, TariffDurationMode.TimeWindow),
			Pack(branchId, zone2.Id, "VIP_NIGHT", "ночь", 0, 2500m, 750m, 15, value3, value4, TariffDurationMode.TimeWindow),
			Hourly(branchId, zone3.Id, "BOOT_HOUR", "1 час", 750m, 21),
			Pack(branchId, zone3.Id, "BOOT_2P1", "2+1", 180, 1500m, 750m, 22),
			Pack(branchId, zone3.Id, "BOOT_3P2", "3+2", 300, 2000m, 750m, 23),
			Pack(branchId, zone3.Id, "BOOT_DAY", "день", 0, 2000m, 750m, 24, value, value2, TariffDurationMode.TimeWindow),
			Pack(branchId, zone3.Id, "BOOT_NIGHT", "ночь", 0, 2500m, 750m, 25, value3, value4, TariffDurationMode.TimeWindow),
			Hourly(branchId, zone4.Id, "PS5_HOUR", "1 час", 2000m, 31),
			Pack(branchId, zone4.Id, "PS5_2P1", "2+1", 180, 4000m, 2000m, 32),
			Pack(branchId, zone4.Id, "PS5_3P2", "3+2", 300, 5000m, 2000m, 33),
			Pack(branchId, zone4.Id, "PS5_DAY", "день", 0, 5000m, 2000m, 34, value, value2, TariffDurationMode.TimeWindow),
			Pack(branchId, zone4.Id, "PS5_NIGHT", "ночь", 0, 6000m, 2000m, 35, value3, value4, TariffDurationMode.TimeWindow)
		};
		HashSet<string> keepCodes = catalog.Select((Tariff t) => t.Code).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<Tariff> source2 = await db.Tariffs.Where((Tariff t) => t.BranchId == branchId).ToListAsync(cancellationToken);
		foreach (Tariff want in catalog)
		{
			Tariff tariff = source2.FirstOrDefault((Tariff t) => t.Code == want.Code);
			if (tariff == null)
			{
				db.Tariffs.Add(want);
				continue;
			}
			tariff.Name = want.Name;
			tariff.Description = want.Description;
			tariff.ZoneId = want.ZoneId;
			tariff.Kind = want.Kind;
			tariff.BillingMode = want.BillingMode;
			tariff.FixedDurationMinutes = want.FixedDurationMinutes;
			tariff.MinDurationMinutes = want.MinDurationMinutes;
			tariff.MaxDurationMinutes = want.MaxDurationMinutes;
			tariff.DaysOfWeekMask = 127;
			tariff.DurationMode = want.DurationMode;
			tariff.AvailableFrom = want.AvailableFrom;
			tariff.AvailableTo = want.AvailableTo;
			tariff.AllowPause = true;
			tariff.IsActive = true;
			tariff.SortOrder = want.SortOrder;
			tariff.UpdatedAt = DateTimeOffset.UtcNow;
		}
		List<Tariff> orphan = source2.Where((Tariff t) => !keepCodes.Contains(t.Code)).ToList();
		if (orphan.Count > 0)
		{
			List<Guid> orphanIds = orphan.Select((Tariff t) => t.Id).ToList();
			List<Guid> list = await (from s in db.GamingSessions.AsNoTracking()
				where orphanIds.Contains(s.TariffId)
				select s.TariffId).Distinct().ToListAsync(cancellationToken);
			foreach (Tariff item in orphan)
			{
				if (list.Contains(item.Id))
				{
					item.IsActive = false;
					item.UpdatedAt = DateTimeOffset.UtcNow;
				}
				else
				{
					db.Tariffs.Remove(item);
				}
			}
		}
		await db.SaveChangesAsync(cancellationToken);
		logger.LogInformation("Synced club tariffs ({Count} active catalog)", catalog.Count);
		static Tariff Hourly(Guid branchId2, Guid zoneId, string code, string name, decimal pricePerHour, int sort)
		{
			return new Tariff
			{
				BranchId = branchId2,
				ZoneId = zoneId,
				Code = code,
				Name = name,
				Description = "Почасовой",
				Kind = TariffKind.Hourly,
				BillingMode = BillingMode.PerMinute,
				PricePerHour = pricePerHour,
				MinCharge = 0m,
				MinDurationMinutes = 15,
				DaysOfWeekMask = 127,
				AllowPause = true,
				IsActive = true,
				SortOrder = sort
			};
		}
		static Tariff Pack(Guid branchId2, Guid zoneId, string code, string name, int minutes, decimal price, decimal hourlyRate, int sort, TimeSpan? from = null, TimeSpan? to = null, TariffDurationMode durationMode = TariffDurationMode.FixedDuration)
		{
			Tariff tariff2 = new Tariff
			{
				BranchId = branchId2,
				ZoneId = zoneId,
				Code = code,
				Name = name
			};
			Tariff tariff3 = tariff2;
			string description = ((durationMode != TariffDurationMode.TimeWindow) ? (minutes switch
			{
				180 => "2 часа оплата + 1 час бесплатно = 3 часа", 
				300 => "3 часа оплата + 2 часа бесплатно = 5 часов", 
				_ => $"Пакет {minutes} мин", 
			}) : ((from.HasValue && to.HasValue) ? $"До {(int)to.Value.TotalHours:00}:{to.Value.Minutes:00} (интервал {(int)from.Value.TotalHours:00}:{from.Value.Minutes:00}–{(int)to.Value.TotalHours:00}:{to.Value.Minutes:00})" : "Тариф по временному интервалу"));
			tariff3.Description = description;
			tariff2.Kind = TariffKind.Package;
			tariff2.BillingMode = BillingMode.PerMinute;
			tariff2.DurationMode = durationMode;
			tariff2.PricePerHour = hourlyRate;
			tariff2.FixedDurationMinutes = ((durationMode == TariffDurationMode.TimeWindow) ? ((int?)null) : new int?(minutes));
			tariff2.FixedPrice = price;
			tariff2.MinDurationMinutes = ((durationMode == TariffDurationMode.TimeWindow) ? ((int?)null) : new int?(minutes));
			tariff2.MaxDurationMinutes = ((durationMode == TariffDurationMode.TimeWindow) ? ((int?)null) : new int?(minutes));
			tariff2.DaysOfWeekMask = 127;
			tariff2.AvailableFrom = from;
			tariff2.AvailableTo = to;
			tariff2.AllowPause = true;
			tariff2.IsActive = true;
			tariff2.SortOrder = sort;
			return tariff2;
		}
	}

	private static async Task UpsertClubHookahAsync(ShiftClubDbContext db, ILogger logger, CancellationToken cancellationToken)
	{
		Guid branchId = await db.Branches.Select((Branch b) => b.Id).FirstAsync(cancellationToken);
		ProductCategory category = await db.ProductCategories.FirstOrDefaultAsync((ProductCategory c) => c.BranchId == branchId && c.Code == "HOOKAH", cancellationToken);
		if (category == null)
		{
			category = new ProductCategory
			{
				BranchId = branchId,
				Name = "Кальяны",
				Code = "HOOKAH",
				SortOrder = 4,
				IsActive = true
			};
			db.ProductCategories.Add(category);
			await db.SaveChangesAsync(cancellationToken);
			logger.LogInformation("Created HOOKAH category");
		}
		else
		{
			category.Name = "Кальяны";
			category.SortOrder = 4;
			category.IsActive = true;
			category.UpdatedAt = DateTimeOffset.UtcNow;
		}
		(string Sku, string Name, decimal Sale)[] catalog = new(string, string, decimal)[3]
		{
			("HOOKAH_LIGHT", "Кальян лайт", 3500m),
			("HOOKAH_HARD", "Кальян хард", 6500m),
			("HOOKAH_BOWL", "Замена чаши", 2000m)
		};
		List<string> skus = catalog.Select(((string Sku, string Name, decimal Sale) x) => x.Sku).ToList();
		List<Product> source = await db.Products.Where((Product p) => p.BranchId == branchId && skus.Contains(p.Sku)).ToListAsync(cancellationToken);
		(string, string, decimal)[] array = catalog;
		for (int num = 0; num < array.Length; num++)
		{
			(string, string, decimal) tuple = array[num];
			string sku = tuple.Item1;
			string item = tuple.Item2;
			decimal item2 = tuple.Item3;
			Product product = source.FirstOrDefault((Product p) => p.Sku == sku);
			if (product == null)
			{
				db.Products.Add(new Product
				{
					BranchId = branchId,
					CategoryId = category.Id,
					Name = item,
					Sku = sku,
					Unit = "шт",
					CostPrice = 0m,
					SalePrice = item2,
					StockQty = 999m,
					MinStockQty = 0m,
					IsActive = true
				});
			}
			else
			{
				product.CategoryId = category.Id;
				product.Name = item;
				product.Unit = "шт";
				if (product.StockQty < 1m)
				{
					product.StockQty = 999m;
				}
				product.MinStockQty = 0m;
				product.IsActive = true;
				product.UpdatedAt = DateTimeOffset.UtcNow;
			}
		}
		await db.SaveChangesAsync(cancellationToken);
		logger.LogInformation("Synced hookah catalog ({Count} products)", catalog.Length);
	}

	private static async Task EnsureClubNewsAsync(ShiftClubDbContext db, ILogger logger, CancellationToken cancellationToken)
	{
		if (!(await db.ClubNewsPosts.AnyAsync(cancellationToken)))
		{
			Guid guid = await db.Branches.Select((Branch b) => b.Id).FirstOrDefaultAsync(cancellationToken);
			if (!(guid == Guid.Empty))
			{
				db.ClubNewsPosts.AddRange(new ClubNewsPost
				{
					BranchId = guid,
					Title = "Добро пожаловать в SHIFT Club",
					Body = "Запускайте игры из каталога, заказывайте напитки из магазина — принесём к вашему ПК. Нужна помощь? Нажмите «Помощь».",
					Category = "Info",
					IsPublished = true,
					IsPinned = true,
					SortOrder = 1,
					PublishAt = DateTimeOffset.UtcNow
				}, new ClubNewsPost
				{
					BranchId = guid,
					Title = "Акции и турниры",
					Body = "Следите за новостями клуба здесь. Актуальные акции и расписание публикует администратор.",
					Category = "Promo",
					IsPublished = true,
					IsPinned = false,
					SortOrder = 10,
					PublishAt = DateTimeOffset.UtcNow
				});
				await db.SaveChangesAsync(cancellationToken);
				logger.LogInformation("Seeded club news posts");
			}
		}
	}
}
