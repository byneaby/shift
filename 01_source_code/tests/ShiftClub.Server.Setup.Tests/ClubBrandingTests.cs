using ShiftClub.Server.Setup;
using ShiftClub.Shared.Branding;

namespace ShiftClub.Server.Setup.Tests;

public class ClubBrandingTests
{
    [Fact]
    public void Club_name_is_required_and_rejects_path_characters()
    {
        var answers = new ServerSetupAnswers { ClubName = "А" };
        Assert.Contains(ServerSetupValidator.ValidateClub(answers), e => e.Contains("2 до 48", StringComparison.Ordinal));

        answers.ClubName = "Клуб/зал";
        Assert.Contains(ServerSetupValidator.ValidateClub(answers), e => e.Contains("кавычек", StringComparison.Ordinal));

        answers.ClubName = "Арена 51";
        Assert.Empty(ServerSetupValidator.ValidateClub(answers));
    }

    [Fact]
    public void Branding_file_keeps_the_name_and_a_copied_logo()
    {
        var root = Path.Combine(Path.GetTempPath(), "shift-brand-" + Guid.NewGuid().ToString("N"));
        var logo = Path.Combine(root, "source.png");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(logo, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        try
        {
            ClubBrandingFiles.Write(root, "  Арена 51  ", logo);
            var read = ClubBrandingFiles.Read(root);
            Assert.Equal("Арена 51", read.Name);
            Assert.Equal("logo.png", read.LogoFile);
            Assert.True(File.Exists(Path.Combine(root, "data", "branding", "logo.png")));

            var dto = ClubBrandingFiles.ToDto(root, null);
            Assert.Equal("Арена 51", dto.Name);
            Assert.StartsWith("/media/branding/logo.png?v=", dto.LogoUrl, StringComparison.Ordinal);
            Assert.DoesNotContain("shift-club.kz", File.ReadAllText(ClubBrandingFiles.DocumentPath(root)), StringComparison.OrdinalIgnoreCase);

            ClubBrandingFiles.Write(root, "Зал Север", null);
            Assert.Equal("Зал Север", ClubBrandingFiles.Read(root).Name);
            Assert.Equal("logo.png", ClubBrandingFiles.Read(root).LogoFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Uninstall_plan_removes_the_firewall_rule_and_refuses_windows()
    {
        var marker = new ServerInstallMarker
        {
            InstallDirectory = @"C:\ShiftClub\Server",
            ListenPort = 5080,
            BundledPostgres = true,
            ClubName = "Арена 51",
            FirewallRuleName = WindowsInstallPlan.FirewallRuleName("Арена 51", 5080)
        };

        var rules = ServerUninstallPlan.FirewallRulesToDelete(marker);
        Assert.Contains(rules, rule => rule.Contains("Арена 51", StringComparison.Ordinal));
        Assert.Contains("localport=5080", WindowsInstallPlan.FirewallArguments(marker.ClubName, marker.ListenPort), StringComparison.Ordinal);
        Assert.Contains("Арена 51", ServerUninstallPlan.AddRegistryArguments("DisplayName", marker.ClubName), StringComparison.Ordinal);
        Assert.Contains("/uninstall", ServerUninstallPlan.UninstallString(@"C:\ShiftClub\Server\Uninstall.exe"), StringComparison.Ordinal);
        Assert.Contains("rmdir", ServerUninstallPlan.DelayedRemoveArguments(marker.InstallDirectory), StringComparison.Ordinal);
        Assert.True(ServerUninstallPlan.CanRemoveBundledPostgres(marker, requested: true));
        Assert.False(ServerUninstallPlan.CanRemoveBundledPostgres(marker, requested: false));
        Assert.Throws<ArgumentException>(() => ServerUninstallPlan.DelayedRemoveArguments(@"C:\Windows"));
    }

    [Fact]
    public void Install_marker_roundtrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "shift-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var marker = new ServerInstallMarker
            {
                InstallDirectory = root,
                ListenPort = 5080,
                BundledPostgres = true,
                ClubName = "Арена 51",
                FirewallRuleName = "Арена 51 5080"
            };
            ServerInstallMarker.Write(root, marker);
            var read = ServerInstallMarker.Read(root);
            Assert.NotNull(read);
            Assert.Equal("Арена 51", read!.ClubName);
            Assert.Equal(5080, read.ListenPort);
            Assert.True(read.BundledPostgres);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
