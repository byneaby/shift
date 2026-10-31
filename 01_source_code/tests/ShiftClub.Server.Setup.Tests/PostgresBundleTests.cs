using System.IO.Compression;
using System.Text;
using ShiftClub.Server.Setup;

namespace ShiftClub.Server.Setup.Tests;

public class PostgresBundleTests
{
    [Fact]
    public void Bundled_install_does_not_ask_for_database_passwords()
    {
        var answers = External();
        answers.InstallBundledPostgres = true;
        answers.DatabasePassword = "";
        answers.PostgresAdminPassword = "";
        Assert.Empty(ServerSetupValidator.Validate(answers, secretsAlreadyExist: false));
    }

    [Fact]
    public void Bundled_install_rejects_a_remote_database_host()
    {
        var answers = External();
        answers.InstallBundledPostgres = true;
        answers.DatabaseHost = "10.0.0.8";
        Assert.Contains(ServerSetupValidator.ValidateDatabase(answers), e => e.Contains("только этот компьютер", StringComparison.Ordinal));
    }

    [Fact]
    public void External_postgres_still_requires_passwords()
    {
        var answers = External();
        answers.DatabasePassword = "";
        Assert.Contains(ServerSetupValidator.ValidateDatabase(answers), e => e.Contains("Пароль базы", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_superuser_password_stays_out_of_secrets()
    {
        var answers = External();
        answers.DatabasePassword = "";
        answers.PostgresAdminPassword = "";
        var superuser = PostgresBundle.CreatePassword();
        PostgresBundle.AssignRuntimeSecrets(answers, superuser);

        var text = ServerSecrets.Render(answers, ServerSecrets.CreateJwtKey(), "4821");
        Assert.Equal("127.0.0.1", answers.DatabaseHost);
        Assert.Equal(PostgresBundle.Port, answers.DatabasePort);
        Assert.Equal(superuser, answers.PostgresAdminPassword);
        Assert.NotEqual(superuser, answers.DatabasePassword);
        Assert.Contains("Password=" + answers.DatabasePassword, text, StringComparison.Ordinal);
        Assert.DoesNotContain(superuser, text, StringComparison.Ordinal);

        var summary = string.Join("\n", WindowsInstallPlan.Summary(answers).Select(row => row.Value));
        Assert.Contains(PostgresBundle.InstallRoot, summary, StringComparison.Ordinal);
        Assert.Contains("только этот компьютер", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(superuser, summary, StringComparison.Ordinal);
        Assert.DoesNotContain(answers.DatabasePassword, summary, StringComparison.Ordinal);

        var network = WindowsInstallPlan.Summary(answers).Single(row => row.Label == "Сеть").Value;
        Assert.Contains("5080", network, StringComparison.Ordinal);
        Assert.DoesNotContain("5432", network, StringComparison.Ordinal);
        Assert.DoesNotContain("5432", WindowsInstallPlan.FirewallArguments(5080), StringComparison.Ordinal);
    }

    [Fact]
    public void Passwords_use_a_safe_alphabet()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 20; i++)
            seen.Add(PostgresBundle.CreatePassword());

        Assert.True(seen.Count > 1);
        foreach (var password in seen)
        {
            Assert.Equal(24, password.Length);
            Assert.All(password, c => Assert.Contains(c, PostgresBundle.PasswordAlphabet));
            Assert.DoesNotContain('\'', password);
            Assert.DoesNotContain(';', password);
            Assert.DoesNotContain('"', password);
        }
    }

    [Fact]
    public void Command_lines_do_not_carry_the_superuser_password()
    {
        const string password = "SUPERSECRET99";
        var data = PostgresBundle.DataDirectory;
        var pwFile = PostgresBundle.InstallRoot + @"\pwfile.tmp";
        var init = PostgresBundle.InitDbArguments(data, pwFile);
        var register = PostgresBundle.RegisterServiceArguments(data, @"NT AUTHORITY\NETWORK SERVICE");
        var start = PostgresBundle.StartArguments(data);
        var acl = PostgresBundle.GrantServiceAccessArguments(data, "F");

        Assert.Contains("--auth-host=scram-sha-256", init, StringComparison.Ordinal);
        Assert.Contains("--locale=C", init, StringComparison.Ordinal);
        Assert.Contains(pwFile, init, StringComparison.Ordinal);
        Assert.Contains("listen_addresses=localhost", register, StringComparison.Ordinal);
        Assert.Contains(PostgresBundle.ServiceName, register, StringComparison.Ordinal);
        Assert.Contains("-w -t 60", start, StringComparison.Ordinal);
        Assert.Contains("-m fast", PostgresBundle.StopArguments(data), StringComparison.Ordinal);
        Assert.Contains("*S-1-5-20:(OI)(CI)F", acl, StringComparison.Ordinal);
        Assert.Contains("*S-1-5-32-544:F", PostgresBundle.LockdownFileArguments(PostgresBundle.SuperuserFile), StringComparison.Ordinal);

        foreach (var line in new[] { init, register, start, acl, PostgresBundle.VcRedistArguments, PostgresBundle.QueryServiceArguments() })
            Assert.DoesNotContain(password, line, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => PostgresBundle.RegisterServiceArguments(data, "bad\"account"));
        Assert.Throws<ArgumentException>(() => PostgresBundle.GrantServiceAccessArguments(data, "F;whoami"));
    }

    [Fact]
    public void Vc_redist_accepts_already_installed_and_reboot_codes()
    {
        Assert.True(PostgresBundle.IsVcRedistSuccess(0));
        Assert.True(PostgresBundle.IsVcRedistSuccess(1638));
        Assert.True(PostgresBundle.IsVcRedistSuccess(3010));
        Assert.True(PostgresBundle.IsVcRedistSuccess(1641));
        Assert.False(PostgresBundle.IsVcRedistSuccess(1603));
        Assert.True(PostgresBundle.NeedsReboot(3010));
        Assert.False(PostgresBundle.NeedsReboot(1638));
    }

    [Theory]
    [InlineData(false, false, false, false, PostgresBundle.Decision.PayloadMissing)]
    [InlineData(true, true, false, false, PostgresBundle.Decision.PortTaken)]
    [InlineData(true, false, true, false, PostgresBundle.Decision.SuperuserUnknown)]
    [InlineData(true, true, true, false, PostgresBundle.Decision.SuperuserUnknown)]
    [InlineData(true, false, true, true, PostgresBundle.Decision.Reuse)]
    [InlineData(true, true, true, true, PostgresBundle.Decision.Reuse)]
    [InlineData(true, false, false, false, PostgresBundle.Decision.Initialize)]
    public void Install_decision_matches_the_machine(bool payload, bool portOpen, bool data, bool superuser, PostgresBundle.Decision expected)
    {
        Assert.Equal(expected, PostgresBundle.Decide(payload, portOpen, data, superuser));
    }

    [Fact]
    public void Zip_keeps_server_files_and_drops_pgadmin_and_data()
    {
        Assert.Equal("bin/initdb.exe", PostgresBundle.MapZipEntry("pgsql/bin/initdb.exe"));
        Assert.Equal("lib/libpq.dll", PostgresBundle.MapZipEntry(@"pgsql\lib\libpq.dll"));
        Assert.Null(PostgresBundle.MapZipEntry("pgsql/pgAdmin 4/runtime/pgAdmin4.exe"));
        Assert.Null(PostgresBundle.MapZipEntry("pgsql/StackBuilder/stackbuilder.exe"));
        Assert.Null(PostgresBundle.MapZipEntry("pgsql/bin/../../Windows/notepad.exe"));
        Assert.Null(PostgresBundle.MapZipEntry("pgsql/data/PG_VERSION"));

        var root = Path.Combine(Path.GetTempPath(), "shift-pg-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(zip, "pgsql/bin/initdb.exe", "init");
                Add(zip, "pgsql/pgAdmin 4/runtime/pgAdmin4.exe", "admin");
                Add(zip, "pgsql/bin/../../outside.txt", "nope");
                Add(zip, "pgsql/share/postgres.bki", "bki");
            }
            buffer.Position = 0;
            using var read = new ZipArchive(buffer, ZipArchiveMode.Read);
            var count = PostgresBundle.ExtractBinaries(read, root);
            Assert.Equal(2, count);
            Assert.True(File.Exists(Path.Combine(root, "bin", "initdb.exe")));
            Assert.True(File.Exists(Path.Combine(root, "share", "postgres.bki")));
            Assert.False(File.Exists(Path.Combine(root, "pgAdmin 4", "runtime", "pgAdmin4.exe")));
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Listen_config_forces_localhost_and_superuser_file_roundtrips()
    {
        var conf = "#listen_addresses = 'localhost'\r\nlisten_addresses = '*'\r\nport = 5432\r\n";
        var updated = PostgresBundle.EnsureListenLocal(conf);
        Assert.EndsWith("listen_addresses = 'localhost'\r\nport = 5432\r\n", updated, StringComparison.Ordinal);
        Assert.Equal(updated, PostgresBundle.EnsureListenLocal(updated));

        var password = PostgresBundle.CreatePassword();
        var file = PostgresBundle.FormatSuperuserFile(password);
        Assert.Equal(password, PostgresBundle.ReadSuperuserPassword(file));
        Assert.DoesNotContain(password, PostgresBundle.SuperuserSavedMessage, StringComparison.Ordinal);
        Assert.Contains(PostgresBundle.SuperuserFile, PostgresBundle.SuperuserSavedMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Init_failure_text_hides_the_password()
    {
        var text = PostgresBundle.BriefFailure("creating directory\npassword SUPERSECRET99 rejected\n", "SUPERSECRET99");
        Assert.Contains("creating directory", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SUPERSECRET99", text, StringComparison.Ordinal);
        Assert.Contains("***", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_check_recognizes_only_our_data_directory()
    {
        var ours = "BINARY_PATH_NAME : \"C:\\ShiftClub\\PostgreSQL\\bin\\pg_ctl.exe\" runservice -D \"C:\\ShiftClub\\PostgreSQL\\data\"";
        var foreign = "BINARY_PATH_NAME : \"C:\\Program Files\\PostgreSQL\\16\\bin\\pg_ctl.exe\" runservice -D \"C:\\Program Files\\PostgreSQL\\16\\data\"";
        Assert.True(PostgresBundle.ServicePointsAt(ours, PostgresBundle.DataDirectory));
        Assert.False(PostgresBundle.ServicePointsAt(foreign, PostgresBundle.DataDirectory));
    }

    [Fact]
    public void Published_binaries_are_pinned_to_the_edb_checksum()
    {
        Assert.EndsWith("postgresql-16.15-5-windows-x64-binaries.zip", PostgresBundle.BinariesUrl, StringComparison.Ordinal);
        Assert.Equal("43bb45f173a6f08cf1d29a97a6d8deb119e8e8093a24c00d2d1001a0ccaa8281", PostgresBundle.BinariesSha256);
        Assert.Equal(64, PostgresBundle.BinariesSha256.Length);
        Assert.Contains("vc_redist.x64.exe", PostgresBundle.VcRedistUrl, StringComparison.Ordinal);
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static ServerSetupAnswers External() => new()
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
        LicenseServerUrl = "https://licence.shift-club.kz"
    };
}
