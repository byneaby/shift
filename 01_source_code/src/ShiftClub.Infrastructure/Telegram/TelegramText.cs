using System.Text;

namespace ShiftClub.Infrastructure.Telegram;

internal static class TelegramText
{
    /// <summary>Telegram hard limit for message text.</summary>
    public const int MaxMessageLength = 4096;

    /// <summary>Как на карте зала: часы/минуты, в конце — мм:сс.</summary>
    public static string FormatRemaining(int? seconds)
    {
        if (seconds is null)
            return "—";

        var total = Math.Max(0, seconds.Value);
        var h = total / 3600;
        var m = (total % 3600) / 60;
        var s = total % 60;

        if (h > 0)
            return m > 0 ? $"{h} ч {m} мин" : $"{h} ч";
        if (m >= 10)
            return $"{m} мин";
        return $"{m}:{s:D2}";
    }

    /// <summary>Длительность в минутах — как formatDuration / formatDurationChip в панели.</summary>
    public static string FormatDuration(int minutes)
    {
        var rounded = Math.Max(0, minutes);
        if (rounded < 60)
            return $"{rounded} мин";
        var hours = rounded / 60;
        var rest = rounded % 60;
        return rest == 0 ? $"{hours} ч" : $"{hours} ч {rest} мин";
    }

    public static bool IsOfflineDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return false;
        return detail.Contains("офлайн", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("offline", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Как на /floor: Выкл отдельно от Свободен; Пауза отдельно от Занят.</summary>
    public static string OccupancyLabel(string? occupancy, string? detail = null)
    {
        if (string.Equals(detail, "Пауза", StringComparison.OrdinalIgnoreCase)
            || (detail?.Contains("пауза", StringComparison.OrdinalIgnoreCase) ?? false)
            || string.Equals(occupancy, "Paused", StringComparison.OrdinalIgnoreCase))
            return "Пауза";

        if (string.Equals(occupancy, "Offline", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(occupancy, "Free", StringComparison.OrdinalIgnoreCase) && IsOfflineDetail(detail)))
            return "Выкл";

        var baseLabel = occupancy switch
        {
            "Free" => "Свободен",
            "Busy" => "Занят",
            "Paused" => "Пауза",
            "Reserved" => "Бронь",
            "Offline" => "Выкл",
            "Maintenance" => "Обслуживание",
            "Updating" => "Обновление",
            "Setup" => "Настройка",
            _ => string.IsNullOrWhiteSpace(occupancy) ? "—" : occupancy
        };

        // Деталь «Офлайн» уже учтена как Выкл; остальные детали (гость/чек) не дублируем в подписи статуса.
        if (string.IsNullOrWhiteSpace(detail) || IsOfflineDetail(detail))
            return baseLabel;
        return $"{baseLabel} · {detail}";
    }

    public static string TruncateButton(string s, int maxChars = 28)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= maxChars)
            return s;
        return s[..(maxChars - 1)] + "…";
    }

    /// <summary>Режет текст на части ≤ maxLen, по возможности по переводам строк.</summary>
    public static IReadOnlyList<string> SplitMessage(string text, int maxLen = MaxMessageLength - 64)
    {
        if (string.IsNullOrEmpty(text))
            return [""];
        if (text.Length <= maxLen)
            return [text];

        var parts = new List<string>();
        var remaining = text;
        while (remaining.Length > 0)
        {
            if (remaining.Length <= maxLen)
            {
                parts.Add(remaining);
                break;
            }

            var chunk = remaining[..maxLen];
            var breakAt = chunk.LastIndexOf('\n');
            if (breakAt < maxLen / 3)
                breakAt = chunk.LastIndexOf(' ');
            if (breakAt < maxLen / 3)
                breakAt = maxLen;

            parts.Add(remaining[..breakAt].TrimEnd());
            remaining = remaining[breakAt..].TrimStart('\n', '\r', ' ');
        }

        // Нумерация частей, если их несколько
        if (parts.Count > 1)
        {
            for (var i = 0; i < parts.Count; i++)
                parts[i] = $"({i + 1}/{parts.Count})\n{parts[i]}";
        }

        return parts;
    }

    public static string BuildFloorLines(
        IEnumerable<(string Name, string Status, int? RemainingSeconds, string? Guest)> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            var rem = row.RemainingSeconds is int s ? $" · {FormatRemaining(s)}" : "";
            var guest = string.IsNullOrWhiteSpace(row.Guest) ? "" : $" · {row.Guest}";
            sb.AppendLine($"• <b>{Html(row.Name)}</b> — {Html(row.Status)}{rem}{Html(guest)}");
        }

        return sb.ToString();
    }

    public static string Html(string? s) =>
        System.Net.WebUtility.HtmlEncode(s ?? "");
}
