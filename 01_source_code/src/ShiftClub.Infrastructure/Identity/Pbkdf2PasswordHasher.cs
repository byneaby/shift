using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using ShiftClub.Application.Abstractions;

namespace ShiftClub.Infrastructure.Identity;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, Iterations, KeySize);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string passwordHash)
    {
        var parts = passwordHash.Split('.', 4);
        if (parts.Length != 4 || parts[0] != "v1")
            return false;

        if (!int.TryParse(parts[1], out var iterations))
            return false;

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, iterations, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
