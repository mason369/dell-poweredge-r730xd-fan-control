[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD Smart Fan Center

WinUI 3-app til Dell PowerEdge R730xd med iDRAC/IPMI-blæserstyring, BMC-sensorovervågning og historiske diagrammer. Den fulde dokumentation findes på [简体中文](README.md) og [English](README.en-US.md); denne side er den danske kerneguide.

## Hurtig start

1. Slå **IPMI over LAN** til i iDRAC, og kontroller at Windows kan nå administrationsnettet.
2. Kør fra kildekoden: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Angiv iDRAC/BMC-host, bruger og adgangskode i Settings. Brug kun DPAPI-lagring, hvis automatisk forbindelse ønskes.
4. Save settings kører rigtige `mc info`- og `sdr elist`-kommandoer; polling starter først efter succes.
5. Start med Dell Auto eller forsigtige 20%/35%. Lave hastigheder og enkelt-Fan mål skal testes under opsyn.

## Vigtig adfærd

- Fejl vises med stdout/stderr, UI-status og JSONL-log; de skjules ikke som succes.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` og `ipmitool` bevares som tekniske termer eller enheder.
- Logs ligger i `%LocalAppData%\DellR730xdFanControlCenter\logs`; diagramhistorik i `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Blæserstyring påvirker serverens køling direkte. Ved ukendt belastning, høj omgivelsestemperatur eller usikre sensorer bør Dell Auto bruges.
