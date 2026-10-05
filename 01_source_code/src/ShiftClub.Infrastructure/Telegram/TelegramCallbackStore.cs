using System.Collections.Concurrent;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Infrastructure.Telegram;

public enum TelegramPendingKind
{
    Confirm,
    PcAction,
    ExtendPay,
    TransferPick,
    OrderStatus,
    BookingAction,
    MessageTemplate,
    MessageCustom,
    DepositAmount,
    DepositPay,
    CustomerPick,
    StartSession,
    ClientComfortToggle,
    ClientComfortLang,
    ClientSetBirth
}

public sealed class TelegramPendingAction
{
    public required TelegramPendingKind Kind { get; init; }
    public Guid? ComputerId { get; init; }
    public Guid? SessionId { get; init; }
    public Guid? OrderId { get; init; }
    public Guid? BookingId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? TargetComputerId { get; init; }
    public Guid? TariffId { get; init; }
    public ComputerCommandType? CommandType { get; init; }
    public BarOrderStatus? OrderStatus { get; init; }
    public string? BookingOp { get; init; }
    public int? Minutes { get; init; }
    public PaymentMethod? PaymentMethod { get; init; }
    public decimal? Amount { get; init; }
    public string? MessageText { get; init; }
    public string? Label { get; init; }
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddHours(2);
}

public enum TelegramWizardKind
{
    None,
    AwaitCustomMessage,
    AwaitCustomerQuery,
    AwaitDepositAmount,
    AwaitClientPhone,
    AwaitClientName,
    AwaitClientCode,
    AwaitClientBirthDate
}

public sealed class TelegramWizardState
{
    public TelegramWizardKind Kind { get; set; }
    public Guid? ComputerId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? MessageText { get; set; }
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(15);
}

public sealed class TelegramCallbackStore
{
    private readonly ConcurrentDictionary<string, TelegramPendingAction> _actions = new();
    private readonly ConcurrentDictionary<long, TelegramWizardState> _wizards = new();

    public string Put(TelegramPendingAction action)
    {
        Purge();
        var id = Guid.NewGuid().ToString("N")[..10];
        _actions[id] = action;
        return id;
    }

    public bool TryGet(string id, out TelegramPendingAction action)
    {
        Purge();
        if (_actions.TryGetValue(id, out action!) && action.ExpiresAt > DateTimeOffset.UtcNow)
            return true;
        action = null!;
        return false;
    }

    public void SetWizard(long userId, TelegramWizardState state)
    {
        state.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        _wizards[userId] = state;
    }

    public bool TryGetWizard(long userId, out TelegramWizardState state)
    {
        if (_wizards.TryGetValue(userId, out state!) && state.ExpiresAt > DateTimeOffset.UtcNow)
            return true;
        _wizards.TryRemove(userId, out _);
        state = null!;
        return false;
    }

    public void ClearWizard(long userId) => _wizards.TryRemove(userId, out _);

    private void Purge()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _actions)
        {
            if (kv.Value.ExpiresAt <= now)
                _actions.TryRemove(kv.Key, out _);
        }
    }
}
