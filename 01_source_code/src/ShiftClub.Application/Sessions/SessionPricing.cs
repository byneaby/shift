using ShiftClub.Domain.Entities;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Application.Sessions;

public static class SessionPricing
{
    /// <summary>₸ за минуту при почасовом тарифе (500₸/ч → ≈8.33).</summary>
    public static decimal PricePerMinute(Tariff tariff)
    {
        if (tariff.PricePerHour > 0)
            return tariff.PricePerHour / 60m;

        // Пакет без почасовой ставки — выводим из фикс. цены
        if (tariff is { FixedPrice: > 0, FixedDurationMinutes: > 0 })
            return tariff.FixedPrice.Value / tariff.FixedDurationMinutes.Value;

        return 0m;
    }

    public static int ToBillableMinutes(BillingMode mode, int durationMinutes)
    {
        if (durationMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationMinutes));

        return mode switch
        {
            BillingMode.Block15 => (int)Math.Ceiling(durationMinutes / 15d) * 15,
            BillingMode.Block30 => (int)Math.Ceiling(durationMinutes / 30d) * 30,
            _ => durationMinutes
        };
    }

    public static decimal CalculateHourlyPrice(Tariff tariff, int durationMinutes)
    {
        if (durationMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationMinutes));

        var billableMinutes = ToBillableMinutes(tariff.BillingMode, durationMinutes);
        var rate = PricePerMinute(tariff);
        var price = Math.Round(rate * billableMinutes, 2, MidpointRounding.AwayFromZero);
        if (price < tariff.MinCharge)
            price = tariff.MinCharge;

        return price;
    }

    public static decimal Calculate(Tariff tariff, int durationMinutes)
    {
        if (tariff.DurationMode == TariffDurationMode.TimeWindow && tariff.FixedPrice.HasValue)
            return tariff.FixedPrice.Value;

        return tariff.Kind switch
        {
            TariffKind.Free => 0m,
            TariffKind.Package when tariff.FixedPrice.HasValue && durationMinutes <= (tariff.FixedDurationMinutes ?? durationMinutes) =>
                tariff.FixedPrice.Value,
            // Пакет + доп. время: фикс пакета + почасовая доплата за выход за фиксированную длительность
            TariffKind.Package when tariff.FixedPrice.HasValue =>
                tariff.FixedPrice.Value + PriceForAdditionalMinutes(tariff, Math.Max(0, durationMinutes - (tariff.FixedDurationMinutes ?? 0))),
            _ => CalculateHourlyPrice(tariff, durationMinutes)
        };
    }

    /// <summary>
    /// Стоимость продления: только доп. минуты, без повторного MinCharge.
    /// 500₸/ч + 30 мин → 250₸ (PerMinute).
    /// </summary>
    public static decimal PriceForAdditionalMinutes(Tariff tariff, int additionalMinutes)
    {
        if (additionalMinutes <= 0)
            return 0m;

        if (tariff.Kind == TariffKind.Free)
            return 0m;

        var billable = ToBillableMinutes(tariff.BillingMode, additionalMinutes);
        var rate = PricePerMinute(tariff);
        if (rate <= 0)
            throw new InvalidOperationException("У тарифа нет ставки для продления (PricePerHour / пакет).");

        return Math.Round(rate * billable, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Сколько минут даёт сумма (пол: вниз по биллингу? — даём floor на сырые минуты).</summary>
    public static int MinutesFromAmount(Tariff tariff, decimal amount)
    {
        if (amount <= 0)
            return 0;

        var rate = PricePerMinute(tariff);
        if (rate <= 0)
            return 0;

        var raw = (int)Math.Floor((double)(amount / rate) + 1e-6);
        if (raw <= 0)
            return 0;

        // Для Block15/30 округляем вниз к целому блоку, который помещается в оплату
        return tariff.BillingMode switch
        {
            BillingMode.Block15 => (raw / 15) * 15,
            BillingMode.Block30 => (raw / 30) * 30,
            _ => raw
        };
    }
}
