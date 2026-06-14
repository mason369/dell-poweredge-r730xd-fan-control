[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD Smart Fan Center

Aplikacja WinUI 3 dla Dell PowerEdge R730xd: sterowanie wentylatorami przez iDRAC/IPMI, monitoring czujników BMC i wykresy historii. Pełna długa dokumentacja jest w [简体中文](README.md) i [English](README.en-US.md); ta strona zawiera polski skrót operacyjny.

## Szybki start

1. Włącz **IPMI over LAN** w iDRAC i sprawdź dostęp Windows do sieci zarządzania.
2. Uruchom ze źródeł: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. W Settings wpisz host, użytkownika i hasło iDRAC/BMC. DPAPI włącz tylko wtedy, gdy potrzebujesz automatycznego połączenia.
4. Save settings wykonuje prawdziwe `mc info` oraz `sdr elist`; polling startuje dopiero po powodzeniu.
5. Zacznij od Dell Auto albo ostrożnych 20%/35%. Niskie prędkości i pojedyncze cele Fan testuj pod nadzorem.

## Ważne zachowanie

- Błędy pokazują stdout/stderr, status UI i log JSONL; nie są ukrywane jako sukces.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` i `ipmitool` pozostają terminami technicznymi lub jednostkami.
- Logi są w `%LocalAppData%\DellR730xdFanControlCenter\logs`, historia wykresów w `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Sterowanie wentylatorami bezpośrednio wpływa na chłodzenie. Przy nieznanym obciążeniu, wysokiej temperaturze otoczenia lub niepewnych czujnikach użyj Dell Auto.
