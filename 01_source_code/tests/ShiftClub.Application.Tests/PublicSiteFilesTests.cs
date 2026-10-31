using ShiftClub.Shared.Sites;

namespace ShiftClub.Application.Tests;

public class PublicSiteFilesTests
{
    [Theory]
    [InlineData("product.shift-club.kz", PublicSiteKind.Product)]
    [InlineData("WWW.Product.shift-club.kz", PublicSiteKind.Product)]
    [InlineData("wiki.shift-club.kz", PublicSiteKind.Wiki)]
    [InlineData("www.wiki.shift-club.kz", PublicSiteKind.Wiki)]
    [InlineData("shift-club.kz", PublicSiteKind.None)]
    [InlineData("licence.shift-club.kz", PublicSiteKind.None)]
    [InlineData("panel.shift-club.kz", PublicSiteKind.None)]
    [InlineData("", PublicSiteKind.None)]
    public void KindForHost_matches_public_sites_only(string host, PublicSiteKind expected)
    {
        Assert.Equal(expected, PublicSiteFiles.KindForHost(host));
    }

    [Fact]
    public void TryResolve_serves_index_extensionless_pages_and_blocks_escape()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shift-sites-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "home");
            File.WriteAllText(Path.Combine(root, "install.html"), "install");
            Directory.CreateDirectory(Path.Combine(root, "shots"));
            File.WriteAllText(Path.Combine(root, "shots", "a.jpg"), "img");

            Assert.True(PublicSiteFiles.TryResolve(root, "/", out var home, out var homeType, out _));
            Assert.EndsWith("index.html", home);
            Assert.StartsWith("text/html", homeType);

            Assert.True(PublicSiteFiles.TryResolve(root, "/install", out var page, out _, out _));
            Assert.EndsWith("install.html", page);

            Assert.True(PublicSiteFiles.TryResolve(root, "/shots/a.jpg", out var shot, out var shotType, out var cache));
            Assert.EndsWith("a.jpg", shot);
            Assert.Equal("image/jpeg", shotType);
            Assert.Contains("max-age", cache);

            Assert.False(PublicSiteFiles.TryResolve(root, "/../secrets.env", out _, out _, out _));
            Assert.False(PublicSiteFiles.TryResolve(root, "/shots/../../index.html", out _, out _, out _));
            Assert.False(PublicSiteFiles.TryResolve(root, "/missing", out _, out _, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
