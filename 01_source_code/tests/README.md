# Тесты

```
dotnet test ShiftClub.sln
```

Три проекта:

| Проект | Что проверяет | Нужна база |
|--------|---------------|------------|
| `ShiftClub.Domain.Tests` | правила домена | нет |
| `ShiftClub.Application.Tests` | разбор лицензионных ключей, CSV-импорт, CORS, чистка персональных данных в ошибках | нет |
| `ShiftClub.IntegrationTests` | деньги: касса, чеки, возвраты, баланс клиента | **да, PostgreSQL** |

## Интеграционные тесты и база

Денежный контур проверяется на настоящем PostgreSQL. На подменённом хранилище
такие тесты почти бесполезны: половина гарантий (транзакции, уникальные
индексы, точность `decimal`) живёт в самой базе.

По умолчанию тесты идут на:

```
Host=localhost;Port=5432;Database=shiftclub_tests;Username=shiftclub;Password=devpass
```

Другую базу можно задать переменной окружения:

```powershell
$env:SHIFTCLUB_TEST_DB = "Host=localhost;Port=5432;Database=shiftclub_tests;Username=postgres;Password=..."
dotnet test tests/ShiftClub.IntegrationTests
```

База создаётся сама и накатывается миграциями. **Нельзя указывать рабочую базу
клуба**: тесты пишут в неё свои филиалы, смены и чеки.

Если сервера нет, тесты не падают, а помечаются как пропущенные — видно, что
они не выполнялись, и сборка при этом не ломается:

```
Skipped! - Failed: 0, Passed: 0, Skipped: 17
```

## Сборка на Linux

Клиент на WPF собирается только под Windows. Чтобы `dotnet test` по решению не
падал на не-Windows машине, в `Directory.Build.props` включён
`EnableWindowsTargeting`. Собрать сам клиент Windows-only всё равно получится
только под Windows.
