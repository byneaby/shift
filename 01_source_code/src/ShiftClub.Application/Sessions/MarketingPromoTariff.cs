using ShiftClub.Domain.Entities;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Application.Sessions;

/// <summary>
/// Маркетинговая акция (−50% и т.п.) только на 2+1, 3+2, день и ночь.
/// Почасовой тариф и продление «+N минут» без покупки пакета — без акции.
/// </summary>
public static class MarketingPromoTariff
{
    public static bool Qualifies(Tariff tariff)
    {
        if (tariff.DurationMode == TariffDurationMode.TimeWindow)
            return true;

        if (tariff.Kind != TariffKind.Package)
            return false;

        var code = tariff.Code?.ToUpperInvariant() ?? "";
        if (code.Contains("2P1", StringComparison.Ordinal) || code.Contains("3P2", StringComparison.Ordinal))
            return true;

        return tariff.Name is "2+1" or "3+2";
    }

    public static decimal DiscountAmount(
        Tariff tariff,
        decimal listPrice,
        MarketingPromoStoredSettings? promo,
        DateTimeOffset now)
    {
        if (listPrice <= 0 || !Qualifies(tariff))
            return 0m;
        if (!MarketingPromoMath.IsActive(promo, now))
            return 0m;
        return MarketingPromoMath.DiscountAmount(listPrice, promo!.Percent);
    }
}
