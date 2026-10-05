namespace ShiftClub.Shared.Contracts.Public;

public static class FreeHoursPromo
{
    public const int DurationMinutes = 300;
    public const string PayloadPrefix = "SHIFTCLUB-FREE:";
    public const string GuestName = "Свои";
}

public sealed record PublicFreeClaimRequest(
    string? QrPayload,
    Guid? ComputerId = null,
    string? IdempotencyKey = null);

public sealed record PublicFreeClaimResultDto(
    Guid SessionId,
    Guid ComputerId,
    string ComputerName,
    string? ZoneName,
    int DurationMinutes,
    DateTimeOffset EndsAt,
    string Message);

public sealed record PublicFreeQrDto(
    Guid ComputerId,
    string ComputerName,
    string Payload,
    string QrPngBase64,
    int DurationMinutes);
