# SHIFT Club

Система управления компьютерным клубом (время, касса, бар, клиенты ПК).

## Где что лежит

См. `C:\ShiftClub\README.txt`.

| Путь | Назначение |
|------|------------|
| `C:\ShiftClub\Project` | Исходники (эта папка) |
| `C:\ShiftClub\Server` | Рабочий API клуба |
| `C:\ShiftClub\dotnet` | Portable .NET для API |
| `D:\Apps\ShiftClub\Shell` | Клиент на ПК зала (только publish) |

**Не класть исходники на `D:`** — диск виден клиентам CCBoot.

## Стек

- Backend: .NET 8, ASP.NET Core, EF Core, PostgreSQL, SignalR, Serilog
- Web: React + TypeScript + Vite
- Client: WPF Shell + Windows Service / Keeper / Updater

## Быстрый старт

```powershell
cd C:\ShiftClub\Project
dotnet restore
dotnet build
dotnet test

# API (dev)
dotnet run --project src/ShiftClub.Server --urls http://0.0.0.0:5080

# Панель
cd src/ShiftClub.Web
npm install
npm run dev
```

- API: http://localhost:5080 или http://192.168.1.200:5080  
- Клуб (production): http://192.168.1.250:5080  
- Health: `/health`

## Публикация клиента

```powershell
.\scripts\Publish-ClientUpdate.ps1 -Version 0.6.x -ClubDeploy
```

Пакеты → `C:\ShiftClub\Server\data\client-updates`  
Файлы Shell → `D:\Apps\ShiftClub\Shell` (и зеркало `D:\01 SHIFT\Shell`)

## Документация

- [docs/SETUP.md](docs/SETUP.md)
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- [docs/SHELL-SYSTEM.md](docs/SHELL-SYSTEM.md)
- **Продукт / конкуренты / продажи:** [docs/product/README.md](docs/product/README.md)
