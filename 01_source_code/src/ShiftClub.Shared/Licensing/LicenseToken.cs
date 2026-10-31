using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShiftClub.Shared.Licensing;

public static class LicenseToken
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan WarningWindow = TimeSpan.FromDays(14);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly RSA Embedded = LoadEmbedded();
    private static readonly object EmbeddedLock = new();

    public static string Sign(LicenseDocument document, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(privateKey);
        var payload = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Base64Url.Encode(payload) + "." + Base64Url.Encode(signature);
    }

    public static bool TryRead(string? token, out LicenseDocument? document, out string error)
    {
        lock (EmbeddedLock)
            return TryRead(token, Embedded, out document, out error);
    }

    public static bool TryRead(string? token, RSA publicKey, out LicenseDocument? document, out string error)
    {
        document = null;
        error = "Лицензия не прочитана.";
        if (string.IsNullOrWhiteSpace(token))
        {
            error = "Лицензия пустая.";
            return false;
        }

        var parts = token.Trim().Split('.');
        if (parts.Length != 2)
        {
            error = "Лицензия повреждена.";
            return false;
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Base64Url.Decode(parts[0]);
            signature = Base64Url.Decode(parts[1]);
        }
        catch (FormatException)
        {
            error = "Лицензия повреждена.";
            return false;
        }

        if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            error = "Подпись лицензии не сходится.";
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<LicenseDocument>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            error = "Лицензия повреждена.";
            return false;
        }

        if (document is null
            || string.IsNullOrWhiteSpace(document.LicenseId)
            || string.IsNullOrWhiteSpace(document.MachineId)
            || document.MaxComputers < 1)
        {
            document = null;
            error = "Лицензия повреждена.";
            return false;
        }

        if (IsExpired(document.ExpiresAt))
        {
            error = "Срок лицензии истёк.";
            return false;
        }

        error = "";
        return true;
    }

    public static bool IsExpired(DateTimeOffset expiresAt, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        return expiresAt.ToUniversalTime() + ClockSkew <= clock.ToUniversalTime();
    }

    public static bool IsWarning(DateTimeOffset expiresAt, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        var left = expiresAt.ToUniversalTime() - clock.ToUniversalTime();
        return left <= WarningWindow;
    }

    private static RSA LoadEmbedded()
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(LicensePublicKey.Pem);
        return rsa;
    }

    private static class Base64Url
    {
        public static string Encode(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] Decode(string text)
        {
            var incoming = text.Replace('-', '+').Replace('_', '/');
            switch (incoming.Length % 4)
            {
                case 2:
                    incoming += "==";
                    break;
                case 3:
                    incoming += "=";
                    break;
            }

            return Convert.FromBase64String(incoming);
        }
    }
}
