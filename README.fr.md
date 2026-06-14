[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Centre intelligent des ventilateurs R730XD

Application WinUI 3 pour Dell PowerEdge R730xd avec contrôle des ventilateurs iDRAC/IPMI, surveillance des capteurs BMC et graphiques historiques. La documentation longue complète est maintenue en [简体中文](README.md) et en [English](README.en-US.md); cette page donne l'entrée essentielle en français.

## Démarrage rapide

1. Activez **IPMI over LAN** dans iDRAC et vérifiez que Windows atteint le réseau de gestion.
2. Lancez depuis les sources : `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Dans Settings, saisissez l'adresse iDRAC/BMC, l'utilisateur et le mot de passe. Activez DPAPI seulement si la connexion automatique est souhaitée.
4. Save settings exécute réellement `mc info` puis `sdr elist`; le polling ne démarre qu'après réussite.
5. Commencez avec Dell Auto ou 20%/35%. Les faibles vitesses et le contrôle d'un seul Fan doivent être validés sous surveillance.

## Points importants

- Les erreurs affichent stdout/stderr, l'état UI et le log JSONL; elles ne sont pas masquées comme des succès.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` et `ipmitool` restent des termes techniques ou des unités.
- Les logs sont dans `%LocalAppData%\DellR730xdFanControlCenter\logs`; l'historique des graphiques dans `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Le contrôle des ventilateurs influence directement la marge thermique. Si la charge ou les capteurs sont incertains, utilisez Dell Auto.
