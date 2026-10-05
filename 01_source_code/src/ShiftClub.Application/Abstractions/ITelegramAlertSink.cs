using ShiftClub.Shared.Contracts.Settings;

namespace ShiftClub.Application.Abstractions;

/// <summary>Fan-out staff alerts to Telegram (and any other sinks). Fire-and-forget safe.</summary>
public interface ITelegramAlertSink
{
    ValueTask PublishAsync(StaffAlertMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Runtime status + restart signal for the Telegram worker.</summary>
public interface ITelegramBotRuntime
{
    string Status { get; }
    string? Detail { get; }
    string? BotUsername { get; }

    void SetStatus(string status, string? detail = null, string? botUsername = null);
    void RequestRestart();
    CancellationToken RestartToken { get; }
}
