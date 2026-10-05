namespace ShiftClub.Application.Time;

/// <summary>
/// Таймзона филиала. Казахстан с 2024 — постоянно UTC+5.
/// Windows «Central Asia Standard Time» всё ещё UTC+6 — не использовать как Asia/Almaty.
/// </summary>
public static class BranchTimeZone
{
    public const string DefaultId = "Asia/Almaty";
    public static readonly TimeSpan DefaultOffset = TimeSpan.FromHours(5);

    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId) ? DefaultId : timeZoneId.Trim();

        if (IsKazakhstanFixedUtc5(id))
            return KazakhstanUtc5();

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        // IANA на системах с ICU
        foreach (var alt in new[] { "Asia/Almaty", DefaultId })
        {
            if (string.Equals(alt, id, StringComparison.OrdinalIgnoreCase))
                continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(alt); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return KazakhstanUtc5();
    }

    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, string? timeZoneId)
    {
        var tz = Resolve(timeZoneId);
        var local = date.ToDateTime(time);
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    public static (DateTimeOffset DayStartUtc, DateTimeOffset DayEndUtc) DayBoundsUtc(DateOnly date, string? timeZoneId)
    {
        var start = ToUtc(date, TimeOnly.MinValue, timeZoneId);
        return (start, start.AddDays(1));
    }

    public static DateOnly TodayLocal(string? timeZoneId, DateTimeOffset? utcNow = null)
    {
        var tz = Resolve(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(utcNow ?? DateTimeOffset.UtcNow, tz);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public static DateTimeOffset EnsureUtc(DateTimeOffset value) => value.ToUniversalTime();

    private static bool IsKazakhstanFixedUtc5(string id) =>
        string.Equals(id, "Asia/Almaty", StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, "Asia/Aqtobe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, "Asia/Aqtau", StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, "Asia/Oral", StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, "Asia/Qyzylorda", StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, "Asia/Qostanay", StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, "Central Asia Standard Time", StringComparison.OrdinalIgnoreCase);

    private static TimeZoneInfo KazakhstanUtc5() =>
        TimeZoneInfo.CreateCustomTimeZone("ShiftClubAlmaty", DefaultOffset, "Asia/Almaty (UTC+5)", "Asia/Almaty (UTC+5)");
}
