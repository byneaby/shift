# SHIFT Club — Shell и клиентская система (полная карта)

Один справочник: **где что лежит**, **как связаны части**, **как выкатить обновление**, **как поставить в другой клуб**.  
Актуально для текущего клуба (`192.168.1.250`) и для развёртывания на новой площадке.

Связанные документы (узже по теме):

- [SETUP.md](SETUP.md) — установка окружения разработки
- [ARCHITECTURE.md](ARCHITECTURE.md) — общий архитектурный план
- Правило Cursor: `.cursor/rules/club-server-deploy.mdc`

---

## 1. Картина целиком

```text
┌─────────────────────── РАЗРАБОТКА (сервер, диск C:) ───────────────┐
│  C:\ShiftClub\Project         ← исходники ТОЛЬКО здесь             │
│    src/ShiftClub.Server / Web / Client/* / scripts                 │
│  ⚠ Не хранить исходники на Games D: — клиенты CCBoot видят весь D: │
└────────────────────────────────────────────────────────────────────┘
                              │ publish / robocopy / ClubDeploy
                              ▼
┌─────────────────────── СЕРВЕР КЛУБА ───────────────────────────────┐
│  Host: 192.168.1.250                                               │
│  C:\ShiftClub\Server          API (задача ShiftClubApi :5080)      │
│  C:\ShiftClub\dotnet          portable .NET runtime                │
│  C:\ShiftClub\Server\secrets.env                                   │
│  C:\ShiftClub\Server\data\client-updates   zip + manifest.json     │
│  PostgreSQL (обычно localhost:5432, БД shiftclub)                  │
└────────────────────────────────────────────────────────────────────┘
                              │ LAN http://192.168.1.250:5080
                              ▼
┌─────────────────────── КЛИЕНТСКИЕ ПК (образ / диск Games) ─────────┐
│  D:\Apps\ShiftClub\Shell\     ТОЛЬКО publish (exe/dll), без .cs    │
│    ShiftClub.Client.Shell.exe / Updater / Keeper / service\        │
│    appsettings.json           Server:BaseUrl                       │
│  %ProgramData%\ShiftClub\Client\state.json   device token / MAC    │
│  Зеркало (legacy): D:\01 SHIFT\Shell                               │
└────────────────────────────────────────────────────────────────────┘
```

Отдельно от Shell (но на том же API):

| Поверхность | Где живёт | Для кого |
|-------------|-----------|----------|
| Staff-панель | `wwwroot` (сборка Web) / LAN или `panel.*` | Касса, зал, настройки |
| Сайт / лендинг | `wwwroot/site/` | Гости, публичные страницы |
| Telegram Mini App | `wwwroot/tg-webapp/` | Клиенты в Telegram |
| Публичный API | `/api/public/*`, `/api/tg/*` | Без device-token ПК |

Shell **не** обслуживает гостевой веб — только киоск на игровом ПК.

---

## 2. Проекты клиента в репозитории

Корень: `src/ShiftClub.Client/`

| Проект | Тип | Назначение |
|--------|-----|------------|
| **ShiftClub.Client.Shell** | WPF | Основная оболочка: регистрация ПК, lock-экран, сеансы, каталог игр/бара/новостей, SignalR, проверка обновлений |
| **ShiftClub.Client.Updater** | Console, single-file | Применяет zip-пакет: гасит Shell, распаковывает, поднимает Shell снова |
| **ShiftClub.Client.Service** | Windows Service `ShiftClubClient` | Работает от SYSTEM: ACL/registry, фиксы NVIDIA-контейнера. **Не** стартует Shell сам по себе |
| **ShiftClub.Client.Keeper** | User-session watchdog | Если Shell нет в сессии пользователя — перезапускает; уважает admin/update lock |
| **ShiftClub.Client.Installer** | WinForms Setup | Распаковка payload, запись API URL, ярлыки, установка сервиса |

Текущая версия клиента задаётся в:

`src/ShiftClub.Client/ShiftClub.Client.Shell/ClientVersionInfo.cs`  
(скрипт publish переписывает её вместе с `<Version>` в `.csproj`).

Конфиг Shell рядом с exe:

```json
{
  "Server": {
    "BaseUrl": "http://192.168.1.250:5080"
  }
}
```

У Service дополнительно путь к exe Shell:

```json
{
  "Server": { "BaseUrl": "http://192.168.1.250:5080" },
  "Shell":  { "ExePath": "D:\\Apps\\ShiftClub\\Shell\\ShiftClub.Client.Shell.exe" }
}
```

---

## 3. Пути на текущем клубе (канон)

### 3.1. API-сервер

| Что | Путь / значение |
|-----|-----------------|
| Хост | `192.168.1.250` |
| Каталог API | `C:\ShiftClub\Server` |
| Portable .NET | `C:\ShiftClub\dotnet` |
| Секреты | `C:\ShiftClub\Server\secrets.env` (не в git) |
| Шаблон секретов | `src/ShiftClub.Server/secrets.env.example` |
| Старт | `Start-ShiftClubApi.ps1` (копия в каталоге Server) |
| Планировщик | задача **`ShiftClubApi`** |
| Порт | **5080** на `0.0.0.0` |
| Пакеты обновлений клиента | `C:\ShiftClub\Server\data\client-updates` |
| Legacy API | `D:\ShiftClub\Server` — **не канон** (см. `MOVED-TO-C.txt`) |

Почему API на **C:**: при offline диска Games / superclient / проблемах CCBoot C: обычно живой.

Минимальный `secrets.env`:

```env
ConnectionStrings__Default=Host=127.0.0.1;Port=5432;Database=shiftclub;Username=shiftclub;Password=...
Jwt__SigningKey=...длинный_секрет_≥32...
Jwt__ExpirationMinutes=720
Seed__OwnerLogin=owner
Seed__OwnerPassword=...
EnableSwagger=false
```

После смены `Seed__OwnerPassword` — перезапуск задачи `ShiftClubApi`.

### 3.2. Клиент на игровых ПК / образе

| Что | Путь |
|-----|------|
| **Канон (CCBoot / Games disk)** | `D:\Apps\ShiftClub\Shell\` |
| Зеркало / legacy | `D:\01 SHIFT\Shell` — держать в sync при релизе; автозагрузка образа → канон |
| Состояние устройства | `%ProgramData%\ShiftClub\Client\state.json` |
| Скачанные обновления | `%ProgramData%\ShiftClub\Client\updates\` |

**Важно для CCBoot:** `InstallationId` в Shell привязан к **MAC** (`mac` + hex), а не к общему GUID в ProgramData образа — иначе все ПК выглядели бы как один.

Сервис `ShiftClubClient` должен быть **в VHD/образе клиентских ПК**, не на CCBoot-сервере.

iCafe / CCBoot boot loader **не трогать** без явной просьбы.

### 3.3. Разработка на этом ПК

| Что | Значение |
|-----|----------|
| Workspace | `C:\ShiftClub\SHIFT PROJECT` (не на Games D:) |
| Dev API | `http://localhost:5080` или `http://192.168.1.200:5080` |
| Dev .NET | обычно `D:\Dev\dotnet` (скрипты publish прописывают PATH сюда) |
| Dev PostgreSQL | `D:\Dev\PostgreSQL` (см. SETUP.md) |

---

## 4. Состав каталога Shell на ПК

Типичное содержимое `D:\Apps\ShiftClub\Shell\` после publish / ClubDeploy:

```text
D:\Apps\ShiftClub\Shell\
  ShiftClub.Client.Shell.exe          # киоск
  ShiftClub.Client.Updater.exe        # только exe рядом с Shell (не полный runtime Updater)
  ShiftClub.Client.Keeper.exe
  appsettings.json                    # BaseUrl клуба
  *.dll / runtime (self-contained)
  service\
    ShiftClub.Client.Service.exe
    appsettings.json                  # BaseUrl + Shell:ExePath
    ...
```

Self-contained win-x64: на игровых ПК **не нужен** отдельно установленный .NET Desktop Runtime.  
API-серверу runtime нужен (portable в `C:\ShiftClub\dotnet` или системный).

---

## 5. Как ПК «вступает» в клуб (device token)

1. Shell читает `Server:BaseUrl` из `appsettings.json`.
2. Берёт MAC, выставляет `InstallationId = mac + <hex MAC>`.
3. `POST /api/client/computers/register` (без employee JWT).
4. Состояние пишет в `%ProgramData%\ShiftClub\Client\state.json`.
5. **Уже одобренный MAC** → в ответе plaintext `DeviceToken` → Shell сохраняет.
6. **Новый ПК** → `PendingApproval` → сотрудник на карте зала одобряет (зона + имя) → токен выдаётся → Shell подхватывает при следующем register/poll.
7. Дальше все client API и SignalR: заголовок **`X-Device-Token`** (на hub — query `device_token=`). На сервере хранится только **хэш** токена.

Без одобрения ПК не получает команды зала и не ведёт сеансы как боевой компьютер.

---

## 6. Автообновление клиента

### 6.1. Публикация с машины разработки

```powershell
cd "C:\ShiftClub\SHIFT PROJECT"
.\scripts\Publish-ClientUpdate.ps1 -Version 0.6.92 -ReleaseNotes "кратко что изменилось" -ClubDeploy
```

Что делает скрипт:

1. Синхронизирует `ClientVersionInfo` + Version в csproj.
2. `dotnet publish` Shell (self-contained win-x64) → stage.
3. Publish Updater (single-file) → копирует **только** `ShiftClub.Client.Updater.exe` в stage Shell.
4. Publish Service → `shell\service\` + bake `appsettings.json`.
5. Bake Shell `appsettings.json` с `BaseUrl=http://192.168.1.250:5080`.
6. Publish Keeper → кладёт exe в корень package.
7. Zip: `ShiftClub.Client.Shell-<ver>.zip` + `manifest.json` (version, sha256, channel, …).
8. По умолчанию пакеты → `src\ShiftClub.Server\data\client-updates\` (+ sync в bin Debug/Release если есть).
9. **`-ClubDeploy`:** копирует zip+manifest в `C:\ShiftClub\Server\data\client-updates` и перезаписывает файлы в `D:\Apps\ShiftClub\Shell` и `D:\01 SHIFT\Shell`.

Параметры для другого клуба (обязательно поменять defaults):

| Параметр | Default сейчас | Зачем |
|----------|----------------|-------|
| `-ApiUrl` | `http://192.168.1.250:5080` | Upload / проверка |
| `-ClubShellDir` | `D:\Apps\ShiftClub\Shell` | путь клиента на площадке |
| `-ClubShellMirror` | `D:\01 SHIFT\Shell` | второе зеркало (можно `""`) |
| `-ClubPackagesDir` | `C:\ShiftClub\Server\data\client-updates` | каталог пакетов у API |

Опционально `-Upload -Token <jwt>` → `POST /api/client-updates/publish`.

### 6.2. Поведение на ПК

1. Shell с device-token ~каждые **5 минут** (или команда зала `UpdateClient`):  
   `GET /api/client/updates/check?currentVersion=…`
2. Если есть новее — скачивает package, проверяет SHA256 →  
   `%ProgramData%\ShiftClub\Client\updates\pending-<ver>.zip`
3. Достаёт Updater, запускает с путём пакета и каталогом установки.
4. Updater: lock-файл обновления, kill Shell, stream-extract в install dir, reload Service, старт Shell.
5. Во время **активного сеанса** автообновление обычно пропускается; force с зала может откладываться.

Админка пакетов в панели: маршрут **`/updates`**.

### 6.3. Первый установщик (новая площадка / чистый ПК)

```powershell
.\scripts\Build-ClientInstaller.ps1 -Version 0.6.92 -ServerUrl http://<IP_API_КЛУБА>:5080
```

Результат: `dist\installer\ShiftClub.Client.Setup-<ver>.exe`  
Default путь установки в UI: `D:\Apps\ShiftClub\Shell`.

Если на уже установленном Shell пропал Updater:

```powershell
.\scripts\Bootstrap-ClientUpdater.ps1
```

(подтягивает Updater из последнего пакета).

---

## 7. Деплой API (кратко)

С машины разработки (типичный цикл клуба):

1. `dotnet publish` проекта `ShiftClub.Server` (Production).
2. Скопировать DLL / содержимое publish в `C:\ShiftClub\Server` (не затирая `secrets.env` и при необходимости `data\`).
3. Собрать Web: `npm run build` в `ShiftClub.Web` → артефакты в `wwwroot` сервера.
4. `robocopy` `wwwroot` (site, tg-webapp, assets панели).
5. Перезапуск задачи **`ShiftClubApi`**.

Старт вручную:

```powershell
# на сервере клуба, из C:\ShiftClub\Server
.\Start-ShiftClubApi.ps1
```

Скрипт грузит `secrets.env`, ищет `C:\ShiftClub\dotnet\dotnet.exe`, слушает `http://0.0.0.0:5080`.

Dev на этом ПК:

```powershell
dotnet run --project src/ShiftClub.Server --urls http://0.0.0.0:5080
```

---

## 8. Поставить систему в **другой** клуб (чеклист)

Цель: отдельный API + БД + клиенты, своя сеть, свои секреты. Не копировать слепо `secrets.env` и device tokens.

### 8.1. Сервер клуба (Windows)

1. **PostgreSQL**  
   - Создать пользователя/БД `shiftclub` (или свои имена).  
   - Строка в `secrets.env` → `ConnectionStrings__Default`.

2. **Каталоги** (рекомендуемая схема как у текущего клуба):
   ```text
   C:\ShiftClub\Server          # API
   C:\ShiftClub\dotnet          # portable runtime (скопировать win-x64 SDK/runtime набор)
   C:\ShiftClub\Server\data\client-updates
   D:\Apps\ShiftClub\Shell      # если у площадки есть D: Games / образ
   ```
   Если диски другие — зафиксировать пути и **везде** прокинуть их в скрипты/appsettings (см. §9).

3. **Опубликовать API**  
   - Publish `ShiftClub.Server` → `C:\ShiftClub\Server`.  
   - Скопировать `Start-ShiftClubApi.ps1`.  
   - `secrets.env` из `secrets.env.example`: уникальные `Jwt__SigningKey`, пароль owner, connection string.

4. **Планировщик**  
   - Задача `ShiftClubApi`: запуск `Start-ShiftClubApi.ps1` или `dotnet ShiftClub.Server.dll` с env Production, при старте системы, от SYSTEM/сервисной УЗ с правами на каталог и Postgres.

5. **Сеть**  
   - Firewall: входящий TCP **5080** с LAN игровых ПК и кассы.  
   - Зафиксировать IP API (статический), например `192.168.x.y`.

6. **Первый вход**  
   - Открыть панель (LAN IP:5080 или настроенный host).  
   - Логин `Seed__OwnerLogin` / `Seed__OwnerPassword`.  
   - Сменить пароль owner, настроить филиал, зоны, тарифы, ПК.

7. **Telegram (если нужен)**  
   - В настройках панели: bot token, allowlist, **https** `PublicWebAppBaseUrl` для Mini App.  
   - Без https Telegram WebApp не откроется.

8. **Публичный домен / tunnel** (опционально)  
   - Cloudflare Tunnel / nginx → на `5080`.  
   - Хосты: лендинг, `panel`, `tg` — по принятой схеме клуба.  
   - В репозитории домены не зашиты жёстко в client Shell; важны настройки Telegram и DNS.

### 8.2. Клиент на игровых ПК

**Вариант A — Installer**

1. Собрать с правильным URL:  
   `.\scripts\Build-ClientInstaller.ps1 -ServerUrl http://<IP_API>:5080`
2. Прогнать Setup на эталонном ПК / в образе → путь `D:\Apps\ShiftClub\Shell` (или свой).
3. Убедиться, что служба `ShiftClubClient` установлена и запущена **в образе клиента**.
4. Автозагрузка / shell replacement по политике клуба (не ломая CCBoot без нужды).
5. ПК появится в зале как pending → Approve.

**Вариант B — ClubDeploy / ручное копирование**

1. Перед publish временно поправить bake BaseUrl в `Publish-ClientUpdate.ps1` **или** после копирования руками править `appsettings.json` на целевых путях (Shell + `service\`).
2. `Publish-ClientUpdate.ps1 -Version x.y.z -ClubDeploy -ClubShellDir <путь> -ClubPackagesDir <путь> -ApiUrl http://<IP>:5080`
3. Зафиксировать образ CCBoot / рассылку на ПК.

### 8.3. Что обязательно уникально на новой площадке

| Артефакт | Почему |
|----------|--------|
| `Jwt__SigningKey` | иначе компрометация/коллизии токенов |
| `Seed__OwnerPassword` | доступ owner |
| PostgreSQL пароль и данные | отдельный клуб = отдельная БД |
| Одобрения ПК / device tokens | привязаны к этой БД и MAC |
| Telegram bot (желательно свой) | иначе чужие чаты/webhook |
| `Server:BaseUrl` в Shell/Service | IP другого клуба |

### 8.4. Чего **не** копировать «как есть» с текущего клуба

- `secrets.env`
- Живую БД без осознанной миграции/анонимизации
- `state.json` с чужого ПК (токен другого клуба)
- Hardcoded IP `192.168.1.250` / `200` в пакетах без пересборки или правки appsettings

---

## 9. Что захардкожено под текущий клуб

При переносе проверить и заменить:

| Место | Значение сейчас |
|-------|-----------------|
| Default в Shell / publish bake | `http://192.168.1.250:5080` |
| Service `Shell:ExePath` в publish | `D:\Apps\ShiftClub\Shell\ShiftClub.Client.Shell.exe` |
| Defaults `Publish-ClientUpdate.ps1` | ClubShellDir, Mirror, PackagesDir, ApiUrl |
| Defaults `Build-ClientInstaller.ps1` | `-ServerUrl` → `.250` |
| PATH в publish-скриптах | `D:\Dev\dotnet` (машина разработки) |
| Fallback пути Updater / Keeper | часто канон `D:\Apps\…` |
| CCBoot-окружение | Games на D:, InfGPU `C:\CCBoot\InfGPU`, VHD-скрипты в `scripts\` |

Пути `C:\ShiftClub\*` и `D:\Apps\ShiftClub\Shell` — **соглашение**, не магия платформы: можно другие, но тогда синхронно менять install, service appsettings, ClubDeploy и автозагрузку образа.

---

## 10. Связь Shell ↔ API (операции)

Shell (с device token) типично использует client-эндпоинты:

- регистрация / heartbeat / команды
- старт/стоп сеанса, баланс, тарифы
- каталог приложений, бар, новости
- проверка и скачивание client-updates
- SignalR: события зала, команды (`Lock`, `Unlock`, `UpdateClient`, …)

Staff-панель и Mini App ходят **другими** auth-схемами (JWT сотрудника / JWT tg) — не путать с `X-Device-Token`.

Команда обновления с карты зала: `UpdateClient` → тот же pipeline, что и таймер 5 мин.

---

## 11. Типовые операции

### Выкатить новую версию Shell на текущий клуб

```powershell
cd "C:\ShiftClub\SHIFT PROJECT"
.\scripts\Publish-ClientUpdate.ps1 -Version <x.y.z> -ReleaseNotes "…" -ClubDeploy
```

Потом: либо ждать авто-check, либо с зала отправить UpdateClient на ПК / перезаписать образ и перезагрузить клиентов.

Если файлы заняты запущенным Shell — кратковременно остановить Shell на эталоне/сервере образа перед копированием, либо полагаться на Updater на живых ПК.

### Починить «Updater не найден»

```powershell
.\scripts\Bootstrap-ClientUpdater.ps1
```

### Сбросить / сменить owner после seed

1. Править `Seed__OwnerPassword` в `C:\ShiftClub\Server\secrets.env` (или password hash в БД — по принятой процедуре клуба).  
2. Перезапуск `ShiftClubApi`.

### Новый ПК на уже работающем клубе

1. Shell с правильным BaseUrl в образе.  
2. Зал → pending computer → Approve (зона, имя).  
3. Проверить heartbeat на карте зала.

---

## 12. Диаграмма обновления (сжато)

```text
[Dev] Publish-ClientUpdate.ps1
        │
        ├─► zip + manifest.json
        │      └─► C:\ShiftClub\Server\data\client-updates
        │
        └─► (-ClubDeploy) файлы → D:\Apps\ShiftClub\Shell (+ mirror)

[ПК] Shell ──check──► API
       │ newer?
       ▼
     download zip → ProgramData\...\updates\
       │
       ▼
     Updater.exe ──kill Shell──extract──start Shell──reload Service
```

---

## 13. Быстрый «где искать файл»

| Ищу | Открыть |
|-----|---------|
| Версия клиента | `src/.../Shell/ClientVersionInfo.cs` |
| Publish клиента | `scripts/Publish-ClientUpdate.ps1` |
| Installer | `scripts/Build-ClientInstaller.ps1` |
| Старт API клуба | `scripts/Start-ShiftClubApi.ps1` → на сервере рядом с DLL |
| Секреты клуба | `C:\ShiftClub\Server\secrets.env` |
| Шаблон секретов | `src/ShiftClub.Server/secrets.env.example` |
| Канон путей клуба | `.cursor/rules/club-server-deploy.mdc` |
| UI Shell | `src/.../Shell/MainWindow.xaml(+.cs)`, `ShellClientAgent.cs` |
| Пакеты на сервере | `C:\ShiftClub\Server\data\client-updates\` |
| Установленный Shell | `D:\Apps\ShiftClub\Shell\` |
| Device state | `%ProgramData%\ShiftClub\Client\state.json` |
| Панель / TG / сайт | `src/ShiftClub.Server/wwwroot/` (+ исходники Web / tg-webapp) |

---

## 14. Анти-паттерны

- Класть API на диск Games (`D:`), который уходит в offline вместе с образом — канон **C:\ShiftClub\Server**.
- Ставить `ShiftClubClient` только на сервер CCBoot, а не в клиентский VHD.
- Публиковать framework-dependent Shell на ПК без Desktop Runtime («You must install or update .NET…»).
- Копировать Updater **со всеми** его DLL поверх Shell (ломает WPF/`WindowsBase`) — в пакете только **Updater.exe**.
- Один общий `InstallationId` на весь образ без MAC — все ПК слипаются в один computer.
- Ломать загрузчик iCafe/CCBoot «заодно» при обновлении Shell.

---

*Документ описывает фактическую схему текущего клуба и переносимый чеклист. При смене канонических путей на площадке — обновить этот файл и `.cursor/rules/club-server-deploy.mdc`.*
