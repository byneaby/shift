using ShiftClub.Application.Time;
using ShiftClub.Domain.Entities;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Application.Sessions;

/// <summary>Проверка окна доступности тарифа и расчёт конца сеанса для TimeWindow.</summary>
public static class TariffAvailability
{
    /// <summary>Пн=bit0 … Вс=bit6. 127 = все дни.</summary>
    public const int AllDays = 127;

    public static bool IsDayAllowed(int mask, DayOfWeek day)
    {
        var bit = day switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => 0
        };
        return (mask & (1 << bit)) != 0;
    }

    public static DateTime GetLocalDateTime(DateTimeOffset utcNow, string? timeZoneId)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(utcNow, BranchTimeZone.Resolve(timeZoneId)).DateTime;
        }
        catch
        {
            return utcNow.UtcDateTime.AddHours(5); // Almaty fallback
        }
    }

    public static TimeZoneInfo ResolveTz(string? timeZoneId)
    {
        try
        {
            return BranchTimeZone.Resolve(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.CreateCustomTimeZone("AlmatyFallback", TimeSpan.FromHours(5), "Almaty", "Almaty");
        }
    }

    public static bool IsAvailableNow(Tariff tariff, DateTimeOffset utcNow, string? timeZoneId)
    {
        if (!tariff.IsActive)
            return false;

        var local = GetLocalDateTime(utcNow, timeZoneId);

        if (!IsDayAllowed(tariff.DaysOfWeekMask, local.DayOfWeek))
            return false;

        if (tariff.AvailableFrom is null && tariff.AvailableTo is null)
            return true;

        return IsTimeOfDayInWindow(local.TimeOfDay, tariff.AvailableFrom ?? TimeSpan.Zero, tariff.AvailableTo ?? new TimeSpan(23, 59, 59));
    }

    /// <summary>
    /// now в [from, to] с учётом перехода через полночь (from &gt; to → 22:00–08:00).
    /// Конец окна (ровно to) считается ещё внутри периода.
    /// </summary>
    public static bool IsTimeOfDayInWindow(TimeSpan now, TimeSpan from, TimeSpan to)
    {
        if (from <= to)
            return now >= from && now <= to;
        return now >= from || now <= to;
    }

    /// <summary>
    /// Для TimeWindow: текущий период (локально→UTC) и оставшиеся минуты до конца.
    /// false — сейчас вне окна или до конца &lt; 1 мин.
    /// </summary>
    public static bool TryResolveTimeWindow(
        Tariff tariff,
        DateTimeOffset utcNow,
        string? timeZoneId,
        out DateTimeOffset periodStartUtc,
        out DateTimeOffset periodEndUtc,
        out int remainingMinutes)
    {
        periodStartUtc = default;
        periodEndUtc = default;
        remainingMinutes = 0;

        if (tariff.AvailableFrom is null || tariff.AvailableTo is null)
            return false;

        var tz = ResolveTz(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(utcNow, tz).DateTime;
        var from = tariff.AvailableFrom.Value;
        var to = tariff.AvailableTo.Value;

        if (!IsTimeOfDayInWindow(local.TimeOfDay, from, to))
            return false;

        DateTime localStart;
        DateTime localEnd;
        if (from <= to)
        {
            localStart = local.Date + from;
            localEnd = local.Date + to;
        }
        else if (local.TimeOfDay >= from)
        {
            // 22:00–08:00, сейчас после from → конец завтра
            localStart = local.Date + from;
            localEnd = local.Date.AddDays(1) + to;
        }
        else
        {
            // после полуночи до to → начало вчера
            localStart = local.Date.AddDays(-1) + from;
            localEnd = local.Date + to;
        }

        // Npgsql timestamptz accepts only UTC (offset 0). Convert before any DB write/query.
        periodStartUtc = new DateTimeOffset(localStart, tz.GetUtcOffset(localStart)).ToUniversalTime();
        periodEndUtc = new DateTimeOffset(localEnd, tz.GetUtcOffset(localEnd)).ToUniversalTime();

        var remaining = periodEndUtc - utcNow.ToUniversalTime();
        remainingMinutes = (int)Math.Floor(remaining.TotalMinutes);
        return remainingMinutes >= 1;
    }

    public static int ResolveDurationMinutes(Tariff tariff, int requestedMinutes)
    {
        if (tariff.DurationMode == TariffDurationMode.TimeWindow)
            throw new InvalidOperationException("Для тарифа с временным интервалом длительность считается по окну AvailableFrom–AvailableTo.");

        if (tariff.Kind == TariffKind.Package && tariff.FixedDurationMinutes is > 0)
            return tariff.FixedDurationMinutes.Value;

        var minutes = requestedMinutes;
        if (tariff.MinDurationMinutes is > 0 && minutes < tariff.MinDurationMinutes)
            minutes = tariff.MinDurationMinutes.Value;
        if (tariff.MaxDurationMinutes is > 0 && minutes > tariff.MaxDurationMinutes)
            minutes = tariff.MaxDurationMinutes.Value;
        return minutes;
    }

    public static string FormatRemaining(int minutes)
    {
        if (minutes < 60)
            return $"{minutes} мин";
        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? $"{h} ч" : $"{h} ч {m} мин";
    }

    public static string FormatClock(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}";
}
