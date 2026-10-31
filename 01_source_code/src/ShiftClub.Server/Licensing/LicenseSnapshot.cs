namespace ShiftClub.Server.Licensing;

public sealed record LicenseSnapshot(
    bool Enforce,
    bool Operational,
    string Message,
    string? Club,
    string? Kind,
    int? MaxComputers,
    DateTimeOffset? ExpiresAt,
    int? DaysRemaining,
    bool Warning);
