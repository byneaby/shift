using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class Tariff : Common.Entity
{
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid? ZoneId { get; set; }
    public Zone? Zone { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TariffKind Kind { get; set; } = TariffKind.Hourly;
    public BillingMode BillingMode { get; set; } = BillingMode.PerMinute;

    /// <summary>
    /// FixedDuration — сеанс = N минут от покупки.
    /// TimeWindow — сеанс до AvailableTo в текущем периоде (AvailableFrom–AvailableTo).
    /// </summary>
    public TariffDurationMode DurationMode { get; set; } = TariffDurationMode.FixedDuration;

    /// <summary>Цена за час для почасового тарифа.</summary>
    public decimal PricePerHour { get; set; }

    public decimal MinCharge { get; set; }
    public int? FixedDurationMinutes { get; set; }
    public decimal? FixedPrice { get; set; }

    /// <summary>Мин/макс длительность сеанса (мин). Для пакетов обычно = FixedDurationMinutes.</summary>
    public int? MinDurationMinutes { get; set; }
    public int? MaxDurationMinutes { get; set; }

    /// <summary>
    /// Битовая маска дней недели: bit0=Пн … bit6=Вс. 127 = все дни.
    /// </summary>
    public int DaysOfWeekMask { get; set; } = 127;

    /// <summary>Окно доступности по локальному времени филиала (null = круглосуточно).</summary>
    public TimeSpan? AvailableFrom { get; set; }
    public TimeSpan? AvailableTo { get; set; }

    public bool AllowPause { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? ColorHex { get; set; }
}
