# Установка окружения разработки (Windows)

Диск **C:** почти заполнен (~16 ГБ свободно). Ставим SDK и тяжёлые пакеты на **D:\Dev**.

## Что нужно

| Инструмент | Зачем | Куда ставить |
|------------|--------|--------------|
| Git for Windows | Версии, репозиторий | D:\Dev\Git (или default) |
| .NET 8 SDK | Server + Client (WPF) | default или D |
| Node.js LTS 20/22 | React/Vite веб-панель | default |
| PostgreSQL 16 | База данных | **D:\Dev\PostgreSQL** |
| Visual Studio 2022 Community *или* только VS Build Tools | WPF/Windows Service | по желанию |
| (опционально) Docker Desktop | compose PostgreSQL+API | если хватит места на C |

Cursor уже есть — его достаточно как редактор для C#/TS.

---

## Способ A — вручную (рекомендуется)

### 1. Создать папки

```powershell
New-Item -ItemType Directory -Force -Path D:\Dev, D:\Dev\Downloads, D:\Dev\PostgreSQL, D:\Dev\Repos
```

### 2. Git

Скачать: https://git-scm.com/download/win  
Установить. В PATH должен появиться `git`.

### 3. .NET 8 SDK

Скачать: https://dotnet.microsoft.com/download/dotnet/8.0  
Выбрать **SDK 8.x** для Windows x64.  
Проверка: `dotnet --version` → `8.0.x`

### 4. Node.js LTS

Скачать: https://nodejs.org/ (LTS)  
Проверка: `node -v`, `npm -v`

### 5. PostgreSQL 16

Скачать: https://www.enterprisedb.com/downloads/postgres-postgresql-downloads  
При установке:

- Installation Directory: `D:\Dev\PostgreSQL\16`
- Data Directory: `D:\Dev\PostgreSQL\16\data`
- Port: `5432`
- Superuser password: запомнить (положим в `.env`, не в git)
- Locale: default

После установки создать БД:

```sql
CREATE USER shiftclub WITH PASSWORD 'ваш_пароль';
CREATE DATABASE shiftclub OWNER shiftclub;
```

Строка подключения (пример):

```text
Host=localhost;Port=5432;Database=shiftclub;Username=shiftclub;Password=ваш_пароль
```

### 6. Visual Studio 2022 Community (для WPF-клиента)

Скачать: https://visualstudio.microsoft.com/downloads/  
Workload: **.NET desktop development**  
Можно отложить до Этапа 2 (клиент ПК) — для API и Web достаточно SDK.

---

## Способ B — winget (если появится в системе)

```powershell
winget install Git.Git --accept-package-agreements --accept-source-agreements
winget install Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements
winget install OpenJS.NodeJS.LTS --accept-package-agreements --accept-source-agreements
winget install PostgreSQL.PostgreSQL.16 --accept-package-agreements --accept-source-agreements
```

---

## Проверка готовности

```powershell
git --version
dotnet --version
node -v
npm -v
psql --version   # если psql в PATH
```

Когда все команды отвечают — можно создавать solution (`dotnet new`).
