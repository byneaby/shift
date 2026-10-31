namespace ShiftClub.Shared.Licensing;

/// <summary>
/// Подписанный снимок лицензии. Клуб верит только тому, что проверил публичным ключом.
/// </summary>
public sealed record LicenseDocument(
    string LicenseId,
    string Key,
    string Club,
    int MaxComputers,
    DateTimeOffset ExpiresAt,
    string MachineId,
    string Kind,
    DateTimeOffset IssuedAt);
