using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Bar;
using ShiftClub.Shared.Contracts.Bookings;
using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Contracts.Computers;
using ShiftClub.Shared.Contracts.FloorMap;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.Contracts.TgWebApp;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared;

namespace ShiftClub.Infrastructure.Services;

public sealed class TgWebAppService : ITgWebAppService
{
	private sealed record TgInitUser(long Id, string DisplayName);

	private sealed record StaffLink(Guid? EmployeeId, Employee? Employee);

	private readonly ShiftClubDbContext _db;

	private readonly IClubSettingsService _settings;

	private readonly IPasswordHasher _passwordHasher;

	private readonly IBarService _bar;

	private readonly IComputerService _computers;

	private readonly ICashService _cash;

	private readonly IFloorMapService _floorMap;

	private readonly ISessionService _sessions;

	private readonly IBookingService _bookings;

	private readonly ITelegramAuthService _telegramAuth;

	private readonly ICaseService _cases;

	private readonly IConfiguration _configuration;

	public TgWebAppService(
		ShiftClubDbContext db,
		IClubSettingsService settings,
		IPasswordHasher passwordHasher,
		IBarService bar,
		IComputerService computers,
		ICashService cash,
		IFloorMapService floorMap,
		ISessionService sessions,
		IBookingService bookings,
		ITelegramAuthService telegramAuth,
		ICaseService cases,
		IConfiguration configuration)
	{
		_db = db;
		_settings = settings;
		_passwordHasher = passwordHasher;
		_bar = bar;
		_computers = computers;
		_cash = cash;
		_floorMap = floorMap;
		_sessions = sessions;
		_bookings = bookings;
		_telegramAuth = telegramAuth;
		_cases = cases;
		_configuration = configuration;
	}

	public async Task<TgWebAppAuthDto> AuthenticateAsync(string initData, CancellationToken cancellationToken = default(CancellationToken))
	{
		TgInitUser tgUser = await ValidateInitDataAsync(initData, cancellationToken);
		StaffLink staff = await ResolveStaffAsync(tgUser.Id, cancellationToken);
		Customer customer = await _db.Customers.Include((Customer c) => c.LoyaltyLevel).FirstOrDefaultAsync((Customer c) => c.TelegramUserId == (long?)tgUser.Id && c.IsActive, cancellationToken);
		if (customer == null)
		{
			return await IssueAuthAsync(null, tgUser.Id, staff, needsRegistration: true, tgUser.DisplayName);
		}
		return await IssueAuthAsync(customer, tgUser.Id, staff, needsRegistration: false);
	}

	public async Task<TgWebAppAuthDto> RegisterAsync(TgWebAppRegisterRequest request, CancellationToken cancellationToken = default(CancellationToken))
	{
		TgInitUser tgUser = await ValidateInitDataAsync(request.InitData, cancellationToken);
		string phone = PhoneDigits.Normalize(request.Phone);
		if (phone.Length < 10)
		{
			throw new InvalidOperationException("Укажите телефон — не менее 10 цифр.");
		}
		string first = (request.FirstName ?? "").Trim();
		string last = (request.LastName ?? "").Trim();
		if (first.Length < 1)
		{
			throw new InvalidOperationException("Укажите имя.");
		}
		if (last.Length < 1)
		{
			throw new InvalidOperationException("Укажите фамилию.");
		}
		string password = (request.Password ?? "").Trim();
		if (password.Length < 4)
		{
			throw new InvalidOperationException("Пароль: минимум 4 символа.");
		}
		string iin = NormalizeIin(request.Iin);
		if (await _db.Customers.AnyAsync((Customer c) => c.TelegramUserId == (long?)tgUser.Id && c.IsActive, cancellationToken))
		{
			throw new InvalidOperationException("Telegram уже привязан к аккаунту.");
		}
		Guid branchId = await (from b in _db.Branches.AsNoTracking()
			orderby b.CreatedAt
			select b.Id).FirstAsync(cancellationToken);
		bool flag = iin != null;
		if (flag)
		{
			flag = await _db.Customers.AnyAsync((Customer c) => c.BranchId == branchId && c.Iin == iin && c.IsActive, cancellationToken);
		}
		if (flag)
		{
			throw new InvalidOperationException("Этот ИИН уже зарегистрирован.");
		}
		string last10 = PhoneDigits.Last10(phone);
		Customer existing = await _db.Customers.Include((Customer c) => c.LoyaltyLevel).FirstOrDefaultAsync((Customer c) => c.BranchId == branchId && (c.Phone == phone || c.Phone.EndsWith(last10)), cancellationToken);
		if (existing != null)
		{
			if (existing.TelegramUserId.HasValue && existing.TelegramUserId != tgUser.Id)
			{
				throw new InvalidOperationException("Этот телефон уже привязан к другому Telegram.");
			}
			if (!VerifyCustomerSecret(existing, password))
			{
				throw new InvalidOperationException("Аккаунт с этим телефоном уже есть. Введите правильный пароль (как на ПК) или откройте «У меня уже есть аккаунт».");
			}
			existing.TelegramUserId = tgUser.Id;
			Customer customer = existing;
			DateTimeOffset? telegramLinkedAt = customer.TelegramLinkedAt;
			telegramLinkedAt.GetValueOrDefault();
			if (!telegramLinkedAt.HasValue)
			{
				DateTimeOffset utcNow = DateTimeOffset.UtcNow;
				customer.TelegramLinkedAt = utcNow;
			}
			existing.FirstName = first;
			existing.LastName = last;
			if (string.IsNullOrWhiteSpace(existing.PasswordHash))
			{
				existing.PasswordHash = _passwordHasher.Hash(password);
			}
			if (iin != null)
			{
				existing.Iin = iin;
			}
			existing.UpdatedAt = DateTimeOffset.UtcNow;
			await _db.SaveChangesAsync(cancellationToken);
			StaffLink staff = await ResolveStaffAsync(tgUser.Id, cancellationToken);
			return await IssueAuthAsync(existing, tgUser.Id, staff, needsRegistration: false);
		}
		LoyaltyLevel loyalty = await (from l in _db.LoyaltyLevels
			where l.BranchId == branchId && l.IsActive
			orderby l.SortOrder
			select l).FirstOrDefaultAsync(cancellationToken);
		Customer customer2 = new Customer
		{
			BranchId = branchId,
			FirstName = first,
			LastName = last,
			Phone = phone,
			Iin = iin,
			LoyaltyLevelId = loyalty?.Id,
			TelegramUserId = tgUser.Id,
			TelegramLinkedAt = DateTimeOffset.UtcNow,
			PasswordHash = _passwordHasher.Hash(password),
			IsActive = true,
			AllowNotifications = true
		};
		_db.Customers.Add(customer2);
		await _db.SaveChangesAsync(cancellationToken);
		try
		{
			await _cases.TryGrantRegistrationKeyAsync(customer2.Id, cancellationToken);
		}
		catch
		{
		}
		customer2.LoyaltyLevel = loyalty;
		StaffLink staff2 = await ResolveStaffAsync(tgUser.Id, cancellationToken);
		return await IssueAuthAsync(customer2, tgUser.Id, staff2, needsRegistration: false);
	}

	public async Task<TgWebAppAuthDto> LinkExistingAsync(TgWebAppLinkRequest request, CancellationToken cancellationToken = default(CancellationToken))
	{
		TgInitUser tgUser = await ValidateInitDataAsync(request.InitData, cancellationToken);
		if (await _db.Customers.AnyAsync((Customer c) => c.TelegramUserId == (long?)tgUser.Id && c.IsActive, cancellationToken))
		{
			throw new InvalidOperationException("Этот Telegram уже привязан к аккаунту клуба.");
		}
		string login = (request.PhoneOrLogin ?? "").Trim();
		if (string.IsNullOrWhiteSpace(login))
		{
			throw new InvalidOperationException("Укажите телефон или логин аккаунта клуба.");
		}
		string password = (request.Password ?? "").Trim();
		if (password.Length < 1)
		{
			throw new InvalidOperationException("Введите пароль или PIN с ПК.");
		}
		Guid branchId = await (from b in _db.Branches.AsNoTracking()
			orderby b.CreatedAt
			select b.Id).FirstAsync(cancellationToken);
		string digits = DigitsOnly(login);
		object obj;
		if (digits.Length < 10)
		{
			obj = null;
		}
		else
		{
			string text = digits;
			int length = text.Length;
			int num = length - 10;
			obj = text.Substring(num, length - num);
		}
		string last10 = (string)obj;
		Customer customer = (await _db.Customers.Include((Customer c) => c.LoyaltyLevel).FirstOrDefaultAsync((Customer c) => c.BranchId == branchId && c.IsActive && (c.Phone == login || c.Phone == digits || (c.Login != null && c.Login == login) || (last10 != null && c.Phone.EndsWith(last10))), cancellationToken)) ?? throw new InvalidOperationException("Аккаунт не найден. Проверьте телефон/логин или создайте новый.");
		if (customer.TelegramUserId.HasValue && customer.TelegramUserId != tgUser.Id)
		{
			throw new InvalidOperationException("Этот аккаунт уже привязан к другому Telegram.");
		}
		if (!VerifyCustomerSecret(customer, password))
		{
			throw new InvalidOperationException("Неверный пароль или PIN.");
		}
		customer.TelegramUserId = tgUser.Id;
		Customer customer2 = customer;
		DateTimeOffset? telegramLinkedAt = customer2.TelegramLinkedAt;
		telegramLinkedAt.GetValueOrDefault();
		if (!telegramLinkedAt.HasValue)
		{
			DateTimeOffset utcNow = DateTimeOffset.UtcNow;
			customer2.TelegramLinkedAt = utcNow;
		}
		customer.UpdatedAt = DateTimeOffset.UtcNow;
		await _db.SaveChangesAsync(cancellationToken);
		StaffLink staff = await ResolveStaffAsync(tgUser.Id, cancellationToken);
		return await IssueAuthAsync(customer, tgUser.Id, staff, needsRegistration: false);
	}

	public async Task<TgWebAppQrConfirmDto> ConfirmQrAsync(TgWebAppQrConfirmRequest request, string? bearerToken, CancellationToken cancellationToken = default(CancellationToken))
	{
		string payload = (request.QrPayload ?? "").Trim();
		if (string.IsNullOrWhiteSpace(payload))
		{
			throw new InvalidOperationException("Пустой QR или код.");
		}
		long telegramUserId;
		string displayName;
		if (!string.IsNullOrWhiteSpace(request.InitData))
		{
			TgInitUser tgInitUser = await ValidateInitDataAsync(request.InitData, cancellationToken);
			telegramUserId = tgInitUser.Id;
			displayName = tgInitUser.DisplayName;
		}
		else
		{
			TgAccessSession tgAccessSession = ((!string.IsNullOrWhiteSpace(bearerToken)) ? ValidateAccessToken(bearerToken) : null);
			if ((object)tgAccessSession == null)
			{
				throw new InvalidOperationException("Нет данных Telegram. Откройте приложение из бота клуба.");
			}
			telegramUserId = tgAccessSession.TelegramUserId;
			displayName = null;
			Guid? customerId = tgAccessSession.CustomerId;
			if (customerId.HasValue)
			{
				Guid cid = customerId.GetValueOrDefault();
				Customer customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync((Customer x) => x.Id == cid, cancellationToken);
				if (customer != null)
				{
					displayName = (customer.FirstName + " " + customer.LastName).Trim();
				}
			}
		}
		string code = _telegramAuth.TryExtractTicketCode(payload) ?? throw new InvalidOperationException("Не удалось распознать код. Наведите камеру на QR на экране ПК.");
		TelegramAuthTicket telegramAuthTicket = await _db.TelegramAuthTickets.AsNoTracking().FirstOrDefaultAsync((TelegramAuthTicket t) => t.Code == code, cancellationToken);
		string purpose = telegramAuthTicket?.Purpose ?? "Unknown";
		Guid? ticketId = telegramAuthTicket?.Id;
		string message = await _telegramAuth.CompleteTicketFromTelegramAsync(code, telegramUserId, displayName, cancellationToken);
		TelegramAuthTicket after = await _db.TelegramAuthTickets.AsNoTracking().FirstOrDefaultAsync((TelegramAuthTicket t) => t.Code == code, cancellationToken);
		bool needsReg = after?.Status == "AwaitingRegistration";
		StaffLink staff = await ResolveStaffAsync(telegramUserId, cancellationToken);
		Customer customer2 = await _db.Customers.Include((Customer c) => c.LoyaltyLevel).FirstOrDefaultAsync((Customer c) => c.TelegramUserId == (long?)telegramUserId && c.IsActive, cancellationToken);
		TgWebAppAuthDto auth = await IssueAuthAsync(customer2, telegramUserId, staff, needsReg || customer2 == null, displayName ?? after?.PendingDisplayName);
		return new TgWebAppQrConfirmDto(message.TrimStart('✅', ' ').Trim(), purpose, auth, needsReg, ticketId ?? after?.Id, code);
	}

	public async Task<TgWebAppQrConfirmDto> CompleteQrRegistrationAsync(TgWebAppQrRegisterRequest request, CancellationToken cancellationToken = default(CancellationToken))
	{
		TgInitUser tgUser = await ValidateInitDataAsync(request.InitData, cancellationToken);
		(string, Guid) tuple = await _telegramAuth.CompleteRegistrationFromTelegramAsync(request.TicketCode, tgUser.Id, request.Phone, request.FirstName, request.LastName, request.Password, request.Iin, request.LinkExisting, cancellationToken);
		string message = tuple.Item1;
		Guid customerId = tuple.Item2;
		Customer customer = await _db.Customers.Include((Customer c) => c.LoyaltyLevel).FirstAsync((Customer c) => c.Id == customerId, cancellationToken);
		StaffLink staff = await ResolveStaffAsync(tgUser.Id, cancellationToken);
		TgWebAppAuthDto auth = await IssueAuthAsync(customer, tgUser.Id, staff, needsRegistration: false, tgUser.DisplayName);
		TelegramAuthTicket telegramAuthTicket = await _db.TelegramAuthTickets.AsNoTracking().FirstOrDefaultAsync((TelegramAuthTicket t) => t.Code == (_telegramAuth.TryExtractTicketCode(request.TicketCode) ?? request.TicketCode.Trim().ToUpperInvariant()), cancellationToken);
		return new TgWebAppQrConfirmDto(message.TrimStart('✅', ' ').Trim(), telegramAuthTicket?.Purpose ?? "Login", auth, NeedsRegistration: false, telegramAuthTicket?.Id, telegramAuthTicket?.Code);
	}

	public async Task<TgWebAppHomeDto> GetHomeAsync(Guid customerId, CancellationToken cancellationToken = default(CancellationToken))
	{
		TgWebAppSessionDto session = await GetActiveSessionAsync(customerId, cancellationToken);
		TgWebAppAuthDto profile = await GetProfileAsync(customerId, cancellationToken);
		if (session?.ZoneId is Guid zid)
		{
			IReadOnlyList<TgZoneTimeBankDto> banks = profile.TimeBanks ?? Array.Empty<TgZoneTimeBankDto>();
			int zoneMins = banks.FirstOrDefault((TgZoneTimeBankDto b) => b.ZoneId == zid)?.Minutes ?? 0;
			profile = profile with
			{
				CurrentZoneId = zid,
				CurrentZoneName = session.ZoneName,
				CurrentZoneTimeBankMinutes = zoneMins
			};
		}
		DateTimeOffset now = DateTimeOffset.UtcNow;
		return new TgWebAppHomeDto(profile, session, await (from n in (from n in _db.ClubNewsPosts.AsNoTracking()
				where n.IsPublished && (n.PublishAt == null || n.PublishAt <= now) && (n.ExpireAt == null || n.ExpireAt > now)
				orderby n.IsPinned descending, n.SortOrder, n.CreatedAt descending
				select n).Take(8)
			select new TgWebAppNewsItemDto(n.Id, n.Title, n.Body, n.CreatedAt)).ToListAsync(cancellationToken));
	}

	public async Task<TgWebAppAuthDto> GetProfileAsync(Guid customerId, CancellationToken cancellationToken = default(CancellationToken))
	{
		Customer customer = (await _db.Customers.AsNoTracking().Include((Customer c) => c.LoyaltyLevel).FirstOrDefaultAsync((Customer c) => c.Id == customerId && c.IsActive, cancellationToken)) ?? throw new KeyNotFoundException("Аккаунт не найден");
		long tgId = customer.TelegramUserId.GetValueOrDefault();
		StaffLink staffLink = ((tgId <= 0) ? null : (await ResolveStaffAsync(tgId, cancellationToken)));
		StaffLink staff = staffLink;
		return await IssueAuthAsync(customer, tgId, staff, needsRegistration: false);
	}

	public async Task<TgWebAppAuthDto> UpdateProfileAsync(Guid customerId, long telegramUserId, TgWebAppUpdateProfileRequest request, CancellationToken cancellationToken = default(CancellationToken))
	{
		Customer customer = (await _db.Customers.Include((Customer c) => c.LoyaltyLevel).FirstOrDefaultAsync((Customer c) => c.Id == customerId && c.IsActive, cancellationToken)) ?? throw new KeyNotFoundException("Аккаунт не найден");
		string first = (request.FirstName ?? "").Trim();
		string last = (request.LastName ?? "").Trim();
		if (first.Length < 1)
		{
			throw new InvalidOperationException("Укажите имя.");
		}
		if (last.Length < 1)
		{
			throw new InvalidOperationException("Укажите фамилию.");
		}
		string phone = PhoneDigits.Normalize(request.Phone);
		if (phone.Length < 10)
		{
			throw new InvalidOperationException("Укажите телефон — не менее 10 цифр.");
		}
		if (await _db.Customers.AsNoTracking().AnyAsync((Customer c) => c.Id != customerId && c.BranchId == customer.BranchId && c.Phone == phone && c.IsActive, cancellationToken))
		{
			throw new InvalidOperationException("Этот телефон уже занят.");
		}
		string iin = NormalizeIin(request.Iin);
		if (iin != null && await _db.Customers.AsNoTracking().AnyAsync((Customer c) => c.Id != customerId && c.BranchId == customer.BranchId && c.Iin == iin && c.IsActive, cancellationToken))
		{
			throw new InvalidOperationException("Этот ИИН уже занят.");
		}
		customer.FirstName = first;
		customer.LastName = last;
		customer.Phone = phone;
		customer.Iin = iin;
		if (!string.IsNullOrWhiteSpace(request.NewPassword))
		{
			string text = request.NewPassword.Trim();
			if (text.Length < 4)
			{
				throw new InvalidOperationException("Пароль: минимум 4 символа.");
			}
			customer.PasswordHash = _passwordHasher.Hash(text);
		}
		customer.UpdatedAt = DateTimeOffset.UtcNow;
		await _db.SaveChangesAsync(cancellationToken);
		StaffLink staff = await ResolveStaffAsync(telegramUserId, cancellationToken);
		Employee emp = staff?.Employee;
		if (emp != null)
		{
			emp.FirstName = first;
			emp.LastName = last;
			emp.DisplayName = (first + " " + last).Trim();
			emp.Phone = phone;
			if (iin != null)
			{
				emp.Iin = iin;
			}
			if (!string.IsNullOrWhiteSpace(request.NewPassword))
			{
				EmployeeCredential employeeCredential = await _db.Set<EmployeeCredential>().FirstOrDefaultAsync((EmployeeCredential c) => c.EmployeeId == emp.Id, cancellationToken);
				if (employeeCredential != null)
				{
					employeeCredential.PasswordHash = _passwordHasher.Hash(request.NewPassword.Trim());
					employeeCredential.PasswordChangedAt = DateTimeOffset.UtcNow;
				}
			}
			await _db.SaveChangesAsync(cancellationToken);
		}
		return await IssueAuthAsync(customer, telegramUserId, staff, needsRegistration: false);
	}

	public async Task<TgWebAppSessionDto?> GetActiveSessionAsync(Guid customerId, CancellationToken cancellationToken = default(CancellationToken))
	{
		GamingSession gamingSession = await (from s in _db.GamingSessions.AsNoTracking().Include((GamingSession s) => s.Computer).ThenInclude((Computer c) => c.Zone)
				.Include((GamingSession s) => s.Tariff)
			where s.CustomerId == customerId && ((int)s.Status == 3 || (int)s.Status == 4)
			orderby s.StartedAt descending
			select s).FirstOrDefaultAsync(cancellationToken);
		if (gamingSession == null)
		{
			return null;
		}
		DateTimeOffset? plannedEndsAt = gamingSession.PlannedEndsAt;
		int num;
		if (plannedEndsAt.HasValue)
		{
			DateTimeOffset valueOrDefault = plannedEndsAt.GetValueOrDefault();
			num = Math.Max(0, (int)Math.Ceiling((valueOrDefault - DateTimeOffset.UtcNow).TotalMinutes));
		}
		else
		{
			num = gamingSession.DurationMinutes;
		}
		int minutesLeft = num;
		return new TgWebAppSessionDto(gamingSession.Id, gamingSession.Computer.DisplayName ?? gamingSession.Computer.WindowsName, gamingSession.Computer.Zone?.Name, gamingSession.Tariff?.Name, gamingSession.Status.ToString(), minutesLeft, gamingSession.StartedAt, gamingSession.PlannedEndsAt, gamingSession.Computer.ZoneId, gamingSession.ComputerId);
	}

	public async Task EndSessionAsync(Guid customerId, bool saveRemainingToTimeBank, CancellationToken cancellationToken = default(CancellationToken))
	{
		GamingSession session = (await (from s in _db.GamingSessions.AsNoTracking()
			where s.CustomerId == customerId && ((int)s.Status == 3 || (int)s.Status == 4)
			orderby s.StartedAt descending
			select s).FirstOrDefaultAsync(cancellationToken)) ?? throw new InvalidOperationException("Нет активного сеанса");
		Guid employeeId = await (from e in _db.Employees.AsNoTracking()
			where e.BranchId == session.BranchId && e.IsActive
			orderby e.CreatedAt
			select e.Id).FirstOrDefaultAsync(cancellationToken);
		await _sessions.EndAsync(session.Id, new EndSessionRequest(ForceUnpaid: false, "telegram-miniapp", saveRemainingToTimeBank), employeeId, cancellationToken);
	}

	public async Task<TgWebAppBarCatalogDto> GetBarCatalogAsync(Guid customerId, CancellationToken cancellationToken = default(CancellationToken))
	{
		Guid value = await (from c in _db.Customers.AsNoTracking()
			where c.Id == customerId
			select c.BranchId).FirstAsync(cancellationToken);
		return new TgWebAppBarCatalogDto((from p in await _bar.GetProductsAsync(value, null, includeInactive: false, cancellationToken)
			where p.IsActive && p.StockQty > 0m
			select new TgWebAppBarProductDto(p.Id, p.CategoryName, p.Name, p.SalePrice, IsAvailable: true, p.ImageUrl)).ToList());
	}

	public async Task<TgWebAppOrderDto> PlaceBarOrderAsync(Guid customerId, TgWebAppPlaceOrderRequest request, CancellationToken cancellationToken = default(CancellationToken))
	{
		GamingSession gamingSession = (await (from s in _db.GamingSessions.AsNoTracking()
			where s.CustomerId == customerId && ((int)s.Status == 3 || (int)s.Status == 4)
			orderby s.StartedAt descending
			select s).FirstOrDefaultAsync(cancellationToken)) ?? throw new InvalidOperationException("Заказ из бара доступен только во время активного сеанса на ПК клуба. Сначала войдите на компьютер.");
		if (gamingSession.ComputerId == Guid.Empty)
		{
			throw new InvalidOperationException("Сеанс не привязан к ПК. Войдите на компьютер клуба.");
		}
		List<CreateBarOrderItemRequest> list = (from i in request.Items ?? Array.Empty<TgWebAppOrderItemRequest>()
			where i.Quantity > 0m
			select new CreateBarOrderItemRequest(i.ProductId, i.Quantity)).ToList();
		if (list.Count == 0)
		{
			throw new InvalidOperationException("Корзина пуста");
		}
		BarOrderPaymentMode barOrderPaymentMode;
		switch ((request.PaymentMode ?? "PayAtCashier").ToLowerInvariant())
		{
		case "balance":
			throw new InvalidOperationException("Оплата бара с баланса недоступна — наличные или Kaspi QR при выдаче");
		case "kaspiqr":
		case "kaspi":
			barOrderPaymentMode = BarOrderPaymentMode.KaspiQrOnDelivery;
			break;
		case "cardondelivery":
		case "card":
			barOrderPaymentMode = BarOrderPaymentMode.CardOnDelivery;
			break;
		default:
			barOrderPaymentMode = BarOrderPaymentMode.PayAtCashier;
			break;
		}
		BarOrderPaymentMode paymentMode = barOrderPaymentMode;
		BarOrderDto barOrderDto = await _bar.CreateOrderAsync(new CreateBarOrderRequest(gamingSession.ComputerId, gamingSession.Id, paymentMode, "Заказ из Telegram", Guid.NewGuid().ToString("N"), list), null, cancellationToken);
		return new TgWebAppOrderDto(barOrderDto.Id, barOrderDto.Status.ToString(), barOrderDto.Total, barOrderDto.CreatedAt, barOrderDto.Items.Select((BarOrderItemDto i) => $"{i.ProductName} ×{i.Quantity:0}").ToList());
	}

	public async Task<IReadOnlyList<TgWebAppOrderDto>> GetBarOrdersAsync(Guid customerId, CancellationToken cancellationToken = default(CancellationToken))
	{
		return (await (from o in _db.BarOrders.AsNoTracking().Include((BarOrder o) => o.Items)
			where o.CustomerId == customerId
			orderby o.CreatedAt descending
			select o).Take(20).ToListAsync(cancellationToken)).Select((BarOrder o) => new TgWebAppOrderDto(o.Id, o.Status.ToString(), o.Total, o.CreatedAt, o.Items.Select((BarOrderItem i) => $"{i.ProductName} ×{i.Quantity:0}").ToList())).ToList();
	}

	public async Task<TgWebAppAuthDto> UpdateComfortAsync(Guid customerId, TgWebAppComfortRequest request, CancellationToken cancellationToken = default(CancellationToken))
	{
		Customer customer = (await _db.Customers.Include((Customer c) => c.LoyaltyLevel).FirstOrDefaultAsync((Customer c) => c.Id == customerId && c.IsActive, cancellationToken)) ?? throw new KeyNotFoundException("Аккаунт не найден");
		customer.ComfortHideBalance = request.ComfortHideBalance;
		customer.ComfortSoundEnabled = request.ComfortSoundEnabled;
		customer.ComfortLanguage = (string.IsNullOrWhiteSpace(request.ComfortLanguage) ? "ru" : request.ComfortLanguage.Trim().ToLowerInvariant());
		customer.ComfortBrightness = Math.Clamp(request.ComfortBrightness, 40, 100);
		customer.UpdatedAt = DateTimeOffset.UtcNow;
		await _db.SaveChangesAsync(cancellationToken);
		long tgId = customer.TelegramUserId.GetValueOrDefault();
		StaffLink staffLink = ((tgId <= 0) ? null : (await ResolveStaffAsync(tgId, cancellationToken)));
		StaffLink staff = staffLink;
		return await IssueAuthAsync(customer, tgId, staff, needsRegistration: false);
	}

	public async Task<TgStaffHomeDto> GetStaffHomeAsync(long telegramUserId, Guid? employeeId, CancellationToken cancellationToken = default(CancellationToken))
	{
		StaffLink staff = (await ResolveStaffAsync(telegramUserId, cancellationToken)) ?? throw new UnauthorizedAccessException("Нет доступа сотрудника");
		if (employeeId.HasValue && staff.EmployeeId != employeeId)
		{
			throw new UnauthorizedAccessException("Нет доступа сотрудника");
		}
		List<ComputerDto> source = (await _computers.GetComputersAsync(null, cancellationToken)).Where((ComputerDto c) => c.IsApproved).ToList();
		var openBookings = await LoadOpenBookingsForTodayAsync(cancellationToken);
		var bookingByPc = IndexBookingsByComputer(openBookings);

		int free = 0;
		int busy = 0;
		int offline = 0;
		foreach (var c in source)
		{
			switch (ClassifyFloorPc(c.Occupancy, c.OccupancyDetail, c.CurrentSessionId, bookingByPc.ContainsKey(c.Id)))
			{
				case "onlineFree": free++; break;
				case "offlineFree": offline++; break;
				case "busy": busy++; break;
			}
		}
		int maintenance = source.Count(c => c.IsMaintenance || c.Occupancy is "Maintenance" or "Updating" or "Setup");

		TgStaffFloorSummaryDto floor = new TgStaffFloorSummaryDto(free, busy, offline, maintenance, (from c in source
			orderby c.DisplayName
			select MapStaffPc(c, bookingByPc)).ToList());
		List<TgStaffBarOrderDto> bar = (from o in (await _bar.GetOpenOrdersAsync(null, cancellationToken)).OrderBy((BarOrderDto o) => o.CreatedAt).Take(30)
			select new TgStaffBarOrderDto(o.Id, o.Status.ToString(), o.Total, o.CreatedAt, o.ComputerName, o.Items.Select((BarOrderItemDto i) => $"{i.ProductName} ×{i.Quantity:0}").ToList())).ToList();
		TgStaffShiftDto shiftDto = null;
		Guid? employeeId2 = staff.EmployeeId;
		if (employeeId2.HasValue)
		{
			Guid valueOrDefault = employeeId2.GetValueOrDefault();
			CashShiftDto cashShiftDto = await _cash.GetOpenShiftAsync(null, valueOrDefault, cancellationToken);
			if ((object)cashShiftDto != null)
			{
				shiftDto = new TgStaffShiftDto(cashShiftDto.Id, cashShiftDto.CashRegisterName, cashShiftDto.OpenedAt, cashShiftDto.SalesCash, cashShiftDto.SalesKaspi);
			}
		}
		var bookingDtos = openBookings
			.OrderBy(b => b.StartsAt)
			.Select(b => new TgStaffBookingDto(
				b.Id,
				b.Number,
				b.ContactName,
				b.ContactPhone,
				b.Status.ToString(),
				b.StartsAt,
				b.EndsAt,
				b.Computers.Select(c => c.ComputerName ?? "ПК").ToList()))
			.ToList();
		Customer customer = await _db.Customers.AsNoTracking().Include((Customer c) => c.LoyaltyLevel).FirstOrDefaultAsync((Customer c) => c.TelegramUserId == (long?)telegramUserId && c.IsActive, cancellationToken);
		return new TgStaffHomeDto(await IssueAuthAsync(customer, telegramUserId, staff, customer == null), floor, bar, shiftDto, bookingDtos);
	}

	public async Task<TgFloorMapDto> GetFloorMapAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		FloorMapDto floorMapDto = await _floorMap.GetMapAsync(null, cancellationToken);
		var openBookings = await LoadOpenBookingsForTodayAsync(cancellationToken);
		var bookingByPc = IndexBookingsByComputer(openBookings);
		List<TgFloorPcDto> list = floorMapDto.Computers.Where((ComputerDto c) => c.IsApproved).Select(delegate(ComputerDto c)
		{
			bookingByPc.TryGetValue(c.Id, out var booking);
			var remSec = c.RemainingSeconds;
			int? remainingMinutes = remSec.HasValue ? (int)Math.Ceiling(remSec.Value / 60.0) : null;
			var occupancy = c.Occupancy;
			if (booking is not null
			    && c.CurrentSessionId is null
			    && occupancy is "Free" or "Reserved")
			{
				occupancy = "Reserved";
			}
			return new TgFloorPcDto(
				c.Id,
				c.DisplayName ?? c.WindowsName,
				occupancy,
				c.OccupancyDetail,
				c.ZoneName,
				c.ZoneColorHex,
				c.GridCol,
				c.GridRow,
				remainingMinutes,
				c.CurrentSessionId,
				c.StationKind.ToString(),
				c.SessionGuestName,
				booking?.Id,
				booking?.ContactName,
				booking?.StartsAt,
				booking?.EndsAt);
		}).ToList();
		int free = 0, busy = 0, reserved = 0, offline = 0, maintenance = 0;
		foreach (var p in list)
		{
			switch (ClassifyFloorPc(p.Occupancy, p.OccupancyDetail, p.SessionId, p.BookingId.HasValue))
			{
				case "onlineFree": free++; break;
				case "offlineFree": offline++; break;
				case "busy": busy++; break;
				case "reserved": reserved++; break;
				case "maintenance": maintenance++; break;
			}
		}
		List<TgFloorElementDto> elements = (from e in floorMapDto.Elements
			where e.IsVisible
			select new TgFloorElementDto(e.Id, e.Kind.ToString(), e.Label, e.GridCol, e.GridRow, e.ColSpan, e.RowSpan, e.ColorHex ?? e.FillHex)).ToList();
		return new TgFloorMapDto(floorMapDto.BranchId, floorMapDto.GridCols, floorMapDto.GridRows, floorMapDto.BackgroundHex, new TgFloorCountsDto(free, busy, reserved, offline, maintenance, list.Count), list, elements);
	}

	public async Task<IReadOnlyList<TgStaffTariffDto>> GetStaffTariffsAsync(
		Guid employeeId,
		Guid? computerId,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		Guid? zoneId = null;
		if (computerId is Guid cid)
		{
			zoneId = await _db.Computers.AsNoTracking()
				.Where(c => c.Id == cid)
				.Select(c => c.ZoneId)
				.FirstOrDefaultAsync(cancellationToken);
		}

		var tariffs = await _sessions.GetTariffsAsync(null, false, true, zoneId, cancellationToken);
		return tariffs
			.Where(t => t.IsActive)
			.OrderBy(t => t.SortOrder)
			.ThenBy(t => t.Name)
			.Select(t => new TgStaffTariffDto(
				t.Id,
				t.Name,
				t.Code,
				t.Kind.ToString(),
				t.DurationMode.ToString(),
				t.PricePerHour,
				t.FixedDurationMinutes,
				t.FixedPrice,
				t.ZoneName,
				t.SalePreview,
				t.IsAvailableNow))
			.ToList();
	}

	public async Task<IReadOnlyList<TgStaffCustomerHitDto>> SearchStaffCustomersAsync(
		Guid employeeId,
		string query,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		var q = (query ?? "").Trim();
		if (q.Length < 2)
			return Array.Empty<TgStaffCustomerHitDto>();

		var digits = new string(q.Where(char.IsDigit).ToArray());
		var rows = await _db.Customers.AsNoTracking()
			.Where(c => c.IsActive && (
				c.Phone.Contains(q) ||
				c.FirstName.Contains(q) ||
				c.LastName.Contains(q) ||
				(c.Login != null && c.Login.Contains(q)) ||
				(digits.Length >= 4 && c.Phone.Contains(digits))))
			.OrderBy(c => c.LastName)
			.ThenBy(c => c.FirstName)
			.Take(20)
			.Select(c => new
			{
				c.Id,
				c.FirstName,
				c.LastName,
				c.Phone,
				c.Balance,
				c.TimeBankMinutes
			})
			.ToListAsync(cancellationToken);

		return rows.Select(c => new TgStaffCustomerHitDto(
			c.Id,
			$"{c.FirstName} {c.LastName}".Trim(),
			c.Phone,
			c.Balance,
			c.TimeBankMinutes)).ToList();
	}

	public async Task<SessionDto> StaffStartSessionAsync(
		Guid employeeId,
		TgStaffStartSessionRequest request,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var pay))
			throw new InvalidOperationException("Неизвестный способ оплаты.");
		if (request.DurationMinutes < 1 && !request.UseTimeBank)
			throw new InvalidOperationException("Укажите длительность сеанса.");

		if (request.WakeIfOffline)
		{
			var computer = await _computers.GetByIdAsync(request.ComputerId, cancellationToken)
				?? throw new KeyNotFoundException("ПК не найден");
			var offline =
				computer.Status is ComputerStatus.Offline or ComputerStatus.Error
				|| string.Equals(computer.OccupancyDetail, "Офлайн", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(computer.Occupancy, "Offline", StringComparison.OrdinalIgnoreCase);
			if (offline && !string.IsNullOrWhiteSpace(computer.MacAddress))
			{
				await _computers.WakeAsync(request.ComputerId, employeeId, cancellationToken);
				await Task.Delay(1200, cancellationToken);
			}
		}

		return await _sessions.StartGuestSessionAsync(
			new StartGuestSessionRequest(
				request.ComputerId,
				request.TariffId,
				request.DurationMinutes,
				pay,
				request.GuestName,
				request.CustomerId,
				Guid.NewGuid().ToString("N"),
				request.UseTimeBank),
			employeeId,
			cancellationToken);
	}

	public async Task<SessionDto> StaffExtendSessionAsync(
		Guid employeeId,
		Guid sessionId,
		TgStaffExtendSessionRequest request,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, ignoreCase: true, out var pay))
			throw new InvalidOperationException("Неизвестный способ оплаты.");
		if (request.AdditionalMinutes < 1 && request.TariffId is null)
			throw new InvalidOperationException("Укажите минуты продления или тариф.");

		return await _sessions.ExtendAsync(
			sessionId,
			new ExtendSessionRequest(
				request.AdditionalMinutes,
				pay,
				request.AmountTendered,
				Guid.NewGuid().ToString("N"),
				null,
				request.TariffId),
			employeeId,
			cancellationToken);
	}

	public Task<ExtendSessionQuoteDto> StaffQuoteExtendAsync(
		Guid employeeId,
		Guid sessionId,
		int additionalMinutes,
		CancellationToken cancellationToken = default,
		Guid? tariffId = null)
	{
		_ = employeeId;
		return _sessions.QuoteExtendAsync(sessionId, additionalMinutes, cancellationToken, tariffId);
	}

	public async Task<SessionDto> StaffEndSessionAsync(
		Guid employeeId,
		Guid sessionId,
		TgStaffEndSessionRequest request,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		return await _sessions.EndAsync(
			sessionId,
			new EndSessionRequest(
				ForceUnpaid: false,
				Reason: request.Reason ?? "telegram-miniapp-staff",
				SaveRemainingToTimeBank: request.SaveRemainingToTimeBank),
			employeeId,
			cancellationToken);
	}

	public async Task<SessionDto> StaffPauseSessionAsync(
		Guid employeeId,
		Guid sessionId,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		return await _sessions.PauseAsync(sessionId, employeeId, cancellationToken);
	}

	public async Task<SessionDto> StaffResumeSessionAsync(
		Guid employeeId,
		Guid sessionId,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		return await _sessions.ResumeAsync(sessionId, employeeId, cancellationToken);
	}

	public async Task StaffWakeComputerAsync(
		Guid employeeId,
		Guid computerId,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		await _computers.WakeAsync(computerId, employeeId, cancellationToken);
	}

	public async Task<IReadOnlyList<TgStaffTransferTargetDto>> StaffTransferTargetsAsync(
		Guid employeeId,
		Guid sessionId,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		var session = await _sessions.GetByIdAsync(sessionId, cancellationToken)
			?? throw new KeyNotFoundException("Сеанс не найден");
		var pcs = await _computers.GetComputersAsync(null, cancellationToken);
		return pcs
			.Where(c => c.IsApproved
				&& c.Id != session.ComputerId
				&& c.ZoneId == session.ZoneId
				&& c.CurrentSessionId is null
				&& !c.IsMaintenance
				&& c.Occupancy is "Free" or "Offline")
			.OrderBy(c => c.DisplayName)
			.Take(40)
			.Select(c => new TgStaffTransferTargetDto(
				c.Id,
				c.DisplayName ?? c.WindowsName,
				c.Occupancy,
				c.OccupancyDetail,
				c.ZoneName,
				IsOfflineFree(c.Occupancy, c.OccupancyDetail)))
			.ToList();
	}

	public async Task<SessionDto> StaffTransferSessionAsync(
		Guid employeeId,
		Guid sessionId,
		TgStaffTransferRequest request,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		var target = await _computers.GetByIdAsync(request.TargetComputerId, cancellationToken)
			?? throw new KeyNotFoundException("Целевой ПК не найден");
		if (IsOfflineFree(target.Occupancy, target.OccupancyDetail)
			|| target.Status is ComputerStatus.Offline or ComputerStatus.Error)
		{
			if (!string.IsNullOrWhiteSpace(target.MacAddress))
			{
				await _computers.WakeAsync(request.TargetComputerId, employeeId, cancellationToken);
				await Task.Delay(800, cancellationToken);
			}
		}
		return await _sessions.TransferAsync(
			sessionId,
			new TransferSessionRequest(request.TargetComputerId, Guid.NewGuid().ToString("N")),
			employeeId,
			cancellationToken);
	}

	public async Task<BookingDto> StaffMarkBookingArrivedAsync(
		Guid employeeId,
		Guid bookingId,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		return await _bookings.MarkArrivedAsync(bookingId, employeeId, cancellationToken);
	}

	public async Task<BookingDto> StaffCancelBookingAsync(
		Guid employeeId,
		Guid bookingId,
		string? reason,
		CancellationToken cancellationToken = default)
	{
		await EnsureStaffEmployeeAsync(employeeId, cancellationToken);
		return await _bookings.CancelAsync(bookingId, new CancelBookingRequest(reason), employeeId, cancellationToken);
	}

	private async Task<List<BookingDto>> LoadOpenBookingsForTodayAsync(CancellationToken cancellationToken)
	{
		var today = DateOnly.FromDateTime(DateTime.Now);
		var tomorrow = today.AddDays(1);
		var a = await _bookings.GetForDayAsync(today, null, null, cancellationToken);
		var b = await _bookings.GetForDayAsync(tomorrow, null, null, cancellationToken);
		return a.Concat(b)
			.Where(x => x.Status is BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.Arrived or BookingStatus.Active)
			.GroupBy(x => x.Id)
			.Select(g => g.First())
			.ToList();
	}

	private static Dictionary<Guid, BookingDto> IndexBookingsByComputer(IEnumerable<BookingDto> bookings)
	{
		var map = new Dictionary<Guid, BookingDto>();
		foreach (var b in bookings.OrderBy(x => x.StartsAt))
		{
			foreach (var c in b.Computers)
			{
				if (!map.ContainsKey(c.ComputerId))
					map[c.ComputerId] = b;
			}
		}
		return map;
	}

	private static TgStaffPcDto MapStaffPc(ComputerDto c, Dictionary<Guid, BookingDto> bookingByPc)
	{
		bookingByPc.TryGetValue(c.Id, out var booking);
		var occupancy = c.Occupancy;
		if (booking is not null && c.CurrentSessionId is null && occupancy is "Free" or "Reserved")
			occupancy = "Reserved";
		return new TgStaffPcDto(
			c.Id,
			c.DisplayName ?? c.WindowsName,
			occupancy,
			c.OccupancyDetail,
			c.RemainingSeconds,
			c.SessionGuestName,
			c.ZoneName,
			c.CurrentSessionId,
			c.StationKind.ToString(),
			booking?.Id,
			booking?.ContactName,
			booking?.StartsAt,
			booking?.EndsAt);
	}

	private static bool IsOfflineFree(string? occupancy, string? detail)
	{
		if (string.Equals(occupancy, "Offline", StringComparison.OrdinalIgnoreCase))
			return true;
		if (!string.Equals(occupancy, "Free", StringComparison.OrdinalIgnoreCase))
			return false;
		return !string.IsNullOrWhiteSpace(detail)
			&& (detail.Contains("офлайн", StringComparison.OrdinalIgnoreCase)
				|| detail.Contains("offline", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>onlineFree | offlineFree | busy | reserved | maintenance | other</summary>
	private static string ClassifyFloorPc(string? occupancy, string? detail, Guid? sessionId, bool hasOpenBooking)
	{
		if (occupancy is "Busy" or "Paused" || sessionId.HasValue)
			return "busy";
		if (occupancy is "Maintenance" or "Updating" or "Setup")
			return "maintenance";
		if (occupancy == "Reserved" || (hasOpenBooking && sessionId is null && occupancy is "Free" or "Reserved"))
			return "reserved";
		if (IsOfflineFree(occupancy, detail))
			return "offlineFree";
		if (occupancy == "Free")
			return "onlineFree";
		return "other";
	}

	private async Task EnsureStaffEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
	{
		var ok = await _db.Employees.AsNoTracking()
			.AnyAsync(e => e.Id == employeeId && e.IsActive, cancellationToken);
		if (!ok)
			throw new UnauthorizedAccessException("Нет доступа сотрудника");
	}

	public TgAccessSession? ValidateAccessToken(string token)
	{
		try
		{
			string s = _configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey missing");
			SecurityToken validatedToken;
			ClaimsPrincipal principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
			{
				ValidateIssuer = true,
				ValidateAudience = true,
				ValidateLifetime = true,
				ValidIssuer = (_configuration["Jwt:Issuer"] ?? "ShiftClub"),
				ValidAudience = "ShiftClub.TgWebApp",
				IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(s)),
				ClockSkew = TimeSpan.FromMinutes(1.0)
			}, out validatedToken);
			if (!long.TryParse(principal.FindFirstValue("telegram_user_id"), out var result) || result <= 0)
			{
				return null;
			}
			Guid? customerId = null;
			if (Guid.TryParse(principal.FindFirstValue("customer_id"), out var result2) && result2 != Guid.Empty)
			{
				customerId = result2;
			}
			Guid? employeeId = null;
			if (Guid.TryParse(principal.FindFirstValue("employee_id"), out var result3) && result3 != Guid.Empty)
			{
				employeeId = result3;
			}
			bool isStaff = principal.FindFirstValue("is_staff") == "1" || employeeId.HasValue;
			return new TgAccessSession(result, customerId, employeeId, isStaff);
		}
		catch
		{
			return null;
		}
	}

	private async Task<StaffLink?> ResolveStaffAsync(long telegramUserId, CancellationToken cancellationToken)
	{
		TelegramBotStoredSettings cfg = await _settings.GetTelegramBotStoredAsync(cancellationToken);
		TelegramAllowedUserDto allowed = cfg.AllowedUsers.FirstOrDefault((TelegramAllowedUserDto u) => u.TelegramUserId == telegramUserId);
		Employee employee = await _db.Employees.FirstOrDefaultAsync((Employee e) => e.TelegramUserId == (long?)telegramUserId && e.IsActive, cancellationToken);
		if ((object)allowed == null && employee == null)
		{
			return null;
		}
		Guid? guid = allowed?.EmployeeId;
		Guid? obj;
		if (!guid.HasValue)
		{
			Employee employee2 = employee;
			obj = ((employee2 != null) ? new Guid?(employee2.Id) : cfg.DefaultEmployeeId);
		}
		else
		{
			obj = guid;
		}
		Guid? employeeId = obj;
		if (employee == null && employeeId.HasValue)
		{
			Guid eid = employeeId.GetValueOrDefault();
			employee = await _db.Employees.FirstOrDefaultAsync((Employee e) => e.Id == eid && e.IsActive, cancellationToken);
		}
		if (employee != null && employee.TelegramUserId != telegramUserId)
		{
			employee.TelegramUserId = telegramUserId;
			employee.TelegramLinkedAt = DateTimeOffset.UtcNow;
			if (string.IsNullOrWhiteSpace(employee.FirstName) && !string.IsNullOrWhiteSpace(employee.DisplayName))
			{
				string[] source = employee.DisplayName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
				employee.FirstName = source.ElementAtOrDefault(0) ?? employee.DisplayName;
				employee.LastName = source.ElementAtOrDefault(1) ?? "";
			}
			await _db.SaveChangesAsync(cancellationToken);
		}
		Employee employee3 = employee;
		return new StaffLink((employee3 != null) ? new Guid?(employee3.Id) : employeeId, employee);
	}

	private async Task<TgWebAppAuthDto> IssueAuthAsync(Customer? customer, long telegramUserId, StaffLink? staff, bool needsRegistration, string? displayFallback = null, CancellationToken cancellationToken = default)
	{
		DateTimeOffset dateTimeOffset = DateTimeOffset.UtcNow.AddDays(14.0);
		string accessToken = IssueToken(telegramUserId, customer?.Id, staff?.EmployeeId, (object)staff != null, dateTimeOffset);
		if (customer == null)
		{
			object obj = displayFallback;
			if (obj == null)
			{
				Employee employee = staff?.Employee;
				obj = ((employee != null) ? (string.IsNullOrWhiteSpace((employee.FirstName + " " + employee.LastName).Trim()) ? employee.DisplayName : (employee.FirstName + " " + employee.LastName).Trim()) : "Гость");
			}
			string fullName = (string)obj;
			return new TgWebAppAuthDto(Guid.Empty, fullName, staff?.Employee?.FirstName ?? "", staff?.Employee?.LastName ?? "", staff?.Employee?.Phone ?? "", staff?.Employee?.Iin, 0m, 0m, null, accessToken, dateTimeOffset, 0, 0, 0, ComfortHideBalance: false, ComfortSoundEnabled: true, "ru", 100, needsRegistration, (object)staff != null, staff?.EmployeeId, staff?.Employee?.DisplayName, HasPassword: false, telegramUserId, Array.Empty<TgZoneTimeBankDto>());
		}

		List<TgZoneTimeBankDto> banks = await _db.CustomerZoneTimeBanks.AsNoTracking()
			.Include((CustomerZoneTimeBank b) => b.Zone)
			.Where((CustomerZoneTimeBank b) => b.CustomerId == customer.Id && b.Minutes > 0)
			.OrderBy((CustomerZoneTimeBank b) => b.Zone.SortOrder)
			.ThenBy((CustomerZoneTimeBank b) => b.Zone.Name)
			.Select((CustomerZoneTimeBank b) => new TgZoneTimeBankDto(b.ZoneId, b.Zone.Name, b.Minutes))
			.ToListAsync(cancellationToken);

		List<LoyaltyLevel> levels = await _db.LoyaltyLevels.AsNoTracking()
			.Where((LoyaltyLevel l) => l.BranchId == customer.BranchId && l.IsActive)
			.OrderBy((LoyaltyLevel l) => l.MinSpent)
			.ToListAsync(cancellationToken);
		LoyaltyLevel? next = levels.FirstOrDefault((LoyaltyLevel l) => l.MinSpent > customer.TotalSpent);
		int goal = (int)(next?.MinSpent ?? Math.Max(1m, levels.LastOrDefault()?.MinSpent ?? 500m));
		int progress = Math.Min(goal, (int)Math.Round(customer.TotalSpent));
		if (next is null && levels.Count > 0)
			progress = goal;

		int bankTotal = banks.Sum((TgZoneTimeBankDto b) => b.Minutes);
		string fullName2 = (customer.FirstName + " " + customer.LastName).Trim();
		return new TgWebAppAuthDto(
			customer.Id,
			fullName2,
			customer.FirstName,
			customer.LastName,
			customer.Phone,
			customer.Iin,
			customer.Balance,
			customer.BonusBalance,
			customer.LoyaltyLevel?.Name,
			accessToken,
			dateTimeOffset,
			customer.VisitStreakDays,
			customer.PendingBarRewards,
			bankTotal > 0 ? bankTotal : customer.TimeBankMinutes,
			customer.ComfortHideBalance,
			customer.ComfortSoundEnabled,
			customer.ComfortLanguage,
			customer.ComfortBrightness,
			needsRegistration,
			(object)staff != null,
			staff?.EmployeeId,
			staff?.Employee?.DisplayName,
			!string.IsNullOrWhiteSpace(customer.PasswordHash),
			telegramUserId,
			banks,
			null,
			null,
			0,
			progress,
			goal,
			customer.LoyaltyLevel?.BonusPercent ?? 0m,
			customer.LoyaltyLevel?.TimeDiscountPercent ?? 0m,
			customer.VisitCount,
			customer.TotalSpent);
	}

	private string IssueToken(long telegramUserId, Guid? customerId, Guid? employeeId, bool isStaff, DateTimeOffset expires)
	{
		string s = _configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey missing");
		SigningCredentials signingCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(s)), "HS256");
		List<Claim> list = new List<Claim>
		{
			new Claim("telegram_user_id", telegramUserId.ToString()),
			new Claim("channel", "tg-webapp"),
			new Claim("is_staff", isStaff ? "1" : "0")
		};
		if (customerId.HasValue)
		{
			Guid valueOrDefault = customerId.GetValueOrDefault();
			if (valueOrDefault != Guid.Empty)
			{
				list.Add(new Claim("customer_id", valueOrDefault.ToString()));
			}
		}
		if (employeeId.HasValue)
		{
			Guid valueOrDefault2 = employeeId.GetValueOrDefault();
			if (valueOrDefault2 != Guid.Empty)
			{
				list.Add(new Claim("employee_id", valueOrDefault2.ToString()));
			}
		}
		string? issuer = _configuration["Jwt:Issuer"] ?? "ShiftClub";
		DateTime? expires2 = expires.UtcDateTime;
		SigningCredentials signingCredentials2 = signingCredentials;
		JwtSecurityToken token = new JwtSecurityToken(issuer, "ShiftClub.TgWebApp", list, null, expires2, signingCredentials2);
		return new JwtSecurityTokenHandler().WriteToken(token);
	}

	private async Task<TgInitUser> ValidateInitDataAsync(string initData, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(initData))
		{
			throw new InvalidOperationException("Откройте приложение из Telegram.");
		}
		TelegramBotStoredSettings telegramBotStoredSettings = await _settings.GetTelegramBotStoredAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(telegramBotStoredSettings.BotToken))
		{
			throw new InvalidOperationException("Бот не настроен.");
		}
		Dictionary<string, string> dictionary = ParseQuery(initData);
		if (!dictionary.TryGetValue("hash", out string value) || string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException("Некорректные данные Telegram.");
		}
		List<string> values = (from p in dictionary.Where<KeyValuePair<string, string>>((KeyValuePair<string, string> p) => !p.Key.Equals("hash", StringComparison.OrdinalIgnoreCase)).OrderBy<KeyValuePair<string, string>, string>((KeyValuePair<string, string> p) => p.Key, StringComparer.Ordinal)
			select p.Key + "=" + p.Value).ToList();
		string s = string.Join('\n', values);
		string s2 = Convert.ToHexString(HMACSHA256.HashData(HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(telegramBotStoredSettings.BotToken)), Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
		if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(s2), Encoding.UTF8.GetBytes(value.ToLowerInvariant())))
		{
			throw new InvalidOperationException("Подпись Telegram не прошла проверку.");
		}
		if (dictionary.TryGetValue("auth_date", out string value2) && long.TryParse(value2, out var result) && DateTimeOffset.FromUnixTimeSeconds(result) < DateTimeOffset.UtcNow.AddHours(-24.0))
		{
			throw new InvalidOperationException("Сессия Telegram устарела. Откройте приложение заново.");
		}
		if (!dictionary.TryGetValue("user", out string value3) || string.IsNullOrWhiteSpace(value3))
		{
			throw new InvalidOperationException("В данных Telegram нет пользователя.");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(value3);
		long @int = jsonDocument.RootElement.GetProperty("id").GetInt64();
		JsonElement value4;
		string text = (jsonDocument.RootElement.TryGetProperty("first_name", out value4) ? value4.GetString() : null);
		JsonElement value5;
		string text2 = (jsonDocument.RootElement.TryGetProperty("last_name", out value5) ? value5.GetString() : null);
		string text3 = string.Join(' ', new string[2] { text, text2 }.Where((string value6) => !string.IsNullOrWhiteSpace(value6)));
		if (string.IsNullOrWhiteSpace(text3))
		{
			text3 = "Гость";
		}
		return new TgInitUser(@int, text3);
	}

	private static Dictionary<string, string> ParseQuery(string initData)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
		string[] array = initData.Split('&', StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			int num = text.IndexOf('=');
			if (num > 0)
			{
				string key = Uri.UnescapeDataString(text.Substring(0, num));
				string text2 = text;
				int num2 = num + 1;
				string value = Uri.UnescapeDataString(text2.Substring(num2, text2.Length - num2));
				dictionary[key] = value;
			}
		}
		return dictionary;
	}

	private bool VerifyCustomerSecret(Customer customer, string secret)
	{
		bool flag = !string.IsNullOrWhiteSpace(customer.PasswordHash);
		bool flag2 = !string.IsNullOrWhiteSpace(customer.PinHash);
		if (!flag && !flag2)
		{
			return false;
		}
		bool num = flag && _passwordHasher.Verify(secret, customer.PasswordHash);
		bool flag3 = flag2 && _passwordHasher.Verify(secret, customer.PinHash);
		return num || flag3;
	}

	private static string DigitsOnly(string? value)
	{
		return new string((value ?? "").Where(char.IsDigit).ToArray());
	}

	private static string? NormalizeIin(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string text = DigitsOnly(raw);
		if (text.Length == 0)
		{
			return null;
		}
		if (text.Length != 12)
		{
			throw new InvalidOperationException("ИИН: ровно 12 цифр.");
		}
		return text;
	}
}
