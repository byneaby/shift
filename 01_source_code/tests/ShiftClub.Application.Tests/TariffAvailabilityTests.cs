using ShiftClub.Application.Sessions;
using ShiftClub.Domain.Entities;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Application.Tests;

public class TariffAvailabilityTests
{
    private static Tariff DayWindow() => new()
    {
        Name = "день",
        Code = "DAY",
        Kind = TariffKind.Package,
        DurationMode = TariffDurationMode.TimeWindow,
        FixedPrice = 5000m,
        AvailableFrom = new TimeSpan(8, 0, 0),
        AvailableTo = new TimeSpan(20, 0, 0),
        DaysOfWeekMask = 127,
        IsActive = true
    };

    private static Tariff NightWindow() => new()
    {
        Name = "ночь",
        Code = "NIGHT",
        Kind = TariffKind.Package,
        DurationMode = TariffDurationMode.TimeWindow,
        FixedPrice = 5000m,
        AvailableFrom = new TimeSpan(22, 0, 0),
        AvailableTo = new TimeSpan(8, 0, 0),
        DaysOfWeekMask = 127,
        IsActive = true
    };

    private static DateTimeOffset UtcAtAlmaty(int year, int month, int day, int hour, int minute)
    {
        // Asia/Almaty = UTC+5 (no DST in modern Kazakhstan)
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeSpan.FromHours(5)).ToUniversalTime();
    }

    private const string Tz = "Asia/Almaty";

    [Fact]
    public void Day_at_start_ends_at_20()
    {
        var utc = UtcAtAlmaty(2026, 7, 18, 8, 0);
        Assert.True(TariffAvailability.TryResolveTimeWindow(DayWindow(), utc, Tz, out var start, out var end, out var mins));
        Assert.Equal(720, mins); // 08→20 = 12h
        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(TimeSpan.Zero, end.Offset);
        var localEnd = end.ToOffset(TimeSpan.FromHours(5));
        Assert.Equal(20, localEnd.Hour);
        Assert.Equal(0, localEnd.Minute);
        Assert.Equal(18, localEnd.Day);
        Assert.True(start <= utc);
    }

    [Fact]
    public void TimeWindow_bounds_are_utc_offset_zero()
    {
        var utc = UtcAtAlmaty(2026, 7, 18, 23, 30);
        Assert.True(TariffAvailability.TryResolveTimeWindow(NightWindow(), utc, Tz, out var start, out var end, out _));
        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(TimeSpan.Zero, end.Offset);
    }

    [Fact]
    public void Day_midday_ends_same_evening()
    {
        var utc = UtcAtAlmaty(2026, 7, 18, 12, 0);
        Assert.True(TariffAvailability.TryResolveTimeWindow(DayWindow(), utc, Tz, out _, out var end, out var mins));
        Assert.Equal(8 * 60, mins); // 12→20
        Assert.Equal(20, end.ToOffset(TimeSpan.FromHours(5)).Hour);
    }

    [Fact]
    public void Day_late_short_remaining()
    {
        var utc = UtcAtAlmaty(2026, 7, 18, 18, 0);
        Assert.True(TariffAvailability.TryResolveTimeWindow(DayWindow(), utc, Tz, out _, out _, out var mins));
        Assert.Equal(120, mins);
    }

    [Fact]
    public void Day_outside_rejected()
    {
        var utc = UtcAtAlmaty(2026, 7, 18, 21, 0);
        Assert.False(TariffAvailability.TryResolveTimeWindow(DayWindow(), utc, Tz, out _, out _, out _));
        Assert.False(TariffAvailability.IsAvailableNow(DayWindow(), utc, Tz));
    }

    [Fact]
    public void Night_before_midnight_ends_next_morning()
    {
        var utc = UtcAtAlmaty(2026, 7, 18, 23, 0);
        Assert.True(TariffAvailability.TryResolveTimeWindow(NightWindow(), utc, Tz, out _, out var end, out var mins));
        Assert.Equal(9 * 60, mins); // 23→08 = 9h
        var localEnd = end.ToOffset(TimeSpan.FromHours(5));
        Assert.Equal(8, localEnd.Hour);
        Assert.Equal(19, localEnd.Day);
    }

    [Fact]
    public void Night_after_midnight_ends_same_morning()
    {
        var utc = UtcAtAlmaty(2026, 7, 19, 2, 0);
        Assert.True(TariffAvailability.TryResolveTimeWindow(NightWindow(), utc, Tz, out var start, out var end, out var mins));
        Assert.Equal(6 * 60, mins);
        Assert.Equal(8, end.ToOffset(TimeSpan.FromHours(5)).Hour);
        Assert.Equal(19, end.ToOffset(TimeSpan.FromHours(5)).Day);
        Assert.Equal(18, start.ToOffset(TimeSpan.FromHours(5)).Day);
        Assert.Equal(22, start.ToOffset(TimeSpan.FromHours(5)).Hour);
    }

    [Fact]
    public void Night_outside_rejected()
    {
        var utc = UtcAtAlmaty(2026, 7, 18, 15, 0);
        Assert.False(TariffAvailability.TryResolveTimeWindow(NightWindow(), utc, Tz, out _, out _, out _));
    }

    [Fact]
    public void TimeWindow_pricing_is_full_fixed_price()
    {
        var t = DayWindow();
        Assert.Equal(5000m, SessionPricing.Calculate(t, 30));
        Assert.Equal(5000m, SessionPricing.Calculate(t, 720));
    }

    [Fact]
    public void Fixed_package_unchanged()
    {
        var t = new Tariff
        {
            Kind = TariffKind.Package,
            DurationMode = TariffDurationMode.FixedDuration,
            FixedDurationMinutes = 180,
            FixedPrice = 1200m,
            PricePerHour = 600m
        };
        Assert.Equal(1200m, SessionPricing.Calculate(t, 180));
        Assert.Equal(180, TariffAvailability.ResolveDurationMinutes(t, 999));
    }
}
