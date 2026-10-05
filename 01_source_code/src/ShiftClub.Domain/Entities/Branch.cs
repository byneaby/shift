namespace ShiftClub.Domain.Entities;

public class Branch : Common.Entity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Asia/Almaty";
    public string CurrencyCode { get; set; } = "KZT";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Единая сетка карты зала (ячейки для ПК и элементов).</summary>
    public int FloorGridCols { get; set; } = 24;
    public int FloorGridRows { get; set; } = 16;

    /// <summary>Цвет фона карты зала (#RRGGBB). null = дефолтный тёмный.</summary>
    public string? FloorBackgroundHex { get; set; }

    public ICollection<Zone> Zones { get; set; } = new List<Zone>();
    public ICollection<FloorMapElement> FloorMapElements { get; set; } = new List<FloorMapElement>();
}
