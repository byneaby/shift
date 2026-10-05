namespace ShiftClub.Server.Hiring;

public static class HiringFormSchema
{
    public static readonly string[] Statuses =
    [
        "анкета заполнена",
        "приглашен на собеседование",
        "собеседование проводится",
        "приглашен на пробную смену",
        "пробная смена",
        "резерв",
        "принят",
        "отказ"
    ];

    public static object Build() => new
    {
        club = "SHIFT Cyber Club",
        city = "Алматы",
        address = "ул. Масанчи 86а",
        statuses = Statuses,
        scoreCriteria = HiringScores.Criteria.Select(c => new { code = c.Code, name = c.Name }),
        steps = new[]
        {
            new { id = 1, title = "Личная информация" },
            new { id = 2, title = "Образование" },
            new { id = 3, title = "Работа" },
            new { id = 4, title = "Опыт" },
            new { id = 5, title = "Компьютер" },
            new { id = 6, title = "Соцсети" },
            new { id = 7, title = "График" },
            new { id = 8, title = "Деньги" },
            new { id = 9, title = "Личность" },
            new { id = 10, title = "Инициативность" },
            new { id = 11, title = "Клиенты" },
            new { id = 12, title = "Маркетинг" },
            new { id = 13, title = "Рост" },
            new { id = 14, title = "Совместимость" },
            new { id = 15, title = "Честность" },
            new { id = 16, title = "Практика" },
            new { id = 17, title = "Финал" }
        }
    };
}
