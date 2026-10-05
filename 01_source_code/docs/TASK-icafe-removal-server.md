# Задача: найти и полностью отключить автозапуск iCafeMenu на клиентских ПК

Ты работаешь **непосредственно на сервере клуба** `WIN-KOMOLOPI2T3` (192.168.1.250, Windows Server 2025). У тебя есть права администратора. Работай локально, SSH не нужен.

## Контекст (что за система)

Это компьютерный клуб (~60 ПК). Клиентские ПК бездисковые, грузятся по сети через **CCBootCloud** (build 20260627) с этого сервера. Параллельно стоял **iCafeCloudServer 2.0** (меню/учёт от Youngzsoft, тот же вендор, общая лицензия `032711175951`, cafe id `91153`, центр `eu25.icafecloud.com`).

Мы **уходим с iCafe** на свою систему **SHIFT Club**:
- Наш API работает на этом сервере: `D:\ShiftClub\Server` (порт 5080, scheduled task `ShiftClubApi`).
- Наш клиентский лаунчер: `D:\01 SHIFT\Shell\ShiftClub.Client.Shell.exe` (должен стоять в автозагрузке образа).
- **CCBoot должен продолжать работать** — он грузит ПК. Его НЕ трогать, НЕ переустанавливать, НЕ останавливать.

**Цель: клиентские ПК после загрузки показывают только Windows + SHIFT Shell. Никакого iCafeMenu.**

## Проблема

iCafeMenu продолжает запускаться на клиентских ПК после загрузки, несмотря на всё, что уже сделано. Последний раз показался с дефолтной (фиолетовой/синей) темой вместо кастомной — это была свежескачанная копия меню.

## Что уже сделано (не повторяй впустую, но перепроверь)

1. В веб-панели iCafeCloud (cp.icafecloud.com) в Настройки → Настройки пользователя → Система выставлено:
   - «Запустите iCafeMenu на бездисковом режиме» = **Нет**
   - «Запуск iCafeMenu до рабочего стола» = **Нет**
   - Это дошло до сервера: в `C:\CCBoot\icafe.js` видно `run_icafemenu_on_diskless":0` и `run_icafemenu_before_desktop":0`.
2. Игра/приложение iCafeMenu удалено из панели (Games → Apps → delete). Но пакет `pkg_id=21613 iCafeMenu` появлялся в `icafe.js` снова.
3. Удалены папки: `D:\Apps\iCafeMenu`, `D:\Apps\iCafeMenu1111`, `E:\icafemenu1`.
   - Через ~1 минуту служба `iCafeCloudServer` **скачала `D:\Apps\iCafeMenu` заново** (лог `C:\iCafeCloudServer\Log\Log-2026-07-17.txt`, 08:46-08:47: «Update: [21613][iCafeMenu] Start Full Update»).
4. После этого служба **iCafeCloudServer остановлена и переведена в Disabled** (`sc config iCafeCloudServer start= disabled`). Проверь, что так и осталось.
5. Из `D:\Apps\iCafeMenu` удалён `iCafeMenu.exe`, папка оставлена пустой с файлом-заглушкой `DISABLED_BY_SHIFT.txt`.
6. Пользователь сохранял образ через SuperClient после чистки автозагрузки (батник чистил HKLM/HKCU Run, Startup-папки, планировщик, Winlogon Shell/Userinit) — **iCafe всё равно запускается**.

## Ключевой факт из логов клиентов

В `C:\iCafeCloudShare\pcs\PC57\log\Log-*.txt` (и PC13/PC17/PC58) при каждой загрузке:

```
parent process CCBootClient.exe <pid>
current process "D:\Apps\iCafeMenu\iCafeMenu.exe" -noautorun <pid>
```

То есть меню запускает **CCBootClient.exe** (клиентская часть CCBoot внутри Windows-образа), а не реестр/автозагрузка. Флаги в `icafe.js` при этом уже стояли в 0 — либо CCBootClient их кэширует, либо у него свой механизм (например, качает `icafe.js`/настройки с сервера при старте, см. в логах «Skip download icafe.js, not modified»), либо есть ещё один источник (GameFix.bat, задание в образе, iCafeMenuBt.dll и т.п.).

## Важные пути на сервере

| Что | Где |
|---|---|
| CCBoot сервер | `C:\CCBoot` (служба `CCBoot`, Running — НЕ ТРОГАТЬ) |
| CCBootClient дистрибутив | `C:\CCBoot\CCBootClient\x64\CCBootClient.exe` |
| iCafeCloudServer | `C:\iCafeCloudServer` (служба Stopped/Disabled — так и оставить) |
| icafe.js (настройки+игры для клиентов) | `C:\CCBoot\icafe.js` (копия и в `D:\Apps\iCafeMenu`) |
| Шара для клиентов | `C:\iCafeCloudShare` (SMB `iCafeCloudShare`), логи клиентов в `pcs\PCxx\log\` |
| Остатки меню | `D:\Apps\iCafeMenu` (сейчас без exe) |
| Windows-образы клиентов (VHD) | `E:\WINDOWS 11 25H2RU-24.02.2026.vhd` + diff-файлы `.001-.004`, `E:\WIN11RU26H1.vhd`, старые в `E:\NYZ\` |
| SHIFT Shell для образа | `D:\01 SHIFT\Shell` |
| Наш API | `D:\ShiftClub\Server`, health: `http://localhost:5080/health` |

Примечание: базовый VHD монтируется diskpart'ом (`attach vdisk readonly`) и появляется как том `B: CCBootVHD` (~99 ГБ). PowerShell `Mount-DiskImage` на этом сервере зависает — используй **diskpart**. Не забывай `detach vdisk` после работы.

## Что нужно сделать

### 1. Диагностика: откуда CCBootClient берёт команду запускать меню
- Посмотри настройки CCBoot GUI/конфиги: чем управляется «run icafemenu» на стороне CCBoot-сервера (в `C:\CCBoot\Language\enu.ini` есть строки «run icafemenu on diskless / before desktop» — значит настройка есть и в CCBoot, найди где она хранится: ini/db/реестр `HKLM\SOFTWARE\Youngzsoft` и т.п.).
- Проверь `C:\CCBoot\icafe.js`: флаги `run_icafemenu_on_diskless`, `run_icafemenu_before_desktop`, наличие пакета `"pkg_name":"iCafeMenu"` в `theGames`. Если пакет там есть — найди, из чего `icafe.js` генерируется, и убери пакет (служба iCafeCloudServer выключена, так что перегенерации быть не должно; возможно, надо править файл вручную и/или сделать его read-only).
- Проверь, не тянет ли CCBootClient настройки напрямую с `eu25.icafecloud.com` (клиентские логи показывают WSS-подключение к облаку). Если да — блокировка на уровне клуба: см. шаг 3.

### 2. Проверка Windows-образа (обязательно)
Смонтируй **базовый** `E:\WINDOWS 11 25H2RU-24.02.2026.vhd` readonly через diskpart и проверь внутри (том появится как `B:`):
- `B:\Windows\System32\config\SOFTWARE` → загрузить хайв (`reg load HKLM\OFFIMG_SOFT ...`) и посмотреть:
  - `Microsoft\Windows NT\CurrentVersion\Winlogon` → `Shell`, `Userinit` (должны быть `explorer.exe` и `C:\Windows\system32\userinit.exe,`)
  - `Microsoft\Windows\CurrentVersion\Run` и WOW6432Node — записи iCafe/Overwolf
- `HKLM\OFFIMG_SYS` (SYSTEM hive) → `ControlSet001\Services` — службы iCafe/CCBootClient, их ImagePath
- Startup-папки: `B:\ProgramData\...\StartUp` и `B:\Users\*\...\Startup`
- Есть ли `B:\...\CCBootClient.exe` и рядом его конфиги (ini) — посмотри, что в них про icafemenu
- Есть ли SHIFT Shell в образе и его автозапуск
- **Выгрузи хайвы (`reg unload`) и `detach vdisk` после проверки!**
- Учти: рабочие изменения могут жить в diff-файлах `.001-.004` — если базовый образ чистый, значит iCafe-механизм в diff'ах, и лечится это через SuperClient + «сохранить образ» (мерж диффов) либо откатом диффов.

### 3. Отрезать облако iCafe (если CCBootClient слушает облако)
Если выяснится, что CCBootClient получает команду запуска меню с `eu25.icafecloud.com` (WSS) — заблокируй на сервере/роутере исходящие к `*.icafecloud.com` для клиентской подсети, или в hosts образа. Но сначала докажи, что это источник.

### 4. Зафиксировать результат
- Убедись, что `D:\Apps\iCafeMenu` без exe и не восстанавливается (служба iCafeCloudServer Disabled).
- Перезагрузи 1-2 клиентских ПК (можно попросить пользователя) и проверь свежие логи `C:\iCafeCloudShare\pcs\PCxx\log\Log-*.txt`:
  - Если новый лог-файл вообще не появился — iCafeMenu не стартовал. Успех.
  - Если появился — смотри `parent process` и путь запуска, копай дальше.
- Напиши краткий отчёт: что было источником запуска, что изменил, как проверил.

## Ограничения (ВАЖНО)
- **НЕ трогать службу CCBoot** и загрузку по сети — клуб должен продолжать работать.
- **НЕ включать обратно iCafeCloudServer.**
- **НЕ удалять** `C:\CCBoot`, VHD-образы, `D:\Games`, `F:\Online Game` — там рабочие данные клуба.
- Ничего не деинсталлировать без явного подтверждения пользователя. Цель — отключить запуск меню, а не снести CCBoot.
- Изменения в образ вносить только readonly-проверками; правки образа — через пользователя (SuperClient) или с его явного согласия (mount rw, когда все клиенты выключены).
- Наш API (`ShiftClubApi`, порт 5080) и PostgreSQL не трогать.
