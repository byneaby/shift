# SHIFT Club — архив проекта для разработки

Снимок от **05.10.2026**. Shell в коде — версия **0.8.4**, база — копия рабочей базы клуба на дату снимка.

## Что внутри

| Папка | Что это |
|---|---|
| `01_source_code` | Весь исходный код: `ShiftClub.sln`, `src` (API, веб-панель, Shell, сервисы), `tests`, `scripts`, `docs`, `docker`, правила `.cursor` |
| `02_database` | База: `shiftclub_full.dump` (данные), `app_settings_safe.sql` (настройки без секретов), `shiftclub_schema.sql` (только схема, для чтения), `restore.ps1` (восстановление) |
| `03_server_files` | Файлы, которые API хранит на диске: картинки бара, обложки игр, звуки, wiki, брендинг, отклики на вакансии. Положить в папку `data` рядом с API |
| `04_config` | `secrets.env.example` — шаблон секретов |

**Не включено намеренно:** сборки (`bin`, `obj`, `dist`, `node_modules`), старые пакеты обновлений Shell (11 ГБ), установщик `client.zip`, настоящий `secrets.env` клуба.

## Важно про безопасность

- В дампе **реальные данные клиентов** (телефоны, имена, балансы, история). Архив не выкладывать в открытый доступ.
- В `app_settings_safe.sql` **Telegram-бот выключен и токен пустой**, CRM-рассылки выключены, Kaspi POS убран. Иначе API на другой машине подключился бы к боту клуба и мог бы слать сообщения реальным клиентам. Для тестов бота заведите **отдельного** бота в @BotFather и впишите его токен в панели: «Настройки → Telegram».
- Никогда не запускайте dev-API с настоящим `secrets.env` или токеном клуба.

## Что установить

- .NET SDK **8.0** (на клубе 8.0.411)
- Node.js **24** (на клубе v24.18)
- PostgreSQL **18**

## Запуск на новой машине

1. **База.** В PowerShell из папки `02_database`:
   ```powershell
   .\restore.ps1
   ```
   Скрипт спросит пароль `postgres`, создаст роль `shiftclub`, базу `shiftclub`, восстановит данные и выведет строку подключения.

2. **Секреты.** Скопируйте `04_config\secrets.env.example` в `01_source_code\src\ShiftClub.Server\secrets.env` и заполните:
   - `ConnectionStrings__Default` — строка, которую вывел `restore.ps1`;
   - `Jwt__SigningKey` — любая случайная строка длиной от 32 символов;
   - `Seed__OwnerPassword` — пароль owner (применяется только к новой пустой базе; в восстановленной базе логины и пароли сотрудников — как в клубе).

   Для запуска из IDE переменные из `secrets.env` нужно задать как переменные окружения, либо через `dotnet user-secrets` / `appsettings.Development.json` (строка `ConnectionStrings:Default`).

3. **Файлы сервера.** Скопируйте содержимое `03_server_files\data` в `01_source_code\src\ShiftClub.Server\data`.

4. **API:**
   ```powershell
   cd 01_source_code
   dotnet run --project src\ShiftClub.Server --urls http://localhost:5080
   ```
   При старте API сам применяет миграции EF и проверяет seed. Проверка: http://localhost:5080/health

5. **Веб-панель:**
   ```powershell
   cd 01_source_code\src\ShiftClub.Web
   npm install
   npm run dev
   ```
   **Важно:** в `vite.config.ts` прокси API указывает на `http://192.168.1.200:5080` (машина в клубе). На новой машине замените его на `http://localhost:5080`.

6. **Тесты:** `dotnet test` из `01_source_code`.

7. **Shell** (WPF, только Windows): `src\ShiftClub.Client\ShiftClub.Client.Shell`. Адрес API — в его `appsettings.json`.

## Как вернуть изменения в клуб

Выкладка на клуб (`192.168.1.250`) делается скриптами из `scripts`: `Deploy-Web.ps1`, `Publish-ClientUpdate.ps1` и копированием DLL API в `C:\ShiftClub\Server`. Скрипты рассчитаны на запуск на сервере клуба. С другой машины ничего в клуб автоматически не уходит.
