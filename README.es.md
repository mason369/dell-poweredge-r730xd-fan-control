[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Centro inteligente de ventiladores R730XD

Aplicación WinUI 3 para Dell PowerEdge R730xd: control de ventiladores por iDRAC/IPMI, supervisión de sensores BMC y gráficos históricos. La documentación larga completa está en [简体中文](README.md) y [English](README.en-US.md); esta página resume el uso esencial en español.

## Inicio rápido

1. Active **IPMI over LAN** en iDRAC y confirme que Windows puede llegar a la red de gestión.
2. Ejecute desde el código fuente: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. En Settings introduzca host, usuario y contraseña de iDRAC/BMC. Active DPAPI solo si necesita conexión automática futura.
4. Al guardar, la app ejecuta `mc info` y `sdr elist` reales; el sondeo continuo empieza solo si ambos pasos funcionan.
5. Empiece con Dell Auto o valores conservadores como 20%/35%. Las velocidades bajas y los objetivos de un solo Fan deben validarse con supervisión.

## Comportamiento importante

- Los fallos se muestran con stdout/stderr, estado visible y logs JSONL; no se convierten en éxito aparente.
- `RPM`, `W`, `V`, `A`, `°C`, `iDRAC`, `IPMI`, `BMC`, `SDR` e `ipmitool` se conservan como términos técnicos o unidades.
- Los logs están en `%LocalAppData%\DellR730xdFanControlCenter\logs`; el historial de gráficos está en `%LocalAppData%\DellR730xdFanControlCenter\chart-history`.
- El control de ventiladores afecta directamente la refrigeración. Si la carga, la temperatura ambiente o los sensores no son claros, use Dell Auto.
