using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.StaffWiki;

namespace ShiftClub.Infrastructure.Services;

public sealed class StaffWikiService : IStaffWikiService
{
    private static readonly Regex SlugRx = new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    private readonly ShiftClubDbContext _db;
    private readonly IHostEnvironment _env;

    public StaffWikiService(ShiftClubDbContext db, IHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public Task<bool> IsOwnerAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        _db.EmployeeRoles.AsNoTracking()
            .AnyAsync(er => er.EmployeeId == employeeId && er.Role.Code == "owner", cancellationToken);

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        EnsureSeedImages();

        if (await _db.StaffWikiPages.AnyAsync(cancellationToken))
            return;

        var now = DateTimeOffset.UtcNow;
        foreach (var (slug, title, sort, body) in SeedArticles())
        {
            _db.StaffWikiPages.Add(new StaffWikiPage
            {
                Slug = slug,
                Title = title,
                SortOrder = sort,
                BodyMarkdown = body,
                CreatedAt = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private void EnsureSeedImages()
    {
        var dest = Path.Combine(_env.ContentRootPath, "data", "wiki");
        Directory.CreateDirectory(dest);
        var names = new[]
        {
            "cash.png", "floor.png", "customers.png", "case-open.png",
            "bar.png", "bookings.png",
        };
        foreach (var name in names)
        {
            var target = Path.Combine(dest, name);
            if (File.Exists(target)) continue;
            foreach (var root in new[]
                     {
                         Path.Combine(_env.ContentRootPath, "data", "wiki"),
                         Path.Combine(AppContext.BaseDirectory, "data", "wiki"),
                         @"C:\ShiftClub\Project\src\ShiftClub.Server\data\wiki",
                     })
            {
                var src = Path.Combine(root, name);
                if (!File.Exists(src) || string.Equals(src, target, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    File.Copy(src, target, overwrite: false);
                    break;
                }
                catch
                {
                    /* ignore */
                }
            }
        }
    }

    public async Task<IReadOnlyList<StaffWikiPageListItemDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        return await _db.StaffWikiPages.AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Title)
            .Select(p => new StaffWikiPageListItemDto(p.Id, p.Slug, p.Title, p.SortOrder, p.UpdatedAt ?? p.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<StaffWikiPageDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);
        var s = NormalizeSlug(slug);
        var p = await _db.StaffWikiPages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == s, cancellationToken);
        return p is null ? null : Map(p);
    }

    public async Task<StaffWikiPageDto> CreateAsync(
        UpsertStaffWikiPageRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var (slug, title, body, sort) = Validate(request);
        if (await _db.StaffWikiPages.AnyAsync(p => p.Slug == slug, cancellationToken))
            throw new InvalidOperationException($"Статья со slug «{slug}» уже есть.");

        var page = new StaffWikiPage
        {
            Slug = slug,
            Title = title,
            BodyMarkdown = body,
            SortOrder = sort,
            CreatedBy = employeeId,
            UpdatedByEmployeeId = employeeId,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = employeeId,
        };
        _db.StaffWikiPages.Add(page);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(page);
    }

    public async Task<StaffWikiPageDto> UpdateAsync(
        Guid id,
        UpsertStaffWikiPageRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var page = await _db.StaffWikiPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                   ?? throw new KeyNotFoundException("Статья не найдена.");

        var (slug, title, body, sort) = Validate(request);
        if (await _db.StaffWikiPages.AnyAsync(p => p.Slug == slug && p.Id != id, cancellationToken))
            throw new InvalidOperationException($"Статья со slug «{slug}» уже есть.");

        page.Slug = slug;
        page.Title = title;
        page.BodyMarkdown = body;
        page.SortOrder = sort;
        page.UpdatedByEmployeeId = employeeId;
        page.UpdatedAt = DateTimeOffset.UtcNow;
        page.UpdatedBy = employeeId;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(page);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await _db.StaffWikiPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                   ?? throw new KeyNotFoundException("Статья не найдена.");
        _db.StaffWikiPages.Remove(page);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static (string Slug, string Title, string Body, int Sort) Validate(UpsertStaffWikiPageRequest request)
    {
        var title = (request.Title ?? "").Trim();
        if (title.Length is < 2 or > 200)
            throw new InvalidOperationException("Заголовок: 2–200 символов.");

        var slug = NormalizeSlug(string.IsNullOrWhiteSpace(request.Slug) ? title : request.Slug);
        if (!SlugRx.IsMatch(slug) || slug.Length > 80)
            throw new InvalidOperationException("Slug: латиница, цифры и дефисы (например shift-case).");

        var body = request.BodyMarkdown ?? "";
        if (body.Length > 200_000)
            throw new InvalidOperationException("Текст слишком длинный.");

        return (slug, title, body, request.SortOrder);
    }

    private static string NormalizeSlug(string raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        s = s.Replace(' ', '-');
        s = Regex.Replace(s, @"[^a-z0-9\-]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    private static StaffWikiPageDto Map(StaffWikiPage p) =>
        new(p.Id, p.Slug, p.Title, p.BodyMarkdown, p.SortOrder, p.CreatedAt, p.UpdatedAt, p.UpdatedByEmployeeId);

    private static IEnumerable<(string Slug, string Title, int Sort, string Body)> SeedArticles()
    {
        yield return ("start-smeny", "1. Смена на кассе", 10, SeedShift());
        yield return ("zal", "2. Зал: посадка и сеанс", 20, SeedFloor());
        yield return ("klienty", "3. Клиенты: баланс и ключи", 30, SeedCustomers());
        yield return ("shift-case", "4. SHIFT CASE", 40, SeedCase());
        yield return ("seriya", "5. Серия посещений", 50, SeedStreak());
        yield return ("bar-bron", "6. Бар и брони", 60, SeedBar());
        yield return ("oshibki", "7. Частые ошибки", 70, SeedFaq());
    }

    private static string SeedShift() => """
# Смена на кассе

Перед любой продажей — сеанс, бар, ключ CASE, пополнение баланса — нужна **открытая кассовая смена**. Без неё панель не пробьёт оплату.

## Открыть смену

1. В меню слева открой **Касса**.
2. Если касс несколько — выбери нужную.
3. Укажи сумму наличных в ящике на старте (пересчитай купюры).
4. Нажми **Открыть смену**.

![Страница Касса](/media/wiki/cash.png)

## Что видно во время смены

- Список чеков текущей смены (сеансы, бар, ключи, пополнения).
- Разбивка по способам оплаты: нал / карта / Kaspi / перевод / баланс.
- Кнопки возврата у чека (только в рамках открытой смены и при наличии прав).

## Закрыть смену

1. На **Касса** нажми **Закрыть смену**.
2. Сверь Z-отчёт с фактом в ящике и терминале.
3. Сдай наличные по отчёту администратору / владельцу.
4. Только после закрытия можно открыть новую смену на этой кассе.

## Правила

- Не работай «мимо кассы» — всё через панель.
- Если смена «зависла» или ошибка открытия — сразу владельцу / старшему, не выдумывай обходные пути.
- Возврат делай только по реальному чеку и причине.
""";

    private static string SeedFloor() => """
# Зал: посадка и сеанс

**Зал** — главный экран кассира: карта ПК, статусы, быстрый старт и действия по месту.

![Карта зала](/media/wiki/floor.png)

## Наушники по зонам (обязательно выдавать правильно)

| Зона | Наушники |
|---|---|
| **Стандарт** | **Проводные** |
| **VIP** | **Беспроводные** |
| **Bootcamp** | **Беспроводные** |

Как работать:

1. Перед посадкой посмотри, в какой зоне ПК (на карте / по названию зоны).
2. **Стандарт** — выдай комплект **проводных** наушников, проверь штекер и звук на месте.
3. **VIP** и **Bootcamp** — выдай **беспроводные**, убедись что они заряжены и сопряжены / подключены к этому ПК.
4. При завершении сеанса забери наушники обратно: проводные — на место в Стандарте; беспроводные VIP/Bootcamp — на зарядку / в лоток зоны.
5. Если беспроводные сели или не подключаются — не сажай гостя «как есть»: замени комплект или пересади (с согласованием), зафиксируй проблему старшему.

> Не путай зоны: в Стандарте не выдаём беспроводные «по желанию», в VIP/Bootcamp не сажаем на проводные вместо штатных беспроводных без причины и пометки.

## Посадить гостя

1. Выбери **свободный** ПК на карте.
2. **Запустить** → найди клиента по телефону или имени. Если нового — создай аккаунт (телефон обязателен).
3. Выбери тариф и длительность под зону.
4. Оплата: наличные / Kaspi / карта / баланс / банк времени (что доступно).
5. Подтверди старт сеанса.
6. Выдай наушники по таблице выше и коротко объясни гостю (особенно беспроводные: включение, громкость).

Важно: для лояльности, **серии посещений**, баланса и кейса гость должен быть **с аккаунтом**, не «просто гость без профиля».

## Во время сеанса

На ПК в сеансе обычно доступны:

- **Продлить** — добавить время и оплату.
- **Бар** — быстрая продажа к этому месту.
- **Ключ CASE** — продать ключ гостю с аккаунтом (если кнопка есть на карточке / в действиях).
- **Пауза / Перенести / Завершить** — по ситуации в зале.

При **переносе** на другую зону — сразу смени тип наушников (Стандарт ↔ VIP/Bootcamp).

## Быстрые кнопки в шапке зала

- **Бар** — продажа без привязки к конкретному ПК.
- **Ключ CASE** — поиск клиента → количество ключей → цена → оплата → ключи на аккаунт.
""";

    private static string SeedCustomers() => """
# Клиенты: баланс и ключи

Раздел **Клиенты** — поиск по телефону / имени, карточка гостя, баланс, банк, кейс.

![Клиенты](/media/wiki/customers.png)

## Найти гостя

1. Открой **Клиенты**.
2. Введи телефон (удобнее всего) или имя.
3. Открой нужную карточку.

Если гостя нет — создай нового с телефоном до старта сеанса.

## Пополнить баланс

Карточка → вкладка **Баланс** → **Пополнить** → сумма и способ оплаты → подтверди. Чек уходит в текущую кассовую смену.

## Купить ключ SHIFT CASE

1. Открой карточку гостя.
2. На вкладке **Данные** или **Баланс** нажми **Купить ключ**.
3. Выбери количество (1 / 2 / 3 / 5) и проверь цену.
4. Прими оплату → чек «Ключ SHIFT CASE», ключи начисляются на аккаунт.

С **Зала** то же самое через кнопку **Ключ CASE** в шапке (быстрее, когда гость уже за ПК).

## Начислить ключ без оплаты

Только по правилам клуба (подарок, компенсация, акция): **Начислить ключ** + понятный комментарий. Это **не** кассовый чек — не путай с продажей.

## Банк времени

Вкладка **Банк** — ручное начисление / списание минут по зоне (нужны права). Используй только по согласованию / инструкции владельца.
""";

    private static string SeedCase() => """
# SHIFT CASE — открытие кейса у кассы

Кейс крутится на **экране акции** (`/promo/upgrade` на втором мониторе / ТВ). Кассир запускает открытие из карточки гостя в **Клиенты**.

![Открыть кейс в карточке клиента](/media/wiki/case-open.png)

## Подготовка

1. На ТВ / втором мониторе открыта страница акции клуба (`/promo/upgrade`).
2. У гостя есть **хотя бы один ключ** (регистрация, покупка или начисление).
3. У тебя открыта **карточка этого гостя** в **Клиенты**.
4. Кассовая смена открыта, если дальше нужна выдача платного приза / бар.

## Открыть кейс — пошагово

1. В карточке нажми **Открыть кейс**.
2. На экране акции сама откроется рулетка и прокрутка — направь гостя смотреть на ТВ.
3. Приз показывается **около 10 секунд** — **не закрывай и не обновляй** экран раньше: гость должен увидеть результат.
4. После показа оверлей кейса уходит обратно на прайс акции.
5. У тебя на панели в **Клиенты** появится окно **что выпало** (название, картинка, остаток ключей) — сверь с тем, что видел гость.

## Выдача приза

- Автоначисление (время / баланс и т.п.) — часто уже применено системой; проверь карточку.
- Если приз «выдать руками» (бар, мерч и т.п.) — раздел **SHIFT CASE** / очередь выдачи, отметь выдачу по правилам клуба.
- Приз из бара пробивай / списывай так, как принято у вас для призов кейса (не «забыть на полке»).

## Нельзя

- Открывать кейс без ключа или «для проверки» без причины.
- Обещать конкретный приз до окончания прокрутки.
- Закрывать / перезагружать экран акции раньше ~10 секунд показа результата.
- Открывать кейс не тому гостю (всегда сверяй телефон / имя в карточке).
""";

    private static string SeedStreak() => """
# Серия посещений

Серия — это **дни подряд**, когда у гостя **с аккаунтом** реально **стартовал игровой сеанс** (не просто вход в Shell).

## Как считается

| Действие | Серия |
|---|---|
| Только логин на ПК (Shell), без сеанса | **Не считается** |
| Старт сеанса с аккаунтом (касса / Shell / бронь) | **+1 день** (один раз в календарный день филиала) |
| Повторный сеанс в тот же день | Серию не увеличивает |
| Пропуск календарного дня | При следующем визите серия начинается заново (обычно с 1) |

«День» считается по **часовому поясу филиала**.

## Награды

Пороги настраивает владелец в **Настройки** (например 3 / 5 / 7 дней → бонусы ₸, напитки и т.д.).

Награда выдаётся в день, когда серия **ровно** достигает порога — проверь карточку / уведомление и выдай по правилам.

## Что говорить гостю

- «Серия растёт, когда мы **запускаем сеанс** на аккаунт, а не когда просто вошёл в Windows.»
- «Нужно приходить **каждый день** без пропуска, иначе серия сбрасывается.»

## Зачем так сделано

Чтобы не абузили: недостаточно «вбить аккаунт и выйти» — нужен реальный старт сеанса.
""";

    private static string SeedBar() => """
# Бар и брони

## Бар

![Бар](/media/wiki/bar.png)

1. Открой **Бар** в меню слева (или быстрый **Бар** из **Зала**).
2. Выбери позиции, количество, способ оплаты.
3. Нужна **открытая кассовая смена**.
4. Если заказ к месту — удобнее продажа с ПК в сеансе (кнопка **Бар** на карточке места).

Склад и списания — по правам; при минусе на складе не «рисуй» продажу мимо системы.

## Брони

![Бронирование](/media/wiki/bookings.png)

1. **Брони** → **Новая бронь**: контакт / клиент, ПК или зона, дата и время.
2. Свободные ПК на ближайшее время могут держаться soft-hold (~30 мин) — смотри подсказку на странице.
3. Когда гость пришёл — отметь прибытие и **запусти сеансы** с брони (не оставляй бронь «висеть» без посадки).
4. Если бронь привязана к аккаунту — при старте сеанса засчитается день **серии посещений**.
5. Выдай наушники по зоне брони: **Стандарт — проводные**, **VIP и Bootcamp — беспроводные**.

Отмена брони — только по правилам клуба и с правами; причину фиксируй.
""";

    private static string SeedFaq() => """
# Частые ошибки и что делать

**«Нет открытой кассовой смены»**  
Открой смену в **Касса**, затем повтори оплату.

**«Недостаточно ключей»**  
Продай ключ (**Купить ключ** / **Ключ CASE**) или начисли по правилам, затем снова **Открыть кейс**.

**Кейс не появился на ТВ**  
Проверь, что на втором мониторе открыт `/promo/upgrade`, обнови страницу акции при необходимости и снова нажми **Открыть кейс** в карточке нужного гостя.

**Серия не растёт после логина на ПК**  
Так и должно быть: нужен **старт сеанса** с аккаунтом, не только вход в Shell.

**Гость без аккаунта**  
Создай клиента с телефоном до старта — иначе не будет нормально работать баланс, ключи и серия.

**Не тот приз на кассе**  
Обнови карточку гостя; после открытия кейса результат ещё раз показывается диалогом. Сверь с экраном акции.

**Перепутали наушники**  
Стандарт = **проводные**. VIP и Bootcamp = **беспроводные**. Забери неверный комплект, выдай правильный, беспроводные поставь на зарядку.

**Гость просит беспроводные в Стандарте**  
По стандарту клуба в Стандарте — проводные. Пересадка в VIP/Bootcamp — только если есть место и гость оплачивает соответствующий тариф/зону.

**Оплата прошла, сеанс не стартанул**  
Не запускай второй раз «наугад». Проверь чек и статус ПК, при зависании — старшему / владельцу.

---

Владелец может править эти статьи прямо в **Инструкция** (кнопка **Редактировать**). Если текст устарел — обнови его после смены правил в зале.
""";
}
