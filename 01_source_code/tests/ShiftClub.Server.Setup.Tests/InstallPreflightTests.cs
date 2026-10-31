using ShiftClub.Server.Setup;

namespace ShiftClub.Server.Setup.Tests;

public class InstallPreflightTests
{
    [Fact]
    public void Clean_pc_can_install_and_lists_the_cluster_step()
    {
        var plan = InstallPreflight.Evaluate(Clean());
        Assert.True(plan.CanInstall);
        Assert.Empty(plan.Blockers);
        Assert.Empty(plan.WillClose);
        Assert.Contains(plan.NextSteps, step => step.Contains("создам кластер", StringComparison.Ordinal));
        Assert.Contains("PostgreSQL 16 ещё не установлен.", plan.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Running_shift_server_is_closed_before_copy()
    {
        var snapshot = Clean();
        snapshot.ServerProcessRunning = true;
        snapshot.PanelPortOpen = true;
        snapshot.ShiftTasksExist = true;
        var plan = InstallPreflight.Evaluate(snapshot);
        Assert.True(plan.CanInstall);
        Assert.Contains(plan.WillClose, item => item.Contains("ShiftClub.Server.exe", StringComparison.Ordinal));
        Assert.Contains(plan.WillClose, item => item.Contains("ShiftClubApi", StringComparison.Ordinal));
    }

    [Fact]
    public void Existing_cluster_is_reused()
    {
        var snapshot = Clean();
        snapshot.PostgresDataInitialized = true;
        snapshot.PostgresDataDirectoryExists = true;
        snapshot.SuperuserFileExists = true;
        snapshot.PostgresBinariesPresent = true;
        snapshot.OurPostgresServiceRunning = true;
        snapshot.PostgresServiceInstalled = true;
        snapshot.PostgresServiceIsOurs = true;
        snapshot.DatabasePortOpen = true;
        var plan = InstallPreflight.Evaluate(snapshot);
        Assert.True(plan.CanInstall);
        Assert.Contains(plan.Found, line => line.Contains("Заново он создаваться не будет", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.NextSteps, step => step.Contains("создам кластер", StringComparison.Ordinal));
        Assert.Contains(plan.WillClose, item => item.Contains(PostgresBundle.ServiceName, StringComparison.Ordinal));
    }

    [Fact]
    public void Foreign_postgres_blocks_before_any_copy()
    {
        var snapshot = Clean();
        snapshot.DatabasePortOpen = true;
        var plan = InstallPreflight.Evaluate(snapshot);
        Assert.False(plan.CanInstall);
        Assert.Contains(plan.Blockers, line => line.Contains("5432", StringComparison.Ordinal));
        Assert.Contains("Установку пока нельзя начать", plan.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Foreign_service_and_foreign_initdb_block()
    {
        var service = Clean();
        service.PostgresServiceInstalled = true;
        service.PostgresServiceIsOurs = false;
        Assert.Contains(InstallPreflight.Evaluate(service).Blockers, line => line.Contains("служба PostgreSQL 16", StringComparison.Ordinal));

        var init = Clean();
        init.ForeignInitDbRunning = true;
        Assert.Contains(InstallPreflight.Evaluate(init).Blockers, line => line.Contains("initdb.exe", StringComparison.Ordinal));
    }

    [Fact]
    public void Our_leftover_initdb_is_closed_instead_of_blocking()
    {
        var snapshot = Clean();
        snapshot.OurInitDbRunning = true;
        snapshot.PostgresDataDirectoryExists = true;
        var plan = InstallPreflight.Evaluate(snapshot);
        Assert.True(plan.CanInstall);
        Assert.Contains(plan.WillClose, item => item.Contains("initdb.exe", StringComparison.Ordinal));
        Assert.Contains(plan.Found, line => line.Contains("Неполная папка данных", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_admin_disk_payload_or_panel_port_block()
    {
        var admin = Clean();
        admin.IsAdministrator = false;
        Assert.Contains(InstallPreflight.Evaluate(admin).Blockers, line => line.Contains("администратора", StringComparison.Ordinal));

        var disk = Clean();
        disk.FreeMegabytes = 200;
        Assert.Contains(InstallPreflight.Evaluate(disk).Blockers, line => line.Contains("мало места", StringComparison.Ordinal));

        var payload = Clean();
        payload.HasServerPayload = false;
        payload.ServerExePresent = false;
        Assert.Contains(InstallPreflight.Evaluate(payload).Blockers, line => line.Contains("нет файлов сервера", StringComparison.Ordinal));

        var port = Clean();
        port.PanelPortOpen = true;
        port.ServerProcessRunning = false;
        Assert.Contains(InstallPreflight.Evaluate(port).Blockers, line => line.Contains("Порт панели", StringComparison.Ordinal));
    }

    [Fact]
    public void External_postgres_must_already_answer()
    {
        var down = Clean();
        down.InstallBundledPostgres = false;
        down.DatabasePortOpen = false;
        Assert.False(InstallPreflight.Evaluate(down).CanInstall);

        var up = Clean();
        up.InstallBundledPostgres = false;
        up.DatabasePortOpen = true;
        var plan = InstallPreflight.Evaluate(up);
        Assert.True(plan.CanInstall);
        Assert.Contains(plan.Found, line => line.Contains("отвечает", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_text_has_no_password()
    {
        var snapshot = Clean();
        snapshot.ServerProcessRunning = true;
        var text = InstallPreflight.Evaluate(snapshot).Text;
        Assert.DoesNotContain("SECRET", text, StringComparison.Ordinal);
        Assert.DoesNotContain("password=", text, StringComparison.OrdinalIgnoreCase);
    }

    private static MachineSnapshot Clean() => new()
    {
        IsAdministrator = true,
        HasServerPayload = true,
        HasPostgresPayload = true,
        InstallBundledPostgres = true,
        CreateDatabase = true,
        StartWithWindows = true,
        FreeMegabytes = 20_000,
        PanelPort = 5080
    };
}
