using System.Text.RegularExpressions;

namespace ShiftClub.Application.Versioning;

/// <summary>
/// Сравнение версий вида 1.2.3. Хвост после третьего числа (суффиксы сборки,
/// «+commit») отбрасывается: для «новее или нет» он ничего не решает.
/// </summary>
public static partial class SemVer
{
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var m = ThreeNumbers().Match(raw.Trim());
        return m.Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}.{m.Groups[3].Value}" : null;
    }

    /// <summary>Больше нуля, если a новее b.</summary>
    public static int Compare(string? a, string? b)
    {
        var pa = Parse(a);
        var pb = Parse(b);

        for (var i = 0; i < 3; i++)
        {
            var cmp = pa[i].CompareTo(pb[i]);
            if (cmp != 0)
                return cmp;
        }

        return 0;
    }

    public static bool IsNewer(string? candidate, string? current) => Compare(candidate, current) > 0;

    private static int[] Parse(string? version)
    {
        var parts = (Normalize(version) ?? "0.0.0").Split('.', StringSplitOptions.RemoveEmptyEntries);
        return
        [
            parts.Length > 0 && int.TryParse(parts[0], out var x) ? x : 0,
            parts.Length > 1 && int.TryParse(parts[1], out var y) ? y : 0,
            parts.Length > 2 && int.TryParse(parts[2], out var z) ? z : 0
        ];
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex ThreeNumbers();
}
