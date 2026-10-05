using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShiftClub.Client.Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallForm());
    }
}

internal sealed class InstallForm : Form
{
    private readonly TextBox _pathBox = new();
    private readonly TextBox _urlBox = new();
    private readonly CheckBox _desktopShortcut = new() { Text = "Ярлык на рабочий стол", Checked = true, AutoSize = true };
    private readonly CheckBox _startup = new() { Text = "Автозапуск при входе в Windows", Checked = true, AutoSize = true };
    private readonly CheckBox _launchNow = new() { Text = "Запустить после установки", Checked = true, AutoSize = true };
    private readonly Button _browseBtn = new() { Text = "Обзор", Width = 88, Height = 30 };
    private readonly Button _installBtn = new() { Text = "Установить", Width = 150, Height = 40 };
    private readonly Button _cancelBtn = new() { Text = "Отмена", Width = 110, Height = 40 };
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new()
    {
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 30,
        Visible = false,
        Height = 16
    };

    public InstallForm()
    {
        Text = "SHIFT Club — установка клиента";
        ClientSize = new Size(560, 460);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.FromArgb(18, 18, 20);
        ForeColor = Color.WhiteSmoke;

        // Кнопки всегда внизу (Dock Bottom)
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 72,
            Padding = new Padding(20, 12, 20, 16)
        };
        StyleButton(_installBtn, true);
        StyleButton(_cancelBtn, false);
        _cancelBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _installBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _cancelBtn.Location = new Point(footer.ClientSize.Width - _cancelBtn.Width - 20, 16);
        _installBtn.Location = new Point(_cancelBtn.Left - _installBtn.Width - 10, 16);
        footer.Resize += (_, _) =>
        {
            _cancelBtn.Location = new Point(footer.ClientSize.Width - _cancelBtn.Width - 20, 16);
            _installBtn.Location = new Point(_cancelBtn.Left - _installBtn.Width - 10, 16);
        };
        footer.Controls.Add(_cancelBtn);
        footer.Controls.Add(_installBtn);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 8),
            AutoScroll = true
        };

        var y = 0;
        void Place(Control c, int height, int gap = 10)
        {
            c.Location = new Point(0, y);
            c.Width = body.ClientSize.Width - body.Padding.Horizontal;
            c.Height = height;
            c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            body.Controls.Add(c);
            y += height + gap;
        }

        var title = new Label
        {
            Text = "SHIFT Club Client",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 106, 0),
            AutoSize = false
        };
        Place(title, 34, 2);

        var sub = new Label
        {
            Text = "Установщик оболочки ПК клуба",
            ForeColor = Color.FromArgb(160, 160, 165),
            AutoSize = false
        };
        Place(sub, 22, 16);

        Place(FieldLabel("Папка установки"), 18, 4);
        Place(PathRow(), 32, 12);

        Place(FieldLabel("Адрес сервера (API)"), 18, 4);
        _urlBox.Text = "http://192.168.1.250:5080";
        StyleInput(_urlBox);
        Place(_urlBox, 30, 14);

        StyleCheck(_desktopShortcut);
        StyleCheck(_startup);
        StyleCheck(_launchNow);
        Place(_desktopShortcut, 24, 4);
        Place(_startup, 24, 4);
        Place(_launchNow, 24, 14);

        _progress.Width = body.ClientSize.Width - body.Padding.Horizontal;
        Place(_progress, 16, 8);

        _status.Text = "Готов к установке.";
        _status.ForeColor = Color.FromArgb(160, 160, 165);
        _status.AutoSize = false;
        Place(_status, 28, 0);

        Controls.Add(body);
        Controls.Add(footer);

        AcceptButton = _installBtn;
        CancelButton = _cancelBtn;
        _browseBtn.Click += (_, _) => Browse();
        _installBtn.Click += async (_, _) => await InstallAsync();
        _cancelBtn.Click += (_, _) => Close();
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        ForeColor = Color.FromArgb(160, 160, 165),
        AutoSize = false
    };

    private Control PathRow()
    {
        var panel = new Panel { Height = 32 };
        _pathBox.Text = @"D:\Apps\ShiftClub\Shell";
        StyleInput(_pathBox);
        StyleButton(_browseBtn, false);
        _pathBox.Location = new Point(0, 1);
        _pathBox.Height = 30;
        _browseBtn.Height = 30;
        panel.Resize += (_, _) =>
        {
            _browseBtn.Location = new Point(panel.ClientSize.Width - _browseBtn.Width, 1);
            _pathBox.Width = _browseBtn.Left - 8;
        };
        panel.Controls.Add(_pathBox);
        panel.Controls.Add(_browseBtn);
        // trigger initial sizes after parent width known
        panel.HandleCreated += (_, _) =>
        {
            _browseBtn.Location = new Point(Math.Max(0, panel.ClientSize.Width - _browseBtn.Width), 1);
            _pathBox.Width = Math.Max(100, _browseBtn.Left - 8);
        };
        return panel;
    }

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
        btn.TabStop = true;
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Папка установки SHIFT Club Client",
            SelectedPath = Directory.Exists(_pathBox.Text) ? _pathBox.Text : @"D:\Apps\ShiftClub"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _pathBox.Text = dlg.SelectedPath;
    }

    private async Task InstallAsync()
    {
        var target = _pathBox.Text.Trim();
        var url = _urlBox.Text.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show(this, "Укажите папку установки.", "SHIFT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show(this, "Укажите корректный URL сервера, например http://192.168.1.200:5080", "SHIFT",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _installBtn.Enabled = false;
        _browseBtn.Enabled = false;
        _progress.Visible = true;
        _status.Text = "Распаковка…";

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(target);
                ExtractPayload(target);
                WriteAppSettings(target, url);
                WriteServiceAppSettings(target, url);
                WriteUninstallHelper(target);
                CreateShortcuts(target);
                InstallWatchdogService(target);
            });

            _status.Text = "Установка завершена.";
            if (_launchNow.Checked)
            {
                var exe = Path.Combine(target, "ShiftClub.Client.Shell.exe");
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = target, UseShellExecute = true });
            }

            MessageBox.Show(this,
                $"Клиент установлен в:\n{target}\n\nСервер: {url}",
                "SHIFT Club",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = "Ошибка установки.";
            MessageBox.Show(this, ex.Message, "SHIFT Club", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progress.Visible = false;
            _installBtn.Enabled = true;
            _browseBtn.Enabled = true;
        }
    }

    private static void ExtractPayload(string targetDir)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("client.zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "В установщике нет пакета клиента. Пересоберите через scripts/Build-ClientInstaller.ps1");

        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Не удалось открыть встроенный пакет.");

        var tmpZip = Path.Combine(Path.GetTempPath(), $"ShiftClubSetup_{Guid.NewGuid():N}.zip");
        try
        {
            using (var fs = File.Create(tmpZip))
                stream.CopyTo(fs);

            ZipFile.ExtractToDirectory(tmpZip, targetDir, overwriteFiles: true);
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { /* ignore */ }
        }

        if (!File.Exists(Path.Combine(targetDir, "ShiftClub.Client.Shell.exe")))
            throw new InvalidOperationException("После распаковки не найден ShiftClub.Client.Shell.exe");
    }

    private static void WriteAppSettings(string targetDir, string baseUrl)
    {
        var path = Path.Combine(targetDir, "appsettings.json");
        JsonNode root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) ?? new JsonObject()
            : new JsonObject();

        var server = root["Server"] as JsonObject ?? new JsonObject();
        server["BaseUrl"] = baseUrl;
        root["Server"] = server;

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    private static void WriteServiceAppSettings(string targetDir, string baseUrl)
    {
        var serviceDir = Path.Combine(targetDir, "service");
        if (!Directory.Exists(serviceDir))
            return;

        var path = Path.Combine(serviceDir, "appsettings.json");
        var root = new JsonObject
        {
            ["Server"] = new JsonObject { ["BaseUrl"] = baseUrl },
            ["Shell"] = new JsonObject
            {
                ["ExePath"] = Path.Combine(targetDir, "ShiftClub.Client.Shell.exe")
            }
        };
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    private static void InstallWatchdogService(string targetDir)
    {
        var serviceExe = Path.Combine(targetDir, "service", "ShiftClub.Client.Service.exe");
        if (!File.Exists(serviceExe))
            serviceExe = Path.Combine(targetDir, "ShiftClub.Client.Service.exe");
        if (!File.Exists(serviceExe))
            return;

        RunSc($"stop ShiftClubClient");
        RunSc($"delete ShiftClubClient");
        RunSc($"create ShiftClubClient binPath= \"\\\"{serviceExe}\\\"\" start= auto DisplayName= \"SHIFT Club Client Watchdog\"");
        RunSc($"description ShiftClubClient \"Keeps SHIFT Club Shell running; restarts Shell/service if terminated\"");
        RunSc($"failure ShiftClubClient reset= 86400 actions= restart/3000/restart/3000/restart/5000");
        RunSc($"failureflag ShiftClubClient 1");
        RunSc($"start ShiftClubClient");
    }

    private static void RunSc(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("sc.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            p?.WaitForExit(20_000);
        }
        catch
        {
            /* installing service requires elevation; Shell still works via Startup */
        }
    }

    private void CreateShortcuts(string targetDir)
    {
        var exe = Path.Combine(targetDir, "ShiftClub.Client.Shell.exe");
        if (_desktopShortcut.Checked)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
                desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            WriteShortcut(Path.Combine(desktop, "SHIFT Club Client.lnk"), exe, targetDir);
        }

        if (_startup.Checked)
        {
            var startup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            if (string.IsNullOrWhiteSpace(startup) || !Directory.Exists(startup))
                startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            WriteShortcut(Path.Combine(startup, "SHIFT Club Client.lnk"), exe, targetDir);
        }

        var programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        if (!string.IsNullOrWhiteSpace(programs))
        {
            var folder = Path.Combine(programs, "SHIFT Club");
            Directory.CreateDirectory(folder);
            WriteShortcut(Path.Combine(folder, "SHIFT Club Client.lnk"), exe, targetDir);
        }
    }

    private static void WriteShortcut(string lnkPath, string targetExe, string workDir)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("WScript.Shell недоступен.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        var shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = targetExe;
        shortcut.WorkingDirectory = workDir;
        shortcut.Description = "SHIFT Club Client";
        shortcut.Save();
    }

    private static void WriteUninstallHelper(string targetDir)
    {
        var bat = Path.Combine(targetDir, "Uninstall.bat");
        var content = """
            @echo off
            echo Uninstall SHIFT Club Client
            taskkill /IM ShiftClub.Client.Shell.exe /F >nul 2>&1
            timeout /t 1 /nobreak >nul
            set "DIR=%~dp0"
            cd /d "%TEMP%"
            rmdir /s /q "%DIR%"
            del "%USERPROFILE%\Desktop\SHIFT Club Client.lnk" >nul 2>&1
            del "%PUBLIC%\Desktop\SHIFT Club Client.lnk" >nul 2>&1
            del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\SHIFT Club Client.lnk" >nul 2>&1
            del "%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup\SHIFT Club Client.lnk" >nul 2>&1
            rmdir /s /q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\SHIFT Club" >nul 2>&1
            echo Done.
            pause
            """;
        File.WriteAllText(bat, content, Encoding.ASCII);
    }
}
