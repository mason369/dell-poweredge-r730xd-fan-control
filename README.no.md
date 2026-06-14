[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD Smart Fan Center

WinUI 3-app for Dell PowerEdge R730xd med iDRAC/IPMI-viftestyring, BMC-sensorovervåking og historiske diagrammer. Full lang dokumentasjon finnes på [简体中文](README.md) og [English](README.en-US.md); denne siden er norsk kjerneveiledning.

## Hurtigstart

1. Slå på **IPMI over LAN** i iDRAC og kontroller at Windows når administrasjonsnettet.
2. Kjør fra kilde: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. I Settings legger du inn iDRAC/BMC-host, bruker og passord. Bruk DPAPI-lagring bare hvis automatisk tilkobling ønskes.
4. Save settings kjører ekte `mc info` og `sdr elist`; polling starter først etter vellykket resultat.
5. Start med Dell Auto eller forsiktige 20%/35%. Lave hastigheter og enkelt-Fan mål må testes under oppsyn.

## Viktig oppførsel

- Feil vises med stdout/stderr, UI-status og JSONL-logg; de skjules ikke som suksess.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` og `ipmitool` beholdes som tekniske termer eller enheter.
- Logger ligger i `%LocalAppData%\DellR730xdFanControlCenter\logs`, diagramhistorikk i `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Viftestyring påvirker kjølingen direkte. Ved ukjent last, høy romtemperatur eller usikre sensorer bør Dell Auto brukes.
