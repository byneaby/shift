# SHIFT Club — архитектурный план

Статус: утверждён для старта Этапа 1.  
Стек: .NET 8 LTS · ASP.NET Core · EF Core · PostgreSQL · React + Vite + TS · WPF Client.

---

## 1. Структура solution

```text
shift/
├── ShiftClub.sln
├── src/
│   ├── ShiftClub.Server/          # ASP.NET Core host (API + SignalR + jobs)
│   ├── ShiftClub.Domain/          # Entities, enums, value objects, domain rules
│   ├── ShiftClub.Application/     # Use-cases, validators, interfaces
│   ├── ShiftClub.Infrastructure/  # EF Core, Identity, SignalR hubs impl, files
│   ├── ShiftClub.Shared/          # DTO, API contracts, SignalR contracts, error codes
│   ├── ShiftClub.Web/             # React + Vite + TypeScript панель
│   └── ShiftClub.Client/
│       ├── ShiftClub.Client.Core/
│       ├── ShiftClub.Client.Infrastructure/
│       ├── ShiftClub.Client.Service/   # Windows Service
│       ├── ShiftClub.Client.Shell/     # WPF оболочка
│       └── ShiftClub.Client.Updater/
├── tests/
│   ├── ShiftClub.Domain.Tests/
│   ├── ShiftClub.Application.Tests/
│   ├── ShiftClub.Server.IntegrationTests/
│   └── ShiftClub.Client.Tests/
├── docker/
│   ├── docker-compose.yml
│   └── .env.example
└── docs/
```

Допускается объединить Api-слой внутрь `ShiftClub.Server` (как в мастер-промте), а Domain/Application/Infrastructure — отдельными class libraries для чистых границ.

---

## 2. Проекты и ответственность

| Проект | Ответственность |
|--------|-----------------|
| **ShiftClub.Domain** | Сущности, статусы, value objects (`Money`, `Period`), инварианты |
| **ShiftClub.Application** | Команды/запросы, FluentValidation, интерфейсы репозиториев/сервисов |
| **ShiftClub.Infrastructure** | PostgreSQL + EF Core, Identity, Serilog sinks, backup, фон. задачи |
| **ShiftClub.Shared** | DTO, коды ошибок, SignalR-контракты — общее для Server/Web/Client |
| **ShiftClub.Server** | Controllers, Hubs, Middleware, DI composition, OpenAPI |
| **ShiftClub.Web** | Касса, карта зала, бар, отчёты, настройки |
| **ShiftClub.Client.\*** | Регистрация ПК, heartbeat, lock/shell, offline-кэш SQLite, автообновление |

---

## 3. Основные сущности БД (Этап 1 + каркас MVP)

**Оргструктура:** `Branch`, `Zone`, `AppSetting`  
**Сотрудники/доступ:** `Employee`, `EmployeeCredential`, `Role`, `Permission`, `RolePermission`, `EmployeeRole`, `RefreshToken`, `AuditLog`  
**ПК (каркас):** `Computer`, `ComputerHeartbeat`, `ComputerCommand`  
**Клиенты/финансы (каркас):** `Customer`, `CustomerBalanceTransaction` (ledger), `CashRegister`, `CashShift`  
**Сеансы/тарифы (каркас с этапа 3):** `Tariff`, `GamingSession`, `SessionHistory`  
**Касса/продажи (этап 4+):** `Receipt`, `ReceiptItem`, `Payment`, `Refund`, `CashMovement`, `Expense`  
**Бар/склад (этап 5+):** `Product`, `InventoryMovement`, `BarOrder`

У каждой сущности audit-поля: `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, при необходимости `IsDeleted` (soft delete только где уместно; финансы — не удалять).

Деньги: только `decimal(18,2)`. Валюта по умолчанию: KZT. TZ: `Asia/Almaty`.

---

## 4. Ключевые связи

```text
Branch 1──* Zone 1──* Computer
Branch 1──* CashRegister 1──* CashShift
Branch 1──* Employee (через EmployeeRole/права)
Customer 1──* CustomerBalanceTransaction (ledger)
Customer 1──* GamingSession *──1 Computer
GamingSession *──1 Tariff
CashShift 1──* Payment / CashMovement / Receipt
Receipt 1──* ReceiptItem
Receipt 1──* Payment
Computer 1──* ComputerHeartbeat
Computer 1──* ComputerCommand
Role *──* Permission
Employee *──* Role
```

---

## 5. Авторизация

- **Сотрудники (Web):** ASP.NET Core Identity + JWT access token (короткий TTL) + refresh token (ротационный, в БД).
- **PIN кассы:** отдельный хэш (не пароль), только при открытой смене / на разрешённых терминалах; rate-limit + lockout.
- **Клиентские ПК:** device token после подтверждения registration code; не путать с employee credentials.
- **Права:** permission-based (`sessions.start`, `cash.open_shift`, …), роли — наборы permission; проверка только на сервере.
- **Аудит:** вход/выход, смена пароля, force logout, критические операции.
- **2FA владельца:** интерфейс `ITwoFactorProvider` — реализация после MVP.

---

## 6. Обмен Server ↔ Web ↔ Client

```text
[Web React] ──REST──► [Server API]
     │                      │
     └──SignalR─────────────┤
                            │
[Client Service] ◄─SignalR──┘
        │ named pipes / local IPC
[Client Shell WPF]
```

- REST: команды кассы, CRUD, отчёты.  
- SignalR groups: `branch:{id}`, `computer:{id}`, `cashier:{userId}`.  
- Идемпотентность: заголовок `Idempotency-Key` на оплаты, старт сеанса, продление.  
- Конкуренция: row version / optimistic concurrency на `GamingSession`, `CashShift`, балансе.

---

## 7. Игровой сеанс

1. Кассир выбирает ПК + тариф/длительность + гость или клиент.  
2. Сервер создаёт `GamingSession` (Active), списывает/резервирует оплату через ledger + Receipt/Payment.  
3. SignalR → Client: `Unlock` / `StartSession(endsAt)`.  
4. BackgroundService: предупреждения 15/10/5/1 мин, auto-end.  
5. По окончании: lock shell, статус Completed/PaymentPending, пересчёт в SessionHistory.  
6. После сбоя: при reconnect сверяется `EndsAt` с сервером; сервер — источник истины для денег.

---

## 8. Кассовая смена

- Без открытой смены оплаты запрещены (если setting не разрешил иное).  
- Open: сотрудник + касса + opening cash + PIN.  
- Учёт: sales cash/card, deposits, refunds, expenses, in/out.  
- Close: fact vs expected, discrepancy, manager approval при превышении порога.  
- Одна активная смена на кассу; передача смены — отдельная операция с аудитом.

---

## 9. Финансовый ledger

- Баланс клиента = сумма проведённых `CustomerBalanceTransaction` (кэш-поле `Customer.Balance` обновляется в той же транзакции БД).  
- Запись неизменяема; коррекция — reverse/adjustment.  
- Обязательны: amount, direction, balanceBefore/After, sourceType/sourceId, employeeId, idempotencyKey, status.  
- Чек (`Receipt`) — документ продажи; Payment — способ оплаты; Ledger — движение баланса клиента.

---

## 10. Устойчивость при потере сети

| Узел | Поведение |
|------|-----------|
| Client offline | Таймер по EndsAt; очередь команд/событий в SQLite; нельзя продлевать локально |
| EndsAt в offline | Политика: lock shell + сохранить OfflineEvent + sync |
| Web reconnect | TanStack Query refetch + SignalR resubscribe |
| Server restart | Sessions Active восстанавливаются; BackgroundService продолжает по EndsAt |
| Дубли POST | Idempotency-Key → тот же результат |

---

## 11. Итерации разработки

| # | Содержание | Результат |
|---|------------|-----------|
| **0** | Установка SDK/БД/Git | Готовое окружение |
| **1** | Solution, проекты, Serilog, health, Swagger | Собирается Server |
| **2** | EF Core + PostgreSQL + Branch/Zone/Employee/Role/Permission | Миграции применяются |
| **3** | Login JWT + seed Owner + Web login stub | Вход в панель |
| **4** | Computers CRUD + карта (stub) | Список ПК в UI |
| **5** | Client Service + heartbeat + SignalR | ПК Online |
| **6** | Tariffs + Guest session start/end | Рабочий сеанс |
| **7** | Cash register + shift + payment | Касса MVP |
| **8** | Bar + inventory basic | Бар MVP |
| **9** | Reports + audit + backup | MVP пакет |
| **10** | Hardening, installer, docs | Production-ready |

---

## 12. Файлы первой итерации (после установки инструментов)

```text
ShiftClub.sln
src/ShiftClub.Domain/ShiftClub.Domain.csproj
src/ShiftClub.Application/ShiftClub.Application.csproj
src/ShiftClub.Infrastructure/ShiftClub.Infrastructure.csproj
src/ShiftClub.Shared/ShiftClub.Shared.csproj
src/ShiftClub.Server/ShiftClub.Server.csproj
src/ShiftClub.Server/Program.cs
src/ShiftClub.Server/appsettings.json
src/ShiftClub.Server/appsettings.Development.json
Directory.Build.props
.gitignore
docker/docker-compose.yml
docker/.env.example
README.md
docs/ARCHITECTURE.md   (этот файл)
docs/SETUP.md
```

Код приложения пишется только после установки .NET SDK и подтверждения, что `dotnet --version` работает.
