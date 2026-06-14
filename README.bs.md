[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD Smart Fan Center

WinUI 3 aplikacija za Dell PowerEdge R730xd: kontrola ventilatora preko iDRAC/IPMI, nadzor BMC senzora i historijski grafikoni. Puna duga dokumentacija je na [简体中文](README.md) i [English](README.en-US.md); ova stranica daje osnovni vodič na bosanskom.

## Brzi početak

1. Uključite **IPMI over LAN** u iDRAC i provjerite da Windows može doći do upravljačke mreže.
2. Pokrenite iz izvornog koda: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. U Settings unesite iDRAC/BMC host, korisnika i lozinku. DPAPI spremanje koristite samo ako želite automatsko povezivanje.
4. Save settings izvršava stvarne `mc info` i `sdr elist`; polling počinje tek nakon uspjeha.
5. Počnite s Dell Auto ili opreznih 20%/35%. Niske brzine i pojedinačni Fan ciljevi moraju se provjeravati uz nadzor.

## Važno ponašanje

- Greške se prikazuju kroz stdout/stderr, UI status i JSONL log; ne skrivaju se kao uspjeh.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` i `ipmitool` ostaju tehnički pojmovi ili jedinice.
- Logovi su u `%LocalAppData%\DellR730xdFanControlCenter\logs`, historija grafikona u `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Kontrola ventilatora direktno utiče na hlađenje. Ako opterećenje, temperatura okoline ili senzori nisu jasni, koristite Dell Auto.
