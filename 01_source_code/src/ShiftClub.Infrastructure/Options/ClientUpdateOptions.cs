namespace ShiftClub.Infrastructure.Options;

public sealed class ClientUpdateOptions
{
    public const string SectionName = "ClientUpdates";

    public bool Enabled { get; set; } = true;

    /// <summary>Folder with manifest.json and *.zip packages (relative to content root or absolute).</summary>
    public string PackagesPath { get; set; } = "data/client-updates";
}
