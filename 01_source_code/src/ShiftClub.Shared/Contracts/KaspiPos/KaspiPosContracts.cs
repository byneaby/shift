namespace ShiftClub.Shared.Contracts.KaspiPos;

public sealed record KaspiPosStatusDto(
    bool Configured,
    bool Registered,
    bool Ready,
    string? Host,
    string TerminalId,
    string? RegisterName,
    string? Message,
    DateTimeOffset? TokenExpiresAt);

public sealed record KaspiPosConfigDto(string? Host, string TerminalId, string RegisterName);

public sealed record UpdateKaspiPosConfigRequest(string? Host);

public sealed record KaspiPosPayRequest(int Amount);

public sealed record KaspiPosPaymentResultDto(
    bool Success,
    string Status,
    string? ProcessId,
    string? TransactionId,
    string? PaymentMethod,
    string? SubStatus,
    string? Message);

public sealed record KaspiPosRegisterResultDto(bool Success, string? Message, DateTimeOffset? TokenExpiresAt);
