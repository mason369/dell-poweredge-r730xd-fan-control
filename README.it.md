[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Centro ventole intelligente R730XD

App WinUI 3 per Dell PowerEdge R730xd con controllo ventole iDRAC/IPMI, monitoraggio sensori BMC e grafici storici. La documentazione completa è in [简体中文](README.md) e [English](README.en-US.md); questa pagina è la guida essenziale in italiano.

## Avvio rapido

1. Abilitare **IPMI over LAN** in iDRAC e verificare che Windows raggiunga la rete di gestione.
2. Avviare dai sorgenti: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. In Settings inserire host, utente e password iDRAC/BMC. Attivare DPAPI solo se serve la connessione automatica.
4. Save settings esegue comandi reali `mc info` e `sdr elist`; il polling parte solo dopo il successo.
5. Iniziare con Dell Auto o valori prudenti come 20%/35%. Basse velocità e controllo del singolo Fan richiedono supervisione.

## Comportamento importante

- Gli errori mostrano stdout/stderr, stato UI e log JSONL; non vengono nascosti come successi.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` e `ipmitool` restano termini tecnici o unità.
- I log sono in `%LocalAppData%\DellR730xdFanControlCenter\logs`; la cronologia grafici in `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Il controllo ventole incide direttamente sul margine termico. Se carico, ambiente o sensori sono incerti, preferire Dell Auto.
