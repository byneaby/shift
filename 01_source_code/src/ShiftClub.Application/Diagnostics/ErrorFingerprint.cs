using System.Security.Cryptography;
using System.Text;

namespace ShiftClub.Application.Diagnostics;

public static class ErrorFingerprint
{
    /// <summary>
    /// Короткий отпечаток ошибки. Одна и та же поломка должна давать один и тот
    /// же отпечаток независимо от того, какой id клиента попал в сообщение.
    /// </summary>
    public static string Compute(string kind, string? where, string? message)
    {
        var input = string.Join(
            '|',
            kind,
            where ?? "",
            PiiScrubber.Normalize(message));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }
}
