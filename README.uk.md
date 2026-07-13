[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Керування вентиляторами Dell PowerEdge R730xd через iDRAC

Застосунок WinUI 3 для Windows 10/11, що керує вентиляторами та контролює апаратний стан Dell PowerEdge R730xd через iDRAC/IPMI. Підтримує ручні швидкості, Dell Auto, криві за температурою CPU або потужністю, датчики BMC SDR, Fan 1-6 RPM, локальні графіки, профілі та меню трея.

## Завантаження та межі підтримки

- Завантажте `DellR730xdFanControlCenter-win-x64.zip` з [останнього GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest), повністю розпакуйте та запустіть `DellR730xdFanControlCenter.exe`.
- Поточний код має версію `1.1.0`. Release `v1.1.0` збирається з відповідного tag, а файли exe/dll у пакеті мають версію `1.1.0.0`.
- Цільове обладнання — Dell PowerEdge R730xd. Локально спостерігався лише R730xd з iDRAC 2.82; інші firmware потребуюють перевірки під наглядом.

## Можливості

- Керування всіма вентиляторами від `0-100%`, ручні профілі та повернення керування Dell Auto.
- Автоматичне регулювання за температурою CPU та редаговані криві температура–вентилятор або потужність–вентилятор.
- Реальні виклики `mc info` і `sdr elist` для температури, RPM, потужності, напруги, струму, резервування та дискретних станів.
- 7 днів локальної JSONL-історії з ECharts/WebView2, 22 мови UI, меню трея та необов'язкове зберігання пароля під захистом DPAPI.

## Швидкий старт

1. Увімкніть **IPMI over LAN** в iDRAC та перевірте мережу керування.
2. Запустіть розпакований Release або код: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. На сторінці налаштувань вкажіть адресу iDRAC/BMC, користувача та пароль. Увімкніть DPAPI лише якщо потрібне подальше автопідключення.
4. Під час збереження спочатку виконується `mc info`, потім `sdr elist`. Опитування та стан успіху починаються лише після фактичного успіху команд і запису журналу.
5. Почніть з Dell Auto або обережних `20%`/`35%` і стежте за температурою та RPM.

## Типові значення та локальні файли

- Опитування `1 s`, тайм-аут команди `35 s`, цільова/висока/аварійна температура `68 °C` / `78 °C` / `84 °C`, автоматичний діапазон `10-42%`, історія `7` днів.
- Налаштування: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Журнали: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Історія та WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- Пароль передається `ipmitool -E` через `IPMI_PASSWORD` і не потрапляє до аргументів командного рядка.

## Помилки та апаратні межі

- Помилки автентифікації, мережі, SDR, WebView2, raw-команд і запису журналу показуються та реєструються; вони не позначаються як успіх.
- Якщо ручний режим увімкнувся, а наступна команда відсотка завершилася помилкою, застосунок один раз надсилає команду відновлення Dell Auto. Початковий запит залишається невдалим і не повторюється.
- `0x00-0x05` — це селектори цілі firmware, а не відсотки. Індивідуальне керування замовчувано вимкнене; `0x00` на тестовому сервері розкрутило всі вентилятори.
- Команди користувача чекають на блокування IPMI. Фонове опитування та автоматичні tick явно пропускаються, коли блокування зайняте; другий процес `ipmitool` не запускається.
- Низькі швидкості або хибні криві можуть перегріти CPU, диски, карти PCIe та блоки живлення. Якщо не впевнені, використовуйте Dell Auto.

## Перевірка та документація

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Ці перевірки не замінюють тестування на цільовому сервері. Запуск GUI, реальні raw-команди, інші iDRAC firmware та всі комбінації DPI повністю не перевірені. Див. [англійський посібник](README.en-US.md), [команди IPMI](docs/COMMANDS.en-US.md) та [безпеку](SECURITY.en-US.md).
