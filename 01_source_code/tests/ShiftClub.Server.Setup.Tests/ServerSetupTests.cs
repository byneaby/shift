using ShiftClub.Server.Setup;
using ShiftClub.Shared.Licensing;

namespace ShiftClub.Server.Setup.Tests;

public class ServerSetupTests
{
    [Fact]
    public void Valid_answers_have_no_errors()
    {
        var errors = ServerSetupValidator.Validate(Sample(), secretsAlreadyExist: false);
        Assert.Empty(errors);
    }

    [Fact]
    public void Existing_secrets_need_an_explicit_replace()
    {
        var errors = ServerSetupValidator.ValidateFolder(Sample(), secretsAlreadyExist: true);
        Assert.Contains(errors, e => e.Contains("уже настроен", StringComparison.Ordinal));

        var answers = Sample();
        answers.ReplaceExistingSecrets = true;
        Assert.Empty(ServerSetupValidator.ValidateFolder(answers, secretsAlreadyExist: true));
    }

    [Fact]
    public void License_can_be_entered_later()
    {
        var answers = Sample();
        answers.LicenseKey = "";
        answers.LicenseLater = true;
        Assert.Empty(ServerSetupValidator.ValidateLicense(answers));
    }

    [Fact]
    public void Bad_license_key_is_rejected()
    {
        var answers = Sample();
        answers.LicenseKey = "SHIFT-IIII-OOOO-0000";
        Assert.Contains(ServerSetupValidator.ValidateLicense(answers), e => e.Contains("SHIFT-XXXX", StringComparison.Ordinal));
    }

    [Fact]
    public void Owner_passwords_must_match()
    {
        var answers = Sample();
        answers.OwnerPasswordConfirm = "other-password";
        Assert.Contains(ServerSetupValidator.ValidateOwner(answers), e => e.Contains("не совпадают", StringComparison.Ordinal));
    }

    [Fact]
    public void Windows_and_drive_root_are_rejected()
    {
        Assert.False(ServerSetupValidator.IsSafeInstallDirectory(@"C:\"));
        Assert.False(ServerSetupValidator.IsSafeInstallDirectory(@"C:\Windows\System32"));
        Assert.False(ServerSetupValidator.IsSafeInstallDirectory(@"C:\ShiftClub\..\Windows"));
        Assert.True(ServerSetupValidator.IsSafeInstallDirectory(@"C:\ShiftClub\Server"));
        Assert.True(ServerSetupValidator.IsSafeInstallDirectory(@"D:\Apps\ShiftClub"));
    }

    [Fact]
    public void Secrets_file_keeps_passwords_and_drops_the_postgres_admin()
    {
        var answers = Sample();
        var text = ServerSecrets.Render(answers, ServerSecrets.CreateJwtKey(), "4821");
        Assert.Contains("Seed__OwnerLogin=owner", text, StringComparison.Ordinal);
        Assert.Contains("Seed__OwnerPassword=" + answers.OwnerPassword, text, StringComparison.Ordinal);
        Assert.Contains("License__Key=" + LicenseKeyFormat.Normalize(answers.LicenseKey), text, StringComparison.Ordinal);
        Assert.Contains("Kestrel__Endpoints__Http__Url=http://0.0.0.0:5080", text, StringComparison.Ordinal);
        Assert.Contains("Database__Host=127.0.0.1", text, StringComparison.Ordinal);
        Assert.Contains("Password=" + answers.DatabasePassword, text, StringComparison.Ordinal);
        Assert.DoesNotContain(answers.PostgresAdminPassword, text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", text.Replace("\r", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_escapes_quotes_and_rejects_injected_names()
    {
        Assert.Equal("\"shiftclub\"", DatabaseSetup.QuoteIdent("shiftclub"));
        Assert.Equal("'o''brien'", DatabaseSetup.QuoteLiteral("o'brien"));
        Assert.Equal(
            "CREATE ROLE \"shiftclub\" WITH LOGIN PASSWORD 'o''brien'",
            DatabaseSetup.CreateRoleSql("shiftclub", "o'brien", exists: false));
        Assert.Throws<ArgumentException>(() => DatabaseSetup.QuoteIdent("shiftclub;drop"));
        Assert.Throws<ArgumentException>(() => DatabaseSetup.QuoteLiteral("bad\npassword"));
    }

    [Fact]
    public void Install_plan_does_not_include_passwords_and_skips_live_data()
    {
        var answers = Sample();
        var script = WindowsInstallPlan.RegisterTasksScript(answers.InstallDirectory, answers.ClubName);
        Assert.Contains(@"C:\ShiftClub\Server\Start-ShiftClubApi.ps1", script, StringComparison.Ordinal);
        Assert.DoesNotContain(answers.OwnerPassword, script, StringComparison.Ordinal);
        Assert.DoesNotContain(answers.DatabasePassword, script, StringComparison.Ordinal);
        Assert.DoesNotContain(answers.PostgresAdminPassword, script, StringComparison.Ordinal);

        Assert.Contains("localport=5080", WindowsInstallPlan.FirewallArguments(answers.ClubName, 5080), StringComparison.Ordinal);
        Assert.Contains(answers.ClubName, WindowsInstallPlan.FirewallArguments(answers.ClubName, 5080), StringComparison.Ordinal);
        Assert.True(WindowsInstallPlan.ShouldSkipExtract("secrets.env"));
        Assert.True(WindowsInstallPlan.ShouldSkipExtract("data/license-key.txt"));
        Assert.True(WindowsInstallPlan.ShouldSkipExtract(@"logs\startup.log"));
        Assert.True(WindowsInstallPlan.ShouldSkipExtract("../secrets.env"));
        Assert.False(WindowsInstallPlan.ShouldSkipExtract("ShiftClub.Server.dll"));

        var summary = string.Join("\n", WindowsInstallPlan.Summary(answers).Select(row => row.Label + "=" + row.Value));
        Assert.Contains("owner", summary, StringComparison.Ordinal);
        Assert.Contains(answers.ClubName, summary, StringComparison.Ordinal);
        Assert.DoesNotContain("shift-club.kz", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(answers.OwnerPassword, summary, StringComparison.Ordinal);
        Assert.DoesNotContain(answers.PostgresAdminPassword, summary, StringComparison.Ordinal);
    }

    private static ServerSetupAnswers Sample() => new()
    {
        InstallDirectory = @"C:\ShiftClub\Server",
        ListenPort = 5080,
        InstallBundledPostgres = false,
        DatabaseHost = "127.0.0.1",
        DatabasePort = 5432,
        DatabaseName = "shiftclub",
        DatabaseUser = "shiftclub",
        DatabasePassword = "club-db-pass-1",
        CreateDatabase = true,
        PostgresAdminUser = "postgres",
        PostgresAdminPassword = "postgres-admin-secret",
        OwnerLogin = "owner",
        OwnerPassword = "owner-pass-1",
        OwnerPasswordConfirm = "owner-pass-1",
        LicenseKey = "SHIFT-PKVW-RFKP-VBYM",
        LicenseServerUrl = "https://licence.shift-club.kz",
        ClubName = "Тестовый клуб"
    };
}
