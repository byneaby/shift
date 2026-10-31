using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Principal;
using ShiftClub.Server.Setup;

namespace ShiftClub.Server.Installer;

internal sealed class MachineProbe
{
    public MachineSnapshot Snapshot { get; init; } = new();
    public IReadOnlyList<int> ProcessIdsToClose { get; init; } = Array.Empty<int>();

    public static MachineProbe Collect(ServerSetupAnswers answers)
    {
        var installDir = (answers.InstallDirectory ?? "").Trim().TrimEnd('\\');
        var bundled = answers.InstallBundledPostgres;
        var dbPort = bundled ? PostgresBundle.Port : answers.DatabasePort;
        var dbHost = bundled ? "127.0.0.1" : (answers.DatabaseHost ?? "").Trim();
        var panelOpen = IsPortOpen("127.0.0.1", answers.ListenPort);
        var dbOpen = dbPort is > 0 and <= 65535 && dbHost.Length > 0 && IsPortOpen(dbHost, dbPort);

        var processes = ScanProcesses();
        var serverRunning = processes.Any(item => item.Close && item.Name.Equals("ShiftClub.Server", StringComparison.OrdinalIgnoreCase));
        var ourInit = processes.Any(item => item.Close && item.Name.Equals("initdb", StringComparison.OrdinalIgnoreCase));
        var foreignInit = processes.Any(item => !item.Close && item.Name.Equals("initdb", StringComparison.OrdinalIgnoreCase));
        var ourPostgres = processes.Any(item => item.Close && item.Name.Equals("postgres", StringComparison.OrdinalIgnoreCase));

        var (serviceInstalled, serviceOurs, serviceRunning) = ReadService();
        var tasks = TaskExists("ShiftClubApi") || TaskExists("ShiftClubApiWatchdog");

        var snapshot = new MachineSnapshot
        {
            IsAdministrator = IsAdmin(),
            HasServerPayload = ResourceExists("payload.server.zip"),
            HasPostgresPayload = ResourceExists(PostgresBundle.ZipResourceName),
            ServerExePresent = File.Exists(Path.Combine(installDir, "ShiftClub.Server.exe"))
                || File.Exists(Path.Combine(installDir, "ShiftClub.Server.dll")),
            PostgresBinariesPresent = File.Exists(PostgresBundle.InitDbPath),
            InstallDirectoryExists = Directory.Exists(installDir),
            SecretsExist = File.Exists(Path.Combine(installDir, "secrets.env")),
            ServerProcessRunning = serverRunning,
            PanelPortOpen = panelOpen,
            PanelPort = answers.ListenPort,
            DatabasePortOpen = dbOpen,
            PostgresDataDirectoryExists = Directory.Exists(PostgresBundle.DataDirectory),
            PostgresDataInitialized = PostgresBundle.IsInitialized(PostgresBundle.DataDirectory),
            SuperuserFileExists = File.Exists(PostgresBundle.SuperuserFile),
            PostgresServiceInstalled = serviceInstalled,
            PostgresServiceIsOurs = serviceOurs,
            OurPostgresServiceRunning = serviceInstalled && serviceOurs && serviceRunning,
            OurInitDbRunning = ourInit,
            ForeignInitDbRunning = foreignInit,
            OurPostgresProcessRunning = ourPostgres,
            ShiftTasksExist = tasks,
            InstallBundledPostgres = bundled,
            CreateDatabase = answers.CreateDatabase,
            StartWithWindows = answers.StartWithWindows,
            FreeMegabytes = FreeMegabytes(installDir, bundled)
        };

        return new MachineProbe
        {
            Snapshot = snapshot,
            ProcessIdsToClose = processes.Where(item => item.Close).Select(item => item.Pid).Distinct().ToArray()
        };
    }

    private readonly record struct SeenProcess(int Pid, string Name, bool Close);

    private static List<SeenProcess> ScanProcesses()
    {
        var found = new List<SeenProcess>();
        foreach (var name in new[] { "ShiftClub.Server", "initdb", "postgres", "pg_ctl" })
        {
            Process[] list;
            try
            {
                list = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in list)
            {
                try
                {
                    var path = "";
                    try { path = process.MainModule?.FileName ?? ""; } catch { /* protected process */ }
                    var close = name switch
                    {
                        "ShiftClub.Server" => true,
                        "initdb" => BelongsToUs(path),
                        _ => BelongsToUs(path)
                    };
                    if (name == "initdb" && path.Length == 0)
                        close = false;
                    if (close || name == "initdb")
                        found.Add(new SeenProcess(process.Id, name, close));
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return found;
    }

    private static bool BelongsToUs(string path) =>
        path.Contains(@"\ShiftClub\PostgreSQL\", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(PostgresBundle.InstallRoot, StringComparison.OrdinalIgnoreCase);

    private static (bool Installed, bool Ours, bool Running) ReadService()
    {
        var (queryCode, query) = ServerInstallRunner.RunVisible("sc.exe", "query " + PostgresBundle.ServiceName, 8_000);
        if (queryCode != 0)
            return (false, false, false);
        var (_, config) = ServerInstallRunner.RunVisible("sc.exe", PostgresBundle.QueryServiceArguments(), 8_000);
        var ours = PostgresBundle.ServicePointsAt(config, PostgresBundle.DataDirectory);
        var running = query.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        return (true, ours, running);
    }

    private static bool TaskExists(string name)
    {
        var (code, _) = ServerInstallRunner.RunVisible("schtasks.exe", $"/Query /TN {name} /FO LIST", 8_000);
        return code == 0;
    }

    private static bool IsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool ResourceExists(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        return stream is not null;
    }

    private static long FreeMegabytes(string installDir, bool bundled)
    {
        var samples = new List<long> { FreeOn(installDir) };
        if (bundled)
            samples.Add(FreeOn(PostgresBundle.InstallRoot));
        var known = samples.Where(value => value >= 0).ToArray();
        return known.Length == 0 ? long.MaxValue : known.Min();
    }

    private static long FreeOn(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return -1;
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return -1;
            return drive.AvailableFreeSpace / (1024 * 1024);
        }
        catch
        {
            return -1;
        }
    }

    private static bool IsPortOpen(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(host, port);
            return task.Wait(TimeSpan.FromSeconds(2)) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
