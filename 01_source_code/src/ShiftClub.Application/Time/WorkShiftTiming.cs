using ShiftClub.Domain.Entities;

namespace ShiftClub.Application.Time;

/// <summary>
/// Плановое окно смены. Если конец раньше начала — смена через полночь (20:00 → 08:00).
/// </summary>
public static class WorkShiftTiming
{
    /// <summary>Ночная смена: конец раньше начала (через полночь).</summary>
    public static bool IsOvernight(TimeOnly plannedStart, TimeOnly plannedEnd)
        => plannedEnd < plannedStart;

    public static void EnsureValidDuration(TimeOnly plannedStart, TimeOnly plannedEnd)
    {
        if (plannedEnd == plannedStart)
            throw new InvalidOperationException("Длительность смены не может быть нулевой (начало и конец совпадают).");
    }

    public static DateOnly PlannedEndDate(DateOnly workDate, TimeOnly plannedStart, TimeOnly plannedEnd)
        => IsOvernight(plannedStart, plannedEnd) ? workDate.AddDays(1) : workDate;

    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) PlannedWindowUtc(
        DateOnly workDate,
        TimeOnly plannedStart,
        TimeOnly plannedEnd,
        string? timeZoneId)
    {
        EnsureValidDuration(plannedStart, plannedEnd);
        var start = BranchTimeZone.ToUtc(workDate, plannedStart, timeZoneId);
        var end = BranchTimeZone.ToUtc(PlannedEndDate(workDate, plannedStart, plannedEnd), plannedEnd, timeZoneId);
        return (start, end);
    }

    public static bool WindowsOverlap(
        DateOnly aDate, TimeOnly aStart, TimeOnly aEnd,
        DateOnly bDate, TimeOnly bStart, TimeOnly bEnd,
        string? timeZoneId)
    {
        var (a1, a2) = PlannedWindowUtc(aDate, aStart, aEnd, timeZoneId);
        var (b1, b2) = PlannedWindowUtc(bDate, bStart, bEnd, timeZoneId);
        return a1 < b2 && b1 < a2;
    }

    public static bool CoversLocalDay(WorkShift shift, DateOnly day)
    {
        if (shift.WorkDate == day)
            return true;
        // Ночная смена вчера ещё идёт сегодня утром.
        return shift.WorkDate == day.AddDays(-1)
               && IsOvernight(shift.PlannedStart, shift.PlannedEnd);
    }
}
