using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShiftClub.Shared.Contracts.Licensing;

namespace ShiftClub.Application.Licensing;

/// <summary>
/// Разбор и проверка лицензионного ключа.
/// Формат: SHIFT1.&lt;payload&gt;.&lt;signature&gt;, обе части — base64url.
/// Подпись — ECDSA P-256 / SHA-256. Приватный ключ только у вендора,
/// в клубе лежит лишь публичный, поэтому срок и лимит ПК клуб не поправит.
/// </summary>
public static class LicenseKeyCodec
{
    public const string Prefix = "SHIFT1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public sealed record ParseResult(bool Ok, LicensePayload? Payload, string? Error)
    {
        public static ParseResult Fail(string error) => new(false, null, error);
        public static ParseResult Success(LicensePayload payload) => new(true, payload, null);
    }

    /// <summary>Проверяет подпись и возвращает полезную нагрузку.</summary>
    public static ParseResult Parse(string? key, string publicKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(key))
            return ParseResult.Fail("Ключ пустой");

        if (string.IsNullOrWhiteSpace(publicKeyBase64))
            return ParseResult.Fail("Не настроен публичный ключ проверки лицензий");

        var parts = key.Trim().Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
            return ParseResult.Fail("Ключ не похож на лицензию SHIFT");

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = FromBase64Url(parts[1]);
            signature = FromBase64Url(parts[2]);
        }
        catch (FormatException)
        {
            return ParseResult.Fail("Ключ повреждён (не читается base64)");
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            if (!ecdsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256))
                return ParseResult.Fail("Подпись лицензии не сходится");
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return ParseResult.Fail("Подпись лицензии не проверяется");
        }

        LicensePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException)
        {
            return ParseResult.Fail("Содержимое лицензии не читается");
        }

        if (payload is null)
            return ParseResult.Fail("Содержимое лицензии пустое");

        if (payload.V != 1)
            return ParseResult.Fail($"Версия лицензии {payload.V} не поддерживается этой сборкой");

        return ParseResult.Success(payload with
        {
            Features = payload.Features ?? []
        });
    }

    /// <summary>Собирает подписанный ключ. Используется утилитой вендора, не сервером клуба.</summary>
    public static string Issue(LicensePayload payload, string privateKeyBase64)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);
        var signature = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256);

        return $"{Prefix}.{ToBase64Url(payloadBytes)}.{ToBase64Url(signature)}";
    }

    /// <summary>Вычисляет состояние по сроку действия.</summary>
    public static LicenseState ResolveState(LicensePayload payload, DateTimeOffset now)
    {
        if (now <= payload.ExpiresAt)
            return LicenseState.Active;

        var graceDays = payload.GraceDays <= 0 ? 0 : payload.GraceDays;
        return now <= payload.ExpiresAt.AddDays(graceDays)
            ? LicenseState.Grace
            : LicenseState.Expired;
    }

    private static string ToBase64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    /// <summary>Генерирует пару ключей для вендора. Вызывается один раз, результат хранить вне git.</summary>
    public static (string PrivateKeyBase64, string PublicKeyBase64) GenerateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (
            Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()),
            Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }
}
