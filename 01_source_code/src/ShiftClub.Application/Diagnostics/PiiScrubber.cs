using System.Text.RegularExpressions;

namespace ShiftClub.Application.Diagnostics;

/// <summary>
/// Вырезает персональные данные и секреты из текста ошибки.
/// Отчёт об ошибке уходит поставщику, а в сообщении легко оказывается телефон
/// клиента, строка подключения с паролем или токен бота — всё это уехать не должно.
/// </summary>
public static class PiiScrubber
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Телефон — это 10–15 цифр, а не любая длинная строка с дефисами.</summary>
    private const int MinPhoneDigits = 10;
    private const int MaxPhoneDigits = 15;

    // Порядок важен: сначала то, что длиннее и специфичнее, иначе телефонная
    // маска съест номер карты, а почту обрежет маска токена.
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        (new Regex(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]+", Opts), "[токен]"),
        (new Regex(@"\b\d{8,10}:[A-Za-z0-9_-]{30,}\b", Opts), "[токен]"),
        (new Regex(@"\b(?:password|pwd|pass)\s*=\s*[^;,\s]+", Opts), "password=[скрыто]"),
        (new Regex(@"\b(?:apikey|api_key|token|secret)\s*[:=]\s*[^\s;,""]+", Opts), "[секрет]"),
        (new Regex(@"\bBearer\s+[A-Za-z0-9._~+/=-]{10,}", Opts), "Bearer [секрет]"),
        (new Regex(@"[\w.+-]+@[\w-]+\.[\w.-]*[\w]", Opts), "[email]"),
        (new Regex(@"\b\d{12,19}\b", Opts), "[номер]"),
        (new Regex(@"(?<prefix>[A-Za-z]:\\Users\\)[^\\\s""]+", Opts), "${prefix}[пользователь]"),
        (new Regex(@"(?<prefix>/home/)[^/\s""]+", Opts), "${prefix}[пользователь]")
    ];

    private static readonly Regex PhoneLike = new(@"\+?\d[\d\s\-()]{7,}\d", Opts);

    private static readonly Regex GuidLike =
        new(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", Opts);

    private static readonly Regex Digits = new(@"\d+", Opts);

    public static string Scrub(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        // Идентификаторы в сообщении полезны для разбора и персональными данными
        // не являются, но под маску телефона попадают. Прячем их на время чистки.
        var guids = new List<string>();
        var result = GuidLike.Replace(text, match =>
        {
            guids.Add(match.Value);
            return $"\u0001{guids.Count - 1}\u0001";
        });

        foreach (var (pattern, replacement) in Rules)
            result = pattern.Replace(result, replacement);

        result = PhoneLike.Replace(result, MaskPhone);

        for (var i = 0; i < guids.Count; i++)
            result = result.Replace($"\u0001{i}\u0001", guids[i], StringComparison.Ordinal);

        return result.Trim();
    }

    /// <summary>
    /// Текст, по которому две одинаковые по сути ошибки считаются одной:
    /// идентификаторы и числа из сообщения убираем, иначе каждый сеанс даст
    /// свою «новую» ошибку и список станет бесполезным.
    /// </summary>
    public static string Normalize(string? text)
    {
        var scrubbed = GuidLike.Replace(Scrub(text), "<id>");
        return Digits.Replace(scrubbed, "<n>");
    }

    private static string MaskPhone(Match match)
    {
        var digits = match.Value.Count(char.IsAsciiDigit);
        return digits is >= MinPhoneDigits and <= MaxPhoneDigits ? "[телефон]" : match.Value;
    }
}
