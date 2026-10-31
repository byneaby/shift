namespace ShiftClub.Server.Setup;

/// <summary>
/// What the club owner fills in the server setup wizard.
/// Postgres admin password is used only to create the database and is never written to secrets.env.
/// </summary>
public sealed class ServerSetupAnswers
{
    public string InstallDirectory { get; set; } = @"C:\ShiftClub\Server";

    public int ListenPort { get; set; } = 5080;

    public bool OpenFirewall { get; set; } = true;

    public bool StartWithWindows { get; set; } = true;

    /// <summary>
    /// Install PostgreSQL 16 from the binaries embedded in the setup EXE.
    /// Database passwords are generated and the server listens on localhost only.
    /// </summary>
    public bool InstallBundledPostgres { get; set; } = true;

    public string DatabaseHost { get; set; } = "127.0.0.1";

    public int DatabasePort { get; set; } = 5432;

    public string DatabaseName { get; set; } = "shiftclub";

    public string DatabaseUser { get; set; } = "shiftclub";

    public string DatabasePassword { get; set; } = "";

    /// <summary>Create the role and database with the postgres superuser. Otherwise the database must already exist.</summary>
    public bool CreateDatabase { get; set; } = true;

    public string PostgresAdminUser { get; set; } = "postgres";

    public string PostgresAdminPassword { get; set; } = "";

    public string OwnerLogin { get; set; } = "owner";

    public string OwnerPassword { get; set; } = "";

    public string OwnerPasswordConfirm { get; set; } = "";

    public string LicenseKey { get; set; } = "";

    /// <summary>Start locked. The owner pastes the key on the lock page.</summary>
    public bool LicenseLater { get; set; }

    public string LicenseServerUrl { get; set; } = "https://licence.shift-club.kz";

    public bool ReplaceExistingSecrets { get; set; }
}
