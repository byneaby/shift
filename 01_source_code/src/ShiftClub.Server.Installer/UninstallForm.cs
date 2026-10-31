using ShiftClub.Server.Setup;

namespace ShiftClub.Server.Installer;

internal sealed class UninstallForm : Form
{
    private readonly CheckBox _postgres = new()
    {
        Text = "Удалить и базу PostgreSQL этого клуба. Гости, касса и сеансы будут стёрты.",
        AutoSize = true
    };
    private readonly Label _status = new() { AutoSize = false, Height = 120 };
    private readonly Button _delete = new() { Text = "Удалить", Width = 140, Height = 40 };
    private readonly Button _close = new() { Text = "Закрыть", Width = 110, Height = 40 };
    private readonly ServerInstallMarker? _marker;
    private bool _removeOnClose;

    public UninstallForm()
    {
        Text = "Удаление сервера";
        ClientSize = new Size(560, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.FromArgb(18, 18, 20);
        ForeColor = Color.WhiteSmoke;

        var directory = UninstallRunner.DetectDirectory();
        _marker = directory is null ? null : UninstallRunner.Describe(directory);

        var title = new Label
        {
            Text = _marker?.ClubName ?? "Сервер не найден",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 106, 0),
            AutoSize = false,
            Height = 36,
            Location = new Point(24, 18),
            Width = 510
        };
        var lead = new Label
        {
            Text = _marker is null
                ? "В C:\\ShiftClub\\Server нет установленного сервера."
                : "Будут сняты автозапуск, правило брандмауэра и файлы сервера. База останется, если не отметить галочку.",
            ForeColor = Color.FromArgb(190, 190, 196),
            AutoSize = false,
            Height = 48,
            Location = new Point(24, 58),
            Width = 510
        };
        var folder = new Label
        {
            Text = _marker is null ? "" : "Папка: " + _marker.InstallDirectory,
            ForeColor = Color.FromArgb(160, 160, 165),
            AutoSize = false,
            Height = 36,
            Location = new Point(24, 114),
            Width = 510
        };
        _postgres.Location = new Point(24, 160);
        _postgres.Width = 510;
        _postgres.ForeColor = Color.WhiteSmoke;
        _postgres.BackColor = Color.Transparent;
        _postgres.Enabled = _marker is not null && ServerUninstallPlan.CanRemoveBundledPostgres(_marker, true);
        _postgres.Visible = _postgres.Enabled;
        _status.Location = new Point(24, 210);
        _status.Width = 510;
        _status.ForeColor = Color.FromArgb(160, 160, 165);
        _status.Text = _marker is null ? "" : "После удаления панель этого клуба больше не откроется.";

        Style(_delete, true);
        Style(_close, false);
        _delete.Enabled = _marker is not null;
        _delete.Location = new Point(300, 350);
        _close.Location = new Point(450, 350);
        _delete.Click += async (_, _) => await DeleteAsync();
        _close.Click += (_, _) => Close();
        FormClosing += (_, _) =>
        {
            if (_removeOnClose && _marker is not null)
                UninstallRunner.ScheduleDirectoryRemoval(_marker.InstallDirectory);
        };

        Controls.Add(title);
        Controls.Add(lead);
        Controls.Add(folder);
        Controls.Add(_postgres);
        Controls.Add(_status);
        Controls.Add(_delete);
        Controls.Add(_close);
    }

    private async Task DeleteAsync()
    {
        if (_marker is null)
            return;
        var question = _postgres.Checked
            ? "Удалить сервер и базу PostgreSQL? Гости и касса будут стёрты."
            : "Удалить сервер? База PostgreSQL останется на этом компьютере.";
        if (MessageBox.Show(this, question, _marker.ClubName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        _delete.Enabled = false;
        _close.Enabled = false;
        _postgres.Enabled = false;
        var marker = _marker;
        var removePostgres = _postgres.Checked;
        var error = await Task.Run(() => UninstallRunner.RunAsync(marker, removePostgres, text =>
        {
            if (IsHandleCreated)
                BeginInvoke(() => _status.Text = text);
        }, CancellationToken.None));

        if (error is null)
        {
            _status.Text = "Сервер удалён. Это окно можно закрыть.";
            _status.ForeColor = Color.FromArgb(61, 220, 151);
            _removeOnClose = true;
            _close.Enabled = true;
            _close.Text = "Готово";
            return;
        }

        _status.Text = error;
        _status.ForeColor = Color.FromArgb(255, 138, 138);
        _close.Enabled = true;
        _delete.Enabled = true;
    }

    private static void Style(Button btn, bool accent)
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
