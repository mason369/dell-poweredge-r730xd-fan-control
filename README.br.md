[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Centro inteligente de ventiladores R730XD

Aplicativo WinUI 3 para Dell PowerEdge R730xd com controle de ventiladores por iDRAC/IPMI, monitoramento de sensores BMC e gráficos históricos. A documentação completa está em [简体中文](README.md) e [English](README.en-US.md); esta página é o guia essencial em Português do Brasil.

## Início rápido

1. Ative **IPMI over LAN** no iDRAC e confirme que o Windows alcança a rede de gerenciamento.
2. Execute a partir do código-fonte: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. Em Settings, informe host, usuário e senha do iDRAC/BMC. Ative DPAPI só se precisar de conexão automática depois.
4. Save settings executa `mc info` e `sdr elist` reais; o polling contínuo começa somente após sucesso.
5. Comece com Dell Auto ou valores conservadores como 20%/35%. Velocidades baixas e alvos individuais de Fan devem ser validados com supervisão.

## Comportamento importante

- Falhas mostram stdout/stderr, estado da UI e log JSONL; não são escondidas como sucesso.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` e `ipmitool` permanecem como termos técnicos ou unidades.
- Logs ficam em `%LocalAppData%\DellR730xdFanControlCenter\logs`; histórico dos gráficos em `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- Controle de ventiladores afeta diretamente a refrigeração. Se carga, ambiente ou sensores forem incertos, prefira Dell Auto.
