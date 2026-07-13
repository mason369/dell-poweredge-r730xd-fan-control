[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Центр управления вентиляторами Dell PowerEdge R730xd через iDRAC

Приложение WinUI 3 для Windows 10/11, которое управляет вентиляторами и контролирует аппаратное состояние Dell PowerEdge R730xd через iDRAC/IPMI. Доступны ручные скорости, Dell Auto, кривые по температуре CPU или мощности, датчики BMC SDR, Fan 1-6 RPM, локальные графики, профили и меню в области уведомлений.

## Загрузка и область применения

- Загрузите `DellR730xdFanControlCenter-win-x64.zip` из [последнего GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest), полностью распакуйте и запустите `DellR730xdFanControlCenter.exe`.
- Текущая версия исходного кода — `1.1.0`. Release `v1.1.0` собирается из соответствующего тега, а версия файлов exe/dll в пакете — `1.1.0.0`.
- Целевое оборудование — Dell PowerEdge R730xd. Локально проверялся только R730xd с iDRAC 2.82; другие прошивки нужно проверять под наблюдением.

## Возможности

- Управление всеми вентиляторами от `0-100%`, ручные профили и возврат управления Dell Auto.
- Автоматическая регулировка по температуре CPU и редактируемые кривые температура–вентилятор и мощность–вентилятор.
- Реальные вызовы `mc info` и `sdr elist` для температуры, RPM, мощности, напряжения, тока, резервирования и дискретных состояний.
- 7 дней локальной JSONL-истории в ECharts/WebView2, 22 языка UI, меню в области уведомлений и необязательное хранение пароля под защитой DPAPI.

## Быстрый старт

1. Включите **IPMI over LAN** в iDRAC и проверьте сеть управления.
2. Запустите распакованный Release или исходный код: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. На странице настроек укажите адрес iDRAC/BMC, имя и пароль. Включайте DPAPI только если нужно последующее автоподключение.
4. При сохранении сначала выполняется `mc info`, затем `sdr elist`. Опрос и успешный статус запускаются только после реального успеха команд и записи лога.
5. Начните с Dell Auto или осторожных `20%`/`35%` и наблюдайте за температурой и RPM.

## Значения по умолчанию и локальные файлы

- Опрос `1 s`, тайм-аут команды `35 s`, целевая/высокая/аварийная температура `68 °C` / `78 °C` / `84 °C`, автодиапазон `10-42%`, история `7` дней.
- Настройки: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Логи: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- История и WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- Пароль передаётся `ipmitool -E` через `IPMI_PASSWORD` и не попадает в аргументы командной строки.

## Ошибки и аппаратные ограничения

- Ошибки аутентификации, сети, SDR, WebView2, raw-команд и записи лога показываются и регистрируются; они не выдаются за успех.
- Если ручной режим включился, но следующая команда процента завершилась ошибкой, приложение один раз отправляет команду восстановления Dell Auto. Исходный запрос остаётся неудачным и не повторяется.
- `0x00-0x05` — селекторы цели прошивки, а не проценты. Индивидуальное управление по умолчанию отключено; `0x00` на тестовой машине раскрутил все вентиляторы.
- Команды пользователя ждут блокировку IPMI. Фоновый опрос и автоматические tick при занятой блокировке явно пропускаются; второй процесс `ipmitool` не запускается.
- Низкие скорости или неверные кривые могут перегреть CPU, диски, PCIe-карты и блоки питания. При сомнениях используйте Dell Auto.

## Проверка и документация

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Эти проверки не заменяют испытание на целевом сервере. Запуск GUI, реальные raw-команды, другие прошивки iDRAC и все сочетания DPI полностью не проверены. См. [английское руководство](README.en-US.md), [команды IPMI](docs/COMMANDS.en-US.md) и [безопасность](SECURITY.en-US.md).
