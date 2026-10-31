namespace ShiftClub.Server.Licensing;

public sealed class LicenseOptions
{
    public const string SectionName = "License";

    public bool Enforce { get; set; } = true;

    public string ServerUrl { get; set; } = "https://licence.shift-club.kz";

    public string Key { get; set; } = "";

    public int RefreshMinutes { get; set; } = 10;
}
