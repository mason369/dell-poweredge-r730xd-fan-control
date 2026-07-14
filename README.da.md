[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Dell PowerEdge R730xd blæserstyring via iDRAC

WinUI 3-app til Windows 10/11, der styrer blæsere og overvåger hardware i en Dell PowerEdge R730xd via iDRAC/IPMI. Den samler manuelle hastigheder, Dell Auto, kurver efter CPU-temperatur eller effekt, BMC SDR-sensorer, Fan 1-6 RPM, lokale diagrammer, profiler og bakkehandlinger.

## Download og omfang

- Hent `DellR730xdFanControlCenter-win-x64.zip` fra den [seneste GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest), pak alt ud, og start `DellR730xdFanControlCenter.exe`.
- Den aktuelle kildekode er `1.1.2`. Den senest udgivne Release er fortsat `v1.1.0`, og pakkens exe/dll har filversion `1.1.0.0`; `1.1.2` bliver først en officiel Release, når det tilsvarende tag er bygget.
- Målhardwaren er Dell PowerEdge R730xd. Kun R730xd med iDRAC 2.82 er observeret lokalt; anden firmware skal testes under opsyn.

## Funktioner

- Styr alle blæsere fra `0-100%`, gem manuelle profiler, eller giv styringen tilbage til Dell Auto.
- Automatisk regulering efter CPU-temperatur og redigerbare temperatur-blæser- eller effekt-blæserkurver.
- Rigtige `mc info`- og `sdr elist`-kald for temperatur, RPM, effekt, spænding, strøm, redundans og diskrete tilstande.
- Syv dages lokal JSONL-historik med ECharts/WebView2, 22 UI-sprog, bakkemenu og valgfri DPAPI-beskyttet adgangskodelagring.

## Hurtig start

1. Aktivér **IPMI over LAN** i iDRAC, og kontrollér administrationsnettet.
2. Start den udpakkede Release eller kildekoden: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Angiv iDRAC/BMC-adresse, bruger og adgangskode på indstillingssiden. Aktivér kun DPAPI, hvis senere automatisk forbindelse er nødvendig.
4. Ved lagring køres først `mc info` og derefter `sdr elist`. Polling og successtatus starter først efter reelle kommando- og logresultater.
5. Begynd med Dell Auto eller forsigtige `20%`/`35%`, og overvåg temperaturer og RPM.

## Standardværdier og lokale filer

- Polling `1 s`, kommandotimeout `35 s`, mål/høj/nødtemperatur `68 °C` / `78 °C` / `84 °C`, automatisk interval `10-42%`, historik `7` dage.
- Indstillinger: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Logs: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Historik og WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history` og `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- Adgangskoden sendes til `ipmitool -E` via `IPMI_PASSWORD` og vises ikke i kommandolinjens argumenter.

## Fejl og hardwaregrænser

- Fejl i godkendelse, netværk, SDR, WebView2, raw-kommandoer eller logskrivning vises og registreres; de vises ikke som succes.
- Hvis manuel tilstand aktiveres, men den efterfølgende procentkommando fejler, sender appen præcis én Dell Auto-gendannelseskommando. Den oprindelige anmodning forbliver mislykket og gentages ikke.
- `0x00-0x05` er firmwaremål, ikke procenter. Styring af en enkelt blæser er deaktiveret som standard; `0x00` fik alle blæsere til at køre hurtigt på den testede maskine.
- Brugerkommandoer venter på IPMI-låsen. Baggrundspolling og automatiske cyklusser springes synligt over, når låsen er optaget; der startes ikke en ekstra `ipmitool`-proces.
- Lave hastigheder eller forkerte kurver kan overophede CPU, diske, PCIe-kort og strømforsyninger. Brug Dell Auto ved tvivl.

## Kontrol og dokumentation

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Kontrollerne erstatter ikke en test på målserveren. GUI-start, rigtige raw-kommandoer, anden iDRAC-firmware og alle DPI-kombinationer er ikke fuldt dækket. Se den [engelske vejledning](README.en-US.md), [IPMI-kommandoer](docs/COMMANDS.en-US.md) og [sikkerhed](SECURITY.en-US.md).
