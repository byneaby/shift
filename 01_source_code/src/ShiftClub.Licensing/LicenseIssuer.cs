using System.Security.Cryptography;
using ShiftClub.Shared.Licensing;

namespace ShiftClub.Licensing;

public enum LeaseFailure
{
    None = 0,
    BadRequest = 400,
    Forbidden = 403,
    NotFound = 404,
    Conflict = 409,
}

public sealed class LicenseIssuer
{
    private readonly LicenseStore _store;
    private readonly RSA _privateKey;

    public LicenseIssuer(LicenseStore store, RSA privateKey)
    {
        _store = store;
        _privateKey = privateKey;
    }

    public async Task<(string? Lease, LicenseRow? Row, LeaseFailure Failure, string Message)> IssueAsync(
        string? rawKey,
        string? machineId,
        string? machineName,
        bool firstBind,
        CancellationToken cancellationToken)
    {
        var key = LicenseKeyFormat.Normalize(rawKey);
        if (!LicenseKeyFormat.IsValid(key))
            return (null, null, LeaseFailure.BadRequest, "Ключ должен быть вида SHIFT-XXXX-XXXX-XXXX.");

        machineId = (machineId ?? "").Trim();
        if (machineId.Length is < 8 or > 80 || machineId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            return (null, null, LeaseFailure.BadRequest, "Некорректный идентификатор сервера.");

        machineName = string.IsNullOrWhiteSpace(machineName) ? null : machineName.Trim();
        if (machineName is { Length: > 120 })
            machineName = machineName[..120];

        var row = await _store.FindByKeyAsync(key, cancellationToken);
        if (row is null)
            return (null, null, LeaseFailure.NotFound, "Такого ключа нет.");

        if (row.Revoked)
            return (null, row, LeaseFailure.Forbidden, "Лицензия отозвана.");

        if (LicenseToken.IsExpired(row.ExpiresAt))
            return (null, row, LeaseFailure.Forbidden, "Срок лицензии истёк.");

        if (!string.IsNullOrEmpty(row.MachineId) && !string.Equals(row.MachineId, machineId, StringComparison.Ordinal))
            return (null, row, LeaseFailure.Conflict, "Ключ уже привязан к другому серверу. Снимите привязку в центре лицензий.");

        if (!firstBind && string.IsNullOrEmpty(row.MachineId))
            return (null, row, LeaseFailure.Conflict, "Ключ ещё не активирован на сервере клуба.");

        var bound = await _store.BindAndTouchAsync(row.Id, machineId, machineName, cancellationToken)
            ?? row;

        var document = new LicenseDocument(
            bound.Id,
            bound.Key,
            bound.ClubName,
            bound.MaxComputers,
            bound.ExpiresAt,
            machineId,
            bound.Kind,
            DateTimeOffset.UtcNow);

        return (LicenseToken.Sign(document, _privateKey), bound, LeaseFailure.None, "ok");
    }
}
