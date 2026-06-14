[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD Smart Fan Center

WinUI-3-App für Dell PowerEdge R730xd mit iDRAC/IPMI-Lüftersteuerung, BMC-Sensorüberwachung und Verlaufsgrafiken. Die vollständige Langdokumentation steht in [简体中文](README.md) und [English](README.en-US.md); diese Seite ist der deutsche Einstieg.

## Schnellstart

1. **IPMI over LAN** in iDRAC aktivieren und die Erreichbarkeit des Management-Netzes prüfen.
2. Aus dem Quellcode starten: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. In Settings Host, Benutzername und Passwort für iDRAC/BMC eintragen. DPAPI-Passwortspeicherung nur einschalten, wenn spätere automatische Verbindung gewünscht ist.
4. Nach Save settings führt die App echte `mc info`- und `sdr elist`-Befehle aus; erst nach Erfolg beginnt das Polling.
5. Zuerst Dell Auto oder konservative 20%/35% verwenden. Niedrige Drehzahlen und einzelne Fan-Ziele nur unter Beobachtung testen.

## Wichtige Regeln

- Fehler werden mit stdout/stderr, UI-Status und JSONL-Log sichtbar gemacht; sie werden nicht als Erfolg versteckt.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` und `ipmitool` bleiben technische Begriffe oder Einheiten.
- Laufzeitlogs liegen unter `%LocalAppData%\DellR730xdFanControlCenter\logs`, Chart-Verlauf unter `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Lüftersteuerung verändert direkt die thermische Reserve. Bei unklarer Last, hoher Umgebungstemperatur oder auffälligen Sensoren Dell Auto bevorzugen.
