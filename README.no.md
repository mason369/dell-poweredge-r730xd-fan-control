[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Dell PowerEdge R730xd-viftestyring via iDRAC

WinUI 3-app for Windows 10/11 som styrer vifter og overvåker maskinvare i Dell PowerEdge R730xd via iDRAC/IPMI. Den samler manuelle hastigheter, Dell Auto, kurver etter CPU-temperatur eller effekt, BMC SDR-sensorer, Fan 1-6 RPM, lokale diagrammer, profiler og systemkurvhandlinger.

## Nedlasting og omfang

- Last ned `DellR730xdFanControlCenter-win-x64.zip` fra [nyeste GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest), pakk ut alt og kjør `DellR730xdFanControlCenter.exe`.
- Gjeldende kildekode og den nyeste publiserte releasen er `v1.1.2`; pakkens exe/dll har filversjon `1.1.2.0`, slik at kildekode, tagg og binærfiler viser samme versjon.
- Målmaskinvaren er Dell PowerEdge R730xd. Bare R730xd med iDRAC 2.82 er observert lokalt; annen fastvare må valideres under tilsyn.

## Funksjoner

- Styr alle vifter fra `0-100%`, lagre manuelle profiler eller gi kontrollen tilbake til Dell Auto.
- Automatisk regulering etter CPU-temperatur og redigerbare temperatur-vifte- eller effekt-viftekurver.
- Ekte `mc info`- og `sdr elist`-kall for temperatur, RPM, effekt, spenning, strøm, redundans og diskrete tilstander.
- Sju dagers lokal JSONL-historikk med ECharts/WebView2, 22 UI-språk, systemkurvmeny og valgfri DPAPI-beskyttet passordlagring.

## Hurtigstart

1. Aktiver **IPMI over LAN** i iDRAC, og kontroller administrasjonsnettet.
2. Kjør utpakket Release eller kildekoden: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Oppgi iDRAC/BMC-adresse, bruker og passord på innstillingssiden. Aktiver DPAPI bare hvis senere automatisk tilkobling er nødvendig.
4. Lagring kjører først `mc info` og deretter `sdr elist`. Polling og suksessstatus starter først etter reell kommando- og loggsuksess.
5. Begynn med Dell Auto eller forsiktige `20%`/`35%`, og overvåk temperaturer og RPM.

## Standardverdier og lokale filer

- Polling `1 s`, kommandotidsavbrudd `35 s`, mål/høy/nødtemperatur `68 °C` / `78 °C` / `84 °C`, automatisk område `10-42%`, historikk `7` dager.
- Innstillinger: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Logger: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Historikk og WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- Passordet sendes til `ipmitool -E` via `IPMI_PASSWORD` og vises ikke i kommandolinjeargumentene.

## Feil og maskinvaregrenser

- Feil i autentisering, nettverk, SDR, WebView2, raw-kommandoer eller loggskriving vises og registreres; de presenteres ikke som suksess.
- Hvis manuell modus aktiveres, men den neste prosentkommandoen feiler, sender appen nøyaktig én Dell Auto-gjenopprettingskommando. Den opprinnelige forespørselen forblir mislykket og prøves ikke på nytt.
- `0x00-0x05` er fastvarens målvelgere, ikke prosenter. Individuell viftestyring er deaktivert som standard; `0x00` satte alle viftene i høy hastighet på testmaskinen.
- Brukerkommandoer venter på IPMI-låsen. Bakgrunnspolling og automatiske tick hoppes synlig over når låsen er opptatt; en ny `ipmitool`-prosess startes ikke.
- Lave hastigheter eller feil kurver kan overopphete CPU, disker, PCIe-kort og strømforsyninger. Bruk Dell Auto ved tvil.

## Verifisering og dokumentasjon

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Disse kontrollene erstatter ikke testing på målserveren. GUI-start, ekte raw-kommandoer, annen iDRAC-fastvare og alle DPI-kombinasjoner er ikke fullt dekket. Se [engelsk veiledning](README.en-US.md), [IPMI-kommandoer](docs/COMMANDS.en-US.md) og [sikkerhet](SECURITY.en-US.md).
