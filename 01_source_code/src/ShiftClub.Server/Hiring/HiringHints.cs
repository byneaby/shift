using System.Text.Json;

namespace ShiftClub.Server.Hiring;

public static class HiringHints
{
    public static IReadOnlyList<string> Build(Dictionary<string, JsonElement> a)
    {
        var hints = new List<string>();
        string S(string k) => a.TryGetValue(k, out var e)
            ? (e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString())
            : "";

        if (S("studying") is "да" or "очно" || S("studyForm") is "очно")
            hints.Add("Учится очно — уточните расписание и возможность выходить в смены.");
        if (S("workingNow") is "да")
            hints.Add("Сейчас работает — уточните дату выхода и конфликт графиков.");
        if (TryParseInt(S("jobsLast5Years"), out var jobs) && jobs >= 4)
            hints.Add("Много мест работы за 5 лет — обсудите причины смены.");
        if (!HasArray(a, "experienceAreas", "касса"))
            hints.Add("Нет опыта кассы — уточните отношение к материальной ответственности.");
        if (S("nightShifts") is "нет" || S("weekendReady") is "нет" || S("holidayReady") is "нет")
            hints.Add("Ограничения по ночным/выходным/праздникам — зафиксируйте.");
        if (TryParseSalary(S("wantedSalary"), out var sal) && sal > 250000)
            hints.Add("Зарплатные ожидания высокие — обсудите реалистичный диапазон.");
        if (S("taskStyle") is "выполнять готовые задачи")
            hints.Add("Предпочитает готовые задачи — проверьте инициативность на кейсах.");
        if (S("ideaWait") is "буду ждать" or "подожду")
            hints.Add("Ждёт, пока спросят идею — слабый сигнал по инициативе.");
        if (S("likeFilming") is "да" or "очень" || HasArray(a, "contentLikes", "снимать"))
            hints.Add("Интерес к контенту — попросите показать примеры.");
        if (S("extraDuties") is "нет" or "только свои обязанности")
            hints.Add("Не готов к задачам «не по должности» — риск по культуре SHIFT.");
        if (S("wantManagePeople") is "да")
            hints.Add("Хочет руководить — уточните, какую ответственность готов брать сейчас.");

        var shortKeys = new[]
        {
            "emptyClubAction", "problemNoBoss", "goodService", "clubIdeas", "whyHireYou", "weakSides"
        };
        var shortAnswers = shortKeys.Count(k => S(k).Trim().Length is > 0 and < 30);
        if (shortAnswers >= 3)
            hints.Add("Много коротких ответов в ключевых блоках — уточните на собеседовании.");

        if (hints.Count == 0)
            hints.Add("Смотрите автопрофиль и блок практики (Stories / пост / продажа ночи).");
        return hints;
    }

    private static bool HasArray(Dictionary<string, JsonElement> a, string key, string value)
    {
        if (!a.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Array) return false;
        return el.EnumerateArray().Any(x =>
            string.Equals(x.GetString(), value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseInt(string raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out value);
    }

    private static bool TryParseSalary(string raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out value);
    }
}

public static class HiringScores
{
    private static readonly Dictionary<string, double> Weights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["responsibility"] = 1.5,
        ["clients"] = 1.5,
        ["initiative"] = 1.5,
        ["motivation"] = 1.5,
        ["learning"] = 1.3,
        ["social"] = 1.3,
        ["stress"] = 1.3
    };

    public static readonly (string Code, string Name)[] Criteria =
    [
        ("communication", "Коммуникабельность"),
        ("speech", "Грамотность речи"),
        ("friendliness", "Доброжелательность"),
        ("responsibility", "Ответственность"),
        ("punctuality", "Пунктуальность"),
        ("honesty", "Честность и открытость"),
        ("stress", "Стрессоустойчивость"),
        ("clients", "Работа с клиентами"),
        ("cash", "Потенциал работы с кассой"),
        ("social", "Знание социальных сетей"),
        ("content", "Умение создавать контент"),
        ("initiative", "Инициативность"),
        ("learning", "Обучаемость"),
        ("team", "Командность"),
        ("motivation", "Мотивация"),
        ("growth", "Желание развиваться"),
        ("scheduleFit", "Соответствие графику"),
        ("overall", "Общее впечатление")
    ];

    public static double? ComputePercent(JsonElement scores)
    {
        if (scores.ValueKind != JsonValueKind.Object) return null;
        double sum = 0, max = 0;
        foreach (var (code, _) in Criteria)
        {
            var w = Weights.GetValueOrDefault(code, 1.0);
            max += 10 * w;
            if (!scores.TryGetProperty(code, out var v)) continue;
            var n = v.ValueKind == JsonValueKind.Number ? v.GetDouble()
                : double.TryParse(v.ToString(), out var p) ? p : 0;
            n = Math.Clamp(n, 0, 10);
            sum += n * w;
        }
        if (max <= 0) return null;
        return Math.Round(100.0 * sum / max, 1);
    }

    public static object Classify(double? percent) => percent switch
    {
        null => new { label = "нет оценки", tone = "muted" },
        >= 85 => new { label = "сильный кандидат", tone = "good" },
        >= 70 => new { label = "подходит, рекомендуется пробная смена", tone = "ok" },
        >= 55 => new { label = "резерв / доп. проверка", tone = "warn" },
        _ => new { label = "скорее не подходит", tone = "bad" }
    };
}
