[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Contrôle des ventilateurs Dell PowerEdge R730xd par iDRAC

Application WinUI 3 pour Windows 10/11 destinée au contrôle des ventilateurs et à la surveillance matérielle d'un Dell PowerEdge R730xd par iDRAC/IPMI. Elle regroupe vitesses manuelles, Dell Auto, courbes selon la température CPU ou la puissance, capteurs BMC SDR, RPM des Fan 1-6, graphiques locaux, préréglages et commandes de zone de notification.

## Téléchargement et périmètre

- Téléchargez `DellR730xdFanControlCenter-win-x64.zip` depuis le [dernier GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest), extrayez tout puis lancez `DellR730xdFanControlCenter.exe`.
- Le code source actuel est en version `1.1.2`. Le dernier Release publié reste `v1.1.0`, dont les exe/dll portent la version de fichier `1.1.0.0` ; `1.1.2` ne deviendra un Release officiel qu'après la construction du tag correspondant.
- Le matériel ciblé est le Dell PowerEdge R730xd. Seul un R730xd avec iDRAC 2.82 a été observé localement; les autres firmwares doivent être validés sous surveillance.

## Fonctions

- Régler tous les ventilateurs de `0-100%`, enregistrer des préréglages manuels ou rendre le contrôle à Dell Auto.
- Régulation automatique selon la température CPU et courbes modifiables température-ventilateur ou puissance-ventilateur.
- Exécution réelle de `mc info` et `sdr elist` pour température, RPM, puissance, tension, courant, redondance et états discrets.
- Sept jours d'historique JSONL local avec ECharts/WebView2, 22 langues d'interface, menu de zone de notification et stockage facultatif du mot de passe protégé par DPAPI.

## Démarrage rapide

1. Activez **IPMI over LAN** dans iDRAC et vérifiez le réseau de gestion.
2. Lancez le Release extrait ou le code source: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Dans la page de paramètres, saisissez l'adresse iDRAC/BMC, l'utilisateur et le mot de passe. Activez DPAPI uniquement si une connexion automatique ultérieure est nécessaire.
4. L'enregistrement exécute d'abord `mc info`, puis `sdr elist`. Le polling et l'état de réussite ne commencent qu'après le succès réel des commandes et de l'écriture du log.
5. Commencez par Dell Auto ou `20%`/`35%` et surveillez les températures et les RPM.

## Valeurs par défaut et fichiers locaux

- Polling `1 s`, délai de commande `35 s`, températures cible/haute/urgence `68 °C` / `78 °C` / `84 °C`, plage automatique `10-42%`, historique `7` jours.
- Paramètres: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Logs: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Historique et WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history` et `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- Le mot de passe est transmis à `ipmitool -E` par `IPMI_PASSWORD` et n'apparaît pas dans les arguments de ligne de commande.

## Échecs et limites matérielles

- Les erreurs d'authentification, de réseau, SDR, WebView2, commande raw ou écriture du log sont affichées et enregistrées; elles ne sont pas signalées comme réussies.
- Si le passage en mode manuel réussit mais que le pourcentage suivant échoue, l'application envoie une seule commande de récupération Dell Auto. La demande initiale reste en échec et n'est pas relancée.
- `0x00-0x05` sont des sélecteurs de cible du firmware, pas des pourcentages. Le contrôle individuel est désactivé par défaut; `0x00` a accélé tous les ventilateurs sur la machine testée.
- Les commandes utilisateur attendent le verrou IPMI. Le polling et les cycles automatiques d'arrière-plan sont ignorés de façon visible lorsque le verrou est occupé; aucun second processus `ipmitool` n'est lancé.
- Des vitesses trop faibles ou de mauvaises courbes peuvent surchauffer CPU, disques, cartes PCIe et alimentations. Utilisez Dell Auto en cas de doute.

## Vérification et documentation

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Ces vérifications ne remplacent pas un essai sur le serveur cible. Elles ne couvrent pas totalement le lancement de la GUI, les commandes raw réelles, les autres firmwares iDRAC ni toutes les combinaisons DPI. Voir le [guide anglais](README.en-US.md), les [commandes IPMI](docs/COMMANDS.en-US.md) et la [sécurité](SECURITY.en-US.md).
