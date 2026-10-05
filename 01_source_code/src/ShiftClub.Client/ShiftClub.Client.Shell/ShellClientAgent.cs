using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using ShiftClub.Shared.Json;
using ShiftClub.Shared.ClientIpc;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.ClientLauncher;
using ShiftClub.Shared.Contracts.Computers;
using ShiftClub.Shared.Contracts.Customers;
using ShiftClub.Shared.Contracts.News;
using ShiftClub.Shared.Contracts.Sessions;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared.Security;
using ShiftClub.Shared.SignalR;

namespace ShiftClub.Client.Shell;

public enum ShellMode
{
    Connecting,
    WaitingApproval,
    Locked,
    LoggedIn,
    InSession,
    Offline
}

public sealed class ShellClientAgent : IAsyncDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _statePath;
    private readonly string _baseUrl;
    private HubConnection? _hub;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private ClientUpdateCoordinator? _updater;
    private DateTimeOffset _lastUpdateCheck = DateTimeOffset.MinValue;
    private DateTimeOffset _lastCatalogLoadUtc = DateTimeOffset.MinValue;
    private readonly object _launchLock = new();
    private readonly List<int> _launchedPids = new();
    private readonly HashSet<string> _launchedProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _launchedExeByPid = new();
    private readonly Dictionary<string, string> _launchedExeByName = new(StringComparer.OrdinalIgnoreCase);
    private int _offlineStreak;
    private DateTimeOffset _lastReconnectAnnounce = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAccountRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset? _idleLoginDeadlineUtc;
    private DateTimeOffset? _postSessionRebootDeadlineUtc;
    private bool _postSessionRebootTriggered;

    /// <summary>После входа без сеанса — 2 минуты на старт, иначе автовыход.</summary>
    public static readonly TimeSpan IdleLoginTimeout = TimeSpan.FromMinutes(2);

    /// <summary>После конца сеанса — столько ждать до перезагрузки ПК (можно успеть докупить время).</summary>
    public static readonly TimeSpan PostSessionRebootGrace = TimeSpan.FromMinutes(7);

    /// <summary>Сколько секунд осталось до автовыхода (null = таймер не активен).</summary>
    public int? IdleLoginSecondsLeft
    {
        get
        {
            if (_idleLoginDeadlineUtc is null || Customer is null)
                return null;
            if (Session is { Status: SessionStatus.Active or SessionStatus.Paused })
                return null;
            var left = (int)Math.Ceiling((_idleLoginDeadlineUtc.Value - DateTimeOffset.UtcNow).TotalSeconds);
            return Math.Max(0, left);
        }
    }

    /// <summary>Идёт окно после сеанса до перезагрузки.</summary>
    public bool IsPostSessionRebootPending =>
        _postSessionRebootDeadlineUtc is not null
        && Session is not { Status: SessionStatus.Active or SessionStatus.Paused };

    /// <summary>Секунд до авто-перезагрузки (null = не в окне после сеанса).</summary>
    public int? PostSessionRebootSecondsLeft
    {
        get
        {
            if (!IsPostSessionRebootPending || _postSessionRebootDeadlineUtc is null)
                return null;
            var left = (int)Math.Ceiling((_postSessionRebootDeadlineUtc.Value - DateTimeOffset.UtcNow).TotalSeconds);
            return Math.Max(0, left);
        }
    }

    public ClientPersistState State { get; private set; } = new();

    /// <summary>Приём экстренных сигналов «ВОЕНКОМАТ» с кассы. По умолчанию включено.</summary>
    public bool EmergencyAlertsEnabled => State.EmergencyAlertsEnabled ?? true;
    public ShellMode Mode { get; private set; } = ShellMode.Connecting;
    public string StatusText { get; private set; } = "Подключение…";
    public string? ComputerName { get; private set; }
    public SessionDto? Session { get; private set; }
    public ClientCustomerAuthDto? Customer { get; private set; }
    public IReadOnlyList<SoftwareAppDto> Apps { get; private set; } = [];
    public IReadOnlyList<TariffDto> Tariffs { get; private set; } = [];
    public IReadOnlyList<ClientBarCategoryDto> BarCategories { get; private set; } = [];
    public IReadOnlyList<ClientBarProductDto> BarProducts { get; private set; } = [];
    public IReadOnlyList<ClientNewsDto> News { get; private set; } = [];
    public IReadOnlyList<CustomerBalanceTransactionDto> AccountTransactions { get; private set; } = [];
    public IReadOnlyList<CustomerTimeBankTransactionDto> AccountTimeBankTransactions { get; private set; } = [];
    public IReadOnlyList<ClientAccountSessionDto> AccountSessions { get; private set; } = [];
    public IReadOnlyList<ClientBarOrderDto> AccountOrders { get; private set; } = [];
    public IReadOnlyList<ClientAccountBookingDto> AccountBookings { get; private set; } = [];
    public string? AccountHistoryError { get; private set; }
    public string? LastWarning { get; private set; }
    public string? LastError { get; private set; }
    public bool IsOnline { get; private set; }
    public bool IsUiLocked { get; private set; }
    public string? LastOrderFlash { get; private set; }
    public ClientBookingHoldDto? BookingHold { get; private set; }
    private DateTimeOffset _lastBookingHoldCheck = DateTimeOffset.MinValue;

    public event Action? Changed;
    public event Action<string>? UiMessageRequested;
    public event Action<int>? SessionEndWarningRequested;
    /// <summary>Экстренное сообщение поверх всех окон/игр (напр. «ВОЕНКОМАТ»).</summary>
    public event Action<string>? EmergencyAlertRequested;
    /// <summary>Открыть встроенный диспетчер задач (локально или командой с кассы).</summary>
    public event Action? TaskManagerRequested;

    public ShellClientAgent(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http.BaseAddress = new Uri(_baseUrl);
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ShiftClub",
            "Client");
        Directory.CreateDirectory(dataDir);
        _statePath = Path.Combine(dataDir, "state.json");

        _updater = new ClientUpdateCoordinator(
            _http,
            () => State.DeviceToken,
            () => Mode == ShellMode.InSession || Session is { Status: SessionStatus.Active },
            msg =>
            {
                StatusText = msg;
                Notify();
            },
            prepareExit: () => PrepareExitForUpdate?.Invoke());
    }

    /// <summary>Kiosk must allow close before update exit.</summary>
    public event Action? PrepareExitForUpdate;

    public void Start()
    {
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_loopCts.Token));
    }

    public async Task LoginCustomerAsync(string phoneOrLogin, string pin)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/auth/login");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Content = JsonContent.Create(new ClientCustomerLoginRequest(phoneOrLogin, pin));
        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientCustomerAuthDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
            throw new InvalidOperationException(payload?.Message ?? "Ошибка входа");

        await ApplyCustomerAuthAsync(payload.Data);
    }

    public async Task<ClientTelegramTicketDto> StartTelegramTicketAsync(string purpose, Guid? sessionId = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/auth/telegram/start");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Content = JsonContent.Create(new ClientTelegramTicketRequest(purpose, sessionId), options: ShellJson.Options);
        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientTelegramTicketDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
            throw new InvalidOperationException(payload?.Message ?? "Не удалось создать QR для Telegram");
        return payload.Data;
    }

    public async Task<ClientTelegramTicketDto> StartTelegramLinkAsync()
    {
        if (Customer is null || string.IsNullOrWhiteSpace(Customer.CustomerToken))
            throw new InvalidOperationException("Войдите в аккаунт");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/auth/telegram/link");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer.CustomerToken);
        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientTelegramTicketDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
            throw new InvalidOperationException(payload?.Message ?? "Не удалось начать привязку Telegram");
        return payload.Data;
    }

    public async Task<ClientTelegramTicketDto> StartTelegramChangeAsync()
    {
        if (Customer is null || string.IsNullOrWhiteSpace(Customer.CustomerToken))
            throw new InvalidOperationException("Войдите в аккаунт");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/auth/telegram/change");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer.CustomerToken);
        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientTelegramTicketDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
            throw new InvalidOperationException(payload?.Message ?? "Не удалось начать смену Telegram");
        return payload.Data;
    }

    public async Task<ClientTelegramTicketStatusDto> PollTelegramTicketAsync(Guid ticketId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/client/auth/telegram/status/{ticketId}");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientTelegramTicketStatusDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
            throw new InvalidOperationException(payload?.Message ?? "Не удалось проверить QR");

        var status = payload.Data;
        if (string.Equals(status.Status, "Consumed", StringComparison.OrdinalIgnoreCase) && status.Auth is not null)
            await ApplyCustomerAuthAsync(status.Auth);

        return status;
    }

    public async Task CancelTelegramTicketAsync(Guid ticketId)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/client/auth/telegram/cancel/{ticketId}");
            req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
            await _http.SendAsync(req);
        }
        catch
        {
            /* ignore */
        }
    }

    public async Task<ClientTelegramTicketStatusDto> CompleteTelegramRegistrationAsync(
        Guid ticketId,
        ClientTelegramCompleteRegistrationRequest request)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/client/auth/telegram/complete-registration/{ticketId}");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Content = JsonContent.Create(request, options: ShellJson.Options);
        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientTelegramTicketStatusDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
            throw new InvalidOperationException(payload?.Message ?? "Не удалось создать аккаунт");

        if (payload.Data.Auth is not null)
            await ApplyCustomerAuthAsync(payload.Data.Auth);
        return payload.Data;
    }

    public async Task UpdateComfortAsync(ClientComfortSettingsRequest request)
    {
        if (Customer is null || string.IsNullOrWhiteSpace(Customer.CustomerToken))
            throw new InvalidOperationException("Войдите в аккаунт");

        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/client/account/comfort");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer.CustomerToken);
        req.Content = JsonContent.Create(request, options: ShellJson.Options);
        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientCustomerAuthDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
            throw new InvalidOperationException(payload?.Message ?? "Не удалось сохранить настройки");

        Customer = payload.Data;
        StatusText = "Настройки комфорта сохранены";
        Notify();
    }

    private async Task ApplyCustomerAuthAsync(ClientCustomerAuthDto auth)
    {
        Customer = auth;
        Mode = Session is { Status: SessionStatus.Active or SessionStatus.Paused } ? ShellMode.InSession : ShellMode.LoggedIn;
        StatusText = $"Вход: {Customer.FullName}";
        if (Session is { Status: SessionStatus.Active or SessionStatus.Paused })
            ClearIdleLoginTimer();
        else
            ArmIdleLoginTimer();
        await LoadCatalogAsync(CancellationToken.None);
        // Если сеанс уже идёт (с кассы с этим клиентом) — подтянуть свежий баланс.
        await RefreshAccountAsync();
        try { await RefreshBookingHoldAsync(CancellationToken.None); } catch { /* ignore */ }
        Notify();
    }

    /// <summary>
    /// Выход из аккаунта. Если есть свой сеанс — завершает его и сохраняет остаток минут в банк зоны.
    /// </summary>
    public async Task LogoutCustomerAsync(string? reason = null, bool saveRemaining = false)
    {
        ClearIdleLoginTimer();

        // Снять серверную привязку «аккаунт ↔ ПК», чтобы можно было войти на другом месте.
        if (Customer is not null && !string.IsNullOrWhiteSpace(Customer.CustomerToken))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/auth/logout");
                req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
                req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer.CustomerToken);
                await _http.SendAsync(req);
            }
            catch
            {
                /* offline — локальный выход всё равно */
            }
        }

        if (Session is { Status: SessionStatus.Active or SessionStatus.Paused } s
            && Customer is not null
            && s.CustomerId == Customer.CustomerId)
        {
            try
            {
                await EndSessionAsync(saveRemaining: saveRemaining);
            }
            catch
            {
                // Даже если API недоступен — локально очищаем; игры не трогаем, экран блокируем.
                ApplySessionEndedLocally(
                    saveRemaining ? "Сеанс завершён · минуты в банке" : "Сеанс завершён");
            }
        }

        Customer = null;
        AccountTransactions = [];
        AccountTimeBankTransactions = [];
        AccountSessions = [];
        AccountOrders = [];
        IsUiLocked = false;
        Mode = Session is { Status: SessionStatus.Active or SessionStatus.Paused }
            ? ShellMode.InSession
            : ShellMode.Locked;
        StatusText = reason
                     ?? (Mode == ShellMode.InSession
                         ? "Сеанс активен (гость с кассы)"
                         : "Вы вышли из аккаунта");
        Notify();
    }

    private void ArmIdleLoginTimer()
    {
        _idleLoginDeadlineUtc = DateTimeOffset.UtcNow.Add(IdleLoginTimeout);
    }

    private void ClearIdleLoginTimer() => _idleLoginDeadlineUtc = null;

    private async Task EnforceIdleLoginTimeoutAsync()
    {
        if (Customer is null)
        {
            ClearIdleLoginTimer();
            return;
        }

        if (Session is { Status: SessionStatus.Active or SessionStatus.Paused })
        {
            ClearIdleLoginTimer();
            return;
        }

        // Владелец брони ждёт старт с кассы — не выкидывать через 2 минуты.
        if (BookingHold is { IsOwner: true })
        {
            ClearIdleLoginTimer();
            return;
        }

        // После сеанса — окно до перезагрузки: аккаунт нужен для продления с баланса/банка.
        if (IsPostSessionRebootPending)
        {
            ClearIdleLoginTimer();
            return;
        }

        // Вошли без сеанса — таймер должен быть
        if (_idleLoginDeadlineUtc is null)
            ArmIdleLoginTimer();

        var deadline = _idleLoginDeadlineUtc;
        if (deadline is null || DateTimeOffset.UtcNow < deadline.Value)
            return;

        await LogoutCustomerAsync("Автовыход: сеанс не начат за 2 минуты");
        UiMessageRequested?.Invoke("Сеанс не был начат за 2 минуты — выполнен выход из аккаунта.");
    }

    public async Task StartBalanceSessionAsync(Guid tariffId, int minutes, bool useTimeBank = false)
    {
        if (Customer is null)
            throw new InvalidOperationException("Сначала войдите в аккаунт");

        const int maxAttempts = 6;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/sessions/start");
            req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
            req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer.CustomerToken);
            req.Content = JsonContent.Create(new ClientStartSessionRequest(
                tariffId,
                minutes,
                Guid.NewGuid().ToString("N"),
                useTimeBank), options: ShellJson.Options);

            var response = await _http.SendAsync(req);
            var payload = await response.Content.ReadFromJsonAsync<ApiResponse<SessionDto>>(ShellJson.Options);
            if (response.IsSuccessStatusCode && payload?.Data is not null)
            {
                Session = payload.Data;
                Mode = ShellMode.InSession;
                StatusText = useTimeBank ? "Сеанс с банка времени" : "Сеанс активен";
                LastEndedTariffId = null;
                ClearIdleLoginTimer();
                ClearPostSessionReboot();
                await LoadCatalogAsync(CancellationToken.None);
                Notify();
                return;
            }

            var message = payload?.Message ?? "Не удалось начать сеанс";
            lastError = new InvalidOperationException(message);
            if (attempt >= maxAttempts
                || !message.Contains("активный сеанс", StringComparison.OrdinalIgnoreCase))
                throw lastError;

            // Сервер ещё закрывает прошлый сеанс (до 10 с) — подождать и повторить.
            try { await RefreshActiveSessionAsync(CancellationToken.None); }
            catch { /* next attempt */ }
            await Task.Delay(TimeSpan.FromSeconds(1.5));
        }

        throw lastError ?? new InvalidOperationException("Не удалось начать сеанс");
    }

    public Process? LaunchApp(SoftwareAppDto app)
    {
        if (Session is not { Status: SessionStatus.Active or SessionStatus.Paused })
            throw new InvalidOperationException("Запуск программ только во время активного сеанса.");

        var path = Environment.ExpandEnvironmentVariables(app.ExePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Файл не найден", path);

        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            Arguments = app.Arguments ?? "",
            WorkingDirectory = string.IsNullOrWhiteSpace(app.WorkingDirectory)
                ? Path.GetDirectoryName(path)!
                : Environment.ExpandEnvironmentVariables(app.WorkingDirectory),
            UseShellExecute = true
        });

        if (proc is null)
            return null;

        var pid = 0;
        try { pid = proc.Id; } catch { /* ignore */ }
        TrackLaunchedProcess(proc, path);
        if (pid <= 0)
            return null;
        try
        {
            return Process.GetProcessById(pid);
        }
        catch
        {
            return null;
        }
    }

    public void KillSessionProcesses()
    {
        List<int> pids;
        List<string> names;
        lock (_launchLock)
        {
            pids = _launchedPids.ToList();
            names = _launchedProcessNames.ToList();
            _launchedPids.Clear();
            _launchedProcessNames.Clear();
            _launchedExeByPid.Clear();
            _launchedExeByName.Clear();
        }

        foreach (var pid in pids)
        {
            try { ProcessInventory.Kill(pid, null); }
            catch { /* already exited / access */ }
        }

        foreach (var name in names)
        {
            try { ProcessInventory.Kill(null, name); }
            catch { /* ignore */ }
        }
    }

    public IReadOnlyList<int> GetLaunchedPids()
    {
        lock (_launchLock)
            return _launchedPids.ToList();
    }

    public bool HasAnyLaunchedProcessAlive()
    {
        List<int> pids;
        lock (_launchLock)
            pids = _launchedPids.ToList();

        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (!p.HasExited)
                    return true;
            }
            catch
            {
                /* gone */
            }
        }

        return false;
    }

    /// <summary>True if any Shell-launched process still has a visible main window.</summary>
    public bool HasVisibleLaunchedProcess()
    {
        List<int> pids;
        lock (_launchLock)
            pids = _launchedPids.ToList();

        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (!p.HasExited && p.MainWindowHandle != IntPtr.Zero)
                    return true;
            }
            catch
            {
                /* gone */
            }
        }

        return false;
    }

    public IReadOnlyList<TaskbarAppItem> GetTaskbarApps()
    {
        List<int> pids;
        Dictionary<int, string> exeByPid;
        Dictionary<string, string> exeByName;
        lock (_launchLock)
        {
            pids = _launchedPids.ToList();
            exeByPid = new Dictionary<int, string>(_launchedExeByPid);
            exeByName = new Dictionary<string, string>(_launchedExeByName, StringComparer.OrdinalIgnoreCase);
        }

        var result = new List<TaskbarAppItem>();
        var seen = new HashSet<int>();

        // Prefer tracked launches from Shell
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited)
                    continue;
                if (p.MainWindowHandle == IntPtr.Zero)
                    continue;
                var title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? p.ProcessName : p.MainWindowTitle;
                exeByPid.TryGetValue(pid, out var exe);
                if (string.IsNullOrWhiteSpace(exe))
                    exeByName.TryGetValue(p.ProcessName, out exe);
                if (string.IsNullOrWhiteSpace(exe))
                {
                    try { exe = p.MainModule?.FileName; } catch { /* access */ }
                }
                result.Add(new TaskbarAppItem(p.Id, title, p.ProcessName, exe));
                seen.Add(p.Id);
            }
            catch
            {
                /* gone */
            }
        }

        // Also surface other visible user windows (so Steam/Epic child windows appear)
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (seen.Contains(p.Id) || p.Id == Environment.ProcessId)
                    continue;
                if (p.MainWindowHandle == IntPtr.Zero)
                    continue;
                var name = p.ProcessName;
                if (IsTaskbarNoise(name))
                    continue;
                var title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? name : p.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title))
                    continue;
                string? exe = null;
                exeByName.TryGetValue(name, out exe);
                if (string.IsNullOrWhiteSpace(exe))
                {
                    try { exe = p.MainModule?.FileName; } catch { /* access */ }
                }
                result.Add(new TaskbarAppItem(p.Id, title, name, exe));
                seen.Add(p.Id);
                if (result.Count >= 12)
                    break;
            }
            catch
            {
                /* ignore */
            }
            finally
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }

        return result;
    }

    private static bool IsTaskbarNoise(string processName) =>
        processName.Equals("ShiftClub.Client.Shell", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("ShiftClub.Client.Keeper", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("SystemSettings", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("dwm", StringComparison.OrdinalIgnoreCase);

    private void TrackLaunchedProcess(Process proc, string? exePath = null)
    {
        try
        {
            var name = proc.ProcessName;
            var pid = proc.Id;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                try { exePath = proc.MainModule?.FileName; } catch { /* access */ }
            }

            lock (_launchLock)
            {
                if (!_launchedPids.Contains(pid))
                    _launchedPids.Add(pid);
                if (!string.IsNullOrWhiteSpace(name))
                    _launchedProcessNames.Add(name);
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    _launchedExeByPid[pid] = exePath;
                    if (!string.IsNullOrWhiteSpace(name))
                        _launchedExeByName[name] = exePath;
                }
            }
        }
        catch
        {
            /* ignore */
        }
        finally
        {
            try { proc.Dispose(); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Конец сеанса: игры не закрываем — экран «сеанс завершён» + таймер до выключения.
    /// Аккаунт сохраняем, если сеанс был с этим клиентом — чтобы сразу продлить с баланса.
    /// </summary>
    private void ApplySessionEndedLocally(string statusText, bool keepCustomer = false)
    {
        var preserveAccount = keepCustomer
                              || (Customer is not null
                                  && Session?.CustomerId is Guid cid
                                  && cid == Customer.CustomerId);

        LastEndedTariffId = Session?.TariffId;
        Session = null;
        if (!preserveAccount)
            Customer = null;
        Mode = ShellMode.Locked;
        IsUiLocked = false;
        LastWarning = null;
        ClearIdleLoginTimer();
        ArmPostSessionReboot(statusText);
    }

    /// <summary>Тариф только что закончившегося сеанса — для быстрого продления с баланса.</summary>
    public Guid? LastEndedTariffId { get; private set; }

    private void ArmPostSessionReboot(string? statusText = null)
    {
        _postSessionRebootDeadlineUtc = DateTimeOffset.UtcNow.Add(PostSessionRebootGrace);
        _postSessionRebootTriggered = false;
        var mins = (int)PostSessionRebootGrace.TotalMinutes;
        StatusText = string.IsNullOrWhiteSpace(statusText)
            ? $"Сеанс завершён · выключение через {mins} мин"
            : $"{statusText} · выключение через {mins} мин";
    }

    public void ClearPostSessionReboot()
    {
        if (_postSessionRebootDeadlineUtc is null && !_postSessionRebootTriggered)
            return;
        _postSessionRebootDeadlineUtc = null;
        _postSessionRebootTriggered = false;
    }

    private void EnforcePostSessionReboot()
    {
        if (_postSessionRebootDeadlineUtc is null)
            return;

        // Новый сеанс (с кассы или с баланса) — окно снято
        if (Session is { Status: SessionStatus.Active or SessionStatus.Paused })
        {
            ClearPostSessionReboot();
            return;
        }

        // Админ-режим CCBoot — не перезагружать образ
        if (ClientAdminMode.IsActive())
            return;

        if (DateTimeOffset.UtcNow < _postSessionRebootDeadlineUtc.Value)
            return;

        if (_postSessionRebootTriggered)
            return;

        _postSessionRebootTriggered = true;
        StatusText = "Выключение…";
        Notify();
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/s /t 5 /c \"SHIFT: сеанс завершён\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            LastError = $"Выключение: {ex.Message}";
            _postSessionRebootTriggered = false;
            Notify();
        }
    }

    public async Task ExtendSessionAsync(int additionalMinutes, Guid? tariffId = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/sessions/extend");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Content = JsonContent.Create(new ClientExtendSessionRequest(
            additionalMinutes,
            Guid.NewGuid().ToString("N"),
            tariffId), options: ShellJson.Options);

        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<SessionDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
            throw new InvalidOperationException(payload?.Message ?? "Не удалось продлить сеанс");

        Session = payload.Data;
        Mode = ShellMode.InSession;
        StatusText = "Сеанс продлён";
        await RefreshAccountAsync();
        Notify();
    }

    public async Task SetEmergencyAlertsEnabledAsync(bool enabled)
    {
        State.EmergencyAlertsEnabled = enabled;
        await SaveStateAsync(CancellationToken.None);
        StatusText = enabled ? "ВОЕНКОМАТ: оповещение включено" : "ВОЕНКОМАТ: оповещение выключено";
        Notify();
    }

    public async Task CallAdminAsync(string? message = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/help");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Content = JsonContent.Create(new ClientHelpRequest(message), options: ShellJson.Options);
        var response = await _http.SendAsync(req);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<ApiResponse>(ShellJson.Options);
            throw new InvalidOperationException(payload?.Message ?? "Не удалось вызвать администратора");
        }
    }

    public async Task ReportSecurityAlertAsync(string reason, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(State.DeviceToken))
            return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/security-alert");
            req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
            req.Content = JsonContent.Create(new ClientSecurityAlertRequest(reason, detail), options: ShellJson.Options);
            await _http.SendAsync(req);
        }
        catch
        {
            /* offline — ignore */
        }
    }

    public void LockUi()
    {
        IsUiLocked = true;
        StatusText = "Экран заблокирован";
        Notify();
    }

    public void UnlockUi()
    {
        IsUiLocked = false;
        StatusText = Session is { Status: SessionStatus.Active or SessionStatus.Paused }
            ? "Сеанс активен"
            : (Customer is null ? "Готов к сеансу" : $"Вход: {Customer.FullName}");
        Notify();
    }

    public async Task RefreshAccountAsync()
    {
        if (Customer is null || string.IsNullOrWhiteSpace(Customer.CustomerToken))
            return;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/client/account");
            req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
            req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer.CustomerToken);
            var response = await _http.SendAsync(req);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Customer = null;
                if (Session is null)
                {
                    Mode = ShellMode.Locked;
                    StatusText = "Вход закрыт (возможно, аккаунт открыт на другом ПК)";
                }
                Notify();
                return;
            }

            if (!response.IsSuccessStatusCode)
                return;

            var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientCustomerAuthDto>>(ShellJson.Options);
            if (payload?.Data is null)
                return;

            var prevBalance = Customer.Balance;
            var prevBank = Customer.CurrentZoneTimeBankMinutes;
            var prevName = Customer.FullName;
            var prevHide = Customer.ComfortHideBalance;
            var prevBright = Customer.ComfortBrightness;
            var prevSound = Customer.ComfortSoundEnabled;
            var prevStreak = Customer.VisitStreakDays;
            var prevBar = Customer.PendingBarRewards;
            Customer = payload.Data;
            _lastAccountRefresh = DateTimeOffset.UtcNow;

            if (prevBalance != Customer.Balance || prevBank != Customer.CurrentZoneTimeBankMinutes)
                StatusText = $"Баланс: {Customer.Balance:0} ₸";

            if (prevBalance != Customer.Balance
                || prevBank != Customer.CurrentZoneTimeBankMinutes
                || !string.Equals(prevName, Customer.FullName, StringComparison.Ordinal)
                || prevHide != Customer.ComfortHideBalance
                || prevBright != Customer.ComfortBrightness
                || prevSound != Customer.ComfortSoundEnabled
                || prevStreak != Customer.VisitStreakDays
                || prevBar != Customer.PendingBarRewards)
                Notify();
        }
        catch
        {
            /* offline — next loop */
        }
    }

    public async Task ChangeCredentialsAsync(string currentSecret, string? newPin = null, string? newPassword = null, string? login = null)
    {
        if (Customer is null || string.IsNullOrWhiteSpace(Customer.CustomerToken))
            throw new InvalidOperationException("Войдите в аккаунт");

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/account/credentials");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer.CustomerToken);
        req.Content = JsonContent.Create(new ClientChangeCredentialsRequest(currentSecret, newPin, newPassword, login), options: ShellJson.Options);
        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientCustomerAuthDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
            throw new InvalidOperationException(payload?.Message ?? "Не удалось сохранить доступ");

        Customer = payload.Data;
        StatusText = "Данные входа обновлены";
        Notify();
    }

    public async Task UpdateProfileAsync(string firstName, string lastName, string? email)
    {
        if (Customer is null || string.IsNullOrWhiteSpace(Customer.CustomerToken))
            throw new InvalidOperationException("Войдите в аккаунт");

        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/client/account/profile");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer.CustomerToken);
        req.Content = JsonContent.Create(new ClientUpdateProfileRequest(firstName, lastName, email), options: ShellJson.Options);
        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientCustomerAuthDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
            throw new InvalidOperationException(payload?.Message ?? "Не удалось сохранить профиль");

        Customer = payload.Data;
        StatusText = "Профиль сохранён";
        Notify();
    }

    public async Task LoadAccountHistoryAsync(int take = 40)
    {
        if (Customer is null || string.IsNullOrWhiteSpace(Customer.CustomerToken))
            return;

        take = Math.Clamp(take, 10, 100);
        AccountHistoryError = null;
        try
        {
            AccountTransactions = await GetAccountListAsync<CustomerBalanceTransactionDto>($"/api/client/account/transactions?take={take}") ?? [];
            AccountTimeBankTransactions = await GetAccountListAsync<CustomerTimeBankTransactionDto>($"/api/client/account/time-bank/transactions?take={take}") ?? [];
            AccountSessions = await GetAccountListAsync<ClientAccountSessionDto>($"/api/client/account/sessions?take={take}") ?? [];
            AccountOrders = await GetAccountListAsync<ClientBarOrderDto>($"/api/client/account/orders?take={take}") ?? [];
            AccountBookings = await GetAccountListAsync<ClientAccountBookingDto>($"/api/client/account/bookings?take={Math.Min(take, 50)}") ?? [];
            Notify();
        }
        catch (Exception ex)
        {
            AccountHistoryError = ex.Message;
            Notify();
        }
    }

    private async Task<IReadOnlyList<T>?> GetAccountListAsync<T>(string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer!.CustomerToken);
        var response = await _http.SendAsync(req);
        if (!response.IsSuccessStatusCode)
            return null;
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<T>>>(ShellJson.Options);
        return payload?.Data;
    }

    public async Task EndSessionAsync(bool saveRemaining = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/sessions/end");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Content = JsonContent.Create(new ClientEndSessionRequest(saveRemaining), options: ShellJson.Options);
        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<SessionDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(payload?.Message ?? "Не удалось завершить сеанс");

        // Игры не закрываем — блокируем экран до продления / нового сеанса с кассы.
        ApplySessionEndedLocally(
            saveRemaining ? "Сеанс завершён · минуты в банке" : "Сеанс завершён");
        Notify();
    }

    public async Task LoadBarCatalogAsync()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/client/bar/catalog");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        var response = await _http.SendAsync(req);
        if (!response.IsSuccessStatusCode)
            return;

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientBarCatalogDto>>(ShellJson.Options);
        BarCategories = payload?.Data?.Categories ?? [];
        BarProducts = payload?.Data?.Products ?? [];
        Notify();
    }

    public async Task PlaceBarOrderAsync(Guid productId, decimal qty, string paymentMode) =>
        await PlaceBarOrderAsync([new ClientPlaceBarOrderItemRequest(productId, qty)], paymentMode);

    public async Task PlaceBarOrderAsync(
        IReadOnlyList<ClientPlaceBarOrderItemRequest> items,
        string paymentMode)
    {
        if (items is null || items.Count == 0)
            throw new InvalidOperationException("Корзина пуста");

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/bar/orders");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        if (Customer is not null)
            req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer.CustomerToken);
        req.Content = JsonContent.Create(new ClientPlaceBarOrderRequest(
            paymentMode,
            Guid.NewGuid().ToString("N"),
            items), options: ShellJson.Options);

        var response = await _http.SendAsync(req);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<ClientBarOrderDto>>(ShellJson.Options);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
            throw new InvalidOperationException(payload?.Message ?? "Не удалось оформить заказ");

        LastOrderFlash = $"Заказ {payload.Data.Number} · {payload.Data.Total:0} ₸ · {ShellUiText.BarOrderStatus(payload.Data.Status)}";
        await LoadBarCatalogAsync();
        Notify();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        State = await LoadStateAsync(ct);
        if (string.IsNullOrWhiteSpace(State.InstallationId))
        {
            State.InstallationId = Guid.NewGuid().ToString("N");
            await SaveStateAsync(ct);
        }

        ComputerName = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(State.DisplayName))
            ComputerName = State.DisplayName;

        // Каждый старт — проверка по MAC (CCboot: образ может быть без локального токена).
        var macClaimedThisRun = false;

        while (!ct.IsCancellationRequested)
        {
            var delaySeconds = 5;
            try
            {
                if (!macClaimedThisRun || string.IsNullOrWhiteSpace(State.DeviceToken))
                {
                    await EnsureRegisteredAsync(ct);
                    macClaimedThisRun = true;
                    delaySeconds = string.IsNullOrWhiteSpace(State.DeviceToken) ? NextOfflineDelaySeconds() : 3;
                }
                else
                {
                    // 1) Heartbeat — главный признак связи. Падение hub/каталога не должно ронять «Онлайн».
                    await SendHeartbeatAsync(ct);
                    MarkOnline();

                    try { await EnsureHubAsync(ct); }
                    catch (Exception hubEx) when (hubEx is not OperationCanceledException)
                    {
                        LastError = $"Нет связи: {hubEx.Message}";
                        AnnounceReconnect("Переподключение…");
                    }

                    try { await PullCommandsAsync(ct); }
                    catch (Exception cmdEx) when (cmdEx is not OperationCanceledException)
                    {
                        LastError = $"Команды: {cmdEx.Message}";
                    }

                    try { await RefreshActiveSessionAsync(ct); }
                    catch (Exception sessEx) when (sessEx is not OperationCanceledException)
                    {
                        LastError = $"Сеанс: {sessEx.Message}";
                    }

                    // Бронь на ПК — обновляем на экране входа / без сеанса
                    if (Session is null
                        && DateTimeOffset.UtcNow - _lastBookingHoldCheck > TimeSpan.FromSeconds(8))
                    {
                        try { await RefreshBookingHoldAsync(ct); }
                        catch { /* next loop */ }
                    }

                    // Баланс / банк времени — часто, чтобы пополнение с кассы сразу видно
                    if (Customer is not null
                        && DateTimeOffset.UtcNow - _lastAccountRefresh > TimeSpan.FromSeconds(3))
                    {
                        try { await RefreshAccountAsync(); }
                        catch { /* next loop */ }
                    }

                    try { await EnforceIdleLoginTimeoutAsync(); }
                    catch { /* ignore */ }

                    try { EnforcePostSessionReboot(); }
                    catch { /* ignore */ }

                    ApplyConnectedModeUi();

                    if (Mode != ShellMode.InSession
                        && DateTimeOffset.UtcNow - _lastUpdateCheck > TimeSpan.FromMinutes(5)
                        && _updater is not null)
                    {
                        _lastUpdateCheck = DateTimeOffset.UtcNow;
                        _ = _updater.CheckAndApplyAsync(force: false, ct);
                    }

                    // Пока ждём старт сеанса / окно до перезагрузки — тик каждую секунду
                    delaySeconds = (Customer is not null
                                    && Session is null
                                    && _idleLoginDeadlineUtc is not null)
                                   || IsPostSessionRebootPending
                        ? 1
                        : 5;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MarkOffline(ex.Message);
                delaySeconds = NextOfflineDelaySeconds();
            }

            NotifyIfChanged();
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
        }
    }

    private void MarkOnline()
    {
        _offlineStreak = 0;
        var wasOffline = !IsOnline;
        IsOnline = true;
        if (LastError is not null && LastError.StartsWith("Нет связи", StringComparison.Ordinal))
            LastError = null;
        if (wasOffline)
            Notify();
    }

    private void MarkOffline(string error)
    {
        IsOnline = false;
        LastError = error;
        _offlineStreak++;

        if (string.IsNullOrWhiteSpace(State.DeviceToken))
        {
            Mode = ShellMode.WaitingApproval;
            StatusText = "Ожидание подтверждения · повтор…";
        }
        else if (Session is { Status: SessionStatus.Active or SessionStatus.Paused })
        {
            // Не сбрасываем сеанс UI при обрыве API — только помечаем офлайн и крутим реконнект.
            Mode = ShellMode.InSession;
            StatusText = "Нет связи · переподключение…";
        }
        else if (Customer is not null)
        {
            Mode = ShellMode.LoggedIn;
            StatusText = "Нет связи · переподключение…";
        }
        else
        {
            Mode = ShellMode.Offline;
            StatusText = "Нет связи · переподключение…";
        }

        AnnounceReconnect(StatusText);
    }

    private int NextOfflineDelaySeconds()
    {
        // Быстрые первые попытки, потом не чаще раза в 15 с — сервер не ддосим.
        return _offlineStreak switch
        {
            <= 1 => 2,
            2 => 3,
            3 => 5,
            4 => 8,
            _ => 15
        };
    }

    private void AnnounceReconnect(string text)
    {
        if (DateTimeOffset.UtcNow - _lastReconnectAnnounce < TimeSpan.FromSeconds(8))
            return;
        _lastReconnectAnnounce = DateTimeOffset.UtcNow;
        StatusText = text;
        Notify();
    }

    private void ApplyConnectedModeUi()
    {
        if (Session is { Status: SessionStatus.Active or SessionStatus.Paused })
        {
            Mode = ShellMode.InSession;
            IsUiLocked = false;
            StatusText = Session.Status == SessionStatus.Paused
                ? "Сеанс на паузе"
                : "Сеанс активен";
            ComputerName = Session.ComputerName ?? ComputerName;
            // Refresh catalog periodically so cover IconPath uploads from panel appear without Shell restart.
            if (Apps.Count == 0 || Tariffs.Count == 0 || BarProducts.Count == 0
                || DateTimeOffset.UtcNow - _lastCatalogLoadUtc > TimeSpan.FromMinutes(3))
                _ = LoadCatalogAsync(CancellationToken.None);
        }
        else if (Customer is not null)
        {
            Mode = ShellMode.LoggedIn;
            StatusText = $"Аккаунт · баланс {Customer.Balance:0} ₸";
        }
        else if (Mode is not ShellMode.WaitingApproval)
        {
            Mode = ShellMode.Locked;
            StatusText = "Войдите или дождитесь запуска с кассы";
        }
    }

    private async Task LoadCatalogAsync(CancellationToken ct)
    {
        try
        {
            using var appsReq = new HttpRequestMessage(HttpMethod.Get, "/api/client/apps");
            appsReq.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
            using var appsResp = await _http.SendAsync(appsReq, ct);
            if (appsResp.IsSuccessStatusCode)
            {
                var appsPayload = await appsResp.Content.ReadFromJsonAsync<ApiResponse<List<SoftwareAppDto>>>(ShellJson.Options, ct);
                // FileExists с API проверяется на машине сервера — у кассы нет D:/F:.
                // Источник истины: локальные диски игрового ПК.
                Apps = (appsPayload?.Data ?? [])
                    .Select(a =>
                    {
                        var path = Environment.ExpandEnvironmentVariables(a.ExePath ?? "");
                        var exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
                        return a with { FileExists = exists };
                    })
                    .ToList();
            }
            else
            {
                LastError = $"Каталог программ: HTTP {(int)appsResp.StatusCode}";
            }

            using var tarReq = new HttpRequestMessage(HttpMethod.Get, "/api/client/tariffs");
            tarReq.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
            using var tarResp = await _http.SendAsync(tarReq, ct);
            if (tarResp.IsSuccessStatusCode)
            {
                var tarPayload = await tarResp.Content.ReadFromJsonAsync<ApiResponse<List<TariffDto>>>(ShellJson.Options, ct);
                Tariffs = tarPayload?.Data ?? [];
            }

            using var barReq = new HttpRequestMessage(HttpMethod.Get, "/api/client/bar/catalog");
            barReq.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
            using var barResp = await _http.SendAsync(barReq, ct);
            if (barResp.IsSuccessStatusCode)
            {
                var barPayload = await barResp.Content.ReadFromJsonAsync<ApiResponse<ClientBarCatalogDto>>(ShellJson.Options, ct);
                BarCategories = barPayload?.Data?.Categories ?? [];
                BarProducts = barPayload?.Data?.Products ?? [];
            }

            using var newsReq = new HttpRequestMessage(HttpMethod.Get, "/api/client/news");
            newsReq.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
            using var newsResp = await _http.SendAsync(newsReq, ct);
            if (newsResp.IsSuccessStatusCode)
            {
                var newsPayload = await newsResp.Content.ReadFromJsonAsync<ApiResponse<List<ClientNewsDto>>>(ShellJson.Options, ct);
                News = newsPayload?.Data ?? [];
            }

            ShellImageCache.Clear();
            _lastCatalogLoadUtc = DateTimeOffset.UtcNow;
            NotifyIfChanged();
        }
        catch
        {
            // ignore
        }
    }

    private async Task EnsureRegisteredAsync(CancellationToken ct)
    {
        Mode = ShellMode.WaitingApproval;
        StatusText = "Проверка ПК по MAC…";
        Notify();

        var mac = GetPrimaryMacAddress();
        if (string.IsNullOrWhiteSpace(mac))
        {
            StatusText = "Нет адреса сетевой карты";
            LastError = "Клиент не видит сетевой адрес. Проверьте сеть.";
            Notify();
            return;
        }

        State.MacAddress = mac;

        // CCBoot: ProgramData\state.json часто общий на образ → один InstallationId на все ПК.
        // Стабильный id от MAC, чтобы места не схлопывались.
        var macInstallId = "mac" + mac.Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        if (!string.Equals(State.InstallationId, macInstallId, StringComparison.OrdinalIgnoreCase))
        {
            State.InstallationId = macInstallId;
            State.DeviceToken = null;
            await SaveStateAsync(ct);
        }

        var request = new RegisterComputerRequest(
            State.InstallationId!,
            Environment.MachineName,
            GetLocalIp(),
            mac,
            ClientVersionInfo.Version,
            RuntimeInformation.OSDescription,
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            null,
            (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)),
            null);

        var response = await _http.PostAsJsonAsync("/api/client/computers/register", request, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<RegisterComputerResponse>>(ShellJson.Options, ct);
        if (payload?.Data is null)
            throw new InvalidOperationException("Пустой ответ регистрации");

        State.ComputerId = payload.Data.ComputerId;
        State.RegistrationCode = null;
        if (!string.IsNullOrWhiteSpace(payload.Data.MacAddress))
            State.MacAddress = payload.Data.MacAddress;

        if (payload.Data is { IsApproved: true, DeviceToken: { Length: > 0 } token })
        {
            State.DeviceToken = token;
            if (!string.IsNullOrWhiteSpace(payload.Data.DisplayName))
            {
                State.DisplayName = payload.Data.DisplayName;
                ComputerName = payload.Data.DisplayName;
            }
            await SaveStateAsync(ct);
            Mode = ShellMode.Locked;
            StatusText = "ПК подтверждён";
            IsOnline = true;
            LastError = null;
            Notify();
            return;
        }

        await SaveStateAsync(ct);
        Mode = ShellMode.WaitingApproval;
        StatusText = $"Ожидает кассу · {State.MacAddress}";
        Notify();
    }

    private static string? GetPrimaryMacAddress()
    {
        try
        {
            var candidates = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .Where(ni => ni.NetworkInterfaceType is not (
                    System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                    or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel))
                .Select(ni =>
                {
                    var bytes = ni.GetPhysicalAddress().GetAddressBytes();
                    if (bytes.Length != 6 || bytes.All(b => b == 0))
                        return null;

                    var name = $"{ni.Name} {ni.Description}";
                    var virtualHint = name.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                                      || name.Contains("vmware", StringComparison.OrdinalIgnoreCase)
                                      || name.Contains("hyper-v", StringComparison.OrdinalIgnoreCase)
                                      || name.Contains("vbox", StringComparison.OrdinalIgnoreCase)
                                      || name.Contains("vpn", StringComparison.OrdinalIgnoreCase)
                                      || name.Contains("tap-", StringComparison.OrdinalIgnoreCase)
                                      || name.Contains("bluetooth", StringComparison.OrdinalIgnoreCase);

                    var score = ni.NetworkInterfaceType switch
                    {
                        System.Net.NetworkInformation.NetworkInterfaceType.Ethernet => 100,
                        System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet => 100,
                        System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 => 80,
                        _ => 40
                    };
                    if (virtualHint) score -= 60;
                    if (ni.GetIPProperties().UnicastAddresses.Any(a =>
                            a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                            && !System.Net.IPAddress.IsLoopback(a.Address)))
                        score += 20;

                    return new
                    {
                        Mac = string.Join(':', bytes.Select(b => b.ToString("X2"))),
                        Score = score
                    };
                })
                .Where(x => x is not null)
                .OrderByDescending(x => x!.Score)
                .FirstOrDefault();

            return candidates?.Mac;
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    private async Task RefreshBookingHoldAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(State.DeviceToken))
            return;

        _lastBookingHoldCheck = DateTimeOffset.UtcNow;
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/client/booking-hold");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        if (Customer is not null)
            req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer.CustomerToken);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return;

        var payload = await resp.Content.ReadFromJsonAsync<ApiResponse<ClientBookingHoldDto?>>(ShellJson.Options, ct);
        BookingHold = payload?.Data;
        if (BookingHold is not null
            && Customer is null
            && Mode is ShellMode.Locked or ShellMode.Offline or ShellMode.Connecting)
        {
            var from = BookingHold.StartsAt.ToOffset(TimeSpan.FromHours(5));
            var to = BookingHold.EndsAt.ToOffset(TimeSpan.FromHours(5));
            StatusText = $"Бронь {BookingHold.Number} · {from:HH:mm}–{to:HH:mm}";
        }
        Notify();
    }

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/computers/heartbeat");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Content = JsonContent.Create(new ComputerHeartbeatRequest(
            null, null, null,
            (long)Environment.TickCount64 / 1000,
            GetLocalIp(),
            ClientVersionInfo.Version,
            true,
            null));

        var response = await _http.SendAsync(req, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            State.DeviceToken = null;
            await SaveStateAsync(ct);
            Mode = ShellMode.WaitingApproval;
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> VerifyAdminUnlockAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(State.DeviceToken))
            await EnsureRegisteredAsync(ct);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/admin/unlock");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Content = JsonContent.Create(new { password });

        var response = await _http.SendAsync(req, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return false;

        response.EnsureSuccessStatusCode();
        return true;
    }

    private async Task EnsureHubAsync(CancellationToken ct)
    {
        if (_hub is { State: HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting })
            return;

        if (_hub is not null)
        {
            try { await _hub.DisposeAsync(); }
            catch { /* ignore */ }
            _hub = null;
        }

        if (string.IsNullOrWhiteSpace(State.DeviceToken))
            return;

        _hub = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}/hubs/computer?device_token={Uri.EscapeDataString(State.DeviceToken)}")
            .WithAutomaticReconnect(new ForeverReconnectPolicy())
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.PayloadSerializerOptions.Converters.Add(new FlexibleJsonEnumConverterFactory());
            })
            .Build();

        _hub.Reconnecting += error =>
        {
            AnnounceReconnect(error is null
                ? "Переподключение…"
                : $"Переподключение… ({error.Message})");
            return Task.CompletedTask;
        };

        _hub.Reconnected += connectionId =>
        {
            IsOnline = true;
            LastError = null;
            if (Session is { Status: SessionStatus.Paused })
                StatusText = "Сеанс на паузе";
            else if (Session is { Status: SessionStatus.Active })
                StatusText = "Сеанс активен";
            else
                StatusText = "Связь восстановлена";
            Notify();
            _ = RecoverAfterHubReconnectAsync();
            return Task.CompletedTask;
        };

        _hub.Closed += async error =>
        {
            if (error is not null)
                AnnounceReconnect($"Связь оборвана · повторяем…");
            // Цикл LoopAsync пересоздаст hub, если авто-реконнект остановится.
            await Task.CompletedTask;
        };

        _hub.On<ComputerCommandDto>(HubMethods.ComputerCommand, async cmd =>
        {
            await ExecuteIncomingCommandAsync(cmd, CancellationToken.None);
        });

        _hub.On<SessionDto>(HubMethods.SessionStarted, async session =>
        {
            Session = session;
            IsUiLocked = false;
            Mode = ShellMode.InSession;
            StatusText = "Сеанс начат";
            LastWarning = null;
            ComputerName = session.ComputerName ?? ComputerName;
            ClearIdleLoginTimer();
            ClearPostSessionReboot();
            await LoadCatalogAsync(CancellationToken.None);
            await RefreshAccountAsync();
            Notify();
        });

        _hub.On<SessionDto>(HubMethods.SessionUpdated, async session =>
        {
            Session = session;
            IsUiLocked = false;
            Mode = ShellMode.InSession;
            ClearPostSessionReboot();
            await RefreshAccountAsync();
            Notify();
        });

        _hub.On<SessionDto>(HubMethods.SessionEnded, session =>
        {
            var keep = Customer is not null
                       && session.CustomerId is Guid cid
                       && cid == Customer.CustomerId;
            ApplySessionEndedLocally("Время сеанса закончилось", keepCustomer: keep);
            Notify();
        });

        _hub.On<SessionWarningEvent>(HubMethods.SessionWarning, warning =>
        {
            var mins = warning.MinutesLeft;
            LastWarning = mins <= 1
                ? "До конца сеанса осталась 1 минута"
                : $"До конца сеанса осталось {mins} мин";
            Notify();
            SessionEndWarningRequested?.Invoke(mins);
        });

        await _hub.StartAsync(ct);
    }

    private async Task RecoverAfterHubReconnectAsync()
    {
        try
        {
            await PullCommandsAsync(CancellationToken.None);
            await RefreshActiveSessionAsync(CancellationToken.None);
        }
        catch
        {
            /* next loop */
        }

        Notify();
    }

    private async Task ExecuteIncomingCommandAsync(ComputerCommandDto cmd, CancellationToken ct)
    {
        try
        {
            // Ack before process-killing actions — otherwise command stays Sent and
            // the next Shell boot re-pulls it (restart storm / stuck queue).
            if (cmd.Type is ComputerCommandType.UpdateClient
                or ComputerCommandType.RestartShell
                or ComputerCommandType.StartShell
                or ComputerCommandType.Restart
                or ComputerCommandType.Shutdown
                or ComputerCommandType.LogOff)
            {
                await AcknowledgeCommandAsync(cmd.Id, ComputerCommandStatus.Completed, null, null, ct);
                await ApplyCommandAsync(cmd, ct);
            }
            else
            {
                var resultJson = await ApplyCommandAsync(cmd, ct);
                await AcknowledgeCommandAsync(cmd.Id, ComputerCommandStatus.Completed, null, resultJson, ct);
            }
        }
        catch (Exception ex)
        {
            await AcknowledgeCommandAsync(cmd.Id, ComputerCommandStatus.Failed, ex.Message, null, ct);
            LastError = ex.Message;
        }

        Notify();
    }

    /// <returns>ResultJson для ответа кассе (список процессов и т.п.).</returns>
    private async Task<string?> ApplyCommandAsync(ComputerCommandDto cmd, CancellationToken ct)
    {
        switch (cmd.Type)
        {
            case ComputerCommandType.Lock:
                IsUiLocked = true;
                StatusText = "ПК заблокирован с кассы";
                LastWarning = null;
                Notify();
                return null;
            case ComputerCommandType.Unlock:
                IsUiLocked = false;
                Mode = Session is { Status: SessionStatus.Active or SessionStatus.Paused }
                    ? ShellMode.InSession
                    : (Customer is null ? ShellMode.Locked : ShellMode.LoggedIn);
                StatusText = Session is null ? "Разблокирован" : "Сеанс активен";
                Notify();
                return null;
            case ComputerCommandType.UpdateClient:
                if (Mode == ShellMode.InSession || Session is { Status: SessionStatus.Active })
                    throw new InvalidOperationException("Нельзя обновлять во время сеанса");
                StatusText = "Обновление клиента…";
                Notify();
                if (_updater is null)
                    throw new InvalidOperationException("Updater не инициализирован");
                await _updater.CheckAndApplyAsync(force: true, ct);
                return null;
            case ComputerCommandType.ShowMessage:
            case ComputerCommandType.PlayNotification:
            {
                var text = ParseMessagePayload(cmd.PayloadJson) ?? "Сообщение от администратора";
                LastWarning = text;
                StatusText = "Сообщение с кассы";
                Notify();
                UiMessageRequested?.Invoke(text);
                return null;
            }
            case ComputerCommandType.EmergencyAlert:
            {
                var text = ParseMessagePayload(cmd.PayloadJson) ?? "ВНИМАНИЕ";
                LastWarning = text;
                if (!EmergencyAlertsEnabled)
                {
                    StatusText = "ВОЕНКОМАТ: сигнал пропущен (выкл. на ПК)";
                    Notify();
                    return null;
                }

                StatusText = "Экстренное сообщение";
                Notify();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    EmergencyAlertRequested?.Invoke(text));
                return null;
            }
            case ComputerCommandType.Shutdown:
                KillSessionProcesses();
                StatusText = "Выключение…";
                Notify();
                Process.Start(new ProcessStartInfo("shutdown", "/s /t 3") { CreateNoWindow = true, UseShellExecute = false });
                return null;
            case ComputerCommandType.Restart:
                KillSessionProcesses();
                StatusText = "Перезагрузка…";
                Notify();
                Process.Start(new ProcessStartInfo("shutdown", "/r /t 3") { CreateNoWindow = true, UseShellExecute = false });
                return null;
            case ComputerCommandType.LogOff:
                KillSessionProcesses();
                StatusText = "Выход из Windows…";
                Notify();
                Process.Start(new ProcessStartInfo("shutdown", "/l") { CreateNoWindow = true, UseShellExecute = false });
                return null;
            case ComputerCommandType.RestartShell:
            case ComputerCommandType.StartShell:
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
                {
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true
                });
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    System.Windows.Application.Current.Shutdown());
                return null;
            case ComputerCommandType.RestartService:
                TryRestartClientService();
                return null;
            case ComputerCommandType.GetDiagnostics:
                return ProcessInventory.CaptureJson();
            case ComputerCommandType.OpenTaskManager:
            {
                var json = ProcessInventory.CaptureJson();
                System.Windows.Application.Current?.Dispatcher.Invoke(() => TaskManagerRequested?.Invoke());
                StatusText = "Диспетчер задач";
                Notify();
                return json;
            }
            case ComputerCommandType.CloseApplication:
            {
                var (pid, name) = ParseKillPayload(cmd.PayloadJson);
                ProcessInventory.Kill(pid, name);
                StatusText = "Процесс завершён";
                Notify();
                return ProcessInventory.CaptureJson();
            }
            case ComputerCommandType.LaunchApplication:
            {
                var path = ParseLaunchPath(cmd.PayloadJson)
                    ?? throw new InvalidOperationException("Нужен payload: { path } или { exePath }");
                path = Environment.ExpandEnvironmentVariables(path);
                if (Session is not { Status: SessionStatus.Active or SessionStatus.Paused })
                    throw new InvalidOperationException("Запуск программ только во время активного сеанса.");
                if (!File.Exists(path))
                    throw new FileNotFoundException("Файл не найден: " + path, path);
                var args = ParseLaunchArgs(cmd.PayloadJson);
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = Path.GetDirectoryName(path)!,
                    UseShellExecute = true
                };
                if (!string.IsNullOrWhiteSpace(args))
                {
                    psi.Arguments = args;
                    psi.UseShellExecute = false;
                }
                var proc = Process.Start(psi);
                if (proc is not null)
                    TrackLaunchedProcess(proc, path);
                StatusText = "Приложение запущено с кассы";
                Notify();
                return null;
            }
            case ComputerCommandType.RunCmd:
            {
                var line = ParseCmdLinePayload(cmd.PayloadJson)
                    ?? throw new InvalidOperationException("Нужен payload: { command } или { cmd }");
                StatusText = "Выполнение CMD…";
                Notify();
                var resultJson = await ExecuteCmdLineAsync(line, ct);
                StatusText = "CMD выполнен";
                Notify();
                return resultJson;
            }
            case ComputerCommandType.ReloadConfiguration:
                await EnsureRegisteredAsync(ct);
                StatusText = "Конфиг перезагружен";
                Notify();
                return null;
            case ComputerCommandType.EnterMaintenance:
                KillSessionProcesses();
                IsUiLocked = true;
                Mode = ShellMode.Locked;
                StatusText = "Техобслуживание";
                Notify();
                return null;
            case ComputerCommandType.EndSession:
                if (Session is null)
                {
                    ApplySessionEndedLocally("Нет активного сеанса");
                    Notify();
                    return null;
                }
                await EndSessionAsync(saveRemaining: false);
                return null;
            case ComputerCommandType.ForceCustomerLogout:
            {
                var msg = "Вход выполнен на другом ПК — вы вышли из аккаунта.";
                try
                {
                    if (!string.IsNullOrWhiteSpace(cmd.PayloadJson))
                    {
                        using var doc = JsonDocument.Parse(cmd.PayloadJson);
                        if (doc.RootElement.TryGetProperty("message", out var m))
                            msg = m.GetString() ?? msg;
                    }
                }
                catch { /* keep default */ }

                // Не трогаем чужой гостевой сеанс с кассы — только аккаунт.
                ClearIdleLoginTimer();
                if (Customer is not null)
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/client/auth/logout");
                        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
                        req.Headers.TryAddWithoutValidation("X-Customer-Token", Customer.CustomerToken);
                        await _http.SendAsync(req, ct);
                    }
                    catch { /* ignore */ }
                }

                Customer = null;
                Mode = Session is { Status: SessionStatus.Active or SessionStatus.Paused }
                    ? ShellMode.InSession
                    : ShellMode.Locked;
                StatusText = msg;
                Notify();
                UiMessageRequested?.Invoke(msg);
                return null;
            }
            default:
                throw new InvalidOperationException($"Команда {cmd.Type} не поддерживается клиентом");
        }
    }

    private static string? ParseLaunchPath(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;
        var trimmed = payloadJson.Trim();
        if (!trimmed.StartsWith('{'))
            return trimmed.Trim('"');
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.TryGetProperty("path", out var p))
                return p.GetString();
            if (doc.RootElement.TryGetProperty("exePath", out var e))
                return e.GetString();
            if (doc.RootElement.TryGetProperty("file", out var f))
                return f.GetString();
        }
        catch (JsonException)
        {
            /* fall through */
        }
        return null;
    }

    private static string? ParseLaunchArgs(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson) || !payloadJson.TrimStart().StartsWith('{'))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("args", out var a))
                return a.GetString();
            if (doc.RootElement.TryGetProperty("arguments", out var b))
                return b.GetString();
        }
        catch (JsonException)
        {
            /* ignore */
        }
        return null;
    }

    private static void TryRestartClientService()
    {
        // Prefer SYSTEM watchdog IPC — Shell often lacks rights for sc.exe stop/start.
        try
        {
            WatchdogRequests.WriteRequest(WatchdogRequests.ReloadService);
            return;
        }
        catch { /* fall through */ }

        try
        {
            Process.Start(new ProcessStartInfo("sc.exe", "stop ShiftClubClient")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(15_000);
            Process.Start(new ProcessStartInfo("sc.exe", "start ShiftClubClient")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch
        {
            /* service may be missing on this seat */
        }
    }

    private static (int? Pid, string? Name) ParseKillPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new InvalidOperationException("Нужен payload: { pid } или { name }");

        using var doc = JsonDocument.Parse(payloadJson);
        int? pid = null;
        if (doc.RootElement.TryGetProperty("pid", out var pidEl) && pidEl.TryGetInt32(out var p))
            pid = p;
        string? name = null;
        if (doc.RootElement.TryGetProperty("name", out var nameEl))
            name = nameEl.GetString();
        if (doc.RootElement.TryGetProperty("processName", out var pnEl))
            name ??= pnEl.GetString();
        return (pid, name);
    }

    private static string? ParseCmdLinePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;
        var trimmed = payloadJson.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("command", out var c))
                    return c.GetString();
                if (doc.RootElement.TryGetProperty("cmd", out var cmdEl))
                    return cmdEl.GetString();
                if (doc.RootElement.TryGetProperty("line", out var line))
                    return line.GetString();
            }
            catch (JsonException)
            {
                /* fall through */
            }
        }
        return trimmed.Trim('"');
    }

    private static async Task<string> ExecuteCmdLineAsync(string commandLine, CancellationToken ct)
    {
        var line = commandLine.Trim();
        if (line.Length == 0)
            throw new InvalidOperationException("Пустая команда");
        if (line.Length > 4000)
            throw new InvalidOperationException("Слишком длинная команда (макс. 4000 символов)");

        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(comSpec) || !File.Exists(comSpec))
            comSpec = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        var psi = new ProcessStartInfo
        {
            FileName = comSpec,
            Arguments = "/d /s /c " + line,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!proc.Start())
            throw new InvalidOperationException("Не удалось запустить cmd.exe");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }
            throw new TimeoutException("Команда не завершилась за 90 секунд");
        }

        var stdout = TruncateCmdOutput(await stdoutTask);
        var stderr = TruncateCmdOutput(await stderrTask);
        var payload = new Dictionary<string, object?>
        {
            ["exitCode"] = proc.ExitCode,
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["command"] = line.Length > 200 ? line[..200] + "…" : line,
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string TruncateCmdOutput(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        const int max = 24_000;
        return text.Length <= max ? text : text[..max] + "\n…(обрезано)";
    }

    private static string? ParseMessagePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;
        var trimmed = payloadJson.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("text", out var text))
                    return text.GetString();
                if (doc.RootElement.TryGetProperty("message", out var message))
                    return message.GetString();
            }
            catch (JsonException)
            {
                /* fall through */
            }
        }
        return trimmed.Trim('"');
    }

    private async Task PullCommandsAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/client/computers/commands/pending");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
            return;

        List<ComputerCommandDto> commands;
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<ApiResponse<List<ComputerCommandDto>>>(ShellJson.Options, ct);
            commands = payload?.Data ?? [];
        }
        catch (JsonException ex)
        {
            // Не роняем весь цикл в Offline из‑за одной битой команды.
            LastError = $"commands JSON: {ex.Message}";
            Notify();
            return;
        }

        foreach (var cmd in commands)
            await ExecuteIncomingCommandAsync(cmd, ct);
    }

    private async Task AcknowledgeCommandAsync(
        Guid commandId,
        ComputerCommandStatus status,
        string? error,
        string? resultJson,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/client/computers/commands/{commandId}/status");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        req.Content = JsonContent.Create(new
        {
            status = status.ToString(),
            errorMessage = error,
            resultJson
        });
        await _http.SendAsync(req, ct);
    }

    private async Task RefreshActiveSessionAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/client/session/current");
        req.Headers.TryAddWithoutValidation("X-Device-Token", State.DeviceToken);
        var response = await _http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode)
            return;

        try
        {
            var hadSession = Session is not null;
            var prevId = Session?.Id;
            var prevRem = Session?.RemainingSeconds;
            var prevCustomerId = Session?.CustomerId;
            var payload = await response.Content.ReadFromJsonAsync<ApiResponse<SessionDto?>>(ShellJson.Options, ct);
            Session = payload?.Data is { Status: SessionStatus.Active or SessionStatus.Paused } s ? s : null;

            if (hadSession && Session is null)
            {
                var keep = Customer is not null
                           && prevCustomerId is Guid cid
                           && cid == Customer.CustomerId;
                // Тот же путь, что SessionEnded: экран пост-сеанса, игры не трогаем.
                ApplySessionEndedLocally("Время сеанса закончилось", keepCustomer: keep);
                Notify();
            }
            else if (Session is not null)
            {
                ClearIdleLoginTimer();
                ClearPostSessionReboot();
                if (Session.Id != prevId || Session.RemainingSeconds != prevRem)
                    Notify();
                else if (Mode != ShellMode.InSession)
                    Notify();
            }
        }
        catch (JsonException ex)
        {
            LastError = $"session JSON: {ex.Message}";
        }
    }

    private void Notify()
    {
        _lastUiSnapshot = null;
        Changed?.Invoke();
    }

    /// <summary>UI refresh only when guest-visible state actually changed (avoids 5s full redraw).</summary>
    private void NotifyIfChanged()
    {
        var snap = CaptureUiSnapshot();
        if (snap == _lastUiSnapshot)
            return;
        _lastUiSnapshot = snap;
        Changed?.Invoke();
    }

    private string? _lastUiSnapshot;

    private string CaptureUiSnapshot() =>
        string.Join('|',
            Mode,
            IsOnline,
            IsUiLocked,
            Session?.Id,
            Session?.Status,
            Session?.RemainingSeconds,
            Customer?.CustomerId,
            Customer?.Balance,
            Customer?.CurrentZoneTimeBankMinutes,
            Customer?.FullName,
            Customer?.ComfortHideBalance,
            Customer?.ComfortBrightness,
            Customer?.ComfortSoundEnabled,
            Customer?.VisitStreakDays,
            Customer?.PendingBarRewards,
            IsPostSessionRebootPending,
            PostSessionRebootSecondsLeft,
            StatusText,
            LastError,
            LastOrderFlash,
            Apps.Count,
            string.Join(',', Apps.Select(a => a.IconPath ?? "")),
            BarProducts.Count,
            string.Join(',', BarProducts.Select(p => p.ImageUrl ?? "")),
            Tariffs.Count,
            News.Count,
            BookingHold?.BookingId,
            State.DeviceToken is null ? "0" : "1");

    private async Task<ClientPersistState> LoadStateAsync(CancellationToken ct)
    {
        if (!File.Exists(_statePath))
            return new ClientPersistState();
        await using var stream = File.OpenRead(_statePath);
        return await JsonSerializer.DeserializeAsync<ClientPersistState>(stream, cancellationToken: ct)
               ?? new ClientPersistState();
    }

    private async Task SaveStateAsync(CancellationToken ct)
    {
        await using var stream = File.Create(_statePath);
        await JsonSerializer.SerializeAsync(stream, State, new JsonSerializerOptions { WriteIndented = true }, ct);
    }

    private static string? GetLocalIp()
    {
        try
        {
            return System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName())
                .AddressList
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_loopCts is not null)
        {
            await _loopCts.CancelAsync();
            _loopCts.Dispose();
        }

        if (_loopTask is not null)
        {
            try { await _loopTask; } catch { /* ignore */ }
        }

        if (_hub is not null)
            await _hub.DisposeAsync();
        _http.Dispose();
    }
}

public sealed class ClientPersistState
{
    public string? InstallationId { get; set; }
    public Guid? ComputerId { get; set; }
    public string? MacAddress { get; set; }
    public string? RegistrationCode { get; set; }
    public string? DeviceToken { get; set; }
    public string? DisplayName { get; set; }
    /// <summary>Принимать экстренный сигнал «ВОЕНКОМАТ» с кассы. По умолчанию включено.</summary>
    public bool? EmergencyAlertsEnabled { get; set; }
}
