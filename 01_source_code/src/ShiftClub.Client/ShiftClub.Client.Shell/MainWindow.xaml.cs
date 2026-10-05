using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using ShiftClub.Shared.Contracts.ClientLauncher;
using ShiftClub.Shared.Contracts.Computers;
using ShiftClub.Shared.Contracts.Customers;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared.Security;

namespace ShiftClub.Client.Shell;

public partial class MainWindow : Window
{
    private readonly ShellClientAgent _agent;
    private readonly DispatcherTimer _uiTimer;
    private readonly KioskGuard _kiosk;
    private readonly string _serverUrl;
    private Guid? _boundSessionId;
    /// <summary>Login/waiting UI: fullscreen over taskbar. False after login/session.</summary>
    private bool _lockScreenUi = true;
    /// <summary>CCBoot superclient / image setup · all Shell protection off.</summary>
    private bool _adminMode;
    private int? _sessionTotalSeconds;
    private int _localRemaining;
    private int _sessionPeakRemaining;
    private readonly HashSet<int> _sessionEndWarned = new();
    private static readonly int[] SessionEndWarnMarks = [7, 5, 3, 1];
    private bool _allowBackgroundForGame;
    /// <summary>Shell уступил фокус (Пуск / NVIDIA / окно в игре). Не форсировать Activate.</summary>
    private bool _yieldDesktop;
    private int _postSessionMinutes = 30;
    private string _postSessionPay = "Cash";
    private bool _postSessionGrabDone;
    private bool _postSessionLoginPeek;
    private string _appCategory = "Все";
    private string _shopCategoryId = "";
    private string _search = "";
    private string _tab = "home";
    private string _accountSub = "overview";
    private string _historySub = "sessions";
    private bool _accountProfileBound;
    private int _accountHistoryTake = 40;
    private string? _lastApplyKey;
    private string? _lastCatalogKey;
    private string? _lastRenderedTab;
    private string? _lastRenderedCatalogKey;
    private string? _localLockPin;
    private TaskCompletionSource<bool>? _dialogTcs;
    private TaskCompletionSource<int>? _dialogChoiceTcs;
    /// <summary>User opened «Купить новый сеанс» — don't hide tariff panel on UI refresh.</summary>
    private bool _showBuySessionExplicit;
    private int _buyDurationMinutes = 60;
    private static readonly int[] BuyDurationPresets = [30, 60, 120, 180, 240];
    private readonly Dictionary<Guid, (ClientBarProductDto Product, decimal Qty)> _cart = new();
    private Storyboard? _launchSpinnerStoryboard;
    private readonly AppLaunchSoundCache _launchSounds;
    private CancellationTokenSource? _telegramPollCts;
    private Guid? _telegramTicketId;
    private bool _telegramQrOnLogin;
    /// <summary>BindSession | LinkAccount | ChangeTelegram — для «Обновить QR» в оверлее.</summary>
    private string? _telegramOverlayPurpose;
    private bool _comfortUiBound;

    public MainWindow()
    {
        InitializeComponent();

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        _serverUrl = (config["Server:BaseUrl"] ?? "http://192.168.1.250:5080").TrimEnd('/');
        _launchSounds = new AppLaunchSoundCache(_serverUrl);
        _agent = new ShellClientAgent(_serverUrl);
        _agent.Changed += OnAgentChanged;
        _agent.UiMessageRequested += msg => Dispatcher.InvokeAsync(async () =>
        {
            await ShowInfoAsync("Сообщение с кассы", msg);
        });
        _agent.SessionEndWarningRequested += minutes => Dispatcher.Invoke(() =>
        {
            RaiseSessionEndWarning(minutes);
        });
        _agent.EmergencyAlertRequested += text => Dispatcher.Invoke(() =>
        {
            if (!_agent.EmergencyAlertsEnabled)
                return;
            EmergencyAlertWindow.ShowAlert(text, _agent.GetLaunchedPids());
        });
        // Диспетчер задач с панели кассы / Dock; локальный Task Manager не открываем гостю (watchdog держит Shell)
        _agent.TaskManagerRequested += () => Dispatcher.Invoke(OpenTaskManager);
        _agent.Start();

        // Сначала SYSTEM-watchdog, потом запросы на NVIDIA bounce (нужны права SYSTEM).
        WatchdogServiceBootstrap.EnsureInstalledAndRunning();
        NvidiaControlLauncher.ScheduleFixOnShellLaunch();

        _kiosk = new KioskGuard();
        _kiosk.BypassAttempt += reason => _ = _agent.ReportSecurityAlertAsync(reason);
        _kiosk.Start();
        // Keeper is the only Shell restarter; do not also put Shell in Startup.
        KeeperBootstrap.EnsureRunning();

        if (ClientAdminMode.IsActive())
        {
            EnterAdminModeUi(alreadyFlagged: true);
        }
        else
        {
            WindowsTaskbar.Hide();
            ShiftClub.Shared.Security.ProcessAccessGuard.ProtectCurrentProcess();
            var aclTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            aclTimer.Tick += (_, _) =>
            {
                aclTimer.Stop();
                if (!_adminMode)
                    ShiftClub.Shared.Security.ProcessAccessGuard.ProtectCurrentProcess();
            };
            aclTimer.Start();
        }

        _agent.PrepareExitForUpdate += () =>
        {
            _kiosk.AllowStaffExit = true;
            try { _kiosk.Dispose(); } catch { /* ignore */ }
            // Grant interactive user wait/kill rights — Admin-only Unprotect breaks OTA handoff.
            ShiftClub.Shared.Security.ProcessAccessGuard.UnprotectCurrentProcessForUpdate();
            WindowsTaskbar.Show();
        };

        VersionLabel.Text = $"v{ClientVersionInfo.Version}";
        LoginVersionLabel.Text = $"v{ClientVersionInfo.Version}";
        WaitingVersionLabel.Text = $"v{ClientVersionInfo.Version}";

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick += (_, _) =>
        {
            try
            {
                ClockText.Text = DateTime.Now.ToString("HH:mm");
                if (_adminMode)
                {
                    // Keep admin.mode fresh so Keeper/Service never treat it as stale mid-setup.
                    if (DateTime.UtcNow.Second % 30 == 0)
                        ClientAdminMode.Touch();
                    return;
                }

                RefreshSessionTimer();
                RefreshIdleLoginHint();
                RefreshPostSessionCountdown();
                // Do NOT BringForward/Activate every second · that fights launched apps —
                // except login/lock screens must stay above games after session end.
                var coverDesktop = _agent.IsUiLocked
                    || (_agent.Session is null && PanelLogin.Visibility == Visibility.Visible)
                    || PanelPostSession.Visibility == Visibility.Visible;
                if (coverDesktop)
                {
                    _yieldDesktop = false;
                    _allowBackgroundForGame = false;
                    if (WindowState == WindowState.Minimized)
                        ShellDesktopHost.EnsureNotMinimized(this, ShellLayoutMode.FullScreen);
                    EnforceKioskFrame();
                }
                else
                {
                    if (WindowState == WindowState.Minimized)
                        ShellDesktopHost.EnsureNotMinimized(this, CurrentLayoutMode());

                    // Оболочка = фон рабочего стола: если гость свернул все окна
                    // (Win+D / «Свернуть все»), показываем Shell вместо пустого стола.
                    if (!_adminMode && FullscreenWindowDetect.IsDesktopForeground())
                    {
                        ShellDesktopHost.EnsureNotMinimized(this, CurrentLayoutMode());
                        ShellDesktopHost.BringForward(
                            this,
                            topmost: false,
                            activate: false,
                            layout: CurrentLayoutMode());
                    }
                }

                _kiosk.PauseForGame = _yieldDesktop || _allowBackgroundForGame;
                SyncDesktopLayer();
            }
            catch
            {
                /* UI tick must never crash Shell */
            }
        };
        _uiTimer.Start();

        Loaded += (_, _) =>
        {
            EnforceKioskFrame();
            ApplyMode();
            _ = ApplyLoginBackground();
        };
        SourceInitialized += (_, _) => EnforceKioskFrame();
        Closing += Window_Closing;
        Closed += (_, _) => WindowsTaskbar.Show();
    }

    private ShellLayoutMode CurrentLayoutMode() =>
        _lockScreenUi ? ShellLayoutMode.FullScreen : ShellLayoutMode.WorkArea;

    private bool HasPlayableSession() =>
        _agent.Session is { Status: SessionStatus.Active or SessionStatus.Paused };

    private void ApplyShellChrome(bool lockScreen)
    {
        _lockScreenUi = lockScreen;
        if (lockScreen)
        {
            WindowsTaskbar.Hide();
            ShellDesktopHost.FitToFullScreen(this);
        }
        else
        {
            WindowsTaskbar.Show();
            ShellDesktopHost.FitToWorkArea(this);
        }
    }

    private void SyncDesktopLayer()
    {
        if (_adminMode)
            return;

        if (_lockScreenUi)
            WindowsTaskbar.Hide();
        else
            WindowsTaskbar.Show();

        if (_allowBackgroundForGame && !_agent.HasAnyLaunchedProcessAlive())
            _allowBackgroundForGame = false;

        IntPtr shellHwnd = IntPtr.Zero;
        try { shellHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle; }
        catch { /* not ready */ }

        var yielding = _yieldDesktop || _allowBackgroundForGame;
        if (!yielding)
            return;

        var launched = _agent.GetLaunchedPids();
        var gameFs = _allowBackgroundForGame
            && launched.Count > 0
            && FullscreenWindowDetect.IsForeignFullscreenForeground(shellHwnd, launched);
        if (gameFs)
            ShellDesktopHost.PushBehind(this);
    }

    private void YieldDesktopForApp()
    {
        _yieldDesktop = true;
        _lockScreenUi = false;
        WindowsTaskbar.Show();
        ShellDesktopHost.PushBehind(this);
    }

    private void ShowShellFromStartMenu()
    {
        _allowBackgroundForGame = false;
        _yieldDesktop = false;
        ApplyShellChrome(lockScreen: false);
        ShellDesktopHost.EnsureNotMinimized(this, ShellLayoutMode.WorkArea);
        ShellDesktopHost.BringForward(this, topmost: false, layout: ShellLayoutMode.WorkArea);
        EnforceKioskFrame();
        Activate();
    }

    private void EnforceKioskFrame()
    {
        if (_adminMode)
            return;

        if (WindowStyle != WindowStyle.None)
            WindowStyle = WindowStyle.None;
        if (ResizeMode != ResizeMode.NoResize)
            ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var layout = CurrentLayoutMode();
        ShellDesktopHost.EnsureNotMinimized(this, layout);

        if (_lockScreenUi)
            WindowsTaskbar.Hide();
        else
            WindowsTaskbar.Show();

        if (_yieldDesktop || _allowBackgroundForGame)
        {
            ShellDesktopHost.PushBehind(this);
            return;
        }

        if (_lockScreenUi)
        {
            // Cover taskbar; stay above Explorer chrome on the lock screen.
            Topmost = true;
            ShellDesktopHost.BringForward(this, topmost: true, activate: false, layout: ShellLayoutMode.FullScreen);
        }
        else
        {
            Topmost = false;
            ShellDesktopHost.BringForward(this, topmost: false, activate: false, layout: ShellLayoutMode.WorkArea);
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (_adminMode)
            return;

        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        ShellDesktopHost.EnsureNotMinimized(this, CurrentLayoutMode());
        if (_lockScreenUi)
            WindowsTaskbar.Hide();
        else
            WindowsTaskbar.Show();
        if (!_yieldDesktop && !_allowBackgroundForGame)
            EnforceKioskFrame();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_adminMode)
            return;

        if (_yieldDesktop || _allowBackgroundForGame)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ShellDesktopHost.PushBehind(this);
                WindowsTaskbar.Show();
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_adminMode || _yieldDesktop || _allowBackgroundForGame)
                return;
            if (_lockScreenUi)
            {
                WindowsTaskbar.Hide();
                ShellDesktopHost.FitToFullScreen(this);
                if (!Topmost)
                    Topmost = true;
            }
            else
            {
                WindowsTaskbar.Show();
                ShellDesktopHost.FitToWorkArea(this);
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_adminMode || _kiosk.AllowStaffExit || ClientAdminMode.IsActive())
            return;

        e.Cancel = true;
        _ = _agent.ReportSecurityAlertAsync("Попытка закрыть оболочку");
        EnforceKioskFrame();
        Activate();
    }

    private void OnAgentChanged() =>
        Dispatcher.BeginInvoke(ApplyMode, DispatcherPriority.Normal);

    private string BuildApplyKey() =>
        $"{_agent.Mode}|{_agent.IsUiLocked}|{_adminMode}|{_agent.Session?.Id}|{_agent.Session?.Status}|{_agent.Customer?.CustomerId}|{string.IsNullOrWhiteSpace(_agent.State.DeviceToken)}|{_agent.IsPostSessionRebootPending}|{_postSessionLoginPeek}";

    private string BuildCatalogKey()
    {
        var appIcons = string.Join(',', _agent.Apps.Select(a => a.IconPath ?? ""));
        var appSounds = string.Join(',', _agent.Apps.Select(a => a.LaunchSoundUrl ?? ""));
        var barImgs = string.Join(',', _agent.BarProducts.Select(p => p.ImageUrl ?? ""));
        return $"{_agent.Apps.Count}:{_agent.BarProducts.Count}:{_agent.Tariffs.Count}:{_agent.News.Count}:{_agent.Apps.FirstOrDefault()?.Id}:{_agent.BarProducts.FirstOrDefault()?.Id}:{appIcons.GetHashCode()}:{appSounds.GetHashCode()}:{barImgs.GetHashCode()}";
    }

    private void ApplyMode()
    {
        if (_adminMode)
        {
            PanelWaiting.Visibility = Visibility.Collapsed;
            PanelLogin.Visibility = Visibility.Collapsed;
            PanelShell.Visibility = Visibility.Collapsed;
            PanelUiLock.Visibility = Visibility.Collapsed;
            PanelPostSession.Visibility = Visibility.Collapsed;
            PanelAdminMode.Visibility = Visibility.Visible;
            _lastApplyKey = null;
            return;
        }

        PanelAdminMode.Visibility = Visibility.Collapsed;

        // Cheap chrome · always safe to refresh
        OnlineText.Text = _agent.IsOnline ? "Онлайн" : "Офлайн";
        OnlineText.Foreground = new SolidColorBrush(_agent.IsOnline
            ? Color.FromRgb(0x3D, 0xDC, 0x97)
            : Color.FromRgb(0xFF, 0x8A, 0x8A));
        OnlineDot.Fill = OnlineText.Foreground;
        OnlineBadge.Background = new SolidColorBrush(_agent.IsOnline
            ? Color.FromRgb(0x14, 0x24, 0x1A)
            : Color.FromRgb(0x2A, 0x14, 0x14));
        OnlineBadge.BorderBrush = new SolidColorBrush(_agent.IsOnline
            ? Color.FromArgb(0x55, 0x3D, 0xDC, 0x97)
            : Color.FromArgb(0x66, 0xFF, 0x4D, 0x4D));

        var pcName = ResolvePcDisplayName();
        var pcNumber = FormatPcNumberBackdrop(pcName);
        LoginPcName.Text = $"ПК {pcNumber}";
        if (LoginPcNameBackdrop is not null)
            LoginPcNameBackdrop.Text = pcNumber;
        WaitingPcName.Text = pcNumber;

        FooterStatus.Text = string.IsNullOrWhiteSpace(_agent.LastError)
            ? $"{_agent.StatusText}"
            : $"{_agent.StatusText} · {ShellUiText.GuestError(_agent.LastError)}";

        if (!string.IsNullOrWhiteSpace(_agent.LastOrderFlash))
            ShopFlash.Text = _agent.LastOrderFlash;

        var applyKey = BuildApplyKey();
        var catalogKey = BuildCatalogKey();
        var modeChanged = applyKey != _lastApplyKey;
        var catalogChanged = catalogKey != _lastCatalogKey;
        _lastApplyKey = applyKey;
        if (catalogChanged)
        {
            _lastCatalogKey = catalogKey;
            _launchSounds.Preload(_agent.Apps.Select(a => (a.Id, a.LaunchSoundUrl)));
        }

        PanelWaiting.Visibility = Visibility.Collapsed;
        PanelLogin.Visibility = Visibility.Collapsed;
        PanelShell.Visibility = Visibility.Collapsed;
        PanelUiLock.Visibility = _agent.IsUiLocked ? Visibility.Visible : Visibility.Collapsed;
        if (!_agent.IsUiLocked)
            _localLockPin = null;

        if (_agent.IsUiLocked)
        {
            _yieldDesktop = false;
            _allowBackgroundForGame = false;
            DismissShellDialog();
            if (UnlockHintText is not null)
                UnlockHintText.Text = "Введите ПИН или пароль, чтобы разблокировать";
        }

        switch (_agent.Mode)
        {
            case ShellMode.WaitingApproval:
            case ShellMode.Connecting when string.IsNullOrWhiteSpace(_agent.State.DeviceToken):
                ApplyShellChrome(lockScreen: true);
                PanelWaiting.Visibility = Visibility.Visible;
                RegCodeText.Text = string.IsNullOrWhiteSpace(_agent.State.MacAddress)
                    ? "MAC не найден"
                    : _agent.State.MacAddress;
                EnforceKioskFrame();
                break;

            case ShellMode.InSession when _agent.Session is not null:
            case ShellMode.LoggedIn when _agent.Customer is not null:
                if (_agent.IsUiLocked)
                {
                    ApplyShellChrome(lockScreen: true);
                }
                else
                {
                    // После продления: если игры ещё живы — снова уступаем им фокус.
                    if (_agent.Mode == ShellMode.InSession && _agent.HasAnyLaunchedProcessAlive())
                        _allowBackgroundForGame = true;
                    else if (_agent.Mode != ShellMode.InSession)
                    {
                        // После сеанса при живой игре — не выталкивать Shell поверх.
                        if (!(_agent.IsPostSessionRebootPending && _agent.HasAnyLaunchedProcessAlive()))
                        {
                            _allowBackgroundForGame = false;
                            _yieldDesktop = false;
                        }
                    }

                    ApplyShellChrome(lockScreen: false);
                }

                PanelShell.Visibility = Visibility.Visible;
                BindProfile();
                BindSessionChrome();
                if (_agent.Mode == ShellMode.InSession)
                    BindSession(_agent.Session!);
                if (modeChanged || catalogChanged)
                    FillTariffs();
                ShowTab(_tab, forceRender: modeChanged || catalogChanged);
                EnforceKioskFrame();
                SyncDesktopLayer();
                break;

            case ShellMode.Locked when _agent.Session is { Status: SessionStatus.Active or SessionStatus.Paused }:
            case ShellMode.Offline when _agent.Session is { Status: SessionStatus.Active or SessionStatus.Paused }:
                ApplyShellChrome(lockScreen: _agent.IsUiLocked);
                PanelShell.Visibility = Visibility.Visible;
                BindProfile();
                BindSessionChrome();
                BindSession(_agent.Session!);
                ShowTab(_tab, forceRender: modeChanged || catalogChanged);
                EnforceKioskFrame();
                break;

            default:
                // Конец сеанса / гостевой вход: экран логина поверх, игры не убиваем.
                if (_agent.IsPostSessionRebootPending && _agent.HasAnyLaunchedProcessAlive())
                    _allowBackgroundForGame = true;
                else
                {
                    _allowBackgroundForGame = false;
                    _yieldDesktop = false;
                }
                DismissShellDialog();
                ApplyShellChrome(lockScreen: true);
                PanelLogin.Visibility = Visibility.Visible;
                BindLoginBookingHold();
                EnforceKioskFrame();
                SyncDesktopLayer();
                if (modeChanged)
                {
                    _ = ApplyLoginBackground();
                    ClearLoginCredentials();
                    EnsureLoginTelegramQr();
                }
                break;
        }

        BindPostSessionPanel(modeChanged);
        if (LoginBackToPostSession is not null)
        {
            LoginBackToPostSession.Visibility =
                _agent.IsPostSessionRebootPending && _postSessionLoginPeek
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    private void BindPostSessionPanel(bool modeChanged)
    {
        var show = _agent.IsPostSessionRebootPending && !_postSessionLoginPeek;
        if (!show)
        {
            PanelPostSession.Visibility = Visibility.Collapsed;
            if (!_agent.IsPostSessionRebootPending)
            {
                _postSessionGrabDone = false;
                _postSessionLoginPeek = false;
            }
            return;
        }

        PanelPostSession.Visibility = Visibility.Visible;
        PostSessionPcLabel.Text = $"ПК {ResolvePcDisplayName()}";
        if (_agent.Customer is not null
            && !string.Equals(_postSessionPay, "Balance", StringComparison.OrdinalIgnoreCase)
            && PostSessionHint.Tag is null)
            _postSessionPay = "Balance";
        RefreshPostSessionCountdown();
        if (modeChanged || PostSessionHint.Tag is null)
            UpdatePostSessionHint();
        StylePostSessionChoiceButtons();
        UpdatePostSessionAccountActions();

        if (!_postSessionGrabDone)
        {
            _postSessionGrabDone = true;
            try
            {
                var rebootLeft = _agent.PostSessionRebootSecondsLeft ?? (int)ShellClientAgent.PostSessionRebootGrace.TotalSeconds;
                SessionEndVoiceAnnouncer.AnnounceSessionEnded(rebootLeft);
                // Не сворачиваем игру — гость сам откроет Shell для продления.
                if (!_agent.HasAnyLaunchedProcessAlive())
                    Activate();
            }
            catch { /* ignore */ }
        }
    }

    private void RefreshPostSessionCountdown()
    {
        if (PanelPostSession.Visibility != Visibility.Visible)
            return;

        var left = _agent.PostSessionRebootSecondsLeft;
        if (left is null)
        {
            PostSessionCountdown.Text = "—";
            return;
        }

        var m = left.Value / 60;
        var s = left.Value % 60;
        PostSessionCountdown.Text = $"{m}:{s:00}";
        if (left.Value <= 60)
            PostSessionCountdown.Foreground = (Brush)FindResource("Danger");
        else
            PostSessionCountdown.Foreground = (Brush)FindResource("Accent");
    }

    private void UpdatePostSessionHint()
    {
        var timeLabel = _postSessionMinutes < 60
            ? $"+{_postSessionMinutes} мин"
            : _postSessionMinutes % 60 == 0
                ? $"+{_postSessionMinutes / 60} ч"
                : $"+{_postSessionMinutes} мин";
        var pc = ResolvePcDisplayName();
        PostSessionHint.Tag = $"{_postSessionMinutes}|{_postSessionPay}";
        var loggedIn = _agent.Customer is not null;
        PostSessionHint.Text = _postSessionPay switch
        {
            "KaspiQr" =>
                $"Подойдите к кассе с ПК {pc}. Скажите «{timeLabel}» и оплатите Kaspi QR. Администратор запустит время.",
            "Balance" when loggedIn =>
                $"Нажмите «Продолжить с баланса» или «Продолжить с банка» — сразу запустим {timeLabel} на ПК {pc}.",
            "Balance" =>
                $"Войдите в аккаунт (кнопка ниже), затем продолжите с баланса или банка — или позовите кассу для {timeLabel}.",
            _ =>
                $"Подойдите к администратору с номером ПК {pc}. Скажите «{timeLabel}» и оплатите наличными."
        };
        UpdatePostSessionAccountActions();
    }

    private void UpdatePostSessionAccountActions()
    {
        var loggedIn = _agent.Customer is not null;
        var balancePay = string.Equals(_postSessionPay, "Balance", StringComparison.OrdinalIgnoreCase);
        var bankMins = loggedIn ? _agent.Customer!.CurrentZoneTimeBankMinutes : 0;
        if (PostSessionContinueBalanceButton is not null)
        {
            PostSessionContinueBalanceButton.Visibility =
                loggedIn && balancePay ? Visibility.Visible : Visibility.Collapsed;
            if (loggedIn)
            {
                var wallet = _agent.Customer!.Balance + _agent.Customer.BonusBalance;
                PostSessionContinueBalanceButton.Content =
                    $"Продолжить с баланса · на счёте {wallet:0} ₸";
            }
        }

        if (PostSessionContinueTimeBankButton is not null)
        {
            PostSessionContinueTimeBankButton.Visibility =
                loggedIn && bankMins > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (loggedIn && bankMins > 0)
            {
                var zone = _agent.Customer!.CurrentZoneName ?? "этой зоны";
                PostSessionContinueTimeBankButton.Content =
                    $"Продолжить с банка · {zone}: {bankMins} мин";
            }
        }

        if (PostSessionShowLoginButton is not null)
            PostSessionShowLoginButton.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;

        if (PostSessionCallAdminButton is not null)
        {
            var hasSelfServe = loggedIn && (balancePay || bankMins > 0);
            PostSessionCallAdminButton.Margin = new Thickness(0, hasSelfServe ? 10 : 20, 0, 0);
        }
    }

    private async void PostSessionContinueBalance_Click(object sender, RoutedEventArgs e) =>
        await PostSessionContinueSelfServeAsync(useTimeBank: false);

    private async void PostSessionContinueTimeBank_Click(object sender, RoutedEventArgs e) =>
        await PostSessionContinueSelfServeAsync(useTimeBank: true);

    private async Task PostSessionContinueSelfServeAsync(bool useTimeBank)
    {
        var balanceBtn = PostSessionContinueBalanceButton;
        var bankBtn = PostSessionContinueTimeBankButton;
        try
        {
            if (_agent.Customer is null)
                throw new InvalidOperationException("Сначала войдите в аккаунт");

            if (balanceBtn is not null) balanceBtn.IsEnabled = false;
            if (bankBtn is not null) bankBtn.IsEnabled = false;

            var tariffs = _agent.Tariffs.Where(t => t.IsActive).ToList();
            var tariff = tariffs.FirstOrDefault(t => t.Id == _agent.LastEndedTariffId)
                         ?? tariffs.FirstOrDefault(t => t.Kind == TariffKind.Hourly && t.PricePerHour > 0)
                         ?? tariffs.FirstOrDefault(t => t.PricePerHour > 0 || t.FixedPrice is > 0)
                         ?? throw new InvalidOperationException("Нет доступного тарифа. Позовите администратора.");

            var minutes = _postSessionMinutes;
            if (useTimeBank)
            {
                minutes = _agent.Customer.CurrentZoneTimeBankMinutes;
                if (minutes <= 0)
                    throw new InvalidOperationException("Нет минут в банке этой зоны");
            }
            else
            {
                if (tariff.Kind == TariffKind.Package && tariff.FixedDurationMinutes is > 0)
                    minutes = tariff.FixedDurationMinutes.Value;
                if (minutes <= 0)
                    minutes = 30;

                var loyaltyPct = _agent.Customer.LoyaltyTimeDiscountPercent;
                var price = QuoteTariffPrice(tariff, minutes, loyaltyPct);
                var wallet = _agent.Customer.Balance + _agent.Customer.BonusBalance;
                if (price > 0 && wallet < price)
                    throw new InvalidOperationException(
                        $"Недостаточно средств: нужно {price:0} ₸, на счёте {wallet:0} ₸. Пополните баланс у кассы.");

                if (!await ShowConfirmAsync(
                        "Продление",
                        $"{tariff.Name}\n{minutes} мин · {price:0.##} ₸\n\nСписать с баланса и продолжить?"))
                    return;
            }

            if (useTimeBank
                && !await ShowConfirmAsync(
                    "Продление с банка",
                    $"Списать {minutes} мин из банка {_agent.Customer.CurrentZoneName ?? "зоны"} и продолжить?"))
                return;

            await _agent.StartBalanceSessionAsync(tariff.Id, minutes, useTimeBank);
            await _agent.RefreshAccountAsync();
            PostSessionFlash.Visibility = Visibility.Collapsed;
            _postSessionLoginPeek = false;
            ApplyMode();
        }
        catch (Exception ex)
        {
            PostSessionFlash.Visibility = Visibility.Visible;
            PostSessionFlash.Foreground = (Brush)FindResource("Danger");
            PostSessionFlash.Text = ShellUiText.GuestError(ex.Message);
        }
        finally
        {
            if (balanceBtn is not null) balanceBtn.IsEnabled = true;
            if (bankBtn is not null) bankBtn.IsEnabled = true;
        }
    }

    private void StylePostSessionChoiceButtons()
    {
        void StyleWrap(WrapPanel? panel, Func<string, bool> isOn)
        {
            if (panel is null) return;
            foreach (var child in panel.Children)
            {
                if (child is not Button btn) continue;
                var tag = btn.Tag?.ToString() ?? "";
                var on = isOn(tag);
                btn.Style = (Style)FindResource(on ? "AccentButton" : "GhostButton");
                if (!on)
                    btn.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x6A, 0x00));
            }
        }

        StyleWrap(PostSessionTimePanel, tag =>
            int.TryParse(tag, out var mins) && mins == _postSessionMinutes);
        StyleWrap(PostSessionPayPanel, tag =>
            string.Equals(tag, _postSessionPay, StringComparison.OrdinalIgnoreCase));
    }

    private void PostSessionTime_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var mins) || mins < 1)
            return;
        _postSessionMinutes = mins;
        UpdatePostSessionHint();
        StylePostSessionChoiceButtons();
    }

    private void PostSessionPay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || string.IsNullOrWhiteSpace(tag))
            return;
        _postSessionPay = tag;
        UpdatePostSessionHint();
        StylePostSessionChoiceButtons();
    }

    private async void PostSessionCallAdmin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PostSessionCallAdminButton.IsEnabled = false;
            var payRu = _postSessionPay switch
            {
                "KaspiQr" => "Kaspi QR",
                "Balance" => "баланс",
                _ => "наличные"
            };
            var timeLabel = _postSessionMinutes < 60
                ? $"+{_postSessionMinutes} мин"
                : $"+{_postSessionMinutes / 60} ч";
            var msg =
                $"ПК {ResolvePcDisplayName()}: нужно {timeLabel}, оплата — {payRu}. Гость ждёт на экране после сеанса.";
            await _agent.CallAdminAsync(msg);
            PostSessionFlash.Visibility = Visibility.Visible;
            PostSessionFlash.Foreground = (Brush)FindResource("Accent");
            PostSessionFlash.Text = "Вызов отправлен на кассу. Оставайтесь у ПК.";
        }
        catch (Exception ex)
        {
            PostSessionFlash.Visibility = Visibility.Visible;
            PostSessionFlash.Foreground = (Brush)FindResource("Danger");
            PostSessionFlash.Text = ShellUiText.GuestError(ex.Message);
        }
        finally
        {
            PostSessionCallAdminButton.IsEnabled = true;
        }
    }

    private void PostSessionShowLogin_Click(object sender, RoutedEventArgs e)
    {
        _postSessionLoginPeek = true;
        PanelPostSession.Visibility = Visibility.Collapsed;
        PostSessionFlash.Visibility = Visibility.Collapsed;
        ApplyMode();
    }

    private void LoginBackToPostSession_Click(object sender, RoutedEventArgs e)
    {
        _postSessionLoginPeek = false;
        ApplyMode();
    }

    private void BindLoginBookingHold()
    {
        var hold = _agent.BookingHold;
        if (hold is null)
        {
            LoginBookingHold.Visibility = Visibility.Collapsed;
            LoginBookingHold.Text = "";
            LoginButton.IsEnabled = true;
            LoginBox.IsEnabled = true;
            PinBox.IsEnabled = true;
            return;
        }

        var from = hold.StartsAt.ToOffset(TimeSpan.FromHours(5));
        var to = hold.EndsAt.ToOffset(TimeSpan.FromHours(5));
        if (hold.IsOwner)
        {
            LoginBookingHold.Visibility = Visibility.Visible;
            LoginBookingHold.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
            LoginBookingHold.Text = $"Ваша бронь {hold.Number} · {from:HH:mm}–{to:HH:mm}";
            LoginButton.IsEnabled = true;
            LoginBox.IsEnabled = true;
            PinBox.IsEnabled = true;
        }
        else
        {
            LoginBookingHold.Visibility = Visibility.Visible;
            LoginBookingHold.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
            LoginBookingHold.Text =
                $"ПК забронирован ({hold.Number}) {from:HH:mm}–{to:HH:mm}\n"
                + $"Клиент: {hold.ContactName}. Вход только по телефону брони или с кассы.";
            // Бронь другого клиента активна — вход только по телефону брони или с кассы.
            LoginButton.IsEnabled = true;
            LoginBox.IsEnabled = true;
            PinBox.IsEnabled = true;
        }
    }

    private string ResolvePcDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(_agent.State.DisplayName))
            return _agent.State.DisplayName!;
        if (!string.IsNullOrWhiteSpace(_agent.ComputerName))
            return _agent.ComputerName!;
        return Environment.MachineName;
    }

    /// <summary>Backdrop on login/waiting: only the station number (e.g. "01"), not "PC 01".</summary>
    private static string FormatPcNumberBackdrop(string displayName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(displayName, @"(\d+)\s*$");
        if (!m.Success)
            m = System.Text.RegularExpressions.Regex.Match(displayName, @"(\d+)");
        if (!m.Success)
            return displayName.Trim();

        var digits = m.Groups[1].Value;
        return digits.Length == 1 ? digits.PadLeft(2, '0') : digits;
    }

    private void BindProfile()
    {
        var c = _agent.Customer;
        var session = _agent.Session;
        var guest = session?.GuestName;
        var name = c?.FullName ?? guest ?? "Гость";
        ProfileName.Text = name;
        ProfileLoyalty.Text = c?.LoyaltyLevelName ?? (session is null ? "Без аккаунта" : "Гостевой сеанс");
        AvatarLetter.Text = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[0].ToString().ToUpperInvariant();

        if (c is not null)
        {
            BalanceLabel.Text = "БАЛАНС";
            if (c.ComfortHideBalance)
            {
                BalanceText.Text = "•••";
                BonusText.Text = "Бонусы •••";
            }
            else
            {
                BalanceText.Text = $"{c.Balance:0} ₸";
                BonusText.Text = c.BonusBalance > 0
                    ? $"Бонусы {c.BonusBalance:0} ₸ · тратятся первыми"
                    : "Бонусы: 0 ₸";
            }
            TimeBankText.Text = FormatTimeBank(c);
            BalanceHint.Text = "Пополнение — на кассе. Бонусы начисляются при пополнении по уровню лояльности.";
        }
        else if (session is not null)
        {
            BalanceLabel.Text = "ТАРИФ";
            var tariff = session.TariffName ?? "Сеанс";
            BalanceText.Text = tariff;
            BonusText.Text = session.TotalPrice > 0
                ? $"{session.TotalPrice:0} ₸ · {ShellUiText.Payment(session.PaymentMethod)}"
                : ShellUiText.Payment(session.PaymentMethod);
            TimeBankText.Text = "";
            BalanceHint.Text = "Продление и оплата — у администратора на кассе";
        }
        else
        {
            BalanceLabel.Text = "БАЛАНС";
            BalanceText.Text = "—";
            BonusText.Text = "";
            TimeBankText.Text = "";
            BalanceHint.Text = "Войдите в аккаунт или дождитесь сеанса с кассы";
        }

        ApplyComfortBrightness(c?.ComfortBrightness ?? 100);

        UpdateSidebarRemainingTime();

        var first = name.Split(' ').FirstOrDefault() ?? name;
        HomeHello.Text = $"ПРИВЕТ, {first.ToUpperInvariant()}!";
        if (session is not null && c is not null && session.CustomerId == c.CustomerId)
        {
            HomeSub.Text = "Сеанс с аккаунта. «Завершить сеанс» — останетесь в аккаунте. «Выйти» слева — сеанс и выход из аккаунта.";
        }
        else if (session is not null)
        {
            HomeSub.Text =
                "Гостевой сеанс. Можно играть и заказать из бара. Привяжите аккаунт справа — после этого остаток минут можно сохранить в банк.";
        }
        else if (c is not null && c.CurrentZoneTimeBankMinutes > 0)
        {
            HomeSub.Text =
                $"В банке {c.CurrentZoneName ?? "этой зоны"} есть {c.CurrentZoneTimeBankMinutes} мин — можно играть с банка или купить новый сеанс.";
        }
        else if (c is not null)
        {
            HomeSub.Text = "Выберите тариф и длительность, чтобы начать сеанс с баланса.";
        }
        else
        {
            HomeSub.Text = "Дождитесь запуска сеанса с кассы или войдите в аккаунт.";
        }

        RefreshIdleLoginHint();

        // Во время аккаунтного сеанса «Выйти» = завершить сеанс + выход. Гостевой с кассы — без кнопки выхода из аккаунта.
        LogoutButton.Visibility = c is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        HomeEndSessionButton.Visibility = session is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        BindAccountButton.Visibility = session is not null && c is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        UnlockHintText.Text = c is not null
            ? "Введите ПИН аккаунта (тот же, что при входе)"
            : "Введите ПИН, который задали при блокировке, или позовите администратора";
    }

    private void RefreshIdleLoginHint()
    {
        if (_agent.Customer is null || _agent.Session is not null)
            return;
        if (PanelShell.Visibility != Visibility.Visible)
            return;

        var left = _agent.IdleLoginSecondsLeft;
        if (left is null)
            return;

        var m = left.Value / 60;
        var s = left.Value % 60;
        HomeSub.Text =
            $"Начните сеанс в течение {m}:{s:00} — иначе будет автоматический выход из аккаунта.";
        if (left.Value <= 30)
            StatusTextSync();
    }

    private void StatusTextSync()
    {
        // Показываем баланс из профиля (актуальный StatusText тоже ок)
        var bal = _agent.Customer is not null ? $"Баланс: {_agent.Customer.Balance:0} ₸" : _agent.StatusText;
        FooterStatus.Text = string.IsNullOrWhiteSpace(_agent.LastError)
            ? $"{_agent.StatusText} · {bal}"
            : $"{_agent.StatusText} · {ShellUiText.GuestError(_agent.LastError)}";
    }

    private void BindSessionChrome()
    {
        var pc = _agent.Session?.ComputerName ?? _agent.ComputerName ?? Environment.MachineName;
        var zone = _agent.Session?.ZoneName;
        SessionPcText.Text = !string.IsNullOrWhiteSpace(zone) ? $"{pc} · {zone}" : pc;
        if (_agent.Session is null)
        {
            RemainingText.Text = "--:--";
            RemainingChip.Visibility = Visibility.Collapsed;
            HomeSessionBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            RemainingChip.Visibility = Visibility.Visible;
            HomeSessionBox.Visibility = Visibility.Visible;
            HomeSessionInfo.Text =
                $"{_agent.Session.TariffName ?? "Сеанс"} · {_agent.Session.TotalPrice:0} ₸ · {ShellUiText.Payment(_agent.Session.PaymentMethod)}";
        }

        ApplyClientBillingUi();
        BindProfile();
        SyncVoenkomatUi();
    }

    /// <summary>
    /// Сеанс под управлением кассы (гость / чужой аккаунт) — без клиента.
    /// Свой аккаунт на ПК — доступен Add time с баланса (как iCafe).
    /// </summary>
    private bool IsCashDeskManagedSession()
    {
        var session = _agent.Session;
        if (session is null) return false;
        if (session.CustomerId is null) return true;
        if (_agent.Customer is null) return true;
        return session.CustomerId != _agent.Customer.CustomerId;
    }

    private bool CanClientUseBalance()
    {
        if (_agent.Customer is null) return false;
        if (_agent.Session is null) return true;
        return !IsCashDeskManagedSession()
               && _agent.Session.CustomerId == _agent.Customer.CustomerId;
    }

    private void ApplyClientBillingUi()
    {
        var cashDesk = IsCashDeskManagedSession();
        var accountReady = _agent.Customer is not null && _agent.Session is null;
        var accountSession = CanClientUseBalance() && _agent.Session is not null;

        HomeAccountStartPanel.Visibility = accountReady ? Visibility.Visible : Visibility.Collapsed;
        HeaderExtendButton.Visibility = accountSession ? Visibility.Visible : Visibility.Collapsed;
        HomeClientExtendPanel.Visibility = accountSession ? Visibility.Visible : Visibility.Collapsed;
        HomeCashDeskHint.Visibility = cashDesk ? Visibility.Visible : Visibility.Collapsed;
        if (!accountSession)
            ExtendPopup.Visibility = Visibility.Collapsed;
        else
            RebuildExtendOptionButtons();

        ApplyTimeBankStartUi(accountReady);

        // Бар с ПК: оплата только при выдаче — наличные или Kaspi QR (без баланса)
        ShopPayGuestHint.Visibility = Visibility.Visible;
        ShopPayAccountPanel.Visibility = Visibility.Visible;
        if (ShopPayCombo.SelectedIndex < 0)
            ShopPayCombo.SelectedIndex = 0;

        if (!accountSession)
            ExtendPopup.Visibility = Visibility.Collapsed;

        RenderNews();
        if (_tab == "home")
            RenderHomePreviews();
    }

    private string FormatTimeBank(ClientCustomerAuthDto c)
    {
        var zoneMins = c.CurrentZoneTimeBankMinutes;
        var zoneName = c.CurrentZoneName ?? "эта зона";
        if (zoneMins > 0)
            return $"Банк {zoneName}: {zoneMins} мин";

        if (c.TimeBanks is { Count: > 0 })
        {
            var parts = string.Join(", ", c.TimeBanks.Select(b => $"{b.ZoneName}:{b.Minutes}"));
            return $"Банк в других зонах: {parts}";
        }

        return c.TimeBankMinutes > 0 ? $"Банк: {c.TimeBankMinutes} мин" : "";
    }

    private void ApplyTimeBankStartUi(bool accountReady)
    {
        if (!accountReady || _agent.Customer is null)
        {
            _showBuySessionExplicit = false;
            HomeTimeBankChoice.Visibility = Visibility.Collapsed;
            HomeBuySessionPanel.Visibility = Visibility.Visible;
            return;
        }

        var zoneMins = _agent.Customer.CurrentZoneTimeBankMinutes;
        var zoneName = _agent.Customer.CurrentZoneName ?? "этой зоны";
        if (zoneMins > 0)
        {
            HomeTimeBankChoice.Visibility = Visibility.Visible;
            HomeTimeBankTitle.Text = $"Банк времени · {zoneName}: {zoneMins} мин";
            HomeTimeBankHint.Text =
                $"Можно запустить сеанс из банка {zoneName}. Минуты других зон на этом ПК недоступны.";
            TimeBankRingText.Text = zoneMins.ToString();
            // Пока пользователь не нажал «Купить новый сеанс», прячем тарифы — иначе тик UI их закрывает.
            HomeBuySessionPanel.Visibility = _showBuySessionExplicit
                ? Visibility.Visible
                : Visibility.Collapsed;
            StartFromBankButton.Content = $"ИГРАТЬ С БАНКА · {zoneMins} МИН";
        }
        else
        {
            HomeTimeBankChoice.Visibility = Visibility.Collapsed;
            HomeBuySessionPanel.Visibility = Visibility.Visible;
        }
    }

    private void ShowBuySession_Click(object sender, RoutedEventArgs e)
    {
        _showBuySessionExplicit = true;
        HomeBuySessionPanel.Visibility = Visibility.Visible;
        FillTariffs();
    }

    private async void StartFromBank_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_agent.Customer is null)
                throw new InvalidOperationException("Войдите в аккаунт");
            var minutes = _agent.Customer.CurrentZoneTimeBankMinutes;
            if (minutes <= 0)
                throw new InvalidOperationException("Нет минут в банке этой зоны");

            var tariff = _agent.Tariffs.FirstOrDefault(t => t.IsActive && t.Kind == TariffKind.Hourly)
                         ?? _agent.Tariffs.FirstOrDefault(t => t.IsActive)
                         ?? throw new InvalidOperationException("Нет доступного тарифа для этой зоны");

            await _agent.StartBalanceSessionAsync(tariff.Id, minutes, useTimeBank: true);
            await _agent.RefreshAccountAsync();
            ShowTab("home");
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Ошибка", ex.Message);
        }
    }

    private void ShowTab(string tab, bool forceRender = true)
    {
        _tab = tab;
        TabHome.Visibility = tab == "home" ? Visibility.Visible : Visibility.Collapsed;
        TabGames.Visibility = tab == "games" ? Visibility.Visible : Visibility.Collapsed;
        TabShop.Visibility = tab == "shop" ? Visibility.Visible : Visibility.Collapsed;
        TabAccount.Visibility = tab == "account" ? Visibility.Visible : Visibility.Collapsed;
        TabHelp.Visibility = tab == "help" ? Visibility.Visible : Visibility.Collapsed;

        PageTitle.Text = tab switch
        {
            "games" => "Игры",
            "shop" => "Бар",
            "help" => "Помощь",
            "account" => "Аккаунт",
            _ => "Главная"
        };

        SetNav(NavHome, tab == "home");
        SetNav(NavGames, tab == "games");
        SetNav(NavShop, tab == "shop");
        SetNav(NavAccount, tab == "account");
        SetNav(NavHelp, tab == "help");

        var catalogKey = BuildCatalogKey();
        var needRender = forceRender
                         || _lastRenderedTab != tab
                         || _lastRenderedCatalogKey != catalogKey;
        if (!needRender)
            return;

        _lastRenderedTab = tab;
        _lastRenderedCatalogKey = catalogKey;

        if (tab == "games")
            RenderApps();
        if (tab == "shop")
            RenderShop();
        if (tab == "home")
            RenderHomePreviews();
        if (tab == "account")
            _ = RenderAccountAsync(forceReloadHistory: forceRender);
    }

    private void ProfileHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (_agent.Customer is null)
        {
            // Без аккаунта — на экран входа.
            if (_agent.Mode is ShellMode.Locked or ShellMode.Offline or ShellMode.Connecting)
            {
                ApplyMode();
                PanelLogin.Visibility = Visibility.Visible;
                LoginBox.Focus();
            }
            else
                ShowTab("account", forceRender: true);
            return;
        }
        ShowTab("account", forceRender: true);
    }

    private async void AccountGoLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_agent.Session is not null && _agent.Customer is null)
        {
            try
            {
                await StartTelegramQrAsync("BindSession", _agent.Session.Id, onLoginPanel: false);
            }
            catch (Exception ex)
            {
                await ShowInfoAsync("Ошибка", ShellUiText.GuestError(ex.Message));
            }
            return;
        }

        if (_agent.Customer is not null)
        {
            try { await _agent.LogoutCustomerAsync("Смена аккаунта"); }
            catch { /* ignore */ }
            await ResetLoginScreenAfterLogoutAsync();
        }
        else
        {
            ClearLoginCredentials();
        }

        ApplyMode();
        if (PanelLogin.Visibility == Visibility.Visible)
            LoginBox.Focus();
    }

    private void AccountSubNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            _accountSub = tag;
            ShowAccountSubPanel();
            if (tag is "wallet" or "history")
                _ = RenderAccountAsync(forceReloadHistory: true);
            else
                BindAccountOverview();
        }
    }

    private void HistorySubNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            _historySub = tag;
            SetHistorySubNavStyles();
            RenderAccountHistoryList();
        }
    }

    private async void AccountRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RenderAccountAsync(forceReloadHistory: true);
    }

    private async void AccountHistoryMore_Click(object sender, RoutedEventArgs e)
    {
        _accountHistoryTake = Math.Min(100, _accountHistoryTake + 40);
        await RenderAccountAsync(forceReloadHistory: true);
    }

    private async Task RenderAccountAsync(bool forceReloadHistory)
    {
        var loggedIn = _agent.Customer is not null;
        var guestSession = !loggedIn && _agent.Session is not null;
        AccountGuestHint.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        AccountScroll.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        AccountSubNavPanel.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        AccountRefreshButton.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        if (!loggedIn)
        {
            AccountLoadError.Visibility = Visibility.Collapsed;
            if (guestSession)
            {
                AccountGuestHintTitle.Text = "Гостевой сеанс";
                AccountGuestHintBody.Text =
                    "Сохраните сеанс в аккаунт через Telegram — баланс и бонусы будут доступны после привязки.";
                AccountGuestLoginButton.Content = "Привязать через Telegram";
                AccountGuestBindButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                AccountGuestHintTitle.Text = "Войдите в аккаунт";
                AccountGuestHintBody.Text =
                    "Профиль, баланс, банк времени, история и смена ПИН — после входа.";
                AccountGuestLoginButton.Content = "Войти";
                AccountGuestBindButton.Visibility = Visibility.Collapsed;
            }
            return;
        }

        BindAccountOverview();
        ShowAccountSubPanel();

        if (forceReloadHistory
            || _agent.AccountTransactions.Count == 0
            || _agent.AccountSessions.Count == 0)
        {
            try
            {
                await _agent.RefreshAccountAsync();
                await _agent.LoadAccountHistoryAsync(_accountHistoryTake);
            }
            catch (Exception ex)
            {
                AccountLoadError.Text = $"Не удалось обновить: {ShellUiText.GuestError(ex.Message)}";
                AccountLoadError.Visibility = Visibility.Visible;
            }
        }

        if (!string.IsNullOrWhiteSpace(_agent.AccountHistoryError))
        {
            AccountLoadError.Text = $"Не удалось загрузить историю: {ShellUiText.GuestError(_agent.AccountHistoryError)}";
            AccountLoadError.Visibility = Visibility.Visible;
        }
        else if (AccountLoadError.Visibility == Visibility.Visible
                 && AccountLoadError.Text.StartsWith("Не удалось загрузить", StringComparison.Ordinal))
        {
            AccountLoadError.Visibility = Visibility.Collapsed;
        }

        BindAccountOverview();
        RenderAccountFinanceList();
        RenderAccountBankList();
        RenderAccountHistoryList();
    }

    private void ShowAccountSubPanel()
    {
        AccountPanelOverview.Visibility = _accountSub == "overview" ? Visibility.Visible : Visibility.Collapsed;
        AccountPanelWallet.Visibility = _accountSub == "wallet" ? Visibility.Visible : Visibility.Collapsed;
        AccountPanelHistory.Visibility = _accountSub == "history" ? Visibility.Visible : Visibility.Collapsed;

        SetAccountSubNav(AccountSubOverview, _accountSub == "overview");
        SetAccountSubNav(AccountSubWallet, _accountSub == "wallet");
        SetAccountSubNav(AccountSubHistory, _accountSub == "history");
        SetHistorySubNavStyles();
    }

    private void SetAccountSubNav(Button btn, bool active)
        => btn.Style = (Style)FindResource(active ? "AccentButton" : "GhostButton");

    private void SetHistorySubNavStyles()
    {
        SetAccountSubNav(HistorySubSessions, _historySub == "sessions");
        SetAccountSubNav(HistorySubMoney, _historySub == "money");
        SetAccountSubNav(HistorySubBank, _historySub == "bank");
        SetAccountSubNav(HistorySubOrders, _historySub == "orders");
        SetAccountSubNav(HistorySubBookings, _historySub == "bookings");
    }

    private void BindAccountOverview()
    {
        var c = _agent.Customer;
        if (c is null) return;

        var name = c.FullName;
        AccountAvatarLetter.Text = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[0].ToString().ToUpperInvariant();
        AccountFullName.Text = name;
        AccountPhone.Text = MaskPhone(c.Phone);
        AccountEmail.Text = string.IsNullOrWhiteSpace(c.Email) ? "Email не указан" : c.Email;
        var bonusPct = c.LoyaltyBonusPercent;
        var timePct = c.LoyaltyTimeDiscountPercent;
        var perkParts = new List<string>();
        if (bonusPct > 0) perkParts.Add($"бонус {bonusPct:0.#}%");
        if (timePct > 0) perkParts.Add($"скидка на время {timePct:0.#}%");
        var perks = perkParts.Count > 0 ? " · " + string.Join(" · ", perkParts) : "";
        AccountLoyaltyLevel.Text = string.IsNullOrWhiteSpace(c.LoyaltyLevelName)
            ? (perkParts.Count > 0 ? $"Без уровня{perks}" : "Без уровня")
            : $"{c.LoyaltyLevelName}{perks}";

        var goal = Math.Max(1, c.LoyaltyGoal);
        var progress = Math.Clamp(c.LoyaltyProgress, 0, goal);
        AccountLoyaltyBar.Maximum = goal;
        AccountLoyaltyBar.Value = progress;
        AccountLoyaltyPct.Text = $"{progress} / {goal} ₸";
        AccountLoyaltyHint.Text = progress >= goal
            ? BuildLoyaltyMaxHint(bonusPct, timePct)
            : BuildLoyaltyProgressHint(goal - progress, bonusPct, timePct);

        AccountVisitCount.Text = c.VisitCount.ToString();
        AccountTotalSpent.Text = c.ComfortHideBalance ? "•••" : $"{c.TotalSpent:0} ₸";
        AccountBankTotal.Text = $"{c.TimeBankMinutes} мин";

        var showStreak = c.VisitStreakDays > 0;
        var showBar = c.PendingBarRewards > 0;
        AccountEngagementCard.Visibility = showStreak || showBar ? Visibility.Visible : Visibility.Collapsed;
        AccountVisitStreakText.Visibility = showStreak ? Visibility.Visible : Visibility.Collapsed;
        AccountPendingBarText.Visibility = showBar ? Visibility.Visible : Visibility.Collapsed;
        if (showStreak)
            AccountVisitStreakText.Text = $"Серия посещений: {c.VisitStreakDays} дн. подряд";
        if (showBar)
            AccountPendingBarText.Text = $"Бесплатный напиток к выдаче: {c.PendingBarRewards} — оформите на кассе";

        if (c.TelegramLinked)
        {
            AccountTelegramLinkButton.Visibility = Visibility.Collapsed;
            AccountTelegramChangeButton.Visibility = Visibility.Visible;
            if (c.TelegramChangeAvailableAt is { } until)
            {
                AccountTelegramChangeButton.IsEnabled = false;
                AccountTelegramStatus.Text =
                    $"Telegram привязан. Смена доступна с {until.ToLocalTime():dd.MM.yyyy}.";
            }
            else
            {
                AccountTelegramChangeButton.IsEnabled = true;
                AccountTelegramStatus.Text =
                    "Telegram привязан. Сменить аккаунт можно не чаще одного раза в установленный период.";
            }
        }
        else
        {
            AccountTelegramLinkButton.Visibility = Visibility.Visible;
            AccountTelegramChangeButton.Visibility = Visibility.Collapsed;
            AccountTelegramStatus.Text =
                "Привяжите Telegram, чтобы входить по QR и получать уведомления о сеансе и заказах.";
        }

        if (c.ComfortHideBalance)
        {
            AccountBalanceBig.Text = "•••";
            AccountBonusBig.Text = "Бонусы •••";
        }
        else
        {
            AccountBalanceBig.Text = $"{c.Balance:0} ₸";
            AccountBonusBig.Text = c.BonusBalance > 0
                ? $"Бонусы {c.BonusBalance:0} ₸ · тратятся первыми"
                : "Бонусы: 0 ₸";
        }

        AccountCredStatus.Text =
            $"ПИН: {(c.HasPin ? "задан" : "нет")} · Пароль: {(c.HasPassword ? "задан" : "нет")}"
            + (string.IsNullOrWhiteSpace(c.Login) ? "" : $" · Логин: {c.Login}");

        if (!_comfortUiBound)
        {
            ComfortHideBalanceBox.IsChecked = c.ComfortHideBalance;
            ComfortSoundBox.IsChecked = c.ComfortSoundEnabled;
            ComfortBrightnessSlider.Value = Math.Clamp(c.ComfortBrightness, 40, 100);
            ComfortBrightnessValue.Text = $"{(int)ComfortBrightnessSlider.Value}%";
            SelectComfortLanguage(c.ComfortLanguage);
            _comfortUiBound = true;
        }

        if (!_accountProfileBound || AccountFirstNameBox.Text.Length == 0)
        {
            AccountFirstNameBox.Text = c.FirstName ?? name.Split(' ').FirstOrDefault() ?? "";
            AccountLastNameBox.Text = c.LastName
                ?? (name.Contains(' ') ? string.Join(' ', name.Split(' ').Skip(1)) : "");
            AccountEmailBox.Text = c.Email ?? "";
            AccountLoginBox.Text = c.Login ?? "";
            _accountProfileBound = true;
        }
    }

    private void SelectComfortLanguage(string? lang)
    {
        var code = string.IsNullOrWhiteSpace(lang) ? "ru" : lang.Trim().ToLowerInvariant();
        for (var i = 0; i < ComfortLanguageCombo.Items.Count; i++)
        {
            if (ComfortLanguageCombo.Items[i] is ComboBoxItem { Tag: string tag }
                && tag.Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                ComfortLanguageCombo.SelectedIndex = i;
                return;
            }
        }
        ComfortLanguageCombo.SelectedIndex = 0;
    }

    private void ApplyComfortBrightness(int brightness)
    {
        var pct = Math.Clamp(brightness, 40, 100);
        ShellRightPanel.Opacity = pct / 100.0;
    }

    private void ComfortBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ComfortBrightnessValue is null) return;
        ComfortBrightnessValue.Text = $"{(int)e.NewValue}%";
    }

    private async void AccountSaveComfort_Click(object sender, RoutedEventArgs e)
    {
        AccountComfortFlash.Text = "";
        AccountComfortError.Text = "";
        try
        {
            var lang = (ComfortLanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "ru";
            var brightness = (int)Math.Round(ComfortBrightnessSlider.Value);
            await _agent.UpdateComfortAsync(new ClientComfortSettingsRequest(
                ComfortHideBalanceBox.IsChecked == true,
                ComfortSoundBox.IsChecked == true,
                lang,
                brightness));
            _comfortUiBound = false;
            AccountComfortFlash.Text = "Сохранено";
            BindProfile();
            BindAccountOverview();
        }
        catch (Exception ex)
        {
            AccountComfortError.Text = ShellUiText.GuestError(ex.Message);
        }
    }

    private static string BuildLoyaltyMaxHint(decimal bonusPct, decimal timePct)
    {
        var parts = new List<string>();
        if (bonusPct > 0) parts.Add($"бонус {bonusPct:0.#}% при пополнении");
        if (timePct > 0) parts.Add($"скидка {timePct:0.#}% на игровое время");
        return parts.Count > 0
            ? "Максимальный уровень. " + string.Join(" · ", parts) + "."
            : "Уровень лояльности достигнут.";
    }

    private static string BuildLoyaltyProgressHint(decimal remaining, decimal bonusPct, decimal timePct)
    {
        var head = $"Ещё {remaining:0} ₸ до следующего уровня.";
        var parts = new List<string>();
        if (bonusPct > 0) parts.Add($"бонус {bonusPct:0.#}%");
        if (timePct > 0) parts.Add($"скидка на время {timePct:0.#}%");
        return parts.Count > 0
            ? $"{head} Сейчас: {string.Join(" · ", parts)}."
            : $"{head} Бонусы — при пополнении на кассе.";
    }

    private static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return "Телефон не указан";
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 4)
            return phone;
        var tail = digits[^4..];
        return $"+7 ••• ••• {tail[..2]} {tail[2..]}";
    }

    private void RenderAccountFinanceList()
    {
        AccountFinanceList.Children.Clear();
        var items = _agent.AccountTransactions;
        AccountFinanceEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var t in items.Take(5))
        {
            var comment = string.IsNullOrWhiteSpace(t.Comment) ? null : t.Comment;
            AccountFinanceList.Children.Add(BuildHistoryRow(
                ShellUiText.LedgerType(t.Type),
                comment is null
                    ? t.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm")
                    : $"{t.CreatedAt.ToLocalTime():dd.MM HH:mm} · {comment}",
                $"{(t.Direction == LedgerDirection.Credit ? "+" : "−")}{t.Amount:0} ₸",
                t.Direction == LedgerDirection.Credit));
        }
    }

    private void RenderAccountBankList()
    {
        AccountBankList.Children.Clear();
        var banks = _agent.Customer?.TimeBanks ?? [];
        AccountBankEmpty.Visibility = banks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var currentZone = _agent.Customer?.CurrentZoneId;
        var canStart = _agent.Session is null;
        foreach (var b in banks)
        {
            var isCurrent = currentZone is Guid z && z == b.ZoneId;
            var card = new Border
            {
                Style = (Style)FindResource("GlassCard"),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(16),
                BorderBrush = isCurrent
                    ? (Brush)FindResource("Accent")
                    : (Brush)FindResource("Stroke"),
                BorderThickness = new Thickness(isCurrent ? 1.5 : 1)
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = isCurrent ? $"{b.ZoneName} · эта зона" : b.ZoneName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"{b.Minutes} мин",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)FindResource("Accent2")
            });

            if (b.Minutes > 0 && canStart)
            {
                var btn = new Button
                {
                    Content = isCurrent ? "Играть с этого банка" : "Только на ПК этой зоны",
                    Style = (Style)FindResource(isCurrent ? "AccentButton" : "GhostButton"),
                    Margin = new Thickness(0, 12, 0, 0),
                    Padding = new Thickness(14, 10, 14, 10),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    IsEnabled = isCurrent,
                    Tag = b
                };
                btn.Click += StartFromSelectedBank_Click;
                sp.Children.Add(btn);
            }
            else if (b.Minutes > 0 && !canStart)
            {
                sp.Children.Add(new TextBlock
                {
                    Text = "Сначала завершите текущий сеанс",
                    Foreground = (Brush)FindResource("Muted"),
                    FontSize = 12,
                    Margin = new Thickness(0, 10, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            card.Child = sp;
            AccountBankList.Children.Add(card);
        }
    }

    private async void StartFromSelectedBank_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: ClientZoneTimeBankDto bank })
                return;
            if (_agent.Customer is null)
                throw new InvalidOperationException("Войдите в аккаунт");
            if (_agent.Session is not null)
                throw new InvalidOperationException("Уже есть активный сеанс");
            if (_agent.Customer.CurrentZoneId != bank.ZoneId)
                throw new InvalidOperationException(
                    $"Минуты зоны «{bank.ZoneName}» можно запустить только с ПК этой зоны.");
            if (bank.Minutes <= 0)
                throw new InvalidOperationException("Нет минут в этом банке");

            var tariff = _agent.Tariffs.FirstOrDefault(t => t.IsActive && t.Kind == TariffKind.Hourly)
                         ?? _agent.Tariffs.FirstOrDefault(t => t.IsActive)
                         ?? throw new InvalidOperationException("Нет доступного тарифа для этой зоны");

            await _agent.StartBalanceSessionAsync(tariff.Id, bank.Minutes, useTimeBank: true);
            await _agent.RefreshAccountAsync();
            ShowTab("home");
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Ошибка", ex.Message);
        }
    }

    private void RenderAccountHistoryList()
    {
        AccountHistoryList.Children.Clear();
        var showMore = false;
        switch (_historySub)
        {
            case "money":
            {
                var txs = _agent.AccountTransactions;
                AccountHistoryEmpty.Visibility = txs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var t in txs)
                {
                    var sub = t.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                    if (!string.IsNullOrWhiteSpace(t.Comment))
                        sub += $" · {t.Comment}";
                    AccountHistoryList.Children.Add(BuildHistoryRow(
                        ShellUiText.LedgerType(t.Type),
                        sub,
                        $"{(t.Direction == LedgerDirection.Credit ? "+" : "−")}{t.Amount:0} ₸",
                        t.Direction == LedgerDirection.Credit));
                }
                showMore = txs.Count >= _accountHistoryTake && _accountHistoryTake < 100;
                break;
            }
            case "bank":
            {
                var banks = _agent.AccountTimeBankTransactions;
                AccountHistoryEmpty.Visibility = banks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var t in banks)
                {
                    var sub = t.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                    if (!string.IsNullOrWhiteSpace(t.Comment))
                        sub += $" · {t.Comment}";
                    AccountHistoryList.Children.Add(BuildHistoryRow(
                        $"{t.ZoneName ?? "Зона"} · {ShellUiText.TimeBankReasonLabel(t.Reason)}",
                        sub,
                        $"{(t.Direction == LedgerDirection.Credit ? "+" : "−")}{t.Minutes} мин",
                        t.Direction == LedgerDirection.Credit));
                }
                showMore = banks.Count >= _accountHistoryTake && _accountHistoryTake < 100;
                break;
            }
            case "orders":
            {
                var orders = _agent.AccountOrders;
                AccountHistoryEmpty.Visibility = orders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var o in orders)
                {
                    var title = $"Заказ {o.Number} · {ShellUiText.BarOrderStatus(o.Status)}";
                    var sub = $"{o.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} · {ShellUiText.Payment(o.PaymentMode)}";
                    if (!string.IsNullOrWhiteSpace(o.ItemsSummary))
                        sub += $" · {o.ItemsSummary}";
                    else if (o.ItemCount > 0)
                        sub += $" · позиций: {o.ItemCount}";
                    AccountHistoryList.Children.Add(BuildHistoryRow(title, sub, $"{o.Total:0} ₸", false));
                }
                showMore = orders.Count >= _accountHistoryTake && _accountHistoryTake < 100;
                break;
            }
            case "bookings":
            {
                var bookings = _agent.AccountBookings;
                AccountHistoryEmpty.Visibility = bookings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var b in bookings)
                {
                    var from = b.StartsAt.ToLocalTime();
                    var to = b.EndsAt.ToLocalTime();
                    var title = $"Бронь {b.Number} · {ShellUiText.BookingStatus(b.Status)}";
                    var sub = $"{from:dd.MM.yyyy HH:mm}–{to:HH:mm}";
                    if (!string.IsNullOrWhiteSpace(b.ZoneName))
                        sub += $" · {b.ZoneName}";
                    if (!string.IsNullOrWhiteSpace(b.Computers))
                        sub += $" · {b.Computers}";
                    var amt = b.PrepaidAmount > 0 ? $"предоплата {b.PrepaidAmount:0} ₸" : "—";
                    AccountHistoryList.Children.Add(BuildHistoryRow(title, sub, amt, false));
                }
                showMore = bookings.Count >= Math.Min(_accountHistoryTake, 50) && _accountHistoryTake < 100;
                break;
            }
            default:
            {
                var sessions = _agent.AccountSessions;
                AccountHistoryEmpty.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var s in sessions)
                {
                    var title = s.TariffName is { Length: > 0 }
                        ? $"Тариф: {s.TariffName}"
                        : "Сеанс";
                    if (!string.IsNullOrWhiteSpace(s.ComputerName))
                        title += $" · {s.ComputerName}";
                    if (!string.IsNullOrWhiteSpace(s.ZoneName))
                        title += $" · {s.ZoneName}";

                    var when = s.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                    if (s.EndedAt is DateTimeOffset ended)
                        when += $" → {ended.ToLocalTime():HH:mm}";
                    var sub = $"{when} · {s.DurationMinutes} мин · {ShellUiText.SessionStatus(s.Status)} · {ShellUiText.Payment(s.PaymentMethod)}";
                    AccountHistoryList.Children.Add(BuildHistoryRow(
                        title,
                        sub,
                        $"{s.TotalPrice:0} ₸",
                        false));
                }
                showMore = sessions.Count >= _accountHistoryTake && _accountHistoryTake < 100;
                break;
            }
        }

        AccountHistoryMoreButton.Visibility = showMore ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border BuildHistoryRow(string title, string subtitle, string amount, bool credit)
    {
        var row = new Border
        {
            Style = (Style)FindResource("GlassCard"),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(14, 12, 14, 12)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });
        left.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = (Brush)FindResource("Muted"),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(left, 0);
        var amt = new TextBlock
        {
            Text = amount,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            Foreground = credit
                ? (Brush)FindResource("Success")
                : (Brush)FindResource("Accent2")
        };
        Grid.SetColumn(amt, 1);
        grid.Children.Add(left);
        grid.Children.Add(amt);
        row.Child = grid;
        return row;
    }

    private async void AccountSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        AccountProfileError.Text = "";
        AccountProfileFlash.Text = "";
        try
        {
            await _agent.UpdateProfileAsync(
                AccountFirstNameBox.Text.Trim(),
                AccountLastNameBox.Text.Trim(),
                string.IsNullOrWhiteSpace(AccountEmailBox.Text) ? null : AccountEmailBox.Text.Trim());
            _accountProfileBound = false;
            BindAccountOverview();
            BindProfile();
            AccountProfileFlash.Text = "Профиль сохранён";
        }
        catch (Exception ex)
        {
            AccountProfileError.Text = ShellUiText.GuestError(ex.Message);
        }
    }

    private async void AccountSavePin_Click(object sender, RoutedEventArgs e)
    {
        AccountSecurityError.Text = "";
        AccountSecurityFlash.Text = "";
        var current = AccountCurrentSecretBox.Password?.Trim() ?? "";
        var newPin = AccountNewPinBox.Password?.Trim() ?? "";
        var confirm = AccountNewPinConfirmBox.Password?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(current))
        {
            AccountSecurityError.Text = "Введите текущий ПИН или пароль";
            return;
        }
        if (string.IsNullOrEmpty(newPin))
        {
            AccountSecurityError.Text = "Укажите новый ПИН";
            return;
        }
        if (newPin != confirm)
        {
            AccountSecurityError.Text = "ПИН и повтор не совпадают";
            return;
        }

        try
        {
            await _agent.ChangeCredentialsAsync(current, newPin: newPin);
            AccountCurrentSecretBox.Password = "";
            AccountNewPinBox.Password = "";
            AccountNewPinConfirmBox.Password = "";
            BindAccountOverview();
            AccountSecurityFlash.Text = "ПИН обновлён";
        }
        catch (Exception ex)
        {
            AccountSecurityError.Text = ShellUiText.GuestError(ex.Message);
        }
    }

    private async void AccountSavePassword_Click(object sender, RoutedEventArgs e)
    {
        AccountPasswordError.Text = "";
        AccountPasswordFlash.Text = "";
        var current = AccountCurrentSecretBox2.Password?.Trim() ?? "";
        var newPwd = AccountNewPasswordBox.Password?.Trim() ?? "";
        var confirm = AccountNewPasswordConfirmBox.Password?.Trim() ?? "";
        var login = AccountLoginBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(current))
        {
            AccountPasswordError.Text = "Введите текущий ПИН или пароль";
            return;
        }
        if (!string.IsNullOrEmpty(newPwd) && newPwd != confirm)
        {
            AccountPasswordError.Text = "Пароль и повтор не совпадают";
            return;
        }
        if (string.IsNullOrEmpty(newPwd) && string.IsNullOrWhiteSpace(login))
        {
            AccountPasswordError.Text = "Укажите новый пароль или логин";
            return;
        }

        try
        {
            await _agent.ChangeCredentialsAsync(
                current,
                newPassword: string.IsNullOrEmpty(newPwd) ? null : newPwd,
                login: string.IsNullOrWhiteSpace(login) ? null : login);
            AccountCurrentSecretBox2.Password = "";
            AccountNewPasswordBox.Password = "";
            AccountNewPasswordConfirmBox.Password = "";
            BindAccountOverview();
            AccountPasswordFlash.Text = "Данные обновлены";
        }
        catch (Exception ex)
        {
            AccountPasswordError.Text = ShellUiText.GuestError(ex.Message);
        }
    }

    private void SetNav(Button btn, bool active)
    {
        btn.Style = (Style)FindResource(active ? "NavButtonActive" : "NavButton");
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
            ShowTab(tag, forceRender: true);
    }

    private void FillTariffs()
    {
        var selected = (TariffCombo.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        TariffCombo.Items.Clear();

        // Как iCafe: пакет / час / бесплатно, почасовые пресеты для этих ПК.
        foreach (var t in _agent.Tariffs
                     .Where(x => x.IsAvailableNow && x.IsActive)
                     .Where(x => x.Kind is TariffKind.Package or TariffKind.Hourly or TariffKind.Free)
                     .OrderBy(x => x.SortOrder)
                     .ThenBy(x => x.Name))
        {
            var label = FormatTariffLabel(t);
            TariffCombo.Items.Add(new ComboBoxItem { Content = label, Tag = t.Id });
        }

        if (TariffCombo.Items.Count == 0)
        {
            DurationButtonsPanel.Children.Clear();
            SessionPriceQuote.Text = "Нет доступных тарифов";
            return;
        }

        if (selected is Guid id)
        {
            foreach (ComboBoxItem item in TariffCombo.Items)
            {
                if (item.Tag is Guid g && g == id)
                {
                    TariffCombo.SelectedItem = item;
                    RefreshBuyDurationUi();
                    if (CanClientUseBalance() && _agent.Session is not null)
                        RebuildExtendOptionButtons();
                    return;
                }
            }
        }

        TariffCombo.SelectedIndex = 0;
        RefreshBuyDurationUi();
        if (CanClientUseBalance() && _agent.Session is not null)
            RebuildExtendOptionButtons();
    }

    private static string FormatMoneyPromo(decimal list, decimal promoPercent, string? promoLabel, string suffix = " ₸")
    {
        if (promoPercent <= 0)
            return $"{list:0}{suffix}";
        var sale = Math.Round(list * (1m - promoPercent / 100m), 0, MidpointRounding.AwayFromZero);
        var badge = string.IsNullOrWhiteSpace(promoLabel) ? $"−{promoPercent:0}%" : promoLabel;
        return $"{sale:0}{suffix} (было {list:0}, {badge})";
    }

    private static string FormatTariffLabel(TariffDto t)
    {
        var zone = string.IsNullOrWhiteSpace(t.ZoneName) ? "" : $"{t.ZoneName} · ";
        var code = t.Code ?? "";
        var promo = t.PromoPercent;
        if (t.DurationMode == TariffDurationMode.TimeWindow && t.FixedPrice is not null)
        {
            var until = t.WindowEndsAtLocal ?? t.AvailableTo ?? "—";
            var left = t.RemainingMinutesInWindow is > 0
                ? $" · осталось {FormatPackageMinutes(t.RemainingMinutesInWindow.Value)}"
                : "";
            return $"{zone}{t.Name} · до {until}{left} · {FormatMoneyPromo(t.FixedPrice.Value, promo, t.PromoLabel)}";
        }

        if (t.Kind == TariffKind.Package && t.FixedPrice is not null)
        {
            var hours = t.FixedDurationMinutes is > 0 ? t.FixedDurationMinutes.Value / 60 : 0;
            var money = FormatMoneyPromo(t.FixedPrice.Value, promo, t.PromoLabel);
            if (code.Contains("2P1", StringComparison.OrdinalIgnoreCase) || t.Name == "2+1")
                return $"{zone}2+1 · {hours} ч · {money} (2+1 бесплатно)";
            if (code.Contains("3P2", StringComparison.OrdinalIgnoreCase) || t.Name == "3+2")
                return $"{zone}3+2 · {hours} ч · {money} (3+2 бесплатно)";
            return hours > 0
                ? $"{zone}{t.Name} · {hours} ч · {money}"
                : $"{zone}{t.Name} · {money}";
        }

        return t.Kind switch
        {
            TariffKind.Free => $"{zone}{t.Name} · бесплатно",
            _ => $"{zone}{t.Name} · {FormatMoneyPromo(t.PricePerHour, promo, t.PromoLabel, " ₸/ч")}"
        };
    }

    private TariffDto? SelectedBuyTariff()
    {
        if (TariffCombo.SelectedItem is not ComboBoxItem { Tag: Guid id })
            return null;
        return _agent.Tariffs.FirstOrDefault(x => x.Id == id);
    }

    private void TariffCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshBuyDurationUi();

    private void RefreshBuyDurationUi()
    {
        var t = SelectedBuyTariff();
        DurationButtonsPanel.Children.Clear();
        if (t is null)
        {
            SessionPriceQuote.Text = "";
            return;
        }

        if (t.DurationMode == TariffDurationMode.TimeWindow)
        {
            _buyDurationMinutes = t.RemainingMinutesInWindow is > 0
                ? t.RemainingMinutesInWindow.Value
                : Math.Max(1, 60);
            DurationLabel.Text = string.IsNullOrWhiteSpace(t.SalePreview)
                ? $"до {t.WindowEndsAtLocal ?? t.AvailableTo ?? "—"}"
                : t.SalePreview!;
            DurationButtonsPanel.Children.Clear();
        }
        else if (t.Kind == TariffKind.Package && t.FixedDurationMinutes is > 0)
        {
            _buyDurationMinutes = t.FixedDurationMinutes.Value;
            DurationLabel.Text = FormatPackageDurationHint(t);
            DurationButtonsPanel.Children.Clear();
        }
        else if (t.Kind == TariffKind.Free)
        {
            _buyDurationMinutes = Math.Max(t.MinDurationMinutes ?? 60, 30);
            DurationLabel.Text = "Длительность";
            foreach (var m in BuyDurationPresets)
                DurationButtonsPanel.Children.Add(MakeDurationChip(m, m == _buyDurationMinutes, enabled: true));
        }
        else
        {
            // Почасовой «1 час» — берём длительность тарифа, без произвольных 60/120/180 кнопок
            _buyDurationMinutes = t.MinDurationMinutes is > 0 ? t.MinDurationMinutes.Value : 60;
            DurationLabel.Text = "Длительность тарифа";
            DurationButtonsPanel.Children.Clear();
        }

        UpdateSessionPriceQuote();
    }

    private static string FormatPackageMinutes(int minutes)
    {
        if (minutes < 60) return $"{minutes} мин";
        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? $"{h} ч" : $"{h} ч {m} мин";
    }

    private static string FormatPackageDurationHint(TariffDto t)
    {
        var code = t.Code ?? "";
        var hours = (t.FixedDurationMinutes ?? 0) / 60;
        if (code.Contains("2P1", StringComparison.OrdinalIgnoreCase) || t.Name == "2+1")
            return $"2+1 · {hours} ч (2 оплата + 1 бесплатно)";
        if (code.Contains("3P2", StringComparison.OrdinalIgnoreCase) || t.Name == "3+2")
            return $"3+2 · {hours} ч (3 оплата + 2 бесплатно)";
        return hours > 0 ? $"Пакет · {hours} ч" : "Длительность пакета";
    }

    private Button MakeDurationChip(int minutes, bool selected, bool enabled)
    {
        var btn = new Button
        {
            Content = $"{minutes} мин",
            Tag = minutes,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(14, 8, 14, 8),
            IsEnabled = enabled,
            Style = (Style)FindResource(selected ? "AccentButton" : "GhostButton")
        };
        if (enabled)
            btn.Click += DurationChip_Click;
        return btn;
    }

    private void DurationChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int minutes })
            return;
        _buyDurationMinutes = minutes;
        RefreshBuyDurationUi();
    }

    private void UpdateSessionPriceQuote()
    {
        var t = SelectedBuyTariff();
        if (t is null)
        {
            SessionPriceQuote.Text = "";
            return;
        }

        var loyaltyPct = _agent.Customer?.LoyaltyTimeDiscountPercent ?? 0m;
        var price = QuoteTariffPrice(t, _buyDurationMinutes, loyaltyPct);
        if (t.DurationMode == TariffDurationMode.TimeWindow && !string.IsNullOrWhiteSpace(t.SalePreview))
        {
            SessionPriceQuote.Text = t.SalePreview!;
            return;
        }

        SessionPriceQuote.Text = price <= 0
            ? $"{_buyDurationMinutes} мин · бесплатно"
            : $"{_buyDurationMinutes} мин · {price:0.##} ₸";
    }

    private static decimal ApplyLoyaltyDiscount(decimal price, decimal loyaltyPercent)
    {
        if (price <= 0 || loyaltyPercent <= 0) return price;
        return Math.Round(price * (1m - Math.Min(90m, loyaltyPercent) / 100m), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Маркетинговая акция на тарифе (с сервера в PromoPercent) — только 2+1/3+2/день/ночь.</summary>
    private static bool TariffHasMarketingPromo(TariffDto t) => t.PromoPercent > 0;

    private static decimal QuoteTariffPrice(TariffDto t, int minutes, decimal loyaltyPercent = 0)
    {
        if (minutes <= 0 && t.DurationMode != TariffDurationMode.TimeWindow) return 0;
        if (t.Kind == TariffKind.Free) return 0;

        decimal ApplyPromo(decimal amount) =>
            t.PromoPercent > 0
                ? Math.Round(amount * (1m - t.PromoPercent / 100m), 2, MidpointRounding.AwayFromZero)
                : amount;

        if (t.DurationMode == TariffDurationMode.TimeWindow && t.FixedPrice is not null)
            return ApplyLoyaltyDiscount(ApplyPromo(t.FixedPrice.Value), loyaltyPercent);

        if (t.Kind == TariffKind.Package && t.FixedPrice is not null)
        {
            var fixedMins = t.FixedDurationMinutes ?? minutes;
            if (minutes <= fixedMins)
                return ApplyLoyaltyDiscount(ApplyPromo(t.FixedPrice.Value), loyaltyPercent);

            var total = ApplyPromo(t.FixedPrice.Value) + QuoteExtendPrice(t, minutes - fixedMins);
            return ApplyLoyaltyDiscount(total, loyaltyPercent);
        }

        var billable = ToBillableMinutes(t.BillingMode, minutes);
        var rate = t.PricePerHour > 0 ? t.PricePerHour / 60m : 0m;
        if (t.PromoPercent > 0)
            rate *= 1m - t.PromoPercent / 100m;
        var price = Math.Round(rate * billable, 2, MidpointRounding.AwayFromZero);
        if (t.MinCharge > price)
            price = ApplyPromo(t.MinCharge);
        return ApplyLoyaltyDiscount(price, loyaltyPercent);
    }

    /// <summary>Доп. минуты к текущему сеансу — без маркетинговой акции (как PriceForAdditionalMinutes на API).</summary>
    private static decimal QuoteExtendPrice(TariffDto t, int additionalMinutes, decimal loyaltyPercent = 0)
    {
        if (additionalMinutes <= 0) return 0;
        var rate = t.PricePerHour > 0
            ? t.PricePerHour / 60m
            : t.FixedPrice is > 0 && t.FixedDurationMinutes is > 0
                ? t.FixedPrice.Value / t.FixedDurationMinutes.Value
                : 0m;
        var billable = ToBillableMinutes(t.BillingMode, additionalMinutes);
        var price = Math.Round(rate * billable, 2, MidpointRounding.AwayFromZero);
        return ApplyLoyaltyDiscount(price, loyaltyPercent);
    }

    private static int ToBillableMinutes(BillingMode mode, int minutes) => mode switch
    {
        BillingMode.Block15 => (int)Math.Ceiling(minutes / 15d) * 15,
        BillingMode.Block30 => (int)Math.Ceiling(minutes / 30d) * 30,
        _ => minutes
    };

    private async void StartSession_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_agent.Customer is null)
                throw new InvalidOperationException("Войдите в аккаунт");
            var t = SelectedBuyTariff()
                    ?? throw new InvalidOperationException("Выберите тариф");

            var minutes = _buyDurationMinutes;
            if (t.Kind == TariffKind.Package && t.FixedDurationMinutes is > 0)
                minutes = t.FixedDurationMinutes.Value;
            if (minutes <= 0)
                throw new InvalidOperationException("Укажите длительность");

            var loyaltyPct = _agent.Customer?.LoyaltyTimeDiscountPercent ?? 0m;
            var price = QuoteTariffPrice(t, minutes, loyaltyPct);
            var wallet = _agent.Customer.Balance + _agent.Customer.BonusBalance;
            if (price > 0 && wallet < price)
                throw new InvalidOperationException(
                    $"Недостаточно средств: нужно {price:0} ₸, на счёте {wallet:0} ₸");

            if (!await ShowConfirmAsync(
                    "Покупка сеанса",
                    $"{t.Name}\n{minutes} мин · {price:0.##} ₸\n\nСписать с баланса?"))
                return;

            await _agent.StartBalanceSessionAsync(t.Id, minutes, useTimeBank: false);
            await _agent.RefreshAccountAsync();
            ShowTab("home");
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Ошибка", ex.Message);
        }
    }

    private void RenderHomePreviews()
    {
        HomeGamesPanel.Children.Clear();
        var apps = _agent.Apps.Take(8).ToList();
        HomeGamesEmpty.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var app in apps)
            HomeGamesPanel.Children.Add(CreateAppCard(app, compact: true));

        HomeShopPanel.Children.Clear();
        var products = _agent.BarProducts.Where(p => p.IsAvailable).Take(8).ToList();
        if (products.Count == 0)
            products = _agent.BarProducts.Take(8).ToList();
        HomeShopEmpty.Visibility = products.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var hues = new[]
        {
            Color.FromRgb(0xFF, 0x6A, 0x00),
            Color.FromRgb(0xC4, 0x4D, 0x00),
            Color.FromRgb(0x3A, 0x2A, 0x1A),
            Color.FromRgb(0x5C, 0x3A, 0x14),
            Color.FromRgb(0x2A, 0x1A, 0x10)
        };
        var i = 0;
        foreach (var p in products)
            HomeShopPanel.Children.Add(CreateShopCard(p, hues[i++ % hues.Length], compact: true));
    }

    private Border CreateAppCard(SoftwareAppDto app, bool compact)
    {
        UIElement media;
        System.Windows.Media.ImageSource? src = null;
        var imageUri = ResolveProductImageUri(app.IconPath);
        if (imageUri is not null)
            src = ShellImageCache.Get(imageUri);
        if (src is null)
        {
            var exe = Environment.ExpandEnvironmentVariables(app.ExePath ?? "");
            src = ExeIconCache.Get(exe);
        }

        if (src is not null)
        {
            media = new Image
            {
                Source = src,
                Stretch = Stretch.UniformToFill,
                SnapsToDevicePixels = true
            };
        }
        else
        {
            media = new TextBlock
            {
                Text = string.IsNullOrEmpty(app.Name) ? "?" : app.Name[..1].ToUpperInvariant(),
                FontSize = compact ? 40 : 48,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.92
            };
        }

        var titleOverlay = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x00, 0, 0, 0),
                Color.FromArgb(0xF0, 8, 8, 10),
                90),
            Padding = new Thickness(compact ? 12 : 14, 36, compact ? 12 : 14, compact ? 12 : 14),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = app.Name,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = compact ? 13 : 14,
                        TextWrapping = TextWrapping.Wrap,
                        MaxHeight = 40,
                        Foreground = Brushes.White,
                        LineHeight = 18
                    },
                    new TextBlock
                    {
                        Text = app.FileExists ? app.Category : $"{app.Category} · нет файла",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xAE)),
                        Margin = new Thickness(0, 4, 0, 0)
                    }
                }
            }
        };

        var scale = new ScaleTransform(1, 1);
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x14)),
            CornerRadius = new CornerRadius(20),
            Margin = new Thickness(compact ? 6 : 10),
            Cursor = Cursors.Hand,
            Opacity = app.FileExists ? 1 : 0.45,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.35,
                Color = Colors.Black,
                Direction = 270
            },
            Child = new Grid
            {
                Children =
                {
                    new Border
                    {
                        Background = new LinearGradientBrush(
                            Color.FromRgb(0x2A, 0x16, 0x0C),
                            Color.FromRgb(0x10, 0x10, 0x12),
                            150),
                        Child = media
                    },
                    titleOverlay
                }
            }
        };

        card.MouseEnter += (_, _) =>
        {
            scale.ScaleX = 1.035;
            scale.ScaleY = 1.035;
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x6A, 0x00));
            card.Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 0,
                Opacity = 0.55,
                Color = Color.FromRgb(0xFF, 0x6A, 0x00)
            };
        };
        card.MouseLeave += (_, _) =>
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            card.Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.35,
                Color = Colors.Black,
                Direction = 270
            };
        };
        card.MouseLeftButtonUp += (_, _) => LaunchApp(app);
        return card;
    }

    private Border CreateShopCard(ClientBarProductDto p, Color color, bool compact)
    {
        var addBtn = new Button
        {
            Content = "+",
            Width = compact ? 36 : 40,
            Height = compact ? 36 : 40,
            FontSize = compact ? 20 : 22,
            FontWeight = FontWeights.Bold,
            Style = (Style)FindResource("AccentButton"),
            Padding = new Thickness(0),
            Tag = p,
            IsEnabled = p.IsAvailable && _agent.Session is not null,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "В корзину"
        };
        addBtn.Click += (_, _) => AddToCart(p);

        UIElement media;
        var imageUri = ResolveProductImageUri(p.ImageUrl);
        var bmp = imageUri is null ? null : ShellImageCache.Get(imageUri);
        if (bmp is not null)
        {
            media = new Border
            {
                Height = compact ? 100 : 128,
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 0, 12),
                ClipToBounds = true,
                Background = Brushes.White,
                Child = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(10)
                }
            };
        }
        else
        {
            media = new Border
            {
                Height = compact ? 100 : 128,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(color),
                Margin = new Thickness(0, 0, 0, 12),
                Child = new TextBlock
                {
                    Text = p.Name.Length > 0 ? p.Name[..1].ToUpperInvariant() : "?",
                    FontSize = compact ? 30 : 36,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.White
                }
            };
        }
        DockPanel.SetDock(media, Dock.Top);

        var priceRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(addBtn, Dock.Right);
        priceRow.Children.Add(addBtn);
        priceRow.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = $"{p.SalePrice:0} ₸",
                    Foreground = (Brush)FindResource("Accent2"),
                    FontWeight = FontWeights.Bold,
                    FontSize = compact ? 16 : 18
                },
                new TextBlock
                {
                    Text = p.IsAvailable
                        ? (_agent.Session is null ? "Нужен сеанс" : p.CategoryName)
                        : "Нет в наличии",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("Muted"),
                    Margin = new Thickness(0, 2, 0, 0)
                }
            }
        });

        var scale = new ScaleTransform(1, 1);
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1A, 0x1A, 0x1D)),
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(compact ? 6 : 8),
            Padding = new Thickness(12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 6,
                Opacity = 0.35,
                Color = Colors.Black,
                Direction = 270
            },
            Child = new DockPanel
            {
                Children =
                {
                    media,
                    new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = p.Name,
                                FontWeight = FontWeights.SemiBold,
                                FontSize = compact ? 13 : 14,
                                TextWrapping = TextWrapping.Wrap
                            },
                            priceRow
                        }
                    }
                }
            }
        };
        card.MouseEnter += (_, _) =>
        {
            scale.ScaleX = 1.03;
            scale.ScaleY = 1.03;
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0x6A, 0x00));
        };
        card.MouseLeave += (_, _) =>
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        };
        return card;
    }

    private void RenderApps()
    {
        BuildCategoryFilters();
        AppsPanel.Children.Clear();
        var apps = _agent.Apps.AsEnumerable();
        if (_appCategory != "Все")
            apps = apps.Where(a => string.Equals(a.Category, _appCategory, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(_search))
            apps = apps.Where(a => a.Name.Contains(_search, StringComparison.OrdinalIgnoreCase));

        foreach (var app in apps)
            AppsPanel.Children.Add(CreateAppCard(app, compact: false));

        if (AppsPanel.Children.Count == 0)
        {
            AppsPanel.Children.Add(new TextBlock
            {
                Text = "Нет программ. Добавьте их в панели клуба → Программы.",
                Foreground = (Brush)FindResource("Muted"),
                Margin = new Thickness(8),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private async void LaunchApp(SoftwareAppDto app)
    {
        try
        {
            if (!HasPlayableSession())
            {
                await ShowInfoAsync(
                    "Нужен сеанс",
                    "Запуск программ доступен только во время активного сеанса.");
                return;
            }

            var path = Environment.ExpandEnvironmentVariables(app.ExePath ?? "");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                await ShowInfoAsync(
                    "Файл не найден",
                    $"Не найден исполняемый файл на этом ПК.\n\n{path}");
                return;
            }

            // Сначала гарантированно стартуем звук — потом лаунчер/игра.
            // Звук доигрывает сам (MediaPlayer удерживается до MediaEnded).
            await PlayLaunchSoundAsync(app);

            ShowLaunchSplash(app.Name);
            Process? proc = null;
            try
            {
                proc = await Task.Run(() => _agent.LaunchApp(app));
                // Для лаунчеров (Steam/Epic) процесс часто сразу «готов» или даже выходит —
                // не ждём долго, звук уже идёт независимо.
                await WaitForAppReadyAsync(proc, TimeSpan.FromSeconds(12));
            }
            finally
            {
                HideLaunchSplash();
            }

            _allowBackgroundForGame = true;
            YieldDesktopForApp();
            SyncDesktopLayer();
        }
        catch (Exception ex)
        {
            HideLaunchSplash();
            await ShowInfoAsync("Ошибка запуска", ex.Message);
        }
    }

    private async Task PlayLaunchSoundAsync(SoftwareAppDto app)
    {
        if (string.IsNullOrWhiteSpace(app.LaunchSoundUrl)) return;
        if (_agent.Customer is { ComfortSoundEnabled: false }) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await _launchSounds.StartAsync(app.Id, app.LaunchSoundUrl, cts.Token).ConfigureAwait(true);
        }
        catch { /* звук не должен блокировать запуск */ }
    }

    private void ShowLaunchSplash(string appName)
    {
        LaunchSplashTitle.Text = appName;
        LaunchSplashHint.Text = "Подождите, запускаем…";
        PanelLaunchSplash.Visibility = Visibility.Visible;

        _launchSpinnerStoryboard?.Stop();
        var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        _launchSpinnerStoryboard = new Storyboard();
        Storyboard.SetTarget(anim, LaunchSpinnerRotate);
        Storyboard.SetTargetProperty(anim, new PropertyPath(RotateTransform.AngleProperty));
        _launchSpinnerStoryboard.Children.Add(anim);
        _launchSpinnerStoryboard.Begin();
    }

    private void HideLaunchSplash()
    {
        _launchSpinnerStoryboard?.Stop();
        _launchSpinnerStoryboard = null;
        PanelLaunchSplash.Visibility = Visibility.Collapsed;
    }

    private static async Task WaitForAppReadyAsync(Process? proc, TimeSpan timeout)
    {
        if (proc is null)
        {
            await Task.Delay(900);
            return;
        }

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                proc.Refresh();
                if (proc.HasExited)
                    return;
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    await Task.Delay(350);
                    return;
                }
            }
            catch
            {
                return;
            }

            await Task.Delay(200);
        }
    }

    private void BuildCategoryFilters()
    {
        var cats = new List<string> { "Все" };
        cats.AddRange(_agent.Apps.Select(a => a.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
        CategoryFilterPanel.Children.Clear();
        foreach (var cat in cats)
        {
            var selected = string.Equals(cat, _appCategory, StringComparison.OrdinalIgnoreCase);
            var btn = new Button
            {
                Content = cat,
                Style = (Style)FindResource(selected ? "PillButtonActive" : "PillButton"),
                Tag = cat
            };
            btn.Click += (_, _) =>
            {
                _appCategory = cat;
                RenderApps();
            };
            CategoryFilterPanel.Children.Add(btn);
        }
    }

    private void RenderShop()
    {
        BuildShopCategories();
        ShopPanel.Children.Clear();
        var products = _agent.BarProducts.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(_shopCategoryId) && Guid.TryParse(_shopCategoryId, out var cid))
            products = products.Where(p => p.CategoryId == cid);
        if (!string.IsNullOrWhiteSpace(_search))
            products = products.Where(p => p.Name.Contains(_search, StringComparison.OrdinalIgnoreCase));

        var hues = new[]
        {
            Color.FromRgb(0xFF, 0x6A, 0x00),
            Color.FromRgb(0xC4, 0x4D, 0x00),
            Color.FromRgb(0x3A, 0x2A, 0x1A),
            Color.FromRgb(0x5C, 0x3A, 0x14),
            Color.FromRgb(0x2A, 0x1A, 0x10)
        };
        var i = 0;
        foreach (var p in products)
            ShopPanel.Children.Add(CreateShopCard(p, hues[i++ % hues.Length], compact: false));

        if (ShopPanel.Children.Count == 0)
        {
            ShopPanel.Children.Add(new TextBlock
            {
                Text = "Товаров пока нет. Добавьте их в панели: Бар → Товары.",
                Foreground = (Brush)FindResource("Muted"),
                Margin = new Thickness(8)
            });
        }

        RefreshCartUi();
    }

    private void AddToCart(ClientBarProductDto product)
    {
        if (_agent.Session is null)
        {
            _ = ShowInfoAsync("Бар", "Заказ доступен во время сеанса");
            return;
        }

        if (!product.IsAvailable)
            return;

        if (_cart.TryGetValue(product.Id, out var existing))
            _cart[product.Id] = (product, existing.Qty + 1);
        else
            _cart[product.Id] = (product, 1);

        ShopFlash.Text = $"В корзине: {product.Name}";
        RefreshCartUi();
    }

    private void ChangeCartQty(Guid productId, decimal delta)
    {
        if (!_cart.TryGetValue(productId, out var line))
            return;
        var next = line.Qty + delta;
        if (next <= 0)
            _cart.Remove(productId);
        else
            _cart[productId] = (line.Product, next);
        RefreshCartUi();
    }

    private void ClearCart_Click(object sender, RoutedEventArgs e)
    {
        _cart.Clear();
        ShopFlash.Text = "";
        RefreshCartUi();
    }

    private void RefreshCartUi()
    {
        ShopCartLines.Children.Clear();
        var totalQty = _cart.Values.Sum(x => x.Qty);
        var totalSum = _cart.Values.Sum(x => x.Product.SalePrice * x.Qty);

        if (_cart.Count == 0)
        {
            ShopCartPanel.Visibility = Visibility.Visible;
            if (ShopCartEmpty is not null) ShopCartEmpty.Visibility = Visibility.Visible;
            if (ShopCartBadgeHost is not null) ShopCartBadgeHost.Visibility = Visibility.Collapsed;
            ShopCartBadge.Text = "0";
            ShopCartSummary.Text = "Корзина пуста";
            ShopCartHint.Text = "Добавьте товары и оформите одним заказом";
            CheckoutCartButton.IsEnabled = false;
            return;
        }

        ShopCartPanel.Visibility = Visibility.Visible;
        if (ShopCartEmpty is not null) ShopCartEmpty.Visibility = Visibility.Collapsed;
        if (ShopCartBadgeHost is not null) ShopCartBadgeHost.Visibility = Visibility.Visible;
        ShopCartBadge.Text = $"{totalQty:0}";
        ShopCartSummary.Text = $"{FormatCartQty(totalQty)} · {totalSum:0} ₸";
        ShopCartHint.Text = "Проверьте позиции и нажмите «Оформить заказ»";
        CheckoutCartButton.IsEnabled = _agent.Session is not null;

        foreach (var (id, line) in _cart.OrderBy(x => x.Value.Product.Name))
        {
            var initial = line.Product.Name.Length > 0
                ? line.Product.Name[..1].ToUpperInvariant()
                : "?";

            var avatar = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x8A, 0x2B),
                    Color.FromRgb(0xE8, 0x5A, 0x00),
                    45),
                Child = new TextBlock
                {
                    Text = initial,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var minus = CreateCartQtyButton("-", id);
            minus.Click += (_, _) => ChangeCartQty(id, -1);
            var plus = CreateCartQtyButton("+", id);
            plus.Click += (_, _) => ChangeCartQty(id, 1);

            var qtyRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { minus, new TextBlock
                {
                    Text = $"{line.Qty:0}",
                    MinWidth = 22,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 2, 0)
                }, plus }
            };

            var nameBlock = new TextBlock
            {
                Text = line.Product.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            var priceBlock = new TextBlock
            {
                Text = $"{line.Product.SalePrice * line.Qty:0} ₸",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("Accent2"),
                Margin = new Thickness(0, 4, 0, 0)
            };

            var textCol = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { nameBlock, priceBlock }
            };

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(avatar, 0);
            Grid.SetColumn(textCol, 1);
            top.Children.Add(avatar);
            top.Children.Add(textCol);

            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(qtyRow, Dock.Right);
            bottom.Children.Add(qtyRow);
            bottom.Children.Add(new TextBlock
            {
                Text = $"{line.Product.SalePrice:0} ₸ / шт",
                FontSize = 11,
                Foreground = (Brush)FindResource("Muted"),
                VerticalAlignment = VerticalAlignment.Center
            });

            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0x1A, 0x1A, 0x1C)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 12, 12, 12),
                Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Child = new StackPanel { Children = { top, bottom } }
            };
            ShopCartLines.Children.Add(row);
        }
    }

    private static string FormatCartQty(decimal qty)
    {
        var n = Math.Max(0, (int)Math.Round(qty));
        var mod100 = n % 100;
        var mod10 = n % 10;
        var word = mod100 is >= 11 and <= 14
            ? "позиций"
            : mod10 == 1
                ? "позиция"
                : mod10 is >= 2 and <= 4
                    ? "позиции"
                    : "позиций";
        return $"{n} {word}";
    }

    private Button CreateCartQtyButton(string label, Guid productId)
    {
        var isPlus = label is "+" or "+";
        return new Button
        {
            Content = label == "-" || label == "-" ? "-" : "+",
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Style = (Style)FindResource(isPlus ? "AccentButton" : "GhostButton"),
            Tag = productId,
            ToolTip = isPlus ? "Добавить" : "Убрать",
            Margin = new Thickness(2, 0, 2, 0)
        };
    }

    private async void CheckoutCart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_agent.Session is null)
                throw new InvalidOperationException("Заказ доступен во время сеанса");
            if (_cart.Count == 0)
                throw new InvalidOperationException("Корзина пуста");

            var mode = ShopPayCombo.SelectedItem is ComboBoxItem { Tag: string tag }
                ? tag
                : "CashOnDelivery";

            var lines = _cart.Values
                .Select(x => new ClientPlaceBarOrderItemRequest(x.Product.Id, x.Qty))
                .ToList();
            var total = _cart.Values.Sum(x => x.Product.SalePrice * x.Qty);
            var preview = string.Join("\n", _cart.Values.Select(x => $"· {x.Product.Name} · {x.Qty:0}"));
            if (!await ShowConfirmAsync(
                    "Оформить заказ?",
                    $"{preview}\n\nИтого: {total:0} ₸"))
                return;

            await _agent.PlaceBarOrderAsync(lines, mode);
            _cart.Clear();
            ShopFlash.Text = _agent.LastOrderFlash ?? "Заказ отправлен";
            RenderShop();
            BindProfile();
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Бар", ex.Message);
        }
    }

    private async Task OrderProduct(ClientBarProductDto product)
    {
        AddToCart(product);
        await Task.CompletedTask;
    }


    private async Task ApplyLoginBackground()
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(_serverUrl.TrimEnd('/') + "/") };
            var json = await http.GetStringAsync("api/client/branding").ConfigureAwait(false);
            string? url = null;
            using (var doc = JsonDocument.Parse(json))
            {
                JsonElement data;
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out data) || root.TryGetProperty("Data", out data))
                {
                    if (data.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                    {
                        if (data.TryGetProperty("loginBackgroundUrl", out var u) ||
                            data.TryGetProperty("LoginBackgroundUrl", out u))
                            url = u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                    }
                }
            }

            await Dispatcher.InvokeAsync(() => ApplyLoginBackgroundResult(url));
        }
        catch
        {
            await Dispatcher.InvokeAsync(() => ApplyLoginBackgroundResult(null));
        }
    }

    private void ApplyLoginBackgroundResult(string? url)
    {
        var uri = ResolveProductImageUri(url);
        if (uri is null)
        {
            LoginBackgroundImage.Source = null;
            LoginBackgroundImage.Opacity = 0;
            LoginBackgroundImage.Visibility = Visibility.Collapsed;
            LoginFallbackBg.Opacity = 1;
            return;
        }

        try
        {
            LoginBackgroundImage.Source = ShellImageCache.Get(uri);
            LoginBackgroundImage.Opacity = 1;
            LoginBackgroundImage.Visibility = Visibility.Visible;
            LoginFallbackBg.Opacity = 0.35;
        }
        catch
        {
            LoginBackgroundImage.Source = null;
            LoginBackgroundImage.Opacity = 0;
            LoginBackgroundImage.Visibility = Visibility.Collapsed;
            LoginFallbackBg.Opacity = 1;
        }
    }
    private Uri? ResolveProductImageUri(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;
        var raw = imageUrl.Trim();
        if (Uri.TryCreate(raw, UriKind.Absolute, out var abs))
            return abs;
        if (!raw.StartsWith('/'))
            raw = "/" + raw;
        if (Uri.TryCreate($"{_serverUrl.TrimEnd('/')}{raw}", UriKind.Absolute, out var rel))
            return rel;
        return null;
    }

    private void BuildShopCategories()
    {
        ShopCategoryPanel.Children.Clear();
        void AddCat(string label, string id)
        {
            var selected = _shopCategoryId == id;
            var btn = new Button
            {
                Content = label,
                Style = (Style)FindResource(selected ? "PillButtonActive" : "PillButton"),
                Tag = id
            };
            btn.Click += (_, _) =>
            {
                _shopCategoryId = id;
                RenderShop();
            };
            ShopCategoryPanel.Children.Add(btn);
        }

        AddCat("Все товары", "");
        foreach (var c in _agent.BarCategories)
            AddCat(c.Name, c.Id.ToString());
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            _search = box.Text.Trim();
            UpdateSearchPlaceholder(box);
        }
        if (_tab == "games")
            RenderApps();
        if (_tab == "shop")
            RenderShop();
    }

    private void UpdateSearchPlaceholder(TextBox box)
    {
        if (ReferenceEquals(box, SearchBoxGames) && SearchPlaceholderGames is not null)
            SearchPlaceholderGames.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (ReferenceEquals(box, SearchBoxShop) && SearchPlaceholderShop is not null)
            SearchPlaceholderShop.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BindSession(SessionDto session)
    {
        var rem = session.RemainingSeconds
                  ?? (session.PlannedEndsAt is DateTimeOffset ends
                      ? Math.Max(0, (int)(ends - DateTimeOffset.UtcNow).TotalSeconds)
                      : 0);

        var isNew = _boundSessionId != session.Id;
        if (isNew || rem > _localRemaining || rem < _localRemaining - 30)
        {
            if (isNew)
            {
                _sessionEndWarned.Clear();
                _sessionPeakRemaining = rem;
            }
            else if (rem > _localRemaining + 5)
            {
                // Продление: снова предупреждать на 7/5/3/1, если снова пройдём эти отметки.
                _sessionEndWarned.RemoveWhere(m => m * 60 < rem);
                _sessionPeakRemaining = Math.Max(_sessionPeakRemaining, rem);
            }

            _boundSessionId = session.Id;
            _localRemaining = rem;
            _sessionTotalSeconds = Math.Max(rem, Math.Max(1, session.DurationMinutes * 60));
            _sessionPeakRemaining = Math.Max(_sessionPeakRemaining, rem);
        }

        if (isNew)
            ScheduleNvidiaContainerBounceAfterSessionStart();

        UpdateSessionTimerDisplay();
        CheckLocalSessionEndWarnings();
    }

    private DateTime _lastSessionNvBounceUtc = DateTime.MinValue;

    /// <summary>
    /// После старта сеанса: SYSTEM-watchdog убивает NVDisplay.Container.exe и поднимает службу.
    /// На CCBoot CPL иначе часто «мёртвый», пока не перезапустить процесс вручную.
    /// </summary>
    private void ScheduleNvidiaContainerBounceAfterSessionStart()
    {
        if (DateTime.UtcNow - _lastSessionNvBounceUtc < TimeSpan.FromSeconds(45))
            return;
        _lastSessionNvBounceUtc = DateTime.UtcNow;

        _ = Task.Run(() =>
        {
            try
            {
                // Небольшая пауза: дать сессии/GPU PnP устояться.
                Thread.Sleep(2500);
                NvidiaControlLauncher.EnsureDisplayContainerReady();
            }
            catch { /* ignore */ }
        });
    }

    private void RefreshSessionTimer()
    {
        var session = _agent.Session;
        if (session is null || PanelShell.Visibility != Visibility.Visible)
            return;
        if (session.Status != SessionStatus.Paused && _localRemaining > 0)
            _localRemaining--;
        UpdateSessionTimerDisplay();
        CheckLocalSessionEndWarnings();
    }

    private void CheckLocalSessionEndWarnings()
    {
        if (_agent.Session is null || _agent.Session.Status == SessionStatus.Paused)
            return;
        foreach (var m in SessionEndWarnMarks)
        {
            var threshold = m * 60;
            // Только если сеанс когда-то был длиннее этой отметки (не спамить при коротком старте).
            if (_sessionPeakRemaining > threshold && _localRemaining <= threshold)
                RaiseSessionEndWarning(m);
        }
    }

    private void RaiseSessionEndWarning(int minutesLeft)
    {
        if (minutesLeft is not (7 or 5 or 3 or 1))
            return;
        if (!_sessionEndWarned.Add(minutesLeft))
            return;

        // Голосовое предупреждение — не сворачиваем игру и не показываем оверлей.
        SessionEndWarningWindow.Show(minutesLeft, _agent.GetLaunchedPids());
    }

    private void UpdateSidebarRemainingTime()
    {
        var session = _agent.Session;
        if (session is null)
        {
            SidebarTimeLabel.Text = "ОСТАЛОСЬ";
            SidebarTimeText.Text = "—";
            SidebarTimeBar.Value = 0;
            SidebarTimeRing.StrokeDashArray = new DoubleCollection { 0.01, 49 };
            SidebarTimeHint.Text = "Нет активного сеанса";
            return;
        }

        var rem = Math.Max(0, _localRemaining);
        var h = rem / 3600;
        var m = (rem % 3600) / 60;
        var s = rem % 60;
        SidebarTimeLabel.Text = session.Status == SessionStatus.Paused ? "НА ПАУЗЕ" : "ОСТАЛОСЬ";
        SidebarTimeText.Text = h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";

        var total = _sessionTotalSeconds is > 0 ? _sessionTotalSeconds.Value : Math.Max(rem, 1);
        var remainFrac = Math.Clamp((double)rem / total, 0, 1);
        SidebarTimeBar.Maximum = 100;
        SidebarTimeBar.Value = remainFrac * 100;
        // Circumference in stroke-thickness units for ·156 (176 - 10*2)
        const double ringUnits = 49.0; // · p * 156 / 10
        var dash = Math.Max(0.01, ringUnits * remainFrac);
        SidebarTimeRing.StrokeDashArray = new DoubleCollection { dash, ringUnits };
        SidebarTimeHint.Text = session.TariffName is { Length: > 0 } t
            ? $"{t} · {ShellUiText.Payment(session.PaymentMethod)}"
            : "Активный сеанс";
    }

    private void UpdateSessionTimerDisplay()
    {
        var rem = Math.Max(0, _localRemaining);
        UpdateSidebarRemainingTime();
        var h = rem / 3600;
        var m = rem % 3600 / 60;
        var s = rem % 60;
        RemainingText.Text = h > 0 ? $"{h}ч {m:00}м" : $"{m} мин";
        _ = s;
    }

    private void RenderNews()
    {
        NewsPanel.Children.Clear();
        var items = _agent.News;
        NewsEmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var n in items)
        {
            var catColor = n.Category?.ToLowerInvariant() switch
            {
                "promo" => Color.FromRgb(0xFF, 0x6A, 0x00),
                "event" => Color.FromRgb(0x3D, 0xDC, 0x97),
                "maintenance" => Color.FromRgb(0xFF, 0x4D, 0x4D),
                _ => Color.FromRgb(0xFF, 0x8A, 0x2B)
            };

            var label = n.IsPinned ? "Закреплено" : ShellUiText.NewsCategory(n.Category);
            var content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(catColor)
                    },
                    new TextBlock
                    {
                        Text = n.Title,
                        FontWeight = FontWeights.Bold,
                        FontSize = 16,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    },
                    new TextBlock
                    {
                        Text = n.Body,
                        Foreground = (Brush)FindResource("Muted"),
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 6, 0, 0)
                    }
                }
            };

            var chevron = new TextBlock
            {
                Text = "›",
                FontSize = 28,
                Foreground = (Brush)FindResource("Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            DockPanel.SetDock(chevron, Dock.Right);

            var row = new DockPanel();
            row.Children.Add(chevron);
            row.Children.Add(content);

            NewsPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1A, 0x1A, 0x1D)),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 12),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 4,
                    Opacity = 0.3,
                    Color = Colors.Black,
                    Direction = 270
                },
                Child = row
            });
        }
    }

    private void HelpMessageBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var len = HelpMessageBox?.Text?.Length ?? 0;
        if (HelpCharCount is not null)
            HelpCharCount.Text = $"{len} / 500";
        if (HelpPlaceholder is not null)
            HelpPlaceholder.Visibility = len == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool _pinVisible;

    private void TogglePinVisibility_Click(object sender, RoutedEventArgs e)
    {
        _pinVisible = !_pinVisible;
        if (_pinVisible)
        {
            PinBoxClear.Text = PinBox.Password;
            PinBox.Visibility = Visibility.Collapsed;
            PinBoxClear.Visibility = Visibility.Visible;
            PinEyeIcon.Text = "\uE7B5";
            PinBoxClear.Focus();
        }
        else
        {
            PinBox.Password = PinBoxClear.Text ?? "";
            PinBoxClear.Visibility = Visibility.Collapsed;
            PinBox.Visibility = Visibility.Visible;
            PinEyeIcon.Text = "\uE7B3";
            PinBox.Focus();
        }
    }

    private string GetLoginPin()
        => _pinVisible ? (PinBoxClear.Text ?? "") : PinBox.Password;

    private void ClearLoginCredentials()
    {
        LoginBox.Text = "";
        if (PinBox is not null)
            PinBox.Password = "";
        if (PinBoxClear is not null)
            PinBoxClear.Text = "";
        LoginError.Text = "";
        if (UnlockPinBox is not null)
            UnlockPinBox.Password = "";
        if (UnlockError is not null)
            UnlockError.Text = "";
    }

    /// <summary>Полный сброс экрана входа после выхода из аккаунта / чужого сеанса.</summary>
    private async Task ResetLoginScreenAfterLogoutAsync()
    {
        ClearLoginCredentials();
        ClearTelegramRegistrationForm();
        _cart.Clear();
        if (ShopFlash is not null)
            ShopFlash.Text = "";
        if (HelpMessageBox is not null && HelpMessageBox.Tag as string != "placeholder")
        {
            HelpMessageBox.Text = "";
            HelpMessageBox.Tag = "placeholder";
            if (HelpPlaceholder is not null)
                HelpPlaceholder.Visibility = Visibility.Visible;
            if (HelpCharCount is not null)
                HelpCharCount.Text = "0 / 500";
        }
        await CancelTelegramQrAsync(cancelServer: true);
        ApplyMode();
        if (PanelLogin.Visibility == Visibility.Visible)
        {
            EnsureLoginTelegramQr();
            LoginBox.Focus();
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        LoginError.Text = "";
        try
        {
            await _agent.LoginCustomerAsync(LoginBox.Text.Trim(), GetLoginPin());
            ClearLoginCredentials();
            HideTelegramQrUi();
            _accountProfileBound = false;
            _comfortUiBound = false;
            _tab = "home";
            _accountSub = "overview";
        }
        catch (Exception ex)
        {
            LoginError.Text = ShellUiText.GuestError(ex.Message);
        }
    }

    private void EnsureLoginTelegramQr()
    {
        if (_telegramTicketId is not null && _telegramQrOnLogin)
            return;
        // Steam-like: не автообновлять, пока висит «Обновить QR» или форма регистрации
        if (TgQrExpiredOverlay?.Visibility == Visibility.Visible)
            return;
        if (TgRegisterPanel?.Visibility == Visibility.Visible)
            return;
        _ = StartTelegramQrAsync("Login", sessionId: null, onLoginPanel: true);
    }

    private async void TelegramLogin_Click(object sender, RoutedEventArgs e)
    {
        LoginError.Text = "";
        try
        {
            await StartTelegramQrAsync("Login", sessionId: null, onLoginPanel: true);
        }
        catch (Exception ex)
        {
            LoginError.Text = ShellUiText.GuestError(ex.Message);
            if (TgQrHint is not null)
                TgQrHint.Text = ShellUiText.GuestError(ex.Message);
            ShowLoginQrExpired(ShellUiText.GuestError(ex.Message));
        }
    }

    private void TgRegLinkExisting_Changed(object sender, RoutedEventArgs e)
    {
        if (TgRegSubmitButton is null)
            return;
        TgRegSubmitButton.Content = TgRegLinkExisting.IsChecked == true
            ? "Привязать и войти"
            : "Создать и войти";
    }

    private async void TgRegisterCancel_Click(object sender, RoutedEventArgs e)
    {
        ClearTelegramRegistrationForm();
        TgRegError.Text = "";
        try
        {
            await StartTelegramQrAsync("Login", sessionId: null, onLoginPanel: true);
        }
        catch (Exception ex)
        {
            ShowLoginQrExpired(ShellUiText.GuestError(ex.Message));
        }
    }

    private async void TgRegisterComplete_Click(object sender, RoutedEventArgs e)
    {
        if (_telegramTicketId is not Guid ticketId)
        {
            TgRegError.Text = "Сначала отсканируйте QR в Telegram.";
            return;
        }

        var first = TgRegFirst.Text.Trim();
        var last = TgRegLast.Text.Trim();
        var phoneDigits = new string(TgRegPhone.Text.Where(char.IsDigit).ToArray());
        var password = TgRegPass.Password ?? "";

        if (first.Length < 1)
        {
            TgRegError.Text = "Укажите имя.";
            TgRegFirst.Focus();
            return;
        }
        if (last.Length < 1)
        {
            TgRegError.Text = "Укажите фамилию.";
            TgRegLast.Focus();
            return;
        }
        if (phoneDigits.Length < 10)
        {
            TgRegError.Text = "Телефон: не менее 10 цифр.";
            TgRegPhone.Focus();
            return;
        }
        if (password.Trim().Length < 4)
        {
            TgRegError.Text = "Пароль: минимум 4 символа.";
            TgRegPass.Focus();
            return;
        }

        TgRegError.Text = "";
        TgRegSubmitButton.IsEnabled = false;
        try
        {
            var status = await _agent.CompleteTelegramRegistrationAsync(
                ticketId,
                new ClientTelegramCompleteRegistrationRequest(
                    first,
                    last,
                    TgRegPhone.Text.Trim(),
                    password,
                    Iin: null,
                    LinkExisting: TgRegLinkExisting.IsChecked == true));

            _telegramTicketId = null;
            try { _telegramPollCts?.Cancel(); } catch { /* ignore */ }
            ClearTelegramRegistrationForm();
            ShowLoginQrActive();
            TgRegisterPanel.Visibility = Visibility.Collapsed;

            if (status.Auth is not null)
            {
                _accountProfileBound = false;
                _comfortUiBound = false;
                _tab = "home";
                _accountSub = "overview";
                ApplyMode();
                if (!string.IsNullOrWhiteSpace(status.Message))
                    await ShowInfoAsync("Добро пожаловать", status.Message);
            }
            else
            {
                ShowLoginQrExpired(status.Message ?? "Аккаунт создан. Обновите QR или войдите слева.");
            }
        }
        catch (Exception ex)
        {
            TgRegError.Text = ShellUiText.GuestError(ex.Message);
        }
        finally
        {
            if (TgRegSubmitButton is not null)
                TgRegSubmitButton.IsEnabled = true;
        }
    }

    private void ClearTelegramRegistrationForm()
    {
        TgRegFirst.Text = "";
        TgRegLast.Text = "";
        TgRegPhone.Text = "";
        TgRegPass.Password = "";
        TgRegLinkExisting.IsChecked = false;
        TgRegError.Text = "";
        if (TgRegSubmitButton is not null)
            TgRegSubmitButton.Content = "Создать и войти";
    }

    private async void BindAccountTelegram_Click(object sender, RoutedEventArgs e)
    {
        if (_agent.Session is null || _agent.Customer is not null)
        {
            await ShowInfoAsync("Привязка", "Доступно только для гостевого сеанса без аккаунта.");
            return;
        }

        try
        {
            await StartTelegramQrAsync("BindSession", _agent.Session.Id, onLoginPanel: false);
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Ошибка", ShellUiText.GuestError(ex.Message));
        }
    }

    private async void AccountTelegramLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await StartTelegramAccountQrAsync(link: true);
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Telegram", ShellUiText.GuestError(ex.Message));
        }
    }

    private async void AccountTelegramChange_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await StartTelegramAccountQrAsync(link: false);
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Telegram", ShellUiText.GuestError(ex.Message));
        }
    }

    private async Task StartTelegramAccountQrAsync(bool link)
    {
        await CancelTelegramQrAsync(cancelServer: true);
        var ticket = link
            ? await _agent.StartTelegramLinkAsync()
            : await _agent.StartTelegramChangeAsync();
        _telegramTicketId = ticket.TicketId;
        _telegramQrOnLogin = false;
        _telegramOverlayPurpose = link ? "LinkAccount" : "ChangeTelegram";

        var bmp = DecodeQrPng(ticket.QrPngBase64);
        var action = link ? "привязки" : "смены";
        var hint =
            $"Код {ticket.Code} · до {ticket.ExpiresAt.ToLocalTime():HH:mm}\n" +
            $"Откройте SHIFT на телефоне и подтвердите {action}.";

        TelegramQrTitle.Text = link ? "Привязка Telegram" : "Смена Telegram";
        ShowBindQrActive(bmp, hint);
        TgQrPanel.Visibility = Visibility.Collapsed;

        _telegramPollCts = new CancellationTokenSource();
        _ = PollTelegramTicketLoopAsync(ticket.TicketId, _telegramPollCts.Token);
    }

    private async void BindQrReload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var purpose = _telegramOverlayPurpose;
            if (string.Equals(purpose, "BindSession", StringComparison.OrdinalIgnoreCase)
                && _agent.Session is not null)
            {
                await StartTelegramQrAsync("BindSession", _agent.Session.Id, onLoginPanel: false);
            }
            else if (string.Equals(purpose, "LinkAccount", StringComparison.OrdinalIgnoreCase))
            {
                await StartTelegramAccountQrAsync(link: true);
            }
            else if (string.Equals(purpose, "ChangeTelegram", StringComparison.OrdinalIgnoreCase))
            {
                await StartTelegramAccountQrAsync(link: false);
            }
            else
            {
                await CancelTelegramQrAsync();
            }
        }
        catch (Exception ex)
        {
            ShowBindQrExpired(ShellUiText.GuestError(ex.Message));
        }
    }

    private async void CancelTelegramQr_Click(object sender, RoutedEventArgs e)
    {
        await CancelTelegramQrAsync();
    }

    private async Task StartTelegramQrAsync(string purpose, Guid? sessionId, bool onLoginPanel)
    {
        await CancelTelegramQrAsync(cancelServer: true);

        var ticket = await _agent.StartTelegramTicketAsync(purpose, sessionId);
        _telegramTicketId = ticket.TicketId;
        _telegramQrOnLogin = onLoginPanel;
        if (!onLoginPanel)
            _telegramOverlayPurpose = purpose;

        var bmp = DecodeQrPng(ticket.QrPngBase64);
        var hint =
            $"Код {ticket.Code} · до {ticket.ExpiresAt.ToLocalTime():HH:mm}\n" +
            "Камера телефона или «Открыть SHIFT» в боте.\n" +
            "Нет аккаунта? После скана — форма на телефоне или на экране входа.";

        if (onLoginPanel)
        {
            ClearTelegramRegistrationForm();
            ShowLoginQrActive(bmp, hint);
            PanelTelegramQr.Visibility = Visibility.Collapsed;
            TgRegisterPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            TelegramQrTitle.Text = purpose.Equals("BindSession", StringComparison.OrdinalIgnoreCase)
                ? "Сохранить сеанс в аккаунт"
                : "Вход через Telegram";
            if (purpose.Equals("BindSession", StringComparison.OrdinalIgnoreCase))
            {
                hint =
                    $"Код {ticket.Code} · до {ticket.ExpiresAt.ToLocalTime():HH:mm}\n" +
                    "Сканируйте в SHIFT. Нет аккаунта — создайте в телефоне, сеанс сохранится сам.";
            }
            ShowBindQrActive(bmp, hint);
        }

        _telegramPollCts = new CancellationTokenSource();
        _ = PollTelegramTicketLoopAsync(ticket.TicketId, _telegramPollCts.Token);
    }

    private void ShowBindQrActive(BitmapImage? bmp, string hint)
    {
        if (bmp is not null)
            BindQrImage.Source = bmp;
        BindQrImage.Opacity = 1;
        if (BindQrExpiredOverlay is not null)
            BindQrExpiredOverlay.Visibility = Visibility.Collapsed;
        BindQrHint.Text = hint;
        PanelTelegramQr.Visibility = Visibility.Visible;
    }

    private void ShowBindQrExpired(string message)
    {
        BindQrImage.Source = null;
        BindQrImage.Opacity = 0;
        if (BindQrExpiredOverlay is not null)
            BindQrExpiredOverlay.Visibility = Visibility.Visible;
        BindQrHint.Text = message;
        PanelTelegramQr.Visibility = Visibility.Visible;
        _telegramTicketId = null;
    }

    private void ShowLoginQrActive(BitmapImage? bmp = null, string? hint = null)
    {
        if (bmp is not null)
            TgQrImage.Source = bmp;
        TgQrFrame.Visibility = Visibility.Visible;
        TgQrExpiredOverlay.Visibility = Visibility.Collapsed;
        TgQrImage.Opacity = 1;
        TgQrPanel.Visibility = Visibility.Visible;
        if (hint is not null)
            TgQrHint.Text = hint;
        TelegramLoginButton.Visibility = Visibility.Visible;
        if (TgHowToPanel is not null)
            TgHowToPanel.Visibility = Visibility.Visible;
        if (TgColumnTitle is not null)
            TgColumnTitle.Text = "Вход через Telegram";
        if (TgColumnSub is not null)
            TgColumnSub.Text =
                "Сканируйте QR в приложении SHIFT. Если аккаунта нет — создадите его на телефоне или здесь, вход на этот ПК будет сразу.";
        if (TgRegisterPanel is not null)
            TgRegisterPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowLoginQrExpired(string message)
    {
        TgQrFrame.Visibility = Visibility.Visible;
        TgQrImage.Source = null;
        TgQrImage.Opacity = 0;
        TgQrExpiredOverlay.Visibility = Visibility.Visible;
        TgQrHint.Text = message;
        TelegramLoginButton.Visibility = Visibility.Collapsed;
        TgRegisterPanel.Visibility = Visibility.Collapsed;
        if (TgHowToPanel is not null)
            TgHowToPanel.Visibility = Visibility.Collapsed;
        _telegramTicketId = null;
    }

    private void ShowLoginRegistration(string? displayName, string? message)
    {
        TgQrExpiredOverlay.Visibility = Visibility.Collapsed;
        TgQrFrame.Visibility = Visibility.Collapsed;
        TgQrPanel.Visibility = Visibility.Collapsed;
        if (TgHowToPanel is not null)
            TgHowToPanel.Visibility = Visibility.Collapsed;
        TgRegisterPanel.Visibility = Visibility.Visible;
        if (TgColumnTitle is not null)
            TgColumnTitle.Text = "Создание аккаунта";
        if (TgColumnSub is not null)
            TgColumnSub.Text = "Telegram уже подтверждён. Осталось заполнить данные — и вы на этом ПК.";
        TelegramLoginButton.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(TgRegFirst.Text))
        {
            var parts = displayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            TgRegFirst.Text = parts.Length > 0 ? parts[0] : "";
            TgRegLast.Text = parts.Length > 1 ? parts[1] : "";
        }
        if (!string.IsNullOrWhiteSpace(message))
            TgRegisterHint.Text = message;
    }

    private async Task PollTelegramTicketLoopAsync(Guid ticketId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                var status = await _agent.PollTelegramTicketAsync(ticketId);

                if (string.Equals(status.Status, "AwaitingRegistration", StringComparison.OrdinalIgnoreCase))
                {
                    var onLogin = _telegramQrOnLogin;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (onLogin)
                            ShowLoginRegistration(status.TelegramDisplayName, status.Message);
                        else if (BindQrHint is not null)
                        {
                            BindQrHint.Text = status.Message
                                ?? "Telegram подтверждён. Создайте аккаунт в приложении SHIFT на телефоне — сеанс сохранится автоматически.";
                            if (BindQrImage is not null)
                                BindQrImage.Opacity = 0.3;
                        }
                    });
                    continue;
                }

                if (string.Equals(status.Status, "Consumed", StringComparison.OrdinalIgnoreCase))
                {
                    if (status.Auth is not null)
                    {
                        var engagement = status.Auth.EngagementMessage ?? status.Message;
                        await await Dispatcher.InvokeAsync(async () =>
                        {
                            _telegramTicketId = null;
                            HideTelegramQrUi();
                            TgRegisterPanel.Visibility = Visibility.Collapsed;
                            _accountProfileBound = false;
                            _comfortUiBound = false;
                            _tab = "home";
                            _accountSub = "overview";
                            ApplyMode();
                            if (!string.IsNullOrWhiteSpace(engagement))
                                await ShowInfoAsync("Добро пожаловать", engagement);
                        });
                        return;
                    }

                    await await Dispatcher.InvokeAsync(async () =>
                    {
                        _telegramTicketId = null;
                        HideTelegramQrUi();
                        TgRegisterPanel.Visibility = Visibility.Collapsed;
                        try { await _agent.RefreshAccountAsync(); } catch { /* ignore */ }
                        _accountProfileBound = false;
                        _comfortUiBound = false;
                        BindAccountOverview();
                        ApplyMode();
                        await ShowInfoAsync("Telegram", status.Message ?? "Готово");
                    });
                    return;
                }

                if (status.Status is "Expired" or "Cancelled")
                {
                    var msg = status.Message ?? (status.Status == "Expired"
                        ? "Срок действия QR истёк"
                        : "Операция отменена");
                    var onLogin = _telegramQrOnLogin;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (onLogin)
                            ShowLoginQrExpired(msg);
                        else if (status.Status == "Expired")
                            ShowBindQrExpired(msg);
                        else
                        {
                            HideTelegramQrUi();
                            _ = ShowInfoAsync("Telegram", msg);
                        }
                    });
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* cancelled */
        }
        catch (Exception ex)
        {
            var onLogin = _telegramQrOnLogin;
            await await Dispatcher.InvokeAsync(async () =>
            {
                if (onLogin)
                {
                    ShowLoginQrExpired(ShellUiText.GuestError(ex.Message));
                    LoginError.Text = ShellUiText.GuestError(ex.Message);
                }
                else
                {
                    ShowBindQrExpired(ShellUiText.GuestError(ex.Message));
                }
            });
        }
    }

    private async Task CancelTelegramQrAsync(bool cancelServer = true)
    {
        var id = _telegramTicketId;
        _telegramTicketId = null;
        try { _telegramPollCts?.Cancel(); } catch { /* ignore */ }
        _telegramPollCts?.Dispose();
        _telegramPollCts = null;
        HideTelegramQrUi();
        if (TgRegisterPanel is not null)
            TgRegisterPanel.Visibility = Visibility.Collapsed;
        if (cancelServer && id is Guid ticketId)
            await _agent.CancelTelegramTicketAsync(ticketId);
    }

    private void HideTelegramQrUi()
    {
        // На экране входа QR-колонка остаётся; чистим только оверлей привязки
        PanelTelegramQr.Visibility = Visibility.Collapsed;
        BindQrImage.Source = null;
        BindQrImage.Opacity = 1;
        BindQrHint.Text = "";
        if (BindQrExpiredOverlay is not null)
            BindQrExpiredOverlay.Visibility = Visibility.Collapsed;
        _telegramOverlayPurpose = null;
    }

    private static BitmapImage? DecodeQrPng(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return null;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private async void EndSession_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_agent.Session is null)
                return;

            var ownAccountSession = _agent.Customer is not null
                                    && _agent.Session.CustomerId == _agent.Customer.CustomerId;

            if (ownAccountSession)
            {
                var choice = await ShowThreeChoiceAsync(
                    "Завершить сеанс?",
                    "Сеанс закроется, вы останетесь в аккаунте.\nСохранить остаток минут в банк этой зоны?",
                    okText: "Сохранить минуты",
                    altText: "Без сохранения",
                    cancelText: "Отмена");
                if (choice == 0)
                    return;
                await _agent.EndSessionAsync(saveRemaining: choice == 1);
            }
            else
            {
                if (!await ShowConfirmAsync(
                        "Завершить сеанс?",
                        "Гостевой сеанс будет закрыт на этом ПК.\nЕсли нажали случайно — отмените и позовите администратора."))
                    return;
                await _agent.EndSessionAsync(saveRemaining: false);
            }

            ShowTab("home");
            BindProfile();
            BindSessionChrome();
            if (_agent.Customer is null && _agent.Session is null)
                await ResetLoginScreenAfterLogoutAsync();
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Ошибка", ex.Message);
        }
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_agent.Customer is null)
                return;

            var hasOwnSession = _agent.Session is not null
                                && _agent.Session.CustomerId == _agent.Customer.CustomerId;
            if (hasOwnSession)
            {
                var choice = await ShowThreeChoiceAsync(
                    "Выйти из аккаунта?",
                    "Сеанс будет завершён, и вы выйдете из аккаунта.\nСохранить остаток минут в банк этой зоны?",
                    okText: "Сохранить и выйти",
                    altText: "Выйти без сохранения",
                    cancelText: "Отмена");
                if (choice == 0)
                    return;
                await _agent.LogoutCustomerAsync(saveRemaining: choice == 1);
            }
            else
            {
                if (!await ShowConfirmAsync("Выйти из аккаунта?", "Выйти из аккаунта на этом ПК?"))
                    return;
                await _agent.LogoutCustomerAsync();
            }

            _accountProfileBound = false;
            _comfortUiBound = false;
            ShowTab("home");
            await ResetLoginScreenAfterLogoutAsync();
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Ошибка", ex.Message);
        }
    }

    private void SyncVoenkomatUi()
    {
        if (VoenkomatOnButton is null || VoenkomatOffButton is null || VoenkomatStatusText is null)
            return;

        var on = _agent.EmergencyAlertsEnabled;
        VoenkomatOnButton.Style = (Style)FindResource(on ? "AccentButton" : "GhostButton");
        VoenkomatOffButton.Style = (Style)FindResource(on ? "GhostButton" : "AccentButton");

        if (on)
        {
            VoenkomatStatusText.Text = "Сейчас: включено — сигнал с кассы покажется поверх игр";
            VoenkomatStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x97));
            VoenkomatStatusPill.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x3D, 0xDC, 0x97));
            VoenkomatStatusPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0x3D, 0xDC, 0x97));
        }
        else
        {
            VoenkomatStatusText.Text = "Сейчас: выключено — сигнал с кассы на этом ПК не покажется";
            VoenkomatStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));
            VoenkomatStatusPill.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0x4D, 0x4D));
            VoenkomatStatusPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0x4D, 0x4D));
        }
    }

    private async void VoenkomatOn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _agent.SetEmergencyAlertsEnabledAsync(true);
            SyncVoenkomatUi();
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Ошибка", ex.Message);
        }
    }

    private async void VoenkomatOff_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await ShowConfirmAsync(
                    "Выключить ВОЕНКОМАТ?",
                    "Сигнал с кассы не будет показываться на этом ПК, пока снова не включите."))
                return;
            await _agent.SetEmergencyAlertsEnabledAsync(false);
            SyncVoenkomatUi();
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Ошибка", ex.Message);
        }
    }

    private void ExtendMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!CanClientUseBalance())
        {
            _ = ShowInfoAsync("Продление", "Продление с баланса доступно для сеанса вашего аккаунта. Кассовый сеанс — через администратора.");
            return;
        }

        RebuildExtendOptionButtons();
        ExtendPopup.Visibility = Visibility.Visible;
    }

    private void CloseExtend_Click(object sender, RoutedEventArgs e) =>
        ExtendPopup.Visibility = Visibility.Collapsed;

    /// <summary>Вариант продления: +30 мин или тариф (пакет/час) с тем же текстом, что в списке тарифов.</summary>
    private sealed record ExtendChoice(int Minutes, Guid? OfferTariffId, string Label);

    private IReadOnlyList<ExtendChoice> BuildExtendChoices()
    {
        var list = new List<ExtendChoice>();
        foreach (var m in new[] { 15, 30, 60 })
            list.Add(new ExtendChoice(m, null, $"+{FormatPackageMinutes(m)}"));

        var session = _agent.Session;
        var zoneId = session?.ZoneId is Guid z && z != Guid.Empty
            ? z
            : _agent.Tariffs.FirstOrDefault(t => t.Id == session?.TariffId)?.ZoneId;

        foreach (var t in _agent.Tariffs
                     .Where(x => x.IsAvailableNow && x.IsActive)
                     .Where(x => x.Kind is TariffKind.Package or TariffKind.Hourly
                                 || x.DurationMode == TariffDurationMode.TimeWindow)
                     .Where(x => zoneId is null || x.ZoneId is null || x.ZoneId == zoneId)
                     .OrderBy(x => x.SortOrder)
                     .ThenBy(x => x.Name))
        {
            int minutes;
            if (t.DurationMode == TariffDurationMode.TimeWindow)
            {
                minutes = t.RemainingMinutesInWindow is > 0
                    ? t.RemainingMinutesInWindow.Value
                    : t.FixedDurationMinutes is > 0 ? t.FixedDurationMinutes.Value : 0;
            }
            else if (t.Kind == TariffKind.Package && t.FixedDurationMinutes is > 0)
                minutes = t.FixedDurationMinutes.Value;
            else if (t.Kind == TariffKind.Hourly)
                minutes = t.MinDurationMinutes is > 0 ? t.MinDurationMinutes.Value : 60;
            else
                continue;

            if (minutes <= 0)
                continue;
            // Почасовое продление — кнопками +15/+30/+60 без акции; пакеты/день/ночь — отдельно.
            if (t.DurationMode != TariffDurationMode.TimeWindow && (minutes == 15 || minutes == 30 || minutes == 60))
                continue;

            list.Add(new ExtendChoice(minutes, t.Id, FormatTariffLabel(t)));
        }

        return list;
    }

    private void RebuildExtendOptionButtons()
    {
        HomeClientExtendPanel.Children.Clear();
        ExtendPopupButtons.Children.Clear();
        if (!CanClientUseBalance() || _agent.Session is null)
            return;

        var choices = BuildExtendChoices();
        var sessionTariff = _agent.Tariffs.FirstOrDefault(t => t.Id == _agent.Session?.TariffId);
        var loyaltyPct = _agent.Customer?.LoyaltyTimeDiscountPercent ?? 0m;
        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            var offerTariff = choice.OfferTariffId is Guid oid
                ? _agent.Tariffs.FirstOrDefault(t => t.Id == oid)
                : null;
            var price = QuoteExtendChoicePrice(sessionTariff, offerTariff, choice.Minutes, loyaltyPct);
            var accent = choice.OfferTariffId is null;
            HomeClientExtendPanel.Children.Add(MakeExtendOptionButton(choice, price, accent, compact: true));
            ExtendPopupButtons.Children.Add(MakeExtendOptionButton(choice, price, accent, compact: false));
        }
    }

    private Button MakeExtendOptionButton(ExtendChoice choice, decimal price, bool accent, bool compact)
    {
        var label = price > 0 ? $"{choice.Label} · {price:0} ₸" : choice.Label;
        var btn = new Button
        {
            Content = label,
            Tag = choice,
            Margin = compact ? new Thickness(0, 0, 8, 8) : new Thickness(0, 0, 0, 8),
            Padding = compact ? new Thickness(14, 10, 14, 10) : new Thickness(18, 13, 18, 13),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Style = (Style)FindResource(accent ? "AccentButton" : "GhostButton")
        };
        btn.Click += ExtendSession_Click;
        return btn;
    }

    private async void ExtendSession_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!CanClientUseBalance())
                throw new InvalidOperationException("Продление с баланса недоступно для этого сеанса. Обратитесь на кассу.");

            ExtendChoice? choice = null;
            if (sender is Button { Tag: ExtendChoice tagged })
                choice = tagged;
            else if (sender is Button { Tag: string s } && int.TryParse(s, out var legacyMin))
                choice = new ExtendChoice(legacyMin, null, $"+{legacyMin} мин");
            else if (sender is Button { Tag: int minutesTag })
                choice = new ExtendChoice(minutesTag, null, $"+{minutesTag} мин");

            if (choice is null || choice.Minutes <= 0)
                throw new InvalidOperationException("Выберите вариант продления.");

            var sessionTariff = _agent.Session is null
                ? null
                : _agent.Tariffs.FirstOrDefault(t => t.Id == _agent.Session.TariffId);
            var offerTariff = choice.OfferTariffId is Guid oid
                ? _agent.Tariffs.FirstOrDefault(t => t.Id == oid)
                : null;

            var loyaltyPct = _agent.Customer?.LoyaltyTimeDiscountPercent ?? 0m;
            var expected = QuoteExtendChoicePrice(sessionTariff, offerTariff, choice.Minutes, loyaltyPct);
            var title = choice.OfferTariffId is null
                ? $"Продлить на {choice.Minutes} мин?"
                : $"Продлить: {choice.Label}?";
            var msg = expected > 0
                ? $"{title}\nС баланса будет списано ≈ {expected:0.##} ₸"
                : title;
            if (!await ShowConfirmAsync("Продление", msg))
                return;

            await _agent.ExtendSessionAsync(choice.Minutes, choice.OfferTariffId);
            await _agent.RefreshAccountAsync();
            ExtendPopup.Visibility = Visibility.Collapsed;
            if (_agent.Session is not null)
                BindSession(_agent.Session);
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Ошибка", ex.Message);
        }
    }

    /// <summary>
    /// Оценка списания: покупка тарифа (2+1/день/ночь — с PromoPercent с API), +N мин — без акции.
    /// </summary>
    private static decimal QuoteExtendChoicePrice(
        TariffDto? sessionTariff,
        TariffDto? offerTariff,
        int minutes,
        decimal loyaltyPercent = 0)
    {
        if (offerTariff is not null)
            return QuoteTariffPrice(offerTariff, Math.Max(1, minutes), loyaltyPercent);

        if (minutes <= 0 || sessionTariff is null) return 0;

        if (sessionTariff is { Kind: TariffKind.Package, FixedPrice: > 0, FixedDurationMinutes: > 0 }
            && sessionTariff.FixedDurationMinutes == minutes
            && TariffHasMarketingPromo(sessionTariff))
            return QuoteTariffPrice(sessionTariff, minutes, loyaltyPercent);

        return QuoteExtendPrice(sessionTariff, minutes, loyaltyPercent);
    }

    private void TaskManager_Click(object sender, RoutedEventArgs e) => OpenTaskManager();

    private void OpenTaskManager()
    {
        if (!HasPlayableSession())
        {
            _ = ShowInfoAsync(
                "Нужен сеанс",
                "Диспетчер задач доступен во время активного сеанса.");
            return;
        }

        TaskManagerError.Text = "";
        PanelTaskManager.Visibility = Visibility.Visible;
        RefreshTaskManagerList();
    }

    private void TaskManagerClose_Click(object sender, RoutedEventArgs e)
    {
        PanelTaskManager.Visibility = Visibility.Collapsed;
        TaskManagerList.ItemsSource = null;
        TaskManagerError.Text = "";
    }

    private void TaskManagerRefresh_Click(object sender, RoutedEventArgs e) => RefreshTaskManagerList();

    private void RefreshTaskManagerList()
    {
        try
        {
            var snap = ProcessInventory.Capture();
            TaskManagerList.ItemsSource = snap.Processes;
            TaskManagerHint.Text = $"{snap.Processes.Count} процессов · {snap.CapturedAt.ToLocalTime():HH:mm:ss}";
            TaskManagerError.Text = "";
        }
        catch (Exception ex)
        {
            TaskManagerError.Text = ex.Message;
        }
    }

    private async void TaskManagerKill_Click(object sender, RoutedEventArgs e)
    {
        if (TaskManagerList.SelectedItem is not ComputerProcessDto proc)
        {
            TaskManagerError.Text = "Выберите процесс";
            return;
        }

        if (!proc.CanKill)
        {
            TaskManagerError.Text = "Этот процесс защищён";
            return;
        }

        var ok = await ShowConfirmAsync("Завершить процесс?", $"{proc.Name} (PID {proc.Pid})");
        if (!ok) return;

        try
        {
            ProcessInventory.Kill(proc.Pid, null);
            RefreshTaskManagerList();
        }
        catch (Exception ex)
        {
            TaskManagerError.Text = ex.Message;
        }
    }

    private void LockUi_Click(object sender, RoutedEventArgs e)
    {
        UnlockPinBox.Password = "";
        UnlockError.Text = "";

        // Гость без аккаунта: сначала задать свой ПИН при блокировке
        if (_agent.Customer is null)
        {
            SetLockPinBox.Password = "";
            SetLockPinConfirmBox.Password = "";
            SetLockPinError.Text = "";
            PanelSetLockPin.Visibility = Visibility.Visible;
            return;
        }

        _localLockPin = null;
        _agent.LockUi();
    }

    private void ConfirmSetLockPin_Click(object sender, RoutedEventArgs e)
    {
        SetLockPinError.Text = "";
        var pin = SetLockPinBox.Password?.Trim() ?? "";
        var confirm = SetLockPinConfirmBox.Password?.Trim() ?? "";
        if (pin.Length is < 4 or > 8 || !pin.All(char.IsDigit))
        {
            SetLockPinError.Text = "ПИН: 4–8 цифр";
            return;
        }
        if (pin != confirm)
        {
            SetLockPinError.Text = "ПИН не совпадает";
            return;
        }

        _localLockPin = pin;
        PanelSetLockPin.Visibility = Visibility.Collapsed;
        _agent.LockUi();
    }

    private void CancelSetLockPin_Click(object sender, RoutedEventArgs e)
    {
        PanelSetLockPin.Visibility = Visibility.Collapsed;
        SetLockPinBox.Password = "";
        SetLockPinConfirmBox.Password = "";
        SetLockPinError.Text = "";
    }

    private async void UnlockUi_Click(object sender, RoutedEventArgs e)
    {
        UnlockError.Text = "";
        try
        {
            var entered = UnlockPinBox.Password?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(entered))
            {
                UnlockError.Text = "Введите ПИН";
                return;
            }

            if (_agent.Customer is not null)
            {
                await _agent.LoginCustomerAsync(_agent.Customer.Phone, entered);
            }
            else
            {
                if (string.IsNullOrEmpty(_localLockPin))
                {
                    UnlockError.Text = "Разблокировка только с кассы или задайте ПИН при блокировке";
                    return;
                }

                if (!string.Equals(entered, _localLockPin, StringComparison.Ordinal))
                {
                    UnlockError.Text = "Неверный ПИН";
                    return;
                }
            }

            _localLockPin = null;
            UnlockPinBox.Password = "";
            _agent.UnlockUi();
        }
        catch (Exception ex)
        {
            UnlockError.Text = ShellUiText.GuestError(ex.Message);
        }
    }

    private void PanelLogin_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Hidden admin unlock for CCBoot superclient / image setup
        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            e.Handled = true;
            LoginAdmin_Click(sender, e);
        }
    }

    private void LoginAdmin_Click(object sender, RoutedEventArgs e)
    {
        AdminLoginError.Text = "";
        AdminPasswordBox.Password = "";
        PanelAdminLogin.Visibility = Visibility.Visible;
        AdminPasswordBox.Focus();
    }

    private void AdminLoginCancel_Click(object sender, RoutedEventArgs e)
    {
        PanelAdminLogin.Visibility = Visibility.Collapsed;
        AdminPasswordBox.Password = "";
        AdminLoginError.Text = "";
    }

    private async void AdminLoginOk_Click(object sender, RoutedEventArgs e)
    {
        AdminLoginError.Text = "";
        var pwd = AdminPasswordBox.Password ?? "";
        if (string.IsNullOrWhiteSpace(pwd))
        {
            AdminLoginError.Text = "Введите пароль";
            return;
        }

        try
        {
            var ok = await _agent.VerifyAdminUnlockAsync(pwd);
            if (!ok)
            {
                AdminLoginError.Text = "Неверный пароль";
                return;
            }

            PanelAdminLogin.Visibility = Visibility.Collapsed;
            AdminPasswordBox.Password = "";
            EnterAdminModeUi(alreadyFlagged: false);
            _ = _agent.ReportSecurityAlertAsync("Включён админ-режим Shell (локальный пароль)");
        }
        catch (HttpRequestException)
        {
            AdminLoginError.Text = "Нет связи с сервером — проверьте сеть";
        }
        catch (Exception ex)
        {
            AdminLoginError.Text = ShellUiText.GuestError(ex.Message);
        }
    }

    private void EnterAdminModeUi(bool alreadyFlagged)
    {
        _adminMode = true;
        try
        {
            if (!alreadyFlagged)
                ClientAdminMode.Enter();
            else
                ClientAdminMode.Touch();
        }
        catch (Exception ex)
        {
            AdminModeError.Text = $"Не удалось записать admin.mode: {ex.Message}";
        }

        _kiosk.AllowStaffExit = true;
        _lockScreenUi = false;
        _yieldDesktop = false;
        _allowBackgroundForGame = false;
        ShiftClub.Shared.Security.ProcessAccessGuard.UnprotectCurrentProcess();
        WindowsTaskbar.Show();
        Topmost = false;
        ShowInTaskbar = true;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        WindowState = WindowState.Normal;
        Title = "SHIFT · админ-режим";
        ShellDesktopHost.FitToWorkArea(this);

        PanelWaiting.Visibility = Visibility.Collapsed;
        PanelLogin.Visibility = Visibility.Collapsed;
        PanelShell.Visibility = Visibility.Collapsed;
        PanelUiLock.Visibility = Visibility.Collapsed;
        PanelAdminLogin.Visibility = Visibility.Collapsed;
        PanelAdminMode.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(AdminModeError.Text))
            AdminModeError.Text = "";
    }

    private void AdminMinimize_Click(object sender, RoutedEventArgs e)
    {
        ClientAdminMode.Touch();
        WindowsTaskbar.Show();
        Topmost = false;
        ShowInTaskbar = true;
        WindowState = WindowState.Minimized;
    }

    private void AdminCloseShell_Click(object sender, RoutedEventArgs e)
    {
        // Full exit like Senet/iCafe: Shell process gone, desktop free, Keeper stays quiet.
        _adminMode = true;
        _kiosk.AllowStaffExit = true;
        try { ClientAdminMode.Enter(); }
        catch (Exception ex)
        {
            AdminModeError.Text = $"Не удалось записать admin.mode: {ex.Message}";
            return;
        }

        ShiftClub.Shared.Security.ProcessAccessGuard.UnprotectCurrentProcess();
        WindowsTaskbar.Show();
        Close();
    }

    private void AdminExitMode_Click(object sender, RoutedEventArgs e)
    {
        ClientAdminMode.Exit();
        _adminMode = false;
        _kiosk.AllowStaffExit = false;
        AdminModeError.Text = "";
        PanelAdminMode.Visibility = Visibility.Collapsed;
        Title = "SHIFT";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShiftClub.Shared.Security.ProcessAccessGuard.ProtectCurrentProcess();
        ApplyShellChrome(lockScreen: true);
        EnforceKioskFrame();
        ApplyMode();
        Activate();
    }

    private void LoginRestart_Click(object sender, RoutedEventArgs e) =>
        SystemPower.ConfirmAndRestart(this);

    private void LoginShutdown_Click(object sender, RoutedEventArgs e) =>
        SystemPower.ConfirmAndShutdown(this);

    private async void CallAdmin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var msg = HelpMessageBox?.Text;
            await _agent.CallAdminAsync(string.IsNullOrWhiteSpace(msg) ? null : msg.Trim());
            await ShowInfoAsync("Помощь", "Вызов отправлен на кассу.");
        }
        catch (Exception ex)
        {
            await ShowInfoAsync("Помощь", ShellUiText.GuestError(ex.Message));
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_adminMode)
            return;

        if (e.Key == Key.Escape)
        {
            if (PanelAdminLogin.Visibility == Visibility.Visible)
            {
                AdminLoginCancel_Click(sender, e);
                e.Handled = true;
                return;
            }

            _allowBackgroundForGame = false;
            _yieldDesktop = false;
            ExtendPopup.Visibility = Visibility.Collapsed;
            EnforceKioskFrame();
            Activate();
            e.Handled = true;
        }
        else if (e.Key == Key.F12
                 && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                 && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            e.Handled = true;
            // Same gate as Alt+A: shell admin password from panel → Сотрудники.
            AdminLoginError.Text = "";
            AdminPasswordBox.Password = "";
            PanelAdminLogin.Visibility = Visibility.Visible;
            AdminPasswordBox.Focus();
        }
    }

    private Task ShowInfoAsync(string title, string message) =>
        ShowDialogAsync(title, message, confirmOnly: true);

    private Task<bool> ShowConfirmAsync(string title, string message) =>
        ShowDialogAsync(title, message, confirmOnly: false);

    /// <summary>0 = отмена, 1 = OK (сохранить), 2 = Alt (без сохранения).</summary>
    private Task<int> ShowThreeChoiceAsync(
        string title,
        string message,
        string okText,
        string altText,
        string cancelText)
    {
        _dialogTcs?.TrySetResult(false);
        _dialogChoiceTcs?.TrySetResult(0);
        var tcs = new TaskCompletionSource<int>();
        _dialogChoiceTcs = tcs;
        _dialogTcs = null;

        ShellDialogTitle.Text = title;
        ShellDialogMessage.Text = message;
        ShellDialogCancel.Visibility = Visibility.Visible;
        ShellDialogCancel.Content = cancelText;
        ShellDialogAlt.Visibility = Visibility.Visible;
        ShellDialogAlt.Content = altText;
        ShellDialogOk.Content = okText;
        PanelShellDialog.Visibility = Visibility.Visible;
        return tcs.Task;
    }

    private void DismissShellDialog()
    {
        if (PanelShellDialog.Visibility != Visibility.Visible)
            return;
        PanelShellDialog.Visibility = Visibility.Collapsed;
        ShellDialogAlt.Visibility = Visibility.Collapsed;
        _dialogTcs?.TrySetResult(false);
        _dialogTcs = null;
        _dialogChoiceTcs?.TrySetResult(0);
        _dialogChoiceTcs = null;
    }

    private Task<bool> ShowDialogAsync(string title, string message, bool confirmOnly)
    {
        _dialogChoiceTcs?.TrySetResult(0);
        _dialogChoiceTcs = null;
        _dialogTcs?.TrySetResult(false);
        var tcs = new TaskCompletionSource<bool>();
        _dialogTcs = tcs;

        ShellDialogTitle.Text = title;
        ShellDialogMessage.Text = message;
        ShellDialogAlt.Visibility = Visibility.Collapsed;
        ShellDialogCancel.Visibility = confirmOnly ? Visibility.Collapsed : Visibility.Visible;
        ShellDialogOk.Content = confirmOnly ? "Хорошо" : "Да";
        ShellDialogCancel.Content = "Нет";
        PanelShellDialog.Visibility = Visibility.Visible;
        return tcs.Task;
    }

    private void ShellDialogOk_Click(object sender, RoutedEventArgs e)
    {
        PanelShellDialog.Visibility = Visibility.Collapsed;
        ShellDialogAlt.Visibility = Visibility.Collapsed;
        if (_dialogChoiceTcs is not null)
        {
            _dialogChoiceTcs.TrySetResult(1);
            _dialogChoiceTcs = null;
            return;
        }
        _dialogTcs?.TrySetResult(true);
        _dialogTcs = null;
    }

    private void ShellDialogAlt_Click(object sender, RoutedEventArgs e)
    {
        PanelShellDialog.Visibility = Visibility.Collapsed;
        ShellDialogAlt.Visibility = Visibility.Collapsed;
        _dialogChoiceTcs?.TrySetResult(2);
        _dialogChoiceTcs = null;
    }

    private void ShellDialogCancel_Click(object sender, RoutedEventArgs e)
    {
        PanelShellDialog.Visibility = Visibility.Collapsed;
        ShellDialogAlt.Visibility = Visibility.Collapsed;
        if (_dialogChoiceTcs is not null)
        {
            _dialogChoiceTcs.TrySetResult(0);
            _dialogChoiceTcs = null;
            return;
        }
        _dialogTcs?.TrySetResult(false);
        _dialogTcs = null;
    }

    protected override async void OnClosed(EventArgs e)
    {
        _uiTimer.Stop();
        _kiosk.Dispose();
        await _agent.DisposeAsync();
        base.OnClosed(e);
    }
}
