[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Upravljanje ventilatorima Dell PowerEdge R730xd putem iDRAC-a

WinUI 3 aplikacija za Windows 10/11 koja upravlja ventilatorima i nadgleda hardver Dell PowerEdge R730xd putem iDRAC/IPMI. Obuhvata ručne brzine, Dell Auto, krivulje prema temperaturi CPU-a ili potrošnji, BMC SDR senzore, Fan 1-6 RPM, lokalne grafikone, profile i radnje u sistemskoj traci.

## Preuzimanje i opseg

- Preuzmite `DellR730xdFanControlCenter-win-x64.zip` sa [najnovijeg GitHub Releasea](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest), raspakujte sve i pokrenite `DellR730xdFanControlCenter.exe`.
- Trenutni izvorni kod i najnoviji objavljeni Release su `v1.1.3`; exe/dll u paketu imaju verziju datoteke `1.1.3.0`, pa izvorni kod, oznaka i binarne datoteke označavaju istu verziju.
- Ciljni hardver je Dell PowerEdge R730xd. Lokalno je posmatran samo R730xd s iDRAC 2.82; drugi firmware mora se provjeravati pod nadzorom.

## Mogućnosti

- Upravljanje svim ventilatorima od `0-100%`, ručni profili i vraćanje kontrole na Dell Auto.
- Automatsko podešavanje prema temperaturi CPU-a i podesive krivulje temperatura–ventilator ili potrošnja–ventilator.
- Stvarni pozivi `mc info` i `sdr elist` za temperaturu, RPM, snagu, napon, struju, redundansu i diskretna stanja.
- Sedam dana lokalne JSONL historije uz ECharts/WebView2, 22 UI jezika, meni sistemske trake i opcionalno čuvanje lozinke zaštićeno DPAPI-jem.

## Brzi početak

1. Uključite **IPMI over LAN** u iDRAC-u i provjerite upravljačku mrežu.
2. Pokrenite raspakovani Release ili izvorni kod: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Na stranici postavki unesite iDRAC/BMC adresu, korisnika i lozinku. DPAPI uključite samo ako je kasnije potrebna automatska veza.
4. Spremanje prvo izvršava `mc info`, zatim `sdr elist`. Polling i status uspjeha počinju tek nakon stvarnog uspjeha naredbi i upisa loga.
5. Počnite s Dell Auto ili opreznih `20%`/`35%` i nadgledajte temperature i RPM.

## Zadane vrijednosti i lokalne datoteke

- Polling `1 s`, istek naredbe `35 s`, ciljna/visoka/hitna temperatura `68 °C` / `78 °C` / `84 °C`, automatski raspon `10-42%`, historija `7` dana.
- Postavke: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Logovi: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Historija i WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- Lozinka se prosljeđuje u `ipmitool -E` kroz `IPMI_PASSWORD` i ne pojavljuje se u argumentima komandne linije.

## Greške i hardverske granice

- Greške autentifikacije, mreže, SDR-a, WebView2, raw naredbi i upisa loga prikazuju se i bilježe; ne označavaju se kao uspjeh.
- Ako ručni režim uspije, ali sljedeća procentualna naredba ne uspije, aplikacija šalje tačno jednu Dell Auto naredbu za oporavak. Izvorni zahtjev ostaje neuspješan i ne ponavlja se.
- `0x00-0x05` su ciljni selektori firmwarea, ne procenti. Upravljanje pojedinačnim ventilatorom zadano je isključeno; `0x00` je na testnom serveru ubrzao sve ventilatore.
- Korisničke naredbe čekaju IPMI zaključavanje. Pozadinski polling i automatski tick vidljivo se preskaču kada je zaključavanje zauzeto; drugi `ipmitool` proces se ne pokreće.
- Niske brzine ili pogrešne krivulje mogu pregrijati CPU, diskove, PCIe kartice i napajanja. Ako niste sigurni, koristite Dell Auto.

## Provjera i dokumentacija

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Ove provjere ne zamjenjuju test na ciljnom serveru. Pokretanje GUI-a, stvarne raw naredbe, drugi iDRAC firmware i sve DPI kombinacije nisu potpuno pokriveni. Pogledajte [engleski vodič](README.en-US.md), [IPMI naredbe](docs/COMMANDS.en-US.md) i [sigurnost](SECURITY.en-US.md).
