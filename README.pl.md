[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Sterowanie wentylatorami Dell PowerEdge R730xd przez iDRAC

Aplikacja WinUI 3 dla Windows 10/11 do sterowania wentylatorami i monitorowania sprzętu Dell PowerEdge R730xd przez iDRAC/IPMI. Obejmuje prędkości ręczne, Dell Auto, krzywe według temperatury CPU lub mocy, czujniki BMC SDR, Fan 1-6 RPM, lokalne wykresy, profile i menu zasobnika.

## Pobieranie i zakres

- Pobierz `DellR730xdFanControlCenter-win-x64.zip` z [najnowszego GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest), rozpakuj całość i uruchom `DellR730xdFanControlCenter.exe`.
- Aktualny kod źródłowy i najnowszy opublikowany Release mają wersję `v1.1.2`; pliki exe/dll w paczce mają wersję `1.1.2.0`, więc kod, tag i pliki binarne wskazują tę samą wersję.
- Docelowy sprzęt to Dell PowerEdge R730xd. Lokalnie sprawdzono tylko R730xd z iDRAC 2.82; inne firmware trzeba weryfikować pod nadzorem.

## Funkcje

- Sterowanie wszystkimi wentylatorami od `0-100%`, ręczne profile i oddanie sterowania do Dell Auto.
- Automatyczna regulacja według temperatury CPU oraz edytowalne krzywe temperatura–wentylator i moc–wentylator.
- Rzeczywiste wywołania `mc info` i `sdr elist` dla temperatury, RPM, mocy, napięcia, prądu, nadmiarowości i stanów dyskretnych.
- Siedem dni lokalnej historii JSONL z ECharts/WebView2, 22 języki UI, menu zasobnika i opcjonalne przechowywanie hasła chronione przez DPAPI.

## Szybki start

1. Włącz **IPMI over LAN** w iDRAC i sprawdź sieć zarządzającą.
2. Uruchom rozpakowany Release lub kod źródłowy: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Na stronie ustawień wpisz adres iDRAC/BMC, nazwę użytkownika i hasło. Włącz DPAPI tylko wtedy, gdy potrzebne jest późniejsze połączenie automatyczne.
4. Zapis najpierw uruchamia `mc info`, a potem `sdr elist`. Odpytywanie i stan sukcesu zaczynają się dopiero po rzeczywistym powodzeniu poleceń i zapisu logu.
5. Zacznij od Dell Auto albo ostrożnych `20%`/`35%` i obserwuj temperatury oraz RPM.

## Wartości domyślne i pliki lokalne

- Odpytywanie `1 s`, limit polecenia `35 s`, temperatura docelowa/wysoka/awaryjna `68 °C` / `78 °C` / `84 °C`, zakres automatyczny `10-42%`, historia `7` dni.
- Ustawienia: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Logi: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Historia i WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- Hasło jest przekazywane do `ipmitool -E` przez `IPMI_PASSWORD` i nie pojawia się w argumentach wiersza poleceń.

## Błędy i ograniczenia sprzętowe

- Błędy uwierzytelniania, sieci, SDR, WebView2, poleceń raw i zapisu logu są pokazywane i zapisywane; nie są przedstawiane jako sukces.
- Jeśli tryb ręczny włączy się, ale następne polecenie procentowe zawiedzie, aplikacja wysyła dokładnie jedno polecenie odzyskiwania Dell Auto. Pierwotne żądanie pozostaje nieudane i nie jest ponawiane.
- `0x00-0x05` to selektory celu firmware, a nie procenty. Sterowanie pojedynczym wentylatorem jest domyślnie wyłączone; `0x00` rozpędziło wszystkie wentylatory na testowanym serwerze.
- Polecenia użytkownika czekają na blokadę IPMI. Odpytywanie w tle i automatyczne tick są jawnie pomijane, gdy blokada jest zajęta; drugi proces `ipmitool` nie jest uruchamiany.
- Niskie prędkości lub błędne krzywe mogą przegrzać CPU, dyski, karty PCIe i zasilacze. W razie wątpliwości użyj Dell Auto.

## Weryfikacja i dokumentacja

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Te kontrole nie zastępują testów na serwerze docelowym. Uruchomienie GUI, rzeczywiste polecenia raw, inne firmware iDRAC i wszystkie kombinacje DPI nie są w pełni objęte. Zobacz [instrukcję angielską](README.en-US.md), [polecenia IPMI](docs/COMMANDS.en-US.md) i [bezpieczeństwo](SECURITY.en-US.md).
