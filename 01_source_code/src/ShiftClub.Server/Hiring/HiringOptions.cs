namespace ShiftClub.Server.Hiring;

public sealed class HiringOptions
{
    public const string SectionName = "Hiring";

    /// <summary>Значение из поставки — на нём сервер предупреждает, что PIN не поменяли.</summary>
    public const string ShippedDefaultPin = "2580";

    /// <summary>PIN for manager panel (digits). Override via Hiring__ManagerPin in secrets.env.</summary>
    public string ManagerPin { get; set; } = ShippedDefaultPin;

    /// <summary>Session lifetime for manager token (hours).</summary>
    public int SessionHours { get; set; } = 12;

    /// <summary>Relative path under ContentRoot for SQLite + photos.</summary>
    public string DataFolder { get; set; } = "data/hiring";
}
