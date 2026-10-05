using Serilog.Core;
using Serilog.Events;
using ShiftClub.Application.Diagnostics;

namespace ShiftClub.Server.Diagnostics;

/// <summary>
/// Складывает все ошибки уровня Error и выше в <see cref="ErrorLogStore"/>.
/// Через Serilog, а не через middleware: так попадают и падения фоновых
/// задач (бэкапы, Telegram, завершение сеансов), а не только запросы API.
/// </summary>
public sealed class ErrorCaptureSink : ILogEventSink
{
    private readonly ErrorLogStore _store;

    public ErrorCaptureSink(ErrorLogStore store)
    {
        _store = store;
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Error)
            return;

        var source = ReadProperty(logEvent, "SourceContext") ?? "ShiftClub";
        var kind = logEvent.Exception?.GetType().Name ?? "LogError";

        _store.Record(
            logEvent.Level == LogEventLevel.Fatal ? "fatal" : "error",
            ShortSource(source),
            kind,
            BuildMessage(logEvent),
            FindOwnFrame(logEvent.Exception),
            logEvent.Timestamp);
    }

    private static string BuildMessage(LogEvent logEvent)
    {
        var text = logEvent.RenderMessage();
        var inner = logEvent.Exception?.GetBaseException().Message;
        if (string.IsNullOrWhiteSpace(inner) || text.Contains(inner, StringComparison.Ordinal))
            return text;
        return $"{text} — {inner}";
    }

    /// <summary>Первая строка стека в нашем коде: по чужим кадрам искать нечего.</summary>
    private static string? FindOwnFrame(Exception? exception)
    {
        var stack = exception?.StackTrace;
        if (string.IsNullOrWhiteSpace(stack))
            return null;

        foreach (var line in stack.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("ShiftClub.", StringComparison.Ordinal))
                return trimmed.Length > 200 ? trimmed[..200] : trimmed;
        }

        return null;
    }

    private static string ShortSource(string source)
    {
        var lastDot = source.LastIndexOf('.');
        return lastDot > 0 && lastDot < source.Length - 1 ? source[(lastDot + 1)..] : source;
    }

    private static string? ReadProperty(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: string text }
            ? text
            : null;
}
