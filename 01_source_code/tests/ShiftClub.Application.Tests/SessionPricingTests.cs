using ShiftClub.Application.Sessions;
using ShiftClub.Domain.Entities;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Application.Tests;

public class SessionPricingTests
{
    [Fact]
    public void Hourly_PerMinute_Calculates_Proportionally()
    {
        var tariff = new Tariff
        {
            PricePerHour = 700m,
            MinCharge = 0,
            BillingMode = BillingMode.PerMinute,
            Kind = TariffKind.Hourly
        };

        var price = SessionPricing.Calculate(tariff, 60);
        Assert.Equal(700m, price);

        var half = SessionPricing.Calculate(tariff, 30);
        Assert.Equal(350m, half);
    }

    [Fact]
    public void Hourly_Block15_Rounds_Up()
    {
        var tariff = new Tariff
        {
            PricePerHour = 600m,
            MinCharge = 0,
            BillingMode = BillingMode.Block15,
            Kind = TariffKind.Hourly
        };

        // 16 minutes -> 30 billable minutes -> 300
        var price = SessionPricing.Calculate(tariff, 16);
        Assert.Equal(300m, price);
    }

    [Fact]
    public void Extend_30min_At_700_Is_350()
    {
        var tariff = new Tariff
        {
            PricePerHour = 700m,
            MinCharge = 175m,
            BillingMode = BillingMode.PerMinute,
            Kind = TariffKind.Hourly
        };

        Assert.Equal(350m, SessionPricing.PriceForAdditionalMinutes(tariff, 30));
        Assert.Equal(175m, SessionPricing.PriceForAdditionalMinutes(tariff, 15));
        Assert.Equal(700m, SessionPricing.PriceForAdditionalMinutes(tariff, 60));
    }

    [Fact]
    public void MinutesFromAmount_700_PerHour()
    {
        var tariff = new Tariff
        {
            PricePerHour = 700m,
            MinCharge = 0,
            BillingMode = BillingMode.PerMinute,
            Kind = TariffKind.Hourly
        };

        Assert.Equal(30, SessionPricing.MinutesFromAmount(tariff, 350m));
        Assert.Equal(60, SessionPricing.MinutesFromAmount(tariff, 700m));
    }

    [Fact]
    public void Package_Overrun_Charges_Hourly()
    {
        var tariff = new Tariff
        {
            PricePerHour = 700m,
            MinCharge = 0,
            BillingMode = BillingMode.PerMinute,
            Kind = TariffKind.Package,
            FixedDurationMinutes = 180,
            FixedPrice = 1500m
        };

        // 3ч пакет + 30 мин сверху
        Assert.Equal(1500m + 350m, SessionPricing.Calculate(tariff, 210));
    }
}
