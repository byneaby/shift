using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class LoyaltyLevel : Common.Entity
{
    public Guid BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int MinSpent { get; set; }
    /// <summary>% бонуса при пополнении баланса на кассе.</summary>
    public decimal BonusPercent { get; set; }
    /// <summary>% скидки на игровое время (тариф) для клиентов этого уровня.</summary>
    public decimal TimeDiscountPercent { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Customer : Common.Entity
{
    public Guid BranchId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateOnly? BirthDate { get; set; }

    /// <summary>ИИН РК — ровно 12 цифр.</summary>
    public string? Iin { get; set; }

    public string? Login { get; set; }
    public string? PasswordHash { get; set; }
    /// <summary>Устаревает: вход по паролю. Оставлено для старых аккаунтов.</summary>
    public string? PinHash { get; set; }

    /// <summary>Кэш баланса. Источник истины — ledger.</summary>
    public decimal Balance { get; set; }
    public decimal BonusBalance { get; set; }

    /// <summary>Сумма минут по всем зонам (кэш). Источник истины — CustomerZoneTimeBank.</summary>
    public int TimeBankMinutes { get; set; }

    /// <summary>Ключи SHIFT CASE (кэш). Источник истины — CaseKeyLedger.</summary>
    public int CaseKeysBalance { get; set; }

    /// <summary>Минуты игры на момент welcome-ключа — для порога «15 часов» без ретро-начислений.</summary>
    public int CasePlayBaselineMinutes { get; set; }

    public Guid? LoyaltyLevelId { get; set; }
    public LoyaltyLevel? LoyaltyLevel { get; set; }

    /// <summary>
    /// Если true — уровень не меняется автоматически по TotalSpent
    /// (зафиксирован сотрудником; без авто-повышения и авто-понижения).
    /// </summary>
    public bool LoyaltyLevelLocked { get; set; }

    public int VisitCount { get; set; }
    public int TotalMinutesPlayed { get; set; }
    public decimal TotalSpent { get; set; }

    public string? Notes { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }
    public bool AllowNotifications { get; set; } = true;
    public bool IsActive { get; set; } = true;

    /// <summary>ПК, на котором сейчас открыт клиентский вход (один аккаунт — один ПК).</summary>
    public Guid? LoggedInComputerId { get; set; }

    /// <summary>Момент последнего успешного входа в Shell.</summary>
    public DateTimeOffset? LoggedInAt { get; set; }

    /// <summary>Инкремент при каждом входе — старые JWT на других ПК становятся недействительны.</summary>
    public int LoginEpoch { get; set; }

    /// <summary>Привязанный Telegram user id (клиентский бот).</summary>
    public long? TelegramUserId { get; set; }
    public DateTimeOffset? TelegramLinkedAt { get; set; }
    /// <summary>Последняя смена Telegram (лимит раз в N дней из настроек).</summary>
    public DateTimeOffset? TelegramChangedAt { get; set; }

    /// <summary>Текущая серия дней подряд с визитом в клуб (засчитывается при старте сеанса с аккаунтом).</summary>
    public int VisitStreakDays { get; set; }
    public DateOnly? VisitStreakLastDate { get; set; }

    /// <summary>Год, за который уже выдан подарок на ДР.</summary>
    public int? BirthdayGiftYear { get; set; }

    /// <summary>Бесплатные бар-позиции (энерг и т.п.) за стрик — касса списывает.</summary>
    public int PendingBarRewards { get; set; }

    public bool ComfortHideBalance { get; set; }
    public bool ComfortSoundEnabled { get; set; } = true;
    /// <summary>ru | kk | en</summary>
    public string ComfortLanguage { get; set; } = "ru";
    /// <summary>Яркость оверлея Shell 40–100.</summary>
    public int ComfortBrightness { get; set; } = 100;

    public ICollection<CustomerBalanceTransaction> BalanceTransactions { get; set; } = new List<CustomerBalanceTransaction>();
    public ICollection<CustomerTimeBankTransaction> TimeBankTransactions { get; set; } = new List<CustomerTimeBankTransaction>();
    public ICollection<CustomerZoneTimeBank> ZoneTimeBanks { get; set; } = new List<CustomerZoneTimeBank>();
}

/// <summary>QR/deep-link тикет для входа или привязки через Telegram.</summary>
public class TelegramAuthTicket : Common.Entity
{
    public Guid BranchId { get; set; }
    public string Code { get; set; } = string.Empty;
    /// <summary>Login | BindSession | LinkAccount | ChangeTelegram | StaffLogin</summary>
    public string Purpose { get; set; } = "Login";
    public Guid? ComputerId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? CustomerId { get; set; }
    public long? TelegramUserId { get; set; }
    /// <summary>Pending | Claiming | AwaitingRegistration | Consumed | Expired | Cancelled</summary>
    public string Status { get; set; } = "Pending";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public string? ResultMessage { get; set; }
    /// <summary>Имя из Telegram до завершения регистрации.</summary>
    public string? PendingDisplayName { get; set; }
    /// <summary>Привязка staff QR к вкладке браузера (не гасит чужие QR).</summary>
    public string? ClientNonce { get; set; }
    /// <summary>JWT/клиентский токен уже отдан один раз через status poll.</summary>
    public bool AuthRedeemed { get; set; }
}

/// <summary>Банк минут клиента в конкретной зоне (Standard ≠ VIP).</summary>
public class CustomerZoneTimeBank : Common.Entity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public Guid ZoneId { get; set; }
    public Zone Zone { get; set; } = null!;
    public int Minutes { get; set; }
}

public class CustomerBalanceTransaction : Common.Entity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public LedgerTransactionType Type { get; set; }
    public LedgerDirection Direction { get; set; }
    public LedgerTransactionStatus Status { get; set; } = LedgerTransactionStatus.Posted;

    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }

    public Guid? EmployeeId { get; set; }
    public Guid? ReceiptId { get; set; }
    public Guid? GamingSessionId { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Comment { get; set; }
}

public class CustomerTimeBankTransaction : Common.Entity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid ZoneId { get; set; }
    public Zone? Zone { get; set; }

    public TimeBankReason Reason { get; set; }
    public LedgerDirection Direction { get; set; }

    public int Minutes { get; set; }
    public int BalanceBefore { get; set; }
    public int BalanceAfter { get; set; }

    public Guid? EmployeeId { get; set; }
    public Guid? GamingSessionId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Comment { get; set; }
}

public class CustomerPackage : Common.Entity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public Guid BranchId { get; set; }

    public string Name { get; set; } = string.Empty;
    public int TotalMinutes { get; set; }
    public int RemainingMinutes { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTimeOffset PurchasedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
}
