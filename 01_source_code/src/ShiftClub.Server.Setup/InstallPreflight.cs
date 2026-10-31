using System.Text;

namespace ShiftClub.Server.Setup;

/// <summary>
/// What the installer saw on the club PC before it changes anything.
/// </summary>
public sealed class MachineSnapshot
{
    public bool IsAdministrator { get; set; }
    public bool HasServerPayload { get; set; }
    public bool HasPostgresPayload { get; set; }
    public bool ServerExePresent { get; set; }
    public bool PostgresBinariesPresent { get; set; }
    public bool InstallDirectoryExists { get; set; }
    public bool SecretsExist { get; set; }
    public bool ServerProcessRunning { get; set; }
    public bool PanelPortOpen { get; set; }
    public int PanelPort { get; set; } = 5080;
    public bool DatabasePortOpen { get; set; }
    public bool PostgresDataDirectoryExists { get; set; }
    public bool PostgresDataInitialized { get; set; }
    public bool SuperuserFileExists { get; set; }
    public bool PostgresServiceInstalled { get; set; }
    public bool PostgresServiceIsOurs { get; set; }
    public bool OurPostgresServiceRunning { get; set; }
    public bool OurInitDbRunning { get; set; }
    public bool ForeignInitDbRunning { get; set; }
    public bool OurPostgresProcessRunning { get; set; }
    public bool ShiftTasksExist { get; set; }
    public bool InstallBundledPostgres { get; set; } = true;
    public bool CreateDatabase { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public long FreeMegabytes { get; set; } = 100_000;
}

public sealed class PreflightPlan
{
    public PreflightPlan(
        bool canInstall,
        IReadOnlyList<string> found,
        IReadOnlyList<string> willClose,
        IReadOnlyList<string> nextSteps,
        IReadOnlyList<string> blockers)
    {
        CanInstall = canInstall;
        Found = found;
        WillClose = willClose;
        NextSteps = nextSteps;
        Blockers = blockers;
    }

    public bool CanInstall { get; }
    public IReadOnlyList<string> Found { get; }
    public IReadOnlyList<string> WillClose { get; }
    public IReadOnlyList<string> NextSteps { get; }
    public IReadOnlyList<string> Blockers { get; }

    public string Text
    {
        get
        {
            var sb = new StringBuilder();
            void Section(string title, IReadOnlyList<string> lines)
            {
                if (lines.Count == 0)
                    return;
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.AppendLine(title);
                foreach (var line in lines)
                    sb.AppendLine("• " + line);
            }

            if (!CanInstall)
                Section("Установку пока нельзя начать:", Blockers);
            Section("Уже на компьютере:", Found);
            Section("Перед установкой будет закрыто:", WillClose);
            if (CanInstall)
                Section("Затем установка:", NextSteps);
            return sb.ToString().TrimEnd();
        }
    }
}

public static class InstallPreflight
{
    public const long MinimumFreeMegabytes = 1536;

    public static PreflightPlan Evaluate(MachineSnapshot snapshot)
    {
        var found = new List<string>();
        var close = new List<string>();
        var next = new List<string>();
        var blockers = new List<string>();

        void Block(string message)
        {
            if (!blockers.Contains(message))
                blockers.Add(message);
        }

        void Close(string message)
        {
            if (!close.Contains(message))
                close.Add(message);
        }

        if (!snapshot.IsAdministrator)
            Block("Запустите установщик от имени администратора.");

        if (!snapshot.HasServerPayload && !snapshot.ServerExePresent)
            Block("В этом установщике нет файлов сервера. Соберите его скриптом Build-ServerInstaller.ps1.");

        if (snapshot.FreeMegabytes < MinimumFreeMegabytes)
            Block($"На диске мало места: {snapshot.FreeMegabytes} МБ. Нужно хотя бы {MinimumFreeMegabytes} МБ.");

        found.Add(snapshot.InstallDirectoryExists
            ? "Папка сервера уже есть."
            : "Папки сервера ещё нет, она будет создана.");

        if (snapshot.SecretsExist)
            found.Add("Файл настроек уже есть. База и папка data не удаляются.");

        if (snapshot.ServerProcessRunning)
        {
            found.Add("Сервер SHIFT Club сейчас запущен.");
            Close("ShiftClub.Server.exe");
        }

        if (snapshot.PanelPortOpen && !snapshot.ServerProcessRunning)
            Block($"Порт панели {snapshot.PanelPort} занят другой программой. Закройте её или укажите другой порт.");

        if (snapshot.ShiftTasksExist)
        {
            found.Add("Автозапуск SHIFT Club уже стоит в планировщике.");
            Close("задачи ShiftClubApi и ShiftClubApiWatchdog на время копирования файлов");
        }

        if (snapshot.InstallBundledPostgres)
        {
            if (!snapshot.HasPostgresPayload && !snapshot.PostgresBinariesPresent)
                Block(PostgresBundle.RebuildMessage);

            if (snapshot.PostgresDataInitialized && !snapshot.SuperuserFileExists)
                Block(PostgresBundle.SuperuserUnknownMessage);
            else if (snapshot.PostgresDataInitialized)
                found.Add("Кластер PostgreSQL SHIFT уже создан. Заново он создаваться не будет.");
            else if (snapshot.PostgresDataDirectoryExists)
                found.Add("Прошлая установка PostgreSQL не закончилась. Неполная папка данных будет удалена.");
            else
                found.Add("PostgreSQL 16 ещё не установлен.");

            var foreignService = snapshot.PostgresServiceInstalled && !snapshot.PostgresServiceIsOurs;
            if (foreignService)
                Block(PostgresBundle.ForeignServiceMessage);

            if (snapshot.DatabasePortOpen && !snapshot.PostgresDataInitialized)
                Block(PostgresBundle.PortTakenMessage);

            if (snapshot.OurPostgresServiceRunning)
            {
                found.Add("Служба PostgreSQL SHIFT запущена.");
                Close("служба " + PostgresBundle.ServiceName);
            }

            if (snapshot.OurPostgresProcessRunning)
                Close("postgres.exe из папки SHIFT Club");

            if (snapshot.OurInitDbRunning)
            {
                found.Add("От прошлой попытки ещё работает создание кластера.");
                Close("initdb.exe от прошлой установки SHIFT Club");
            }
            else if (snapshot.ForeignInitDbRunning)
                Block("Запущено создание другой базы (initdb.exe). Закройте его в диспетчере задач и запустите установщик снова.");

            if (snapshot.PostgresDataInitialized && snapshot.SuperuserFileExists)
                next.Add("Запущу уже созданный PostgreSQL SHIFT. Новый кластер создаваться не будет.");
            else if (!foreignService && !(snapshot.DatabasePortOpen && !snapshot.PostgresDataInitialized))
                next.Add("Поставлю PostgreSQL 16 и создам кластер. Антивирус может задержать этот шаг на несколько минут.");
        }
        else if (!snapshot.DatabasePortOpen)
        {
            Block("Выбран уже установленный PostgreSQL, но он не отвечает. Запустите его службу и повторите.");
        }
        else
        {
            found.Add("Выбранный PostgreSQL отвечает.");
            next.Add(snapshot.CreateDatabase
                ? "Создам базу на выбранном PostgreSQL."
                : "Подключусь к уже созданной базе.");
        }

        if (blockers.Count == 0)
        {
            next.Add("Скопирую файлы сервера и запишу настройки.");
            if (snapshot.StartWithWindows)
                next.Add("Включу автозапуск вместе с Windows.");
        }

        return new PreflightPlan(blockers.Count == 0, found, close, next, blockers);
    }
}
