using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShiftClub.Client.Shell;

/// <summary>
/// Кеш и воспроизведение звуков запуска игр.
/// MediaPlayer держится в поле до MediaEnded — иначе GC обрывает звук
/// (особенно когда лаунчер сразу отдаёт фокус и Shell уходит на задний план).
/// </summary>
internal sealed class AppLaunchSoundCache
{
    private readonly string _serverUrl;
    private readonly string _cacheDir;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly Dictionary<Guid, string> _paths = new();
    private readonly object _lock = new();
    private MediaPlayer? _player;
    private DispatcherTimer? _keepAlive;

    public AppLaunchSoundCache(string serverUrl)
    {
        _serverUrl = serverUrl.TrimEnd('/');
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ShiftClub", "Client", "cache", "sounds");
        Directory.CreateDirectory(_cacheDir);
    }

    public void Preload(IEnumerable<(Guid Id, string? SoundUrl)> apps)
    {
        _ = Task.Run(async () =>
        {
            foreach (var (id, url) in apps)
            {
                if (!string.IsNullOrWhiteSpace(url))
                    await EnsureCachedAsync(id, url).ConfigureAwait(false);
            }
        });
    }

    public async Task<string?> EnsureCachedAsync(Guid appId, string? soundUrl)
    {
        if (string.IsNullOrWhiteSpace(soundUrl))
            return null;

        lock (_lock)
        {
            if (_paths.TryGetValue(appId, out var existing) && File.Exists(existing))
                return existing;
        }

        var ext = Path.GetExtension(soundUrl.Split('?', 2)[0]);
        if (string.IsNullOrEmpty(ext)) ext = ".mp3";
        var local = Path.Combine(_cacheDir, $"{appId:N}{ext}");

        try
        {
            var fullUrl = soundUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? soundUrl
                : $"{_serverUrl}/{soundUrl.TrimStart('/')}";
            var bytes = await _http.GetByteArrayAsync(fullUrl).ConfigureAwait(false);
            await File.WriteAllBytesAsync(local, bytes).ConfigureAwait(false);
            lock (_lock) _paths[appId] = local;
            return local;
        }
        catch
        {
            if (File.Exists(local))
            {
                lock (_lock) _paths[appId] = local;
                return local;
            }
            return null;
        }
    }

    /// <summary>
    /// Стартует звук и ждёт, пока MediaPlayer реально начнёт играть (или таймаут).
    /// Дальше звук доигрывает сам — запуск игры/лаунчера его не обрывает.
    /// </summary>
    public async Task StartAsync(Guid appId, string? soundUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(soundUrl))
            return;

        var path = await EnsureCachedAsync(appId, soundUrl).ConfigureAwait(false);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        var disp = Application.Current?.Dispatcher;
        if (disp is null)
            return;

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await disp.InvokeAsync(() =>
        {
            try
            {
                StopInternal();

                var player = new MediaPlayer { Volume = 0.55, ScrubbingEnabled = false };
                _player = player;

                void OnOpened(object? s, EventArgs e)
                {
                    try
                    {
                        player.Play();
                        started.TrySetResult();
                    }
                    catch
                    {
                        started.TrySetResult();
                    }
                }

                void OnEnded(object? s, EventArgs e) => StopInternal();
                void OnFailed(object? s, ExceptionEventArgs e) => StopInternal();

                player.MediaOpened += OnOpened;
                player.MediaEnded += OnEnded;
                player.MediaFailed += OnFailed;
                player.Open(new Uri(path, UriKind.Absolute));

                // KeepAlive: периодический TouchPosition удерживает плеер от «засыпания»
                // при уходе Shell на задний план (лаунчеры / полноэкран).
                _keepAlive = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _keepAlive.Tick += (_, _) =>
                {
                    try
                    {
                        if (_player is null) return;
                        _ = _player.Position;
                    }
                    catch { /* ignore */ }
                };
                _keepAlive.Start();
            }
            catch
            {
                started.TrySetResult();
            }
        });

        using var reg = ct.Register(() => started.TrySetResult());
        await Task.WhenAny(started.Task, Task.Delay(2500, CancellationToken.None)).ConfigureAwait(false);
    }

    private void StopInternal()
    {
        try { _keepAlive?.Stop(); } catch { }
        _keepAlive = null;
        var p = _player;
        _player = null;
        if (p is null) return;
        try { p.Stop(); } catch { }
        try { p.Close(); } catch { }
    }
}
