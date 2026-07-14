[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Dell PowerEdge R730xd Lüftersteuerung für iDRAC

Windows-10/11-App mit WinUI 3 zur Lüftersteuerung und Hardwareüberwachung eines Dell PowerEdge R730xd über iDRAC/IPMI. Sie vereint manuelle Drehzahlen, Dell Auto, CPU-Temperatur- und Leistungskurven, BMC-SDR-Sensoren, Fan-1-6-RPM, lokale Diagramme, Presets und Tray-Aktionen.

## Download und Einsatzbereich

- Laden Sie `DellR730xdFanControlCenter-win-x64.zip` aus dem [aktuellen GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest) herunter, entpacken Sie alles und starten Sie `DellR730xdFanControlCenter.exe`.
- Der aktuelle Quellcode hat Version `1.1.2`. Die neueste veröffentlichte Release bleibt `v1.1.0`; deren exe/dll tragen Dateiversion `1.1.0.0`. `1.1.2` wird erst nach dem Build des zugehörigen Tags zur offiziellen Release.
- Zielhardware ist Dell PowerEdge R730xd. Lokal beobachtet wurde nur R730xd mit iDRAC 2.82; andere Firmware muss unter Aufsicht geprüft werden.

## Funktionen

- Alle Lüfter von `0-100%` steuern, manuelle Presets speichern oder die Regelung an Dell Auto zurückgeben.
- Automatische Regelung nach CPU-Temperatur sowie editierbare Temperatur-Lüfter- und Leistung-Lüfter-Kurven.
- Echte `mc info`- und `sdr elist`-Abfragen für Temperatur, RPM, Leistung, Spannung, Strom, Redundanz und diskrete Zustände.
- Sieben Tage lokaler JSONL-Verlauf mit ECharts/WebView2, 22 UI-Sprachen, Tray-Menü und DPAPI-geschützte optionale Passwortspeicherung.

## Schnellstart

1. Aktivieren Sie **IPMI over LAN** in iDRAC und prüfen Sie das Management-Netz.
2. Starten Sie den entpackten Release oder den Quellcode: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Tragen Sie auf der Einstellungsseite iDRAC/BMC-Adresse, Benutzername und Passwort ein. Aktivieren Sie DPAPI nur, wenn eine spätere automatische Verbindung gewünscht ist.
4. Beim Speichern werden zuerst `mc info` und danach `sdr elist` ausgeführt. Polling und Erfolgsanzeige starten erst nach echten Befehls- und Log-Erfolgen.
5. Beginnen Sie mit Dell Auto oder konservativen `20%`/`35%` und beobachten Sie Temperaturen und RPM.

## Standardwerte und lokale Dateien

- Polling `1 s`, Befehls-Timeout `35 s`, Ziel/hoch/notfall `68 °C` / `78 °C` / `84 °C`, automatischer Bereich `10-42%`, Verlauf `7` Tage.
- Einstellungen: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Laufzeitlogs: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Diagrammverlauf und WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history` und `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- Das Passwort wird für `ipmitool -E` über `IPMI_PASSWORD` übergeben und erscheint nicht in der Befehlszeile.

## Fehler- und Hardwaregrenzen

- Authentifizierungs-, Netzwerk-, SDR-, WebView2-, raw-Befehls- und Log-Schreibfehler werden angezeigt und protokolliert; sie gelten nicht als Erfolg.
- Wenn der Wechsel in den manuellen Modus gelingt, die folgende Prozentvorgabe aber fehlschlägt, sendet die App genau einen Dell-Auto-Wiederherstellungsbefehl. Der ursprüngliche Auftrag bleibt fehlgeschlagen und wird nicht wiederholt.
- `0x00-0x05` sind Firmware-Zielselektoren, keine Prozentwerte. Einzel-Lüftersteuerung ist standardmäßig deaktiviert; `0x00` ließ auf dem getesteten Gerät alle Lüfter hochdrehen.
- Benutzerbefehle warten auf die IPMI-Sperre. Hintergrund-Polling und automatische Ticks werden bei belegter Sperre sichtbar übersprungen; es startet kein zweiter `ipmitool`-Prozess.
- Niedrige Drehzahlen und eigene Kurven können CPU, Laufwerke, PCIe-Karten und Netzteile überhitzen. Bei Unsicherheit Dell Auto verwenden.

## Prüfung und Dokumentation

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Diese Prüfungen ersetzen keinen Test auf dem Zielserver. GUI-Start, reale raw-Befehle, andere iDRAC-Firmware und alle DPI-Kombinationen sind damit nicht vollständig abgedeckt. Ausführliche Informationen: [englische Anleitung](README.en-US.md), [IPMI-Befehle](docs/COMMANDS.en-US.md) und [Sicherheit](SECURITY.en-US.md).
