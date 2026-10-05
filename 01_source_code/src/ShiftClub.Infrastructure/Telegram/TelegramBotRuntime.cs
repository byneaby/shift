using ShiftClub.Application.Abstractions;

namespace ShiftClub.Infrastructure.Telegram;

public sealed class TelegramBotRuntime : ITelegramBotRuntime
{
    private readonly object _gate = new();
    private CancellationTokenSource _restartCts = new();

    public string Status { get; private set; } = "stopped";
    public string? Detail { get; private set; }
    public string? BotUsername { get; private set; }

    public CancellationToken RestartToken
    {
        get
        {
            lock (_gate)
                return _restartCts.Token;
        }
    }

    public void SetStatus(string status, string? detail = null, string? botUsername = null)
    {
        Status = status;
        Detail = detail;
        if (botUsername is not null)
            BotUsername = botUsername;
    }

    public void RequestRestart()
    {
        CancellationTokenSource old;
        lock (_gate)
        {
            old = _restartCts;
            _restartCts = new CancellationTokenSource();
        }

        try { old.Cancel(); } catch { /* ignore */ }
        try { old.Dispose(); } catch { /* ignore */ }
    }
}
