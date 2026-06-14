[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD Smart Fan Center

WinUI 3-приложение для Dell PowerEdge R730xd: управление вентиляторами через iDRAC/IPMI, мониторинг датчиков BMC и исторические графики. Полная подробная документация поддерживается на [简体中文](README.md) и [English](README.en-US.md); эта страница дает основной русский вход.

## Быстрый старт

1. Включите **IPMI over LAN** в iDRAC и проверьте доступ Windows к сети управления.
2. Запустите из исходников: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. В Settings укажите host, пользователя и пароль iDRAC/BMC. DPAPI включайте только если нужна автоматическая повторная связь.
4. Save settings выполняет реальные `mc info` и `sdr elist`; постоянный polling начинается только после успеха.
5. Начинайте с Dell Auto или осторожных 20%/35%. Низкие обороты и одиночные цели Fan проверяйте только под наблюдением.

## Важное поведение

- Ошибки показывают stdout/stderr, состояние UI и JSONL-лог; они не скрываются как успех.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` и `ipmitool` сохраняются как технические термины или единицы.
- Логи находятся в `%LocalAppData%\DellR730xdFanControlCenter\logs`, история графиков в `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Управление вентиляторами напрямую влияет на охлаждение. При неизвестной нагрузке, высокой температуре или сомнительных датчиках используйте Dell Auto.
