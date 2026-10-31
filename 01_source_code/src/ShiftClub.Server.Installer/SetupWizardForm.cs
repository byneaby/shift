using ShiftClub.Server.Setup;

namespace ShiftClub.Server.Installer;

internal sealed class SetupWizardForm : Form
{
    private readonly TextBox _dir = new() { Text = @"C:\ShiftClub\Server" };
    private readonly TextBox _port = new() { Text = "5080" };
    private readonly CheckBox _firewall = new() { Text = "Открыть порт в локальной сети", Checked = true, AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "Запускать сервер вместе с Windows", Checked = true, AutoSize = true };
    private readonly CheckBox _replaceSecrets = new() { Text = "Заменить уже записанные настройки. Базу и папку data не удалять.", AutoSize = true, Visible = false };
    private readonly Label _existing = new() { ForeColor = Color.FromArgb(255, 180, 90), AutoSize = false, Visible = false, Height = 36 };

    private readonly CheckBox _bundle = new() { Text = "Поставить PostgreSQL 16 вместе с сервером", Checked = true, AutoSize = true };
    private readonly Label _bundleNote = Note("Мастер сам создаст пароли. PostgreSQL будет доступен только с этого компьютера. Если PostgreSQL уже установлен, снимите галочку и укажите его пароль.");
    private readonly CheckBox _createDb = new() { Text = "Создать базу на этом PostgreSQL", Checked = true, AutoSize = true };
    private readonly Label _hostLabel = LabelOf("Адрес");
    private readonly TextBox _dbHost = new() { Text = "127.0.0.1" };
    private readonly Label _portLabel = LabelOf("Порт");
    private readonly TextBox _dbPort = new() { Text = "5432" };
    private readonly Label _nameLabel = LabelOf("Имя базы");
    private readonly TextBox _dbName = new() { Text = "shiftclub" };
    private readonly Label _userLabel = LabelOf("Пользователь базы");
    private readonly TextBox _dbUser = new() { Text = "shiftclub" };
    private readonly Label _passwordLabel = LabelOf("Пароль пользователя базы");
    private readonly TextBox _dbPassword = new() { UseSystemPasswordChar = true };
    private readonly TextBox _adminUser = new() { Text = "postgres" };
    private readonly TextBox _adminPassword = new() { UseSystemPasswordChar = true };
    private readonly Label _adminUserLabel = new() { Text = "Администратор PostgreSQL", ForeColor = Color.FromArgb(160, 160, 165), AutoSize = false, Height = 18 };
    private readonly Label _adminPasswordLabel = new() { Text = "Пароль администратора", ForeColor = Color.FromArgb(160, 160, 165), AutoSize = false, Height = 18 };
    private readonly Button _testDb = new() { Text = "Проверить подключение", Width = 210, Height = 34 };
    private Panel _databasePage = null!;

    private readonly TextBox _owner = new() { Text = "owner" };
    private readonly TextBox _ownerPassword = new() { UseSystemPasswordChar = true };
    private readonly TextBox _ownerPassword2 = new() { UseSystemPasswordChar = true };

    private readonly TextBox _license = new();
    private readonly CheckBox _licenseLater = new() { Text = "Ключа ещё нет — сервер попросит его при запуске", AutoSize = true };
    private readonly TextBox _licenseUrl = new() { Text = "https://licence.shift-club.kz" };

    private readonly Label _summary = new() { AutoSize = false };
    private readonly Label _status = new() { AutoSize = false, Height = 150 };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Visible = false, Height = 16 };
    private readonly Label _adminHint = new() { Text = "Пароль пользователя postgres нужен один раз, чтобы создать базу. В файл настроек он не пишется.", ForeColor = Color.FromArgb(160, 160, 165), AutoSize = false, Height = 36 };
    private bool _done;

    private readonly Button _back = new() { Text = "Назад", Width = 110, Height = 40 };
    private readonly Button _next = new() { Text = "Далее", Width = 140, Height = 40 };
    private readonly Panel[] _pages;
    private int _step;

    public SetupWizardForm()
    {
        Text = "SHIFT Club — установка сервера";
        ClientSize = new Size(640, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.FromArgb(18, 18, 20);
        ForeColor = Color.WhiteSmoke;

        _pages = new[]
        {
            Page("Сервер клуба", "Мастер поставит сервер кассы и PostgreSQL 16, затем спросит пароль владельца и ключ лицензии. После установки панель кассы откроется в браузере.",
                Note("PostgreSQL 16, Visual C++ и .NET уже внутри установщика. База слушает только этот компьютер. Ключ берут в центре лицензий licence.shift-club.kz — пробный бывает на 3 или 7 дней.")),
            FolderPage(),
            DatabasePage(),
            Page("Владелец панели", "Этим логином открывают кассу и настройки. Потом пароль можно сменить в панели.",
                LabelOf("Логин"), _owner,
                LabelOf("Пароль, минимум 8 символов"), _ownerPassword,
                LabelOf("Пароль ещё раз"), _ownerPassword2),
            LicensePage(),
            ReviewPage()
        };

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        foreach (var page in _pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            body.Controls.Add(page);
        }

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(20, 12, 20, 16) };
        StyleButton(_back, false);
        StyleButton(_next, true);
        footer.Controls.Add(_back);
        footer.Controls.Add(_next);
        footer.Resize += (_, _) => PlaceFooter(footer);
        PlaceFooter(footer);

        Controls.Add(body);
        Controls.Add(footer);
        _back.Click += (_, _) => Go(-1);
        _next.Click += async (_, _) => await OnNextAsync();
        _dir.Leave += (_, _) => RefreshExistingSecrets();
        _bundle.CheckedChanged += (_, _) => ToggleDatabaseMode();
        _createDb.CheckedChanged += (_, _) => ToggleDatabaseMode();
        _licenseLater.CheckedChanged += (_, _) => _license.Enabled = !_licenseLater.Checked;
        StyleButton(_testDb, false);
        _testDb.Click += async (_, _) => await TestDatabaseAsync();
        ShowStep();
    }

    private Panel FolderPage()
    {
        var browse = new Button { Text = "Обзор", Width = 88, Height = 30 };
        StyleButton(browse, false);
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Папка сервера SHIFT Club", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _dir.Text = dlg.SelectedPath;
                RefreshExistingSecrets();
            }
        };
        var row = new Panel { Height = 32 };
        row.Controls.Add(_dir);
        row.Controls.Add(browse);
        row.Resize += (_, _) =>
        {
            browse.Location = new Point(row.ClientSize.Width - browse.Width, 1);
            _dir.Width = Math.Max(80, browse.Left - 8);
            _dir.Height = 30;
        };
        return Page("Куда ставить", "Сервер кассы будет лежать в этой папке и слушать порт. Компьютеры зала подключаются к нему по локальной сети.",
            LabelOf("Папка"), row, LabelOf("Порт панели"), _port, _firewall, _autostart, _existing, _replaceSecrets);
    }

    private Panel DatabasePage()
    {
        _bundleNote.Height = 72;
        _databasePage = Page("База PostgreSQL", "Гости, касса и сеансы хранятся в PostgreSQL на этом компьютере.",
            _bundle,
            _bundleNote,
            _createDb,
            _hostLabel, _dbHost,
            _portLabel, _dbPort,
            _nameLabel, _dbName,
            _userLabel, _dbUser,
            _passwordLabel, _dbPassword,
            _adminHint,
            _adminUserLabel, _adminUser,
            _adminPasswordLabel, _adminPassword,
            _testDb);
        return _databasePage;
    }

    private Panel LicensePage()
    {
        _license.Font = new Font("Consolas", 12f);
        return Page("Лицензия", "Без действующего ключа сервер запускается, но касса и компьютеры не открываются, пока ключ не введут.",
            LabelOf("Ключ вида SHIFT-XXXX-XXXX-XXXX"), _license, _licenseLater,
            LabelOf("Адрес центра лицензий"), _licenseUrl,
            Note("Продление того же ключа сервер увидит сам. Новый ключ можно вставить потом в панели, в разделе «Лицензия»."));
    }

    private Panel ReviewPage()
    {
        _summary.Height = 220;
        _status.Text = "Проверьте и нажмите «Установить».";
        _status.ForeColor = Color.FromArgb(160, 160, 165);
        return Page("Проверка", "Пароли в этот список не выводятся.",
            _summary, _progress, _status);
    }

    private async Task OnNextAsync()
    {
        if (_done)
        {
            Close();
            return;
        }

        if (_step < _pages.Length - 1)
        {
            if (!ValidateCurrent())
                return;
            if (_step == 0)
                RefreshExistingSecrets();
            _step++;
            if (_step == _pages.Length - 1)
                FillSummary();
            ShowStep();
            return;
        }

        if (!ValidateCurrent())
            return;
        await InstallAsync();
    }

    private void Go(int delta)
    {
        _step = Math.Clamp(_step + delta, 0, _pages.Length - 1);
        ShowStep();
    }

    private bool ValidateCurrent()
    {
        var answers = ReadAnswers();
        var secretsExist = File.Exists(Path.Combine(answers.InstallDirectory.Trim().TrimEnd('\\'), "secrets.env"));
        IReadOnlyList<string> errors = _step switch
        {
            1 => ServerSetupValidator.ValidateFolder(answers, secretsExist),
            2 => ServerSetupValidator.ValidateDatabase(answers),
            3 => ServerSetupValidator.ValidateOwner(answers),
            4 => ServerSetupValidator.ValidateLicense(answers),
            5 => ServerSetupValidator.Validate(answers, secretsExist),
            _ => Array.Empty<string>()
        };
        if (errors.Count == 0)
            return true;
        MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Проверьте поля", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private async Task TestDatabaseAsync()
    {
        var answers = ReadAnswers();
        var errors = ServerSetupValidator.ValidateDatabase(answers);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "База", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        UseWaitCursor = true;
        var error = answers.CreateDatabase
            ? await DatabaseSetup.TryConnectAsync(ServerSecrets.AdminConnectionString(answers), CancellationToken.None)
            : await DatabaseSetup.TryConnectAsync(ServerSecrets.AppConnectionString(answers), CancellationToken.None);
        UseWaitCursor = false;
        MessageBox.Show(this,
            error ?? (answers.CreateDatabase ? "Администратор PostgreSQL отвечает. База будет создана при установке." : "База отвечает."),
            "База",
            MessageBoxButtons.OK,
            error is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async Task InstallAsync()
    {
        _back.Enabled = false;
        _next.Enabled = false;
        _progress.Visible = true;
        try
        {
            var outcome = await ServerInstallRunner.RunAsync(ReadAnswers(), text =>
            {
                if (IsHandleCreated)
                    BeginInvoke(() => _status.Text = text);
            }, CancellationToken.None);
            _status.Text = outcome.Message;
            _status.ForeColor = outcome.Ok ? Color.FromArgb(61, 220, 151) : Color.FromArgb(255, 138, 138);
            _back.Enabled = !outcome.Ok;
            _next.Enabled = true;
            if (outcome.Ok)
            {
                _done = true;
                _next.Text = "Закрыть";
            }
        }
        catch (Exception)
        {
            _status.Text = "Установка остановилась. Запустите программу от имени администратора.";
            _status.ForeColor = Color.FromArgb(255, 138, 138);
            _back.Enabled = true;
            _next.Enabled = true;
        }
        finally
        {
            _progress.Visible = false;
            UseWaitCursor = false;
        }
    }

    private void FillSummary()
    {
        var lines = WindowsInstallPlan.Summary(ReadAnswers())
            .Select(row => row.Label + ": " + row.Value);
        _summary.Text = string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private ServerSetupAnswers ReadAnswers() => new()
    {
        InstallDirectory = _dir.Text,
        ListenPort = int.TryParse(_port.Text.Trim(), out var port) ? port : 0,
        OpenFirewall = _firewall.Checked,
        StartWithWindows = _autostart.Checked,
        ReplaceExistingSecrets = _replaceSecrets.Checked,
        InstallBundledPostgres = _bundle.Checked,
        CreateDatabase = _bundle.Checked || _createDb.Checked,
        DatabaseHost = _bundle.Checked ? "127.0.0.1" : _dbHost.Text,
        DatabasePort = _bundle.Checked ? 5432 : int.TryParse(_dbPort.Text.Trim(), out var dbPort) ? dbPort : 0,
        DatabaseName = _dbName.Text,
        DatabaseUser = _dbUser.Text,
        DatabasePassword = _bundle.Checked ? "" : _dbPassword.Text,
        PostgresAdminUser = _bundle.Checked ? "postgres" : _adminUser.Text,
        PostgresAdminPassword = _bundle.Checked ? "" : _adminPassword.Text,
        OwnerLogin = _owner.Text,
        OwnerPassword = _ownerPassword.Text,
        OwnerPasswordConfirm = _ownerPassword2.Text,
        LicenseKey = _license.Text,
        LicenseLater = _licenseLater.Checked,
        LicenseServerUrl = _licenseUrl.Text
    };

    private void RefreshExistingSecrets()
    {
        var path = Path.Combine(_dir.Text.Trim().TrimEnd('\\'), "secrets.env");
        var exists = File.Exists(path);
        _existing.Visible = exists;
        _existing.Text = exists ? "В этой папке сервер уже настроен. Можно заменить только файл настроек." : "";
        _replaceSecrets.Visible = exists;
    }

    private void ToggleDatabaseMode()
    {
        var bundled = _bundle.Checked;
        var admin = !bundled && _createDb.Checked;
        _bundleNote.Visible = bundled;
        _createDb.Visible = !bundled;
        _hostLabel.Visible = _dbHost.Visible = !bundled;
        _portLabel.Visible = _dbPort.Visible = !bundled;
        _passwordLabel.Visible = _dbPassword.Visible = !bundled;
        _adminHint.Visible = admin;
        _adminUserLabel.Visible = _adminUser.Visible = admin;
        _adminPasswordLabel.Visible = _adminPassword.Visible = admin;
        _testDb.Visible = !bundled;
        if (_databasePage is not null)
            Reflow(_databasePage);
    }

    private static void Reflow(Panel page)
    {
        var y = 0;
        foreach (Control control in page.Controls)
        {
            if (!control.Visible)
                continue;
            control.Location = new Point(0, y);
            control.Width = 560;
            var gap = control is Label ? 4 : 10;
            y += control.Height + gap;
        }
    }

    private void ShowStep()
    {
        for (var i = 0; i < _pages.Length; i++)
            _pages[i].Visible = i == _step;
        _back.Enabled = _step > 0;
        _next.Text = _step == _pages.Length - 1 ? "Установить" : "Далее";
        if (_step == 1)
            RefreshExistingSecrets();
        if (_step == 2)
            ToggleDatabaseMode();
    }

    private void PlaceFooter(Panel footer)
    {
        _next.Location = new Point(footer.ClientSize.Width - _next.Width - 20, 16);
        _back.Location = new Point(_next.Left - _back.Width - 10, 16);
    }

    private Panel Page(string title, string lead, params Control[] fields)
    {
        var page = new Panel { AutoScroll = true, Padding = new Padding(24, 18, 24, 8), BackColor = BackColor };
        var y = 0;
        void Add(Control control, int height, int gap = 8)
        {
            control.Location = new Point(0, y);
            control.Width = 560;
            if (control.Height < 8)
                control.Height = height;
            if (control is TextBox)
                control.Height = 30;
            page.Controls.Add(control);
            y += control.Height + gap;
        }

        var heading = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 106, 0),
            AutoSize = false,
            Height = 36
        };
        Add(heading, 36, 4);
        var text = new Label { Text = lead, ForeColor = Color.FromArgb(190, 190, 196), AutoSize = false, Height = 48 };
        Add(text, 48, 12);
        foreach (var field in fields)
        {
            if (field is TextBox box)
                StyleInput(box);
            if (field is CheckBox check)
                StyleCheck(check);
            var height = field is Label { Height: > 0 } ? field.Height : field is CheckBox ? 28 : 30;
            Add(field, height, field is Label ? 4 : 10);
        }
        return page;
    }

    private static Label LabelOf(string text) => new()
    {
        Text = text,
        ForeColor = Color.FromArgb(160, 160, 165),
        AutoSize = false,
        Height = 18
    };

    private static Label Note(string text) => new()
    {
        Text = text,
        ForeColor = Color.FromArgb(160, 160, 165),
        AutoSize = false,
        Height = 64
    };

    private static void StyleInput(TextBox box)
    {
        box.BackColor = Color.FromArgb(28, 28, 32);
        box.ForeColor = Color.WhiteSmoke;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void StyleCheck(CheckBox box)
    {
        box.ForeColor = Color.WhiteSmoke;
        box.BackColor = Color.Transparent;
    }

    private static void StyleButton(Button btn, bool accent)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = accent ? 0 : 1;
        btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 88);
        btn.BackColor = accent ? Color.FromArgb(255, 106, 0) : Color.FromArgb(40, 40, 46);
        btn.ForeColor = accent ? Color.Black : Color.WhiteSmoke;
        btn.Font = new Font("Segoe UI", 10f, accent ? FontStyle.Bold : FontStyle.Regular);
        btn.Cursor = Cursors.Hand;
    }
}
