[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Controllo ventole Dell PowerEdge R730xd tramite iDRAC

Applicazione WinUI 3 per Windows 10/11 dedicata al controllo ventole e al monitoraggio hardware di Dell PowerEdge R730xd tramite iDRAC/IPMI. Riunisce velocità manuali, Dell Auto, curve basate su temperatura CPU o potenza, sensori BMC SDR, RPM di Fan 1-6, grafici locali, preset e azioni dall'area di notifica.

## Download e ambito

- Scaricare `DellR730xdFanControlCenter-win-x64.zip` dall'[ultimo GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest), estrarre tutto e avviare `DellR730xdFanControlCenter.exe`.
- Il codice sorgente attuale è `1.1.2`. L'ultimo Release pubblicato resta `v1.1.0`, i cui exe/dll hanno versione file `1.1.0.0`; `1.1.2` diventa un Release ufficiale solo dopo la creazione del tag corrispondente.
- L'hardware di destinazione è Dell PowerEdge R730xd. Localmente è stato osservato solo R730xd con iDRAC 2.82; altri firmware richiedono verifica sorvegliata.

## Funzioni

- Controllo di tutte le ventole da `0-100%`, preset manuali e ritorno a Dell Auto.
- Regolazione automatica in base alla temperatura CPU e curve modificabili temperatura-ventola o potenza-ventola.
- Comandi reali `mc info` e `sdr elist` per temperatura, RPM, potenza, tensione, corrente, ridondanza e stati discreti.
- Sette giorni di cronologia JSONL locale con ECharts/WebView2, 22 lingue dell'interfaccia, menu nell'area di notifica e salvataggio password facoltativo protetto da DPAPI.

## Avvio rapido

1. Abilitare **IPMI over LAN** in iDRAC e verificare la rete di gestione.
2. Avviare il Release estratto o il sorgente: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Nella pagina delle impostazioni inserire indirizzo iDRAC/BMC, utente e password. Attivare DPAPI solo se serve una connessione automatica successiva.
4. Il salvataggio esegue prima `mc info` e poi `sdr elist`. Il polling e lo stato di successo iniziano solo dopo il reale successo dei comandi e della scrittura del log.
5. Iniziare con Dell Auto o valori prudenti `20%`/`35%` e osservare temperature e RPM.

## Valori predefiniti e file locali

- Polling `1 s`, timeout comando `35 s`, temperature obiettivo/alta/emergenza `68 °C` / `78 °C` / `84 °C`, intervallo automatico `10-42%`, cronologia `7` giorni.
- Impostazioni: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Log: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Cronologia e WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history` e `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- La password viene passata a `ipmitool -E` tramite `IPMI_PASSWORD` e non compare negli argomenti della riga di comando.

## Errori e limiti hardware

- Errori di autenticazione, rete, SDR, WebView2, comandi raw o scrittura log vengono mostrati e registrati; non sono presentati come successi.
- Se l'ingresso in modalità manuale riesce ma il successivo comando percentuale fallisce, l'app invia una sola richiesta di recupero Dell Auto. La richiesta originale resta fallita e non viene ripetuta.
- `0x00-0x05` sono selettori del firmware, non percentuali. Il controllo della singola ventola è disattivato per impostazione predefinita; `0x00` ha portato tutte le ventole ad alta velocità sulla macchina provata.
- I comandi utente attendono il blocco IPMI. Polling e cicli automatici in background vengono saltati in modo visibile quando il blocco è occupato; non parte un secondo processo `ipmitool`.
- Velocità basse o curve errate possono surriscaldare CPU, dischi, schede PCIe e alimentatori. In caso di dubbio usare Dell Auto.

## Verifica e documentazione

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Questi controlli non sostituiscono una prova sul server target. Avvio GUI, comandi raw reali, altri firmware iDRAC e tutte le combinazioni DPI non sono coperti completamente. Consultare la [guida inglese](README.en-US.md), i [comandi IPMI](docs/COMMANDS.en-US.md) e la [sicurezza](SECURITY.en-US.md).
