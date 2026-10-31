using System.Security.Cryptography;

namespace ShiftClub.Shared.Licensing;

public static class LicenseKeyFormat
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(12);
        Span<char> raw = stackalloc char[14];
        var n = 0;
        for (var i = 0; i < 12; i++)
        {
            if (i > 0 && i % 4 == 0)
                raw[n++] = '-';
            raw[n++] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return "SHIFT-" + new string(raw);
    }

    public static string Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "";

        var chars = key.Trim().ToUpperInvariant().Where(c => c != ' ' && c != '\t' && c != '\r' && c != '\n').ToArray();
        return new string(chars);
    }

    public static bool IsValid(string? key)
    {
        var text = Normalize(key);
        // SHIFT-XXXX-XXXX-XXXX
        if (text.Length != 20 || !text.StartsWith("SHIFT-", StringComparison.Ordinal))
            return false;

        for (var i = 6; i < text.Length; i++)
        {
            var dash = i is 10 or 15;
            if (dash)
            {
                if (text[i] != '-')
                    return false;
                continue;
            }

            if (Alphabet.IndexOf(text[i]) < 0)
                return false;
        }

        return true;
    }
}
