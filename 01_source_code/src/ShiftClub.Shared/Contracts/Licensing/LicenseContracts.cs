namespace ShiftClub.Shared.Contracts.Licensing;

/// <summary>Состояние лицензии клуба.</summary>
public enum LicenseState
{
    /// <summary>Ключ не установлен. Поведение зависит от настройки License:Enforce.</summary>
    Missing = 0,

    /// <summary>Ключ установлен и действует.</summary>
    Active = 1,

    /// <summary>Срок вышел, но идёт льготный период: новые сеансы нельзя, текущие доигрывают.</summary>
    Grace = 2,

    /// <summary>Срок вышел и льготный период закончился: только просмотр и отчёты.</summary>
    Expired = 3,

    /// <summary>Ключ не читается или подпись не сходится.</summary>
    Invalid = 4
}

/// <summary>Полезная нагрузка лицензионного ключа (то, что подписано).</summary>
public sealed record LicensePayload(
    int V,
    Guid ClubId,
    string ClubName,
    string Plan,
    int MaxComputers,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    int GraceDays,
    IReadOnlyList<string> Features);

/// <summary>Состояние лицензии для панели и внутренних проверок.</summary>
public sealed record LicenseStatusDto(
    LicenseState State,
    string? ClubName,
    string? Plan,
    DateTimeOffset? ExpiresAt,
    int? DaysLeft,
    int MaxComputers,
    int UsedComputers,
    IReadOnlyList<string> Features,
    bool CanStartSessions,
    bool CanRegisterComputers,
    bool CanSell,
    string Summary,
    string? Warning)
{
    /// <summary>0 — лимит не задан (без ключа при выключенном enforce).</summary>
    public bool HasComputerLimit => MaxComputers > 0;
}

public sealed record SetLicenseKeyRequest(string Key);
