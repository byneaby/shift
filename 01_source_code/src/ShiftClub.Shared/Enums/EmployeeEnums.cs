namespace ShiftClub.Shared.Enums;

public enum EmployeePayType
{
    Hourly = 0,
    FixedMonthly = 1,
    PerShift = 2
}

public enum WorkShiftStatus
{
    Scheduled = 0,
    Working = 1,
    OnBreak = 2,
    Completed = 3,
    Absent = 4,
    Late = 5,
    Cancelled = 6
}

public enum PayrollAccrualType
{
    Salary = 0,
    Hourly = 1,
    ShiftPay = 2,
    SalesPercent = 3,
    Bonus = 4,
    Penalty = 5,
    Advance = 6,
    Deduction = 7,
    Premium = 8
}

public enum PayrollAccrualStatus
{
    Draft = 0,
    Approved = 1,
    Paid = 2,
    Cancelled = 3
}
