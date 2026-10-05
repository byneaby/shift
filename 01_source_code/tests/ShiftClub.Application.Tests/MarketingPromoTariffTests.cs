using ShiftClub.Application.Sessions;
using ShiftClub.Domain.Entities;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Application.Tests;

public class MarketingPromoTariffTests
{
    private static MarketingPromoStoredSettings ActivePromo() => new()
    {
        Enabled = true,
        Percent = 50,
        ApplyToTariffs = true,
    };

    [Fact]
    public void Qualifies_TimeWindow_DayNight()
    {
        var t = new Tariff { DurationMode = TariffDurationMode.TimeWindow, Kind = TariffKind.Package };
        Assert.True(MarketingPromoTariff.Qualifies(t));
    }

    [Fact]
    public void Qualifies_PromoPack_2P1_3P2()
    {
        Assert.True(MarketingPromoTariff.Qualifies(new Tariff
        {
            Kind = TariffKind.Package,
            Code = "STD_2P1",
            Name = "2+1",
        }));
        Assert.True(MarketingPromoTariff.Qualifies(new Tariff
        {
            Kind = TariffKind.Package,
            Code = "VIP_3P2",
            Name = "3+2",
        }));
    }

    [Fact]
    public void Qualifies_Hourly_False()
    {
        var t = new Tariff { Kind = TariffKind.Hourly, Code = "STD_HOUR", Name = "1 час" };
        Assert.False(MarketingPromoTariff.Qualifies(t));
    }

    [Fact]
    public void DiscountAmount_Hourly_Is_Zero()
    {
        var t = new Tariff { Kind = TariffKind.Hourly, PricePerHour = 500 };
        var d = MarketingPromoTariff.DiscountAmount(t, 500, ActivePromo(), DateTimeOffset.UtcNow);
        Assert.Equal(0m, d);
    }

    [Fact]
    public void DiscountAmount_2P1_Applies_Promo()
    {
        var t = new Tariff { Kind = TariffKind.Package, Code = "STD_2P1", FixedPrice = 1000 };
        var d = MarketingPromoTariff.DiscountAmount(t, 1000, ActivePromo(), DateTimeOffset.UtcNow);
        Assert.Equal(500m, d);
    }
}
