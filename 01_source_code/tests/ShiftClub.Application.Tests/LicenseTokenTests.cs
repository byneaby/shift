using System.Security.Cryptography;
using ShiftClub.Shared.Licensing;

namespace ShiftClub.Application.Tests;

public class LicenseTokenTests
{
    [Fact]
    public void Roundtrip_accepts_a_token_signed_by_the_matching_key()
    {
        using var rsa = RSA.Create(2048);
        var document = Sample(DateTimeOffset.UtcNow.AddDays(3));
        var token = LicenseToken.Sign(document, rsa);

        var ok = LicenseToken.TryRead(token, rsa, out var read, out var error);

        Assert.True(ok, error);
        Assert.Equal(document.Club, read!.Club);
        Assert.Equal(document.MaxComputers, read.MaxComputers);
        Assert.Equal("trial", read.Kind);
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var token = LicenseToken.Sign(Sample(DateTimeOffset.UtcNow.AddDays(7)), rsa);
        var dot = token.IndexOf('.');
        var flipped = (token[0] == 'A' ? 'B' : 'A') + token[1..];

        var ok = LicenseToken.TryRead(flipped, rsa, out _, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.True(dot > 0);
    }

    [Fact]
    public void Expired_token_does_not_open_the_club()
    {
        using var rsa = RSA.Create(2048);
        var token = LicenseToken.Sign(Sample(DateTimeOffset.UtcNow.AddDays(-1)), rsa);

        var ok = LicenseToken.TryRead(token, rsa, out var read, out var error);

        Assert.False(ok);
        Assert.Contains("истёк", error);
        Assert.NotNull(read);
    }

    [Fact]
    public void Other_key_cannot_verify()
    {
        using var signer = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var token = LicenseToken.Sign(Sample(DateTimeOffset.UtcNow.AddDays(30)), signer);

        Assert.False(LicenseToken.TryRead(token, other, out _, out _));
    }

    [Fact]
    public void Embedded_public_key_imports_and_matches_the_authority_key_when_present()
    {
        using var embedded = RSA.Create();
        embedded.ImportFromPem(LicensePublicKey.Pem);
        Assert.Equal(2048, embedded.KeySize);

        var path = "/tmp/shift-license-private.pem";
        if (!File.Exists(path))
            return;

        using var authority = RSA.Create();
        authority.ImportFromPem(File.ReadAllText(path));
        var token = LicenseToken.Sign(Sample(DateTimeOffset.UtcNow.AddDays(365)), authority);

        Assert.True(LicenseToken.TryRead(token, out var read, out var error), error);
        Assert.Equal("SHIFT Demo", read!.Club);
    }

    [Theory]
    [InlineData("SHIFT-ABCD-EFGH-JK23", true)]
    [InlineData("shift-abcd-efgh-jk23", true)]
    [InlineData("SHIFT-ABCD-EFGH-JK2", false)]
    [InlineData("SHIFT-ABC0-EFGH-JK23", false)]
    [InlineData("", false)]
    public void Key_shape(string key, bool valid)
    {
        Assert.Equal(valid, LicenseKeyFormat.IsValid(key));
    }

    [Fact]
    public void Created_keys_match_the_shape()
    {
        var key = LicenseKeyFormat.Create();
        Assert.True(LicenseKeyFormat.IsValid(key));
        Assert.StartsWith("SHIFT-", key);
    }

    private static LicenseDocument Sample(DateTimeOffset expiresAt) => new(
        Guid.NewGuid().ToString("D"),
        "SHIFT-ABCD-EFGH-JK23",
        "SHIFT Demo",
        40,
        expiresAt,
        Guid.NewGuid().ToString("D"),
        "trial",
        DateTimeOffset.UtcNow);
}
