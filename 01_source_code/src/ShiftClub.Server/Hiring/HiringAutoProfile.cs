using System.Text.Json;

namespace ShiftClub.Server.Hiring;

/// <summary>Скрытый автопрофиль кандидата (только для руководителя).</summary>
public static class HiringAutoProfile
{
    public static object Build(Dictionary<string, JsonElement> a)
    {
        string S(string k) => a.TryGetValue(k, out var e)
            ? e.ValueKind switch
            {
                JsonValueKind.String => e.GetString() ?? "",
                JsonValueKind.Number => e.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Array => string.Join(", ", e.EnumerateArray().Select(x => x.GetString() ?? x.ToString())),
                _ => e.ToString()
            }
            : "";

        bool ArrHas(string key, string value) =>
            a.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Array
            && el.EnumerateArray().Any(x => string.Equals(x.GetString(), value, StringComparison.OrdinalIgnoreCase));

        int Len(string k) => S(k).Trim().Length;
        int PcSkill() => int.TryParse(S("pcSkill"), out var n) ? Math.Clamp(n, 1, 10) : 5;

        // —— Ответственность ——
        var responsibility = 55.0;
        if (ArrHas("experienceAreas", "касса") || ArrHas("experienceAreas", "обслуживание клиентов")) responsibility += 12;
        if (S("nightShifts") is "да" or "иногда") responsibility += 8;
        if (S("weekendReady") is "да" or "иногда") responsibility += 6;
        if (S("holidayReady") is "да" or "иногда") responsibility += 6;
        if (S("stayForTournament") is "да") responsibility += 8;
        if (S("extraDuties") is "нормально" or "с радостью" or "да") responsibility += 10;
        if (Len("emptyClubAction") > 40) responsibility += 6;
        if (Len("problemNoBoss") > 40) responsibility += 6;
        if (S("seenDirt") is "уберу сам" or "уберу" or "приберу") responsibility += 5;
        responsibility = Clamp(responsibility);

        // —— Коммуникабельность ——
        var communication = 50.0;
        if (ArrHas("experienceAreas", "обслуживание клиентов") || ArrHas("experienceAreas", "продажи")
            || ArrHas("experienceAreas", "call-center")) communication += 14;
        if (Len("goodService") > 30) communication += 10;
        if (Len("adminMain") > 25) communication += 8;
        if (Len("rudeGuest") > 30) communication += 10;
        if (Len("sellNight") > 40) communication += 10;
        if (ArrHas("contentLikes", "отвечать клиентам")) communication += 6;
        communication = Clamp(communication);

        // —— Инициативность ——
        var initiative = 45.0;
        if (S("taskStyle") is "самому придумывать решения") initiative += 18;
        if (S("ideaWait") is "предложу сам" or "предложу") initiative += 16;
        if (Len("lastImprovement") > 40) initiative += 14;
        if (Len("emptyClubAction") > 50) initiative += 8;
        if (Len("clubIdeas") > 60) initiative += 10;
        if (ArrHas("selfTraits", "лидером") || ArrHas("selfTraits", "организатором")) initiative += 8;
        if (S("taskStyle") is "выполнять готовые задачи") initiative -= 8;
        initiative = Clamp(initiative);

        // —— Готовность к развитию ——
        var growth = 50.0;
        if (S("wantMoreResponsibility") is "да") growth += 15;
        if (S("wantManagePeople") is "да" or "когда-нибудь") growth += 10;
        if (Len("inOneYear") > 20) growth += 10;
        if (Len("inThreeYears") > 20) growth += 10;
        if (PcSkill() >= 7) growth += 6;
        if (ArrHas("priorities", "развитие") || ArrHas("priorities", "карьерный рост")) growth += 12;
        if (ArrHas("selfTraits", "творческим")) growth += 5;
        growth = Clamp(growth);

        // —— Работа с конфликтами ——
        var conflicts = 48.0;
        if (Len("rudeGuest") > 40) conflicts += 14;
        if (Len("noisyChild") > 30) conflicts += 10;
        if (Len("pcFrozen") > 25) conflicts += 8;
        if (Len("critiqueAttitude") > 30) conflicts += 10;
        if (Len("stressReaction") > 30) conflicts += 10;
        if (Len("whenWrong") > 40) conflicts += 8;
        conflicts = Clamp(conflicts);

        // —— Культура SHIFT ——
        var culture = 55.0;
        if (S("extraDuties") is "нормально" or "с радостью" or "да") culture += 12;
        if (S("stayForTournament") is "да") culture += 10;
        if (S("workOnDayOff") is "да" or "если важно") culture += 10;
        if (S("seenDirt") is "уберу сам" or "уберу" or "приберу") culture += 8;
        if (S("bossWrong") is "мягко скажу" or "скажу наедине" or "подскажу") culture += 8;
        if (ArrHas("priorities", "коллектив") || ArrHas("priorities", "интересная работа")) culture += 8;
        if (Len("compatibleNote") > 30) culture += 5;
        culture = Clamp(culture);

        // —— Маркетинговый потенциал ——
        var marketing = 40.0;
        if (ArrHas("experienceAreas", "соцсети") || ArrHas("experienceAreas", "фото")
            || ArrHas("experienceAreas", "видео") || ArrHas("experienceAreas", "монтаж")
            || ArrHas("experienceAreas", "Canva") || ArrHas("experienceAreas", "CapCut")
            || ArrHas("experienceAreas", "Photoshop")) marketing += 14;
        if (S("likeFilming") is "да" or "очень") marketing += 10;
        if (ArrHas("contentLikes", "снимать") || ArrHas("contentLikes", "монтировать")
            || ArrHas("contentLikes", "придумывать идеи")) marketing += 10;
        if (Len("clubIdeas") > 50) marketing += 12;
        if (Len("promoIdeas") > 30) marketing += 8;
        if (Len("reelsIdea") > 20) marketing += 8;
        if (Len("storiesIdea") > 30) marketing += 8;
        if (Len("discountPost") > 30) marketing += 6;
        if (!string.IsNullOrWhiteSpace(S("instagram")) || !string.IsNullOrWhiteSpace(S("tiktok"))) marketing += 5;
        marketing = Clamp(marketing);

        // —— Риск быстрой смены (ниже = лучше; показываем как риск) ——
        var churnRisk = 25.0;
        if (TryParseInt(S("jobsLast5Years"), out var jobs))
        {
            if (jobs >= 5) churnRisk += 25;
            else if (jobs >= 3) churnRisk += 12;
            else if (jobs <= 1) churnRisk -= 8;
        }
        if (S("workingNow") is "нет") churnRisk += 5;
        if (S("studyForm") is "очно") churnRisk += 8;
        if (TryParseSalary(S("wantedSalary"), out var want) && TryParseSalary(S("currentSalary"), out var cur)
            && cur > 0 && want > cur * 1.5m) churnRisk += 12;
        else if (TryParseSalary(S("wantedSalary"), out want) && want > 280000) churnRisk += 10;
        if (S("whyLeave").Contains("временно", StringComparison.OrdinalIgnoreCase)
            || S("whyLeave").Contains("на время", StringComparison.OrdinalIgnoreCase)) churnRisk += 10;
        if (Len("longestJobWhy") > 40) churnRisk -= 5;
        churnRisk = Clamp(churnRisk);

        var items = new object[]
        {
            Item("responsibility", "Ответственность", responsibility),
            Item("communication", "Коммуникабельность", communication),
            Item("initiative", "Инициативность", initiative),
            Item("growth", "Готовность к развитию", growth),
            Item("conflicts", "Работа с конфликтами", conflicts),
            Item("culture", "Совместимость с культурой SHIFT", culture),
            Item("marketing", "Маркетинговый потенциал", marketing),
            Item("churnRisk", "Риск быстрой смены работы", churnRisk, invertGood: true)
        };

        var positiveAvg = new[] { responsibility, communication, initiative, growth, conflicts, culture, marketing }.Average();
        var fit = Math.Round(positiveAvg * 0.92 + (100 - churnRisk) * 0.08, 0);

        return new
        {
            fitPercent = fit,
            summary = fit >= 80 ? "сильный предварительный профиль"
                : fit >= 65 ? "хороший профиль, стоит на собеседование"
                : fit >= 50 ? "средний профиль — уточнить на интервью"
                : "слабый предварительный профиль",
            items
        };
    }

    private static object Item(string code, string name, double value, bool invertGood = false)
    {
        var p = (int)Math.Round(Clamp(value));
        var tone = invertGood
            ? p <= 25 ? "good" : p <= 45 ? "ok" : p <= 65 ? "warn" : "bad"
            : p >= 80 ? "good" : p >= 65 ? "ok" : p >= 50 ? "warn" : "bad";
        var emoji = tone switch
        {
            "good" => "🟢",
            "ok" => "🔵",
            "warn" => "🟡",
            _ => "⚪"
        };
        if (invertGood && p > 65) emoji = "🔴";
        if (invertGood && p <= 30) emoji = "⚪";
        return new { code, name, percent = p, tone, emoji };
    }

    private static double Clamp(double v) => Math.Clamp(v, 5, 98);

    private static bool TryParseInt(string raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out value);
    }

    private static bool TryParseSalary(string raw, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return decimal.TryParse(digits, out value);
    }
}
