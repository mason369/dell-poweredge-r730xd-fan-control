[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD Smart Fan Center

WinUI 3-застосунок для Dell PowerEdge R730xd: керування вентиляторами через iDRAC/IPMI, моніторинг датчиків BMC і історичні графіки. Повна довга документація є в [简体中文](README.md) та [English](README.en-US.md); ця сторінка є українським основним входом.

## Швидкий старт

1. Увімкніть **IPMI over LAN** в iDRAC і перевірте доступ Windows до мережі керування.
2. Запустіть із джерел: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. У Settings введіть host, користувача й пароль iDRAC/BMC. DPAPI вмикайте лише якщо потрібне автоматичне підключення.
4. Save settings виконує справжні `mc info` і `sdr elist`; постійний polling стартує тільки після успіху.
5. Починайте з Dell Auto або обережних 20%/35%. Низькі оберти й одиночні Fan-цілі перевіряйте під наглядом.

## Важлива поведінка

- Помилки показують stdout/stderr, стан UI і JSONL-лог; вони не маскуються як успіх.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` і `ipmitool` лишаються технічними термінами або одиницями.
- Логи містяться в `%LocalAppData%\DellR730xdFanControlCenter\logs`, історія графіків у `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Керування вентиляторами прямо впливає на охолодження. Якщо навантаження, температура або датчики неясні, використовуйте Dell Auto.
