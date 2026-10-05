namespace ShiftClub.Domain.Entities;

public class Zone : Common.Entity
{
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#3B82F6";
    public int SortOrder { get; set; }
    public int? MinSessionMinutes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Hall | Restroom | Cafe | Other — amenity zones render as labeled blocks.</summary>
    public string Kind { get; set; } = "Hall";

    /// <summary>Grid width for PC slots (iCafe-style room matrix).</summary>
    public int GridColumns { get; set; } = 6;

    /// <summary>Grid height for PC slots.</summary>
    public int GridRows { get; set; } = 4;
}
