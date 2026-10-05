using ShiftClub.Shared.Contracts.Diagnostics;

namespace ShiftClub.Application.Diagnostics;

/// <summary>
/// Последние ошибки сервера, сгруппированные по смыслу.
/// Живёт в памяти: это не аудит, а ответ на вопрос «что у клуба ломается
/// прямо сейчас». Подробности остаются в файловых логах Serilog.
/// </summary>
public sealed class ErrorLogStore
{
    /// <summary>Разных групп держим немного: список читает человек, а не машина.</summary>
    public const int MaxGroups = 100;

    /// <summary>Полный текст с вложенными исключениями остаётся в файловых логах.</summary>
    public const int MaxMessageLength = 600;

    private readonly object _gate = new();
    private readonly Dictionary<string, Group> _groups = new(StringComparer.Ordinal);

    private sealed class Group
    {
        public required string Fingerprint { get; init; }
        public required string Level { get; set; }
        public required string Source { get; set; }
        public required string Kind { get; set; }
        public required string Message { get; set; }
        public string? Where { get; set; }
        public int Count { get; set; }
        public DateTimeOffset FirstAt { get; init; }
        public DateTimeOffset LastAt { get; set; }
        public bool Reported { get; set; }
    }

    public void Record(
        string level,
        string source,
        string kind,
        string? message,
        string? where,
        DateTimeOffset at)
    {
        var scrubbedMessage = Shorten(PiiScrubber.Scrub(message), MaxMessageLength);
        var scrubbedWhere = where is null ? null : Shorten(PiiScrubber.Scrub(where), 200);
        var fingerprint = ErrorFingerprint.Compute(kind, scrubbedWhere, scrubbedMessage);

        lock (_gate)
        {
            if (_groups.TryGetValue(fingerprint, out var existing))
            {
                existing.Count++;
                existing.LastAt = at;
                existing.Level = level;
                existing.Message = scrubbedMessage;
                existing.Where = scrubbedWhere;
                existing.Source = source;
                existing.Kind = kind;
                return;
            }

            if (_groups.Count >= MaxGroups)
            {
                // Вытесняем самую старую по последнему появлению: свежие
                // проблемы важнее тех, что давно не повторялись.
                var oldest = _groups.Values.OrderBy(g => g.LastAt).First();
                _groups.Remove(oldest.Fingerprint);
            }

            _groups[fingerprint] = new Group
            {
                Fingerprint = fingerprint,
                Level = level,
                Source = source,
                Kind = kind,
                Message = scrubbedMessage,
                Where = scrubbedWhere,
                Count = 1,
                FirstAt = at,
                LastAt = at
            };
        }
    }

    public IReadOnlyList<ErrorGroupDto> Snapshot(int limit = 50)
    {
        lock (_gate)
        {
            return _groups.Values
                .OrderByDescending(g => g.LastAt)
                .Take(limit)
                .Select(ToDto)
                .ToList();
        }
    }

    /// <summary>Группы, которые ещё не отправляли поставщику.</summary>
    public IReadOnlyList<ErrorGroupDto> TakeUnreported(int limit = 20)
    {
        lock (_gate)
        {
            return _groups.Values
                .Where(g => !g.Reported)
                .OrderBy(g => g.FirstAt)
                .Take(limit)
                .Select(ToDto)
                .ToList();
        }
    }

    public void MarkReported(IEnumerable<string> fingerprints)
    {
        lock (_gate)
        {
            foreach (var fingerprint in fingerprints)
            {
                if (_groups.TryGetValue(fingerprint, out var group))
                    group.Reported = true;
            }
        }
    }

    public int CountSince(DateTimeOffset since)
    {
        lock (_gate)
        {
            return _groups.Values.Where(g => g.LastAt >= since).Sum(g => g.Count);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _groups.Clear();
        }
    }

    private static string Shorten(string text, int limit) =>
        text.Length <= limit ? text : text[..limit] + "…";

    private static ErrorGroupDto ToDto(Group g) => new(
        g.Fingerprint,
        g.Level,
        g.Source,
        g.Kind,
        g.Message,
        g.Where,
        g.Count,
        g.FirstAt,
        g.LastAt,
        g.Reported);
}
