[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Control de ventiladores Dell PowerEdge R730xd mediante iDRAC

Aplicación WinUI 3 para Windows 10/11 que controla los ventiladores y supervisa el hardware de un Dell PowerEdge R730xd mediante iDRAC/IPMI. Incluye velocidades manuales, Dell Auto, curvas por temperatura de CPU o potencia, sensores BMC SDR, RPM de Fan 1-6, gráficos locales, preajustes y acciones de bandeja.

## Descarga y alcance

- Descargue `DellR730xdFanControlCenter-win-x64.zip` desde el [GitHub Release más reciente](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest), extraiga todo y ejecute `DellR730xdFanControlCenter.exe`.
- El código fuente actual y el Release publicado más reciente son `v1.1.3`; los exe/dll del paquete tienen la versión de archivo `1.1.3.0`, por lo que el código, la etiqueta y los binarios identifican la misma versión.
- El objetivo es Dell PowerEdge R730xd. Solo se ha observado localmente R730xd con iDRAC 2.82; otras versiones de firmware requieren validación supervisada.

## Funciones

- Control de todos los ventiladores de `0-100%`, preajustes manuales y devolución del control a Dell Auto.
- Regulación automática por temperatura de CPU y curvas editables de temperatura-ventilador y potencia-ventilador.
- Consultas reales `mc info` y `sdr elist` para temperatura, RPM, potencia, voltaje, corriente, redundancia y estados discretos.
- Historial JSONL local de siete días con ECharts/WebView2, 22 idiomas de interfaz, menú de bandeja y almacenamiento opcional de contraseña protegido con DPAPI.

## Inicio rápido

1. Active **IPMI over LAN** en iDRAC y compruebe la red de gestión.
2. Ejecute el Release extraído o el código fuente: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. En la página de configuración, introduzca dirección iDRAC/BMC, usuario y contraseña. Active DPAPI solo si necesita conexión automática posterior.
4. Al guardar se ejecutan primero `mc info` y luego `sdr elist`. El sondeo y el estado de éxito empiezan solo tras confirmar los comandos y escribir sus logs.
5. Empiece con Dell Auto o valores prudentes de `20%`/`35%` y observe temperaturas y RPM.

## Valores predeterminados y archivos locales

- Sondeo `1 s`, espera de comando `35 s`, temperaturas objetivo/alta/emergencia `68 °C` / `78 °C` / `84 °C`, rango automático `10-42%`, historial `7` días.
- Configuración: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- Logs: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- Historial y WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history` y `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- La contraseña se entrega a `ipmitool -E` mediante `IPMI_PASSWORD`; no se incluye en los argumentos de la línea de comandos.

## Fallos y límites de hardware

- Los fallos de autenticación, red, SDR, WebView2, comandos raw y escritura de logs se muestran y registran; no se presentan como éxito.
- Si el modo manual se activa pero falla el porcentaje siguiente, la aplicación envía una sola orden de recuperación Dell Auto. La solicitud original sigue fallida y no se reintenta.
- `0x00-0x05` son selectores de destino del firmware, no porcentajes. El control individual está desactivado de forma predeterminada; `0x00` aceleró todos los ventiladores en el equipo probado.
- Las órdenes del usuario esperan el bloqueo IPMI. El sondeo y los ciclos automáticos en segundo plano se omiten de forma visible cuando está ocupado; no se inicia un segundo proceso `ipmitool`.
- Velocidades bajas o curvas inadecuadas pueden sobrecalentar CPU, discos, tarjetas PCIe y fuentes. Use Dell Auto si hay dudas.

## Verificación y documentación

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

Estas comprobaciones no sustituyen una prueba en el servidor real. No cubren por completo el inicio de la GUI, comandos raw reales, otras versiones de iDRAC ni todas las combinaciones DPI. Consulte la [guía inglesa](README.en-US.md), los [comandos IPMI](docs/COMMANDS.en-US.md) y la [seguridad](SECURITY.en-US.md).
