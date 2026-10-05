using ShiftClub.Application.Versioning;

namespace ShiftClub.Application.Tests;

public class SemVerTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData(" 1.2.3 ", "1.2.3")]
    [InlineData("1.2.3+abc123", "1.2.3")]
    [InlineData("1.2.3.4", "1.2.3")]
    [InlineData("0.6.74-beta", "0.6.74")]
    public void NormalizeDropsBuildTail(string raw, string expected)
    {
        Assert.Equal(expected, SemVer.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("неизвестно")]
    [InlineData("v1.2.3")]
    public void NormalizeRejectsNonVersions(string? raw)
    {
        Assert.Null(SemVer.Normalize(raw));
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.1.0", "1.0.99")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("0.10.0", "0.9.0")]
    public void NewerVersionWins(string candidate, string current)
    {
        Assert.True(SemVer.IsNewer(candidate, current));
        Assert.False(SemVer.IsNewer(current, candidate));
    }

    [Fact]
    public void SameVersionIsNotNewer()
    {
        Assert.False(SemVer.IsNewer("1.2.3", "1.2.3"));
        Assert.False(SemVer.IsNewer("1.2.3+build", "1.2.3"));
    }

    [Fact]
    public void UnreadableVersionCountsAsZero()
    {
        // Сервер без версии в сборке не должен считаться новее любого выпуска.
        Assert.True(SemVer.IsNewer("0.0.1", "неизвестно"));
        Assert.False(SemVer.IsNewer("неизвестно", "0.0.1"));
    }
}
