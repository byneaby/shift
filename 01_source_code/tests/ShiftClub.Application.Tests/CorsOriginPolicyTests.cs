using ShiftClub.Application.Security;

namespace ShiftClub.Application.Tests;

public class CorsOriginPolicyTests
{
    private static readonly string[] None = [];

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://192.168.1.50")]
    [InlineData("http://192.168.0.10:8080")]
    [InlineData("http://10.0.0.7")]
    [InlineData("http://172.16.4.1")]
    [InlineData("http://172.31.255.254")]
    [InlineData("https://shift-server.local")]
    public void LocalNetworkIsAlwaysAllowed(string origin)
    {
        Assert.True(CorsOriginPolicy.IsAllowed(origin, None));
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://8.8.8.8")]
    [InlineData("https://shift-club.kz.attacker.com")]
    [InlineData("http://172.32.0.1")]
    [InlineData("http://172.15.0.1")]
    public void OutsideOriginsAreRejectedByDefault(string origin)
    {
        Assert.False(CorsOriginPolicy.IsAllowed(origin, None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://192.168.1.1")]
    [InlineData("javascript:alert(1)")]
    public void GarbageOriginsAreRejected(string origin)
    {
        Assert.False(CorsOriginPolicy.IsAllowed(origin, None));
    }

    [Fact]
    public void NullOriginIsRejected()
    {
        Assert.False(CorsOriginPolicy.IsAllowed(null, None));
    }

    [Fact]
    public void ExplicitFullOriginMatchesSchemeHostAndPort()
    {
        string[] allowed = ["https://panel.myclub.kz"];

        Assert.True(CorsOriginPolicy.IsAllowed("https://panel.myclub.kz", allowed));
        Assert.False(CorsOriginPolicy.IsAllowed("http://panel.myclub.kz", allowed));
        Assert.False(CorsOriginPolicy.IsAllowed("https://panel.myclub.kz:8443", allowed));
        Assert.False(CorsOriginPolicy.IsAllowed("https://other.myclub.kz", allowed));
    }

    [Fact]
    public void BareHostEntryMatchesAnySchemeAndPort()
    {
        string[] allowed = ["panel.myclub.kz"];

        Assert.True(CorsOriginPolicy.IsAllowed("https://panel.myclub.kz", allowed));
        Assert.True(CorsOriginPolicy.IsAllowed("http://panel.myclub.kz:9000", allowed));
        Assert.False(CorsOriginPolicy.IsAllowed("https://panel.otherclub.kz", allowed));
    }

    [Fact]
    public void WildcardEntryMatchesSubdomainsOnly()
    {
        string[] allowed = ["*.myclub.kz"];

        Assert.True(CorsOriginPolicy.IsAllowed("https://panel.myclub.kz", allowed));
        Assert.True(CorsOriginPolicy.IsAllowed("https://tv.myclub.kz", allowed));
        Assert.False(CorsOriginPolicy.IsAllowed("https://myclub.kz", allowed));
        Assert.False(CorsOriginPolicy.IsAllowed("https://notmyclub.kz", allowed));
    }

    [Fact]
    public void StarOptsOutOfProtectionEntirely()
    {
        string[] allowed = ["*"];

        Assert.True(CorsOriginPolicy.AllowsEverything(allowed));
        Assert.True(CorsOriginPolicy.IsAllowed("https://evil.example.com", allowed));
    }

    [Fact]
    public void EmptyEntriesInListAreIgnored()
    {
        string[] allowed = ["", "   ", "panel.myclub.kz"];

        Assert.False(CorsOriginPolicy.IsAllowed("https://evil.example.com", allowed));
        Assert.True(CorsOriginPolicy.IsAllowed("https://panel.myclub.kz", allowed));
    }
}
