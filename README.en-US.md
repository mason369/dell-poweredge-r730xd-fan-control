<p align="center">
  <img src="Assets/Logo.svg" width="128" alt="R730XD Smart Fan Center logo" />
</p>

<h1 align="center">Dell R730xd iDRAC Fan Control Center</h1>

<p align="center">
  <strong>A WinUI 3 iDRAC/IPMI fan-control, sensor-monitoring, and historical-visualization app for Dell PowerEdge R730xd.</strong>
</p>

<p align="center">
  <a href="README.en-US.md">English</a> |
  <a href="README.md">简体中文</a> |
  <a href="README.zht.md">繁體中文</a> |
  <a href="README.ko.md">한국어</a> |
  <a href="README.de.md">Deutsch</a> |
  <a href="README.es.md">Español</a> |
  <a href="README.fr.md">Français</a> |
  <a href="README.it.md">Italiano</a> |
  <a href="README.da.md">Dansk</a> |
  <a href="README.ja.md">日本語</a> |
  <a href="README.pl.md">Polski</a> |
  <a href="README.ru.md">Русский</a> |
  <a href="README.bs.md">Bosanski</a> |
  <a href="README.ar.md">العربية</a> |
  <a href="README.no.md">Norsk</a> |
  <a href="README.br.md">Português (Brasil)</a> |
  <a href="README.th.md">ไทย</a> |
  <a href="README.tr.md">Türkçe</a> |
  <a href="README.uk.md">Українська</a> |
  <a href="README.bn.md">বাংলা</a> |
  <a href="README.gr.md">Ελληνικά</a> |
  <a href="README.vi.md">Tiếng Việt</a>
</p>

<p align="center">
  <a href="docs/COMMANDS.en-US.md">IPMI Command Reference</a> ·
  <a href="SECURITY.en-US.md">Security</a> ·
  <a href="docs/PROJECT_METADATA.en-US.md">Project Metadata</a>
</p>

<p align="center">
  <img alt="Windows 10 2004+" src="https://img.shields.io/badge/Windows-10%202004%2B-0078D4?logo=windows&logoColor=white" />
  <img alt=".NET 8.0" src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="WinUI 3" src="https://img.shields.io/badge/UI-WinUI%203-0078D7" />
  <img alt="Dell PowerEdge R730xd" src="https://img.shields.io/badge/Dell-PowerEdge%20R730xd-0672CB" />
  <img alt="iDRAC IPMI over LAN" src="https://img.shields.io/badge/iDRAC-IPMI%20over%20LAN-0F766E" />
  <img alt="ECharts" src="https://img.shields.io/badge/Charts-ECharts-AA344D" />
  <img alt="License MIT" src="https://img.shields.io/badge/License-MIT-16A34A" />
  <img alt="Explicit failure handling" src="https://img.shields.io/badge/failures-explicit-B91C1C" />
  <img alt="22 UI languages" src="https://img.shields.io/badge/UI%20languages-22-4B5563" />
</p>

Dell R730xd iDRAC Fan Control Center is a Windows WinUI 3 desktop app for controlling Dell PowerEdge R730xd fans through iDRAC/IPMI, reading BMC sensors, visualizing live hardware state, and handing control back to Dell firmware automatic mode when needed.

This project is not a generic server management suite. It is a local control center for the R730xd, iDRAC/BMC, `ipmitool`, and Dell OEM raw fan commands. The design priorities are command transparency, explicit failure reporting, and recoverable operation. The app does not silently switch backends or pretend a command succeeded. Missing tools, authentication errors, insufficient privileges, unsupported firmware commands, and empty SDR reads are shown directly in the UI and logs.

## Quick Links

| Goal | Entry |
| --- | --- |
| See what the app does | [Feature Overview](#feature-overview) |
| Connect to iDRAC for the first time | [First-Run Workflow](#first-run-workflow) |
| Build or run locally | [Build](#build) / [Run](#run) |
| Review raw commands and fan target selectors | [IPMI Command Reference](docs/COMMANDS.en-US.md) |
| Check credential, log, and supply-chain risks | [Security](SECURITY.en-US.md) |
| Troubleshoot sensors, polling, charts, or tray behavior | [Troubleshooting](#troubleshooting) |

## 5-Minute Quick Start

Use this path when you already know the iDRAC address, username, and password, and you want to confirm that sensors and fan control work before reading the full manual.

1. Enable **IPMI over LAN** in iDRAC and confirm that the current Windows machine can reach the iDRAC management network.
2. Start the development build from source:

   ```powershell
   cd C:\DellR730xdFanControlCenter
   dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
   ```

3. First launch opens Settings. Enter the iDRAC/BMC address, username, and password. Enable "Remember password with DPAPI" only if you want automatic connection on later launches.
4. Click "Save settings". The app runs a real `mc info` connection test, then one `sdr elist` refresh, and starts persistent polling only after those succeed.
5. Return to Overview and confirm real readings for CPU/inlet/exhaust temperatures, Fan 1-6 RPM, power, voltage, current, and state cards.
6. Start with "Restore Dell factory fan speed" or conservative 20%/35% manual speeds before testing lower speeds. Low fan speeds, individual target selectors, and curve-auto policies should be validated gradually while someone is watching the machine.
7. To hand control back to BMC, click "Restore Dell factory fan speed". If smart auto or curve auto is running, also click "Stop auto"; otherwise the next auto tick can send software fan commands again.

```mermaid
flowchart LR
    A["Enable iDRAC IPMI over LAN"] --> B["Enter Host / User / Password"]
    B --> C["Save settings"]
    C --> D["mc info connection test"]
    D --> E["First sdr elist refresh"]
    E --> F["Overview boards and charts update"]
    F --> G["Choose Dell Auto, manual percent, or curve policy"]
```

## Running App Interface

The image below is an actual running app screenshot. It shows the interactive visualization page, history-range filters, temperature/fan/performance/electrical charts, and visible status feedback. The values shown come from the local R730xd validation environment and are examples of the UI and data flow; different workloads, iDRAC firmware, fan walls, drive counts, and ambient conditions produce different readings.

![R730XD Smart Fan Center running interface with interactive history charts and hardware status](Assets/Screenshots/main-window.png)

## Visual Workflows

### Commands And Failure Handling

```mermaid
flowchart TB
    U["User button, tray menu, or background tick"] --> L{"IPMI lock available?"}
    L -- "No, background tick" --> S["Skip this tick and log: no new ipmitool"]
    L -- "No, user command" --> W["Wait for the current user command chain to finish"]
    L -- "Yes" --> P["Start ipmitool.exe"]
    P --> R{"Exit code is 0?"}
    R -- "Yes" --> O["Update UI, JSONL logs, and chart history"]
    R -- "No" --> X["Show stdout/stderr root cause and stop the current operation"]
```

### Pages And Common Entrypoints

```mermaid
flowchart LR
    O["Overview"] --> O1["Live summary, charts, quick actions, recent log"]
    F["Fan Control"] --> F1["Manual presets, temperature curves, power curves, smart auto"]
    S["Sensors"] --> S1["Full SDR table and localized sensor names"]
    C["Settings"] --> C1["Connection, password, polling, theme, language, tray"]
    T["Tray Menu"] --> T1["Restore window, refresh sensors, presets, stop auto, exit"]
```

## Multilingual Docs And UI

- README language entries: [English](README.en-US.md), [简体中文](README.md), [繁體中文](README.zht.md), [한국어](README.ko.md), [Deutsch](README.de.md), [Español](README.es.md), [Français](README.fr.md), [Italiano](README.it.md), [Dansk](README.da.md), [日本語](README.ja.md), [Polski](README.pl.md), [Русский](README.ru.md), [Bosanski](README.bs.md), [العربية](README.ar.md), [Norsk](README.no.md), [Português (Brasil)](README.br.md), [ไทย](README.th.md), [Türkçe](README.tr.md), [Українська](README.uk.md), [বাংলা](README.bn.md), [Ελληνικά](README.gr.md), and [Tiếng Việt](README.vi.md).
- Compatibility entry: [README.zh.md](README.zh.md) points readers to the default Simplified Chinese README. The full long-form manuals are maintained in [README.md](README.md) and this file [README.en-US.md](README.en-US.md); other language README files provide real localized entry pages for core use, safety, and verification instead of dead language-switch links.
- 中文配套文档: [IPMI 命令参考](docs/COMMANDS.md), [安全说明](SECURITY.md), [项目元数据](docs/PROJECT_METADATA.md).
- English companion docs: [IPMI Command Reference](docs/COMMANDS.en-US.md), [Security](SECURITY.en-US.md), [Project Metadata](docs/PROJECT_METADATA.en-US.md).
- The app includes 22 UI languages. Open Settings, choose a language in the UI language dropdown, and save; visible UI switches immediately. Existing JSONL runtime logs keep their structured fields and original runtime semantics, and are not rewritten into another language.

## Scope

- Target server: Dell PowerEdge R730xd.
- Target control plane: iDRAC/BMC IPMI over LAN.
- Target OS: Windows 10 2004 / build 19041 or newer.
- Target users: homelab operators, server caretakers, dense-drive R730xd owners, and noise-constrained environments that still need visible thermal control.
- Locally observed environment: R730xd / iDRAC firmware 2.82. Documentation and default configuration use reserved example host `192.0.2.10` and example user `idrac-user`; real private addresses and accounts should not be committed to the repository.

Different iDRAC firmware versions, backplanes, fan layouts, and sensor layouts can change individual fan target-selector behavior. Fan 1-6 target selectors are implemented in code but disabled by default; `0x00` is a target selector in the firmware raw command, not `0%` fan speed, so verify your firmware mapping before enabling them.

### Usage-Scope Photos

The photos below show the actual Dell PowerEdge R730xd chassis environment this project targets, including dual CPU heatsinks, the front fan wall, expansion-card area, and chassis airflow path. They document the hardware scope of the app; they do not imply that every R730xd has the same backplane, cards, drive count, cable routing, or iDRAC SDR sensor names.

![Dell PowerEdge R730xd internal top view showing CPU heatsinks, fan wall, and expansion-card area](Assets/Screenshots/r730xd-hardware-top.jpg)

![Dell PowerEdge R730xd internal airflow view showing CPU heatsinks, fan wall, and front chassis structure](Assets/Screenshots/r730xd-hardware-airflow.jpg)

## Feature Overview

- Modern WinUI 3 interface with light, dark, and system theme support.
- All-fan percentage control from 0-100%; the app enters manual fan mode before setting a percentage.
- The built-in Default/Restore Manual preset keeps manual mode plus all fans at 10% for users who explicitly choose that preset as a local quiet baseline.
- Starter presets: Default 10%, Balanced 20%, Cooling 35%, Performance 50%, and Dell Auto.
- Edit preset names, descriptions, and available percentages; add, save, and delete manual percentage presets. Starter presets can also be deleted; the deletion is written to `settings.json`, the next launch does not re-seed them, and starter presets return only when the settings file is removed or recreated.
- Add and edit temperature-fan curve presets, plus power-fan curve presets in the editor below them. Both curve editors support clicking empty chart space to add points, dragging existing points to adjust them live, right-side numeric controls for fine tuning, optional smooth transitions, and continuous all-fan control after switching to the preset.
- Dell automatic fan mode remains available as both a separate action and a preset entry. It sends the command that hands fan control back to iDRAC/BMC; if the software auto timer is still running, a later tick can send software fan commands again, so use "Stop auto" to stop the background policy.
- Individual target-byte control for fans 1-6 is implemented but disabled by default.
- The software auto policy reads BMC SDR. The global policy and temperature curves use CPU temperature; power curves use the power sensor whose unit is `Watts` or whose key contains `Pwr Consumption`, while still checking the CPU emergency temperature threshold first.
- At the emergency temperature threshold, the smart auto policy sends the Dell automatic mode command. That command does not stop the software auto timer; later ticks continue to run the current policy.
- Clicking "Start polling" or successfully saving settings tests the iDRAC connection, reads SDR once, and starts persistent SDR polling with a 1-second default interval. While polling is active, the same command reads "Cancel polling"; clicking it stops future sensor polling ticks and updates the visible state instead of pretending another connection test ran. If an `ipmitool` command is already in flight, that command is allowed to finish and the app does not start a new tick. Only one IPMI operation is allowed at a time, so a tick is skipped when the previous poll is still running, and no new `ipmitool` process or RMCP+ session is started for that skipped tick. When a background sensor polling command fails, the app first stops the current poll, shows and logs the original failure, then runs one real reconnect flow (`mc info` + `sdr elist`); polling resumes only after that reconnect succeeds. If reconnect fails, the app stays disconnected and shows the root cause without writing fabricated chart history.
- After a manual preset, Dell Auto, temperature curve, power curve, or smart temperature policy starts successfully, the running state is written to `%LocalAppData%\DellR730xdFanControlCenter\settings.json`. On the next app launch, after a real connection/start-polling sequence succeeds, the app re-executes the saved preset or automatic policy. Restore failures show the real error instead of only marking the UI as running.
- User-triggered fan commands wait for the current IPMI command to finish before continuing, so switching presets no longer shows the "IPMI command is already running" busy error. Background polling ticks, smart-auto ticks, and curve-auto ticks still skip while IPMI is busy to avoid accumulating queued commands.
- While smart auto or curve auto is running, the regular sensor polling timer does not start a second independent `sdr elist` sampling path. Each auto-policy tick reads SDR, updates the sensor list, dashboard boards, interactive charts, and JSONL chart history. After auto policy is stopped, regular polling continues on the configured cadence.
- Smart auto and curve auto remember the most recent all-fan percentage successfully sent by the same automatic mode. If a later tick computes the same percentage from the current temperature or power reading, it logs "No fan command sent" and still refreshes sensors/charts, but it does not resend the same Dell raw fan-control command. Switching automatic modes, manual all-fan control, individual fan control, Dell Auto, or Stop Auto clears this cache so the next automatic takeover sends the required target again.
- Every real `ipmitool` command, including Dell fan-control raw commands (`raw 0x30 0x30 ...`), fails immediately on a non-zero exit code and shows stdout/stderr. The app does not retry the same failed command, delay and retry, or record the failure as "retrying". The only background recovery action is one visible real reconnect after a sensor polling failure; that reconnect does not reapply the last saved fan preset or automatic policy, avoiding a duplicate fan command after a read failure.
- After a user-triggered fan command succeeds, including all-fan control, individual fan control, manual presets, restore-manual presets, and Dell automatic mode, the app immediately runs one more `sdr elist` read and uses the real BMC response to refresh the fan RPM board, power/state board, interactive charts, and JSONL chart history point. If that refresh fails, the real error is shown; the app does not fabricate sensor data from the just-sent percentage. After the refresh succeeds, the next polling timer interval is restarted from that completion time so the original tick does not immediately duplicate the same read.
- Bundled `ipmitool.exe` and required Cygwin DLLs under `BundledTools/ipmitool`.
- Bundled local ECharts dashboard assets under `Assets/Charts/dashboard.html` and `Assets/Charts/echarts.min.js`; runtime does not depend on an online CDN.
- The Overview interactive charts store a complete chart snapshot after each successful SDR poll and persist it under `%LocalAppData%\DellR730xdFanControlCenter\chart-history\chart-history-YYYYMMDD.jsonl`. The chart header's "History range" control switches between Current, Last 6 hours, Last 1 day, Last 3 days, Last 7 days, and Custom. History files are retained for the latest 7 days by default, and the app reloads retained points after restart.
- Tray icon supports background operation. Its right-click menu keeps page entry points, refresh sensors, open iDRAC, open logs, stop auto policy, Restore Dell factory fan speed, all-fan 20/35/50%, and preset switching mostly at one level; dynamic presets remain a single submenu. The tray restore action sends Dell automatic mode and does not switch to manual 10%.
- iDRAC web console shortcut opens `https://<host>/`.
- All visible UI strings are wired to localization resources. The app now includes 22 interface languages: English, 简体中文, 繁體中文, 한국어, Deutsch, Español, Français, Italiano, Dansk, 日本語, Polski, Русский, Bosanski, العربية, Norsk, Português (Brasil), ไทย, Türkçe, Українська, বাংলা, Ελληνικά, and Tiếng Việt. The language selector shows each option in that language's own native name instead of translating every language name into the current UI language; the MSIX package manifest display name and description are also localized through `Strings/<language>/Resources.resw`, so Start menu, package metadata, and shell surfaces do not keep fixed Chinese or English text; backend JSONL logs and internal runtime records keep Chinese/structured internal semantics and are not converted through UI localization.
- Charts, dashboard cards, status messages, and the Sensors table's Sensor column use localized display names. Known SDR names are translated into the current UI language; unregistered English/vendor discrete event names display as localized "Hardware event <SDR ID>" labels so the UI does not expose fixed English strings. The original BMC key is still used internally for classification and matching; run `ipmitool sdr elist` manually when exact raw output needs to be compared.
- The Overview interactive chart runs in WebView2, but mouse-wheel events are forwarded to the outer WinUI `ScrollViewer` for page scrolling. Forwarding distinguishes discrete mouse-wheel input from high-frequency precision scrolling: mouse wheels use WinUI platform scroll animation, while touchpad-style small deltas move by the coalesced distance directly. ECharts internal wheel zoom is disabled, and history ranges are still adjusted with the bottom slider, so scrolling over the chart should not trap the page.
- The main window and chart page adapt to Windows DPI scaling, window width, and narrow layouts. `app.manifest` declares `PerMonitorV2`; `MainPage.xaml.cs` switches Small, Medium, and Large layouts from effective pixel width on startup and every window-size change; and `Assets/Charts/dashboard.html` also reads container width plus `window.devicePixelRatio` before laying out charts. This adaptation does not require a Settings toggle, and failures do not create fake charts or fake layout-success states.
- Passwords can be stored with Windows DPAPI under the current Windows user context.
- Runtime logs are written as JSON Lines to `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`; each line is a complete atomic event, including user commands, sensor refreshes, smart auto ticks, and IPMI command timing.
- Startup exceptions are written to `%LocalAppData%\DellR730xdFanControlCenter\startup-error.log`.

## Screens

### Resolution, DPI, And Window Scaling

The main UI adapts automatically at app startup and after every window-size change; users do not need to enable an extra Settings switch.

- Entry points and scope: adaptation covers Overview, historical charts, Quick Actions, Temperature Board, Fan RPM Board, Power & Health, Fan Control, curve editors, Sensors table, and Settings. The tray menu remains a native Windows menu rendered by the operating system for the current DPI.
- Default configuration: `app.manifest` uses `PerMonitorV2` DPI awareness, and the app switches layout by WinUI effective pixels instead of physical resolution. The current `MainPage.xaml.cs` breakpoints are `<641` for Small, `641-1007` for Medium, and `>=1008` for Large.
- Execution flow: `MainPage.OnPageSizeChanged` calls `ApplyResponsiveLayout`, then reflows the hero, overview metric cards, quick actions, all-fan controls, preset editor, temperature/power curve editors, smart-auto controls, and Settings command bar. Small layout changes `NavigationView` to top navigation; Medium and Small layouts let temperature, fan, and power/health boards use page scrolling instead of fixed-height clipping.
- Chart flow: the Overview WebView minimum height is `1520` in Large layout and `3200` in Medium/Small layout so historical chart panels are not covered by following content. `Assets/Charts/dashboard.html` adjusts grid, legend, axis margins, and line rendering from container width, chart count, and `window.devicePixelRatio`. Chart labels do not use ellipsis as the normal display strategy.
- Success behavior: on narrow windows or high DPI, cards, command bars, charts, and tables should show complete content through wrapping, reflow, or outer page scrolling. The UI should not show bottom divider lines through content, hidden charts, crowded button text, abbreviated performance/electrical labels, or horizontal page overflow.
- Failure behavior: if WebView2 resources, chart scripts, or local chart assets fail to load, the top error and runtime log show the real failure. The app does not use blank images, fabricated data, or silent downgrade behavior to pretend charts are ready.
- Verified scope: the current repository static checks include `RunContentScrollWidthXamlChecks`, `RunDpiTextWrappingXamlChecks`, and `RunDashboardChartLayoutChecks` in `Tests/PresetModelTests/Program.cs`. Local manual validation covered Windows window sizes `640x900`, `920x900`, and `1366x900`, plus chart-page equivalent DPR `1.0`, `1.25`, `1.5`, `1.75`, and `2.0`. That validation record is not a guarantee that every remote-desktop, GPU-driver scaling, or third-party window-manager combination is identical; when reporting an issue, include the Windows scaling percentage, window size, screenshot, and runtime log.

### Overview

The Overview page is for observing hardware state and running common actions:

- The hero live summary shows current temperature, average fan RPM, live power, average voltage, and total current from the latest successful SDR sensor refresh. Under each large value, the app shows every concrete sensor detail for that category on separate lines, such as all inlet/exhaust/CPU temperatures, Fan 1-6 RPM, power consumption, all voltage rails, and all current rails; it no longer truncates the hero details to the first three items. The live summary cards have a taller baseline and grow with the actual number of returned sensors, so systems with more fans or electrical rails display those additional rows. Before the first refresh, or when one sensor category is missing, that value shows "Waiting". The temperature summary uses the average of the latest temperature sensor readings, not a historical maximum; the overview card and emergency automatic protection keep their separate max/CPU temperature semantics. Large values and details are color-coded by recommended live ranges: normal green, near-risk yellow, clear deviation orange, and danger red. Current thresholds are temperature `<60/60-69/70-79/>=80 C`; average RPM `2500-6000` green, `1500-2499 or 6001-9000` yellow, `500-1499 or >9000` orange, `<500` red; power `<500/500-699/700-899/>=900 W`; voltage `210-240` green, `200-209 or 241-250` yellow, `190-199 or 251-260` orange, otherwise red; and total current `<4/4-5.9/6-7.9/>=8 A`. These colors are UI hints only and do not replace the full sensor table, iDRAC alerts, or emergency automatic protection.
- Under the title, the left side of the hero shows a "Current thermal mode" badge that explicitly names whether the app is idle, in manual control, Dell automatic thermal control, the smart temperature policy, temperature-curve auto, or power-curve auto. Preset switches, start/stop auto actions, and auto ticks that update the current percent keep this badge in sync with the right-side status card.
- The right-side hero status card shows the current iDRAC target, connection state, current control mode, latest request status, and last update time.
- Latest request status updates in real time for Switch actions, start/cancel polling, refresh sensors, save settings, start/stop smart auto, curve auto ticks, and polling success/skip/failure. The hero card shows compact states such as "Requesting", "OK", "Skipped", "Failed", and "Applied"; the complete reason remains visible in the top InfoBar and runtime log.
- Metric cards show max CPU temperature, fan state, live power, average voltage, total current, and current control mode. Power, voltage, and current also come from the latest successful SDR refresh and show waiting/no-reading text when absent.
- Interactive visualization is now built around historical ranges: the overview trend shows max temperature, average fan RPM, CPU/memory/I/O/system usage, and power; the range profile compares range average, range peak, and range latest; the temperature chart shows time series for every temperature sensor in the selected range; the fan chart shows time series for every fan; the performance/electrical chart shows CPU, memory, I/O, system usage, power, voltage, and current over time; and the health chart shows OK and attention sensor counts over time. Every successful poll saves summary, current readings, type counts, and sensor tree data as a JSONL point with `timestamp` and `unixMilliseconds`; failed polls, skipped ticks, or polls without new SDR data do not create synthetic history. History load or write failures are shown as top errors and written to the runtime log.
- Temperature Board shows every temperature sensor reported by BMC SDR, with temperature icons and live recommended color states. Card subtitles label SDR metadata as compact text such as "ID 0x30 / Location 7.1"; raw `30h` is converted to `0x30` so it is not mistaken for hours, and `7.1` is the IPMI entity/instance location.
- Fan RPM Board shows current RPM readings for fan sensors, with a fan icon that rotates continuously. The animation period is derived from RPM: higher RPM rotates faster, while zero or missing readings are not shown as normal high-speed motion. Fan cards use the same "ID 0x30 / Location 7.1" subtitle instead of bare `30h · 7.1` values.
- Power & Health shows every matched power, voltage, current, redundancy, battery, intrusion, Power Optimized, and related state sensor with matching electrical/state icons and the same recommended color states. In the UI, `Power Optimized` appears as "Power optimization policy". If the BMC returns a sensor and the app classifies it as power or health, the UI creates a card for it instead of limiting the board to the first 14. Long state values wrap inside the card, and the labeled ID/location metadata remains visible.
- Quick Actions include refresh sensors, Restore Dell factory fan speed, open iDRAC, and all-fan percentage control. Start/stop controls for the smart auto policy now live on the Fan Control page.
- Recent Log shows command results, success/failure state, and polling warnings, and includes an "Open logs" entry point. UI status badges are color-coded by level: info is blue, warning is amber, success is green, and only error/failure uses red; the local JSONL runtime log still writes plain structured fields and does not store color values.

### Fan Control

The Fan Control page manages presets and advanced control:

- The preset area shows the current mode, starter presets, custom presets, and editable descriptions.
- Manual presets send Dell OEM raw commands for all-fan percentage control.
- Default/restore and manual presets can edit their percentage. The Overview quick restore and tray restore actions now restore Dell factory fan speed by sending Dell automatic mode instead of manual 10%.
- The Dell Auto preset restores the BMC firmware fan policy and does not expose a percentage field.
- The Smart Auto Policy start/stop card lives on the Fan Control page. Target, high, and emergency temperature thresholds no longer have an in-app editing entry point; the policy continues using values already stored in the settings file or the code defaults. Polling seconds are saved from the Settings page's Application area. Each tick writes runtime log records and updates the hero request status.
- Adding a manual preset requires a name and validates the percentage from 0-100.
- Curve presets are maintained with graphical editors instead of multiline text boxes. The temperature editor stores `TemperatureCelsius` + `FanPercent`; the power editor stores `PowerWatts` + `FanPercent`. After entering a curve name, click empty chart space to add a point; dragging an existing point updates the chart point and matching right-side numeric controls live. Hovering over either curve chart shows crosshair guides plus the current X-axis temperature/power value and Y-axis fan-speed percent, so the point meaning is visible before clicking. During drag, the editor does not repeatedly rebuild the full preview text; it refreshes preview text and strict validation after pointer release, right-side numeric edits, add-point actions, or save. Clicking "Edit points" on an existing curve preset loads it into the matching editor for its preset type and automatically scrolls to the temperature or power curve chart; after a successful save, the page scrolls back to the newly added or updated preset card.
- The temperature editor starts with `45 C = 18%`, `68 C = 28%`, and `78 C = 42%`; the power editor starts with `280 W = 18%`, `500 W = 28%`, and `750 W = 42%`. These defaults only seed a new editor and do not overwrite saved presets.
- Saving a curve requires at least 2 points. Temperature curves require temperatures from `-40` to `125` C, fan percentages from `0` to `100`, and no duplicate temperatures; power curves require power from `0` to `1200` W, fan percentages from `0` to `100`, and no duplicate power points. Invalid points show the validation reason in the preview area, and Add/Save still run the same strict validation instead of silently replacing the input with a default curve.
- The Smooth curve switch is saved with the preset. When it is off, the app evaluates the current temperature or power reading on the polyline formed by the configured points and uses the fan percent at that position. When it is on, the same points are used with a smoothed curve position, reducing abrupt percentage jumps as the input crosses a point. Values below the first point and above the last point are still clamped to the endpoint percentages.
- Switching to a temperature curve preset starts the software auto polling loop. Each tick reads SDR, parses CPU temperature, computes the percentage from the curve points and smooth setting, and sends the all-fan percentage command only when the result differs from the last percentage successfully sent by the same automatic mode. Switching to a power curve preset reads SDR, checks CPU emergency temperature first, then computes the percentage from the power reading; if the current SDR result has no matching power sensor, the UI and logs show the real failure and no fan command is sent.
- Manual all-fan control, individual fan control, the built-in Restore Manual preset, Dell Auto, Overview/tray Restore Dell factory fan speed, and Stop Auto all clear the current curve state. Only Stop Auto, deleting the running curve preset, or an auto-policy failure stops the software auto timer; if that timer is still running, the next tick continues controlling all fans with the global linear policy.
- Except for Stop Auto, successful user-triggered fan commands immediately append one real SDR refresh so Overview cards, performance/electrical charts, and chart history reflect the BMC's current values. That refresh still uses the same serialized IPMI lock; if it fails, the UI and logs show the failure reason instead of pretending the chart data was updated.
- Saving a preset writes it to local settings, and the tray menu reads those saved presets. Temperature curves are stored as `Kind = TemperatureCurve`; power curves are stored as `Kind = PowerCurve`; both keep points in `Presets[].CurvePoints` with their `TemperatureCelsius` or `PowerWatts` fields plus `SmoothCurve`. If the currently running manual preset, Dell Auto preset, temperature curve, or power curve is saved, the app waits for the current IPMI command to finish and immediately re-applies that preset; another "Switch" click is not required. Saving an active curve immediately runs one real `sdr elist` read and fan calculation; if that first run fails, the error is shown and that automatic policy is stopped.
- Individual fan controls are disabled by default and must be enabled and saved in Settings.
- Smart auto UI parameters are concentrated on the Settings page: polling seconds, minimum fan percentage, and maximum fan percentage remain editable; target temperature, high temperature, and emergency auto threshold remain settings-file fields and code defaults only.

### Sensors

The Sensors page shows the parsed `ipmitool sdr elist` table:

- `Key`: localized sensor display name. The app derives this from the raw BMC name, so `Fan1 RPM` displays as "Fan 1 RPM" and `Inlet Temp` displays as "Inlet temperature"; unregistered English/vendor discrete event names display as "Hardware event <SDR ID>", while the raw key remains available through manual `ipmitool sdr elist` comparison.
- `ID`: sensor record ID from SDR output, commonly shaped like `30h` or `76h`; the `h` suffix is a hexadecimal-style record marker, not hours. Overview cards display it as `0x30` or `0x76`.
- `Entity`: IPMI entity/instance location from SDR output, commonly shaped like `7.1` or `10.2`; dashboard cards label it as "Location 7.1" so it is not mistaken for a reading or version number.
- `Value`: numeric value or status from the raw reading text. Common IPMI enum values are localized, including `No Reading`, `State Deasserted`, `Fully Redundant`, `OEM Specific`/`Vendor specific`, and `Bus Uncorrectable error`. `OEM Specific`/`Vendor specific` displays as "Dell custom state", meaning the BMC returned a Dell/iDRAC private enum rather than a standalone fault conclusion; normal/abnormal health still comes from the same card's `Status` row and iDRAC alerts. Unknown enum values keep the raw BMC text so they can be compared with the original `sdr elist` output.
- `Unit`: unit such as `degrees C`, `RPM`, `Watts`, `Volts`, `Amps`, or `percent`. The UI localizes them as `°C`, RPM, W, V, A, and `%`.
- `Status`: BMC state such as `ok`, `ns`, `na`, or an attention state. Known short codes are localized; unknown values stay raw for troubleshooting.

If `ipmitool` exits successfully but returns no SDR rows, the app raises an error instead of inventing placeholder sensor data.

### Settings

The Settings page controls connection, persistence, and runtime behavior:

- The top "Save settings" and "Start polling / Cancel polling" actions are global commands spanning the connection and application columns. "Save settings" stores the iDRAC connection, password persistence option, polling seconds, smart-auto fan percentages, theme, and language together. "Start polling" runs the connection test, reads SDR once, and starts continuous polling; after polling is running the button becomes "Cancel polling", which stops the polling timer and updates the state. Save failures, start-polling failures, and cancel status appear in the top InfoBar and runtime log instead of being scoped to only the left connection column.
- iDRAC/BMC IP address or host name.
- iDRAC username.
- iDRAC password.
- Whether to save the password with DPAPI.
- Read-only bundled `ipmitool.exe` path.
- Whether the close button minimizes to tray.
- Whether individual fan target-selector control is enabled.
- Fan count, default 6.
- Command timeout seconds, default 35, minimum enforced by code is 5.
- SDR polling seconds, default 1. Saved values of 1 second or higher are allowed; 1 second is only the polling tick cadence and does not mean iDRAC can return a full SDR read every second.
- Smart auto minimum and maximum fan percentages.
- UI theme and language.

Settings are stored at:

```text
%LocalAppData%\DellR730xdFanControlCenter\settings.json
```

### Tray Right-Click Menu

When "minimize to tray on close" is enabled, closing the window hides the main window while background polling and the tray icon remain active. Right-clicking the tray icon shows these groups:

- Window and page entries: Restore window, Open Overview, Open Fan Control, Open Sensors, and Settings. These restore the window and switch pages; they do not send IPMI commands.
- Operations: Refresh sensors, Open iDRAC, and Open logs. Refresh sensors reads `sdr elist` and updates the table, dashboard cards, and charts on success; failures show the real error and are written to the runtime log. Open iDRAC builds `https://<host>/` from the current saved host.
- Fan quick controls: Restore Dell factory fan speed, Stop auto policy, All fans 20%, All fans 35%, and All fans 50%. Except for Stop auto policy, these directly trigger IPMI fan commands and share the same IPMI lock; if another command is running, user-triggered commands wait for the current command to finish and do not start a concurrent `ipmitool`.
- Presets: reads saved presets from `settings.json` and keeps them in one submenu. Manual presets show their percentage, curve presets are marked as curves, and switching to a curve preset runs the first auto-policy tick before starting the background timer; if that first tick fails, the timer is not started.
- Exit: closes the app and removes the tray icon.

## Default Configuration

| Setting | Default | Notes |
| --- | --- | --- |
| Host | `192.0.2.10` | Reserved documentation example iDRAC address; replace it with your BMC/iDRAC address on first use. |
| UserName | `idrac-user` | Documentation example user; replace it with an iDRAC user that has enough IPMI/OEM raw privilege. |
| RememberPassword | `false` | Password saving is off by default; when enabled, the password is protected with current-user Windows DPAPI in `settings.json`. |
| IpmiToolPath | `BundledTools\ipmitool\ipmitool.exe` | Loading and saving settings normalize this to the bundled relative path; Settings displays the resolved absolute path read-only. |
| FanCount | `6` | Common R730xd Fan 1-6 layout. |
| DefaultAllFanPercent | `10` | Local manual baseline used by the built-in Default/Restore Manual preset. Overview/tray "Restore Dell factory fan speed" does not use this value; it restores Dell automatic mode. |
| MinimizeToTrayOnClose | `true` | Closing the window hides it to the tray by default; the tray menu can restore the window or exit the app. |
| EnableIndividualFanTargets | `false` | Individual fan target-selector control is disabled by default; `0x00` is a target selector, not `0%` fan speed. |
| SensorRefreshSeconds | `1` | Default polling tick cadence. Actual SDR response speed depends on iDRAC; the locally observed R730xd/iDRAC 2.82 takes about 11-13 seconds for a full SDR read. If the previous read has not finished, later ticks are skipped and do not start a new `ipmitool` process or RMCP+ session. |
| CommandTimeoutSeconds | `35` | Timeout for one `ipmitool` command. |
| TargetCpuTemperatureCelsius | `68` | Smart auto target temperature. |
| HighCpuTemperatureCelsius | `78` | Smart auto maximum fan percent is used at or above this threshold. |
| EmergencyCpuTemperatureCelsius | `84` | Dell Auto is restored at or above this threshold. |
| AutoMinimumFanPercent | `10` | Smart auto minimum all-fan percentage. |
| AutoMaximumFanPercent | `42` | Smart auto maximum all-fan percentage. |
| Theme | `Default` | Follows the system theme. |
| Language | `zh-CN` | Default UI language is Simplified Chinese. |
| LastRunningPresetId | Empty string | ID of the last manual preset, Dell Auto preset, temperature curve, or power curve that started successfully. On the next launch, after connection/start polling succeeds, the app re-executes that preset. If the preset was deleted or is invalid, restore shows an error. |
| LastSmartAutoPolicyRunning | `false` | Whether the smart temperature policy was the last successfully started running mode. This is used only when `LastRunningPresetId` is empty; after the next successful connection the app runs one smart-auto pass and starts the background timer. |

Fan raw commands have no built-in retry setting. When any `ipmitool` child process returns a non-zero exit code, the app records that single real execution and immediately exposes stdout/stderr to the user.

Runtime logs are not a settings option. They always use:

```text
%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl
```

## Requirements

- Windows 10 2004 / build 19041 or newer.
- .NET 8 Desktop Runtime, or a self-contained published build.
- Reachable Dell PowerEdge R730xd iDRAC/BMC.
- IPMI over LAN enabled in iDRAC.
- iDRAC user with enough privilege to send OEM raw IPMI commands.
- Application output contains `BundledTools/ipmitool/ipmitool.exe` and required DLLs.
- Application output contains `Assets/Charts/dashboard.html` and `Assets/Charts/echarts.min.js`.

## First-Run Workflow

1. Confirm that IPMI over LAN is enabled in iDRAC.
2. Confirm that the Windows machine running this app can reach the iDRAC address.
3. Start the app. On first run, or when no password is saved, Settings opens automatically.
4. Enter the iDRAC address, username, and password.
5. Enable "Remember password with DPAPI" if you want later automatic connection.
6. Save settings. If the password is not empty, the app immediately tests the connection, refreshes sensors, and starts polling.
7. On Overview, verify CPU temperature, fan RPM, power, and state sensors.
8. In the Overview Recent Log area, click "Open logs" and confirm that today's `runtime-YYYYMMDD.jsonl` file exists.
9. Start with Dell Auto or a conservative manual percentage before trying lower fan speeds.

## Build

```powershell
cd C:\DellR730xdFanControlCenter
dotnet restore .\DellR730xdFanControlCenter.csproj
dotnet build .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
```

The project declares `x86`, `x64`, and `ARM64` platforms. Local development and debugging usually use `x64`.

## Run

```powershell
cd C:\DellR730xdFanControlCenter
dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
```

`Properties/launchSettings.json` includes two launch profiles:

- `DellR730xdFanControlCenter (Package)`: MSIX package launch.
- `DellR730xdFanControlCenter (Unpackaged)`: plain project launch.

## Verification

After changes, run at least these commands to confirm the app builds and this repository's model, i18n, layout, tray, chart, and failure-handling static checks pass:

```powershell
cd C:\DellR730xdFanControlCenter
dotnet build .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj
```

`Tests/PresetModelTests/Program.cs` does not only check preset models; it also covers sensor display-name localization, 22-language resource completeness, visible XAML text localization, package manifest localization, log-level styling, polling-skip logging, IPMI command no-retry behavior, auto-policy sampling ownership, Settings command bar, tray menu, chart layout, DPI/text wrapping, and content scroll width. This command does not replace real R730xd/iDRAC hardware validation; fan raw commands, individual fan target IDs, and SDR read duration still need supervised confirmation on the target machine.

## Publish

The project enables MSIX tooling and configures `Microsoft.Windows.SDK.BuildTools.WinApp` to support WinUI `dotnet run` and packaging-related workflows. Published output must include:

- `LICENSE`
- `THIRD_PARTY_NOTICES.md`
- `BundledTools/ipmitool/**`
- `Assets/Charts/**`
- WinUI / Windows App SDK runtime files
- Application icon and manifest assets

Development runs still use `dotnet run`. To create a directly runnable unpackaged exe output directory, use:

```powershell
cd C:\DellR730xdFanControlCenter
.\tools\Publish-UnpackagedExe.ps1
```

The output directory is:

```text
artifacts/exe/win-x64/
```

`DellR730xdFanControlCenter.exe` inside that directory can be launched directly. The publish script verifies that the exe, `LICENSE`, `THIRD_PARTY_NOTICES.md`, `Assets/AppIcon.ico`, dashboard assets, ECharts license/NOTICE files, the bundled `BundledTools/ipmitool/ipmitool.exe`, and `BundledTools/ipmitool/LICENSES/**` are all present, and fails if any required file is missing. This exe output is a self-contained unpackaged directory; it does not rely on MSIX package identity. Distribute the whole directory, not just the single exe file. Do not use `bin\Release\...\publish\DellR730xdFanControlCenter.exe` to verify the unpackaged release; that path can come from an MSIX build intermediate and is not this project's supported directly runnable exe output.

To create the downloadable zip used by GitHub Actions and GitHub Releases, run:

```powershell
cd C:\DellR730xdFanControlCenter
.\tools\Publish-ReleaseZip.ps1
```

The output file is:

```text
artifacts/release/DellR730xdFanControlCenter-win-x64.zip
```

The script first runs `tools\Publish-UnpackagedExe.ps1`, compresses the full unpackaged output directory, then extracts the zip to a temporary verification directory and checks the exe, WinUI/Windows App SDK runtime files, project license, third-party notices, dashboard assets, ECharts license/NOTICE files, bundled `ipmitool.exe`, and `BundledTools/ipmitool/LICENSES/**`. This downloadable zip is explicitly an unsigned unpackaged release: it does not create, upload, or require installing an MSIX. If `.msix`, `.pfx`, `.cer`, `AppxManifest.xml`, or `Package.appxmanifest` appears inside the zip, the script fails so the GitHub Release download cannot become unusable because of a self-signed certificate, certificate trust chain, or package identity problem. On a local machine, add `-VerifyLaunch` to start `DellR730xdFanControlCenter.exe` from the extracted zip and confirm that it creates a titled top-level window without new `.NET Runtime` or `Application Error` startup events. CI does not start the GUI by default; it verifies the downloaded-zip file layout, license/notice files, and that no signed/package-identity files leaked into the zip.

The repository `.github/workflows/release.yml` runs the same zip publishing script on a Windows runner. The workflow publishes only the unsigned unpackaged zip; it does not call `tools\Publish-SignedMsix.ps1`, `Add-AppxPackage`, or `Get-AuthenticodeSignature`. Manual `workflow_dispatch` runs upload `DellR730xdFanControlCenter-win-x64.zip` as a workflow artifact. Pushing a `v*` tag creates or reuses the matching GitHub Release and uploads the zip with `gh release upload --clobber`. Tag-triggered releases do not upload a workflow artifact, so a full Actions artifact quota cannot block GitHub Release asset publication; manual artifact runs still fail explicitly when quota is unavailable. Rerunning the workflow for the same tag can package again and replace the downloadable asset.

To create an installable signed MSIX package, use the repository publish script:

```powershell
cd C:\DellR730xdFanControlCenter
.\tools\Publish-SignedMsix.ps1
```

The script verifies that the `Package.appxmanifest` `Publisher` equals the signing certificate subject. By default it creates or reuses a `CN=mason369` code-signing certificate in the current user's certificate store and exports the public certificate to `artifacts/certificates/mason369-msix-signing.cer`. The default publish path must run from an elevated PowerShell session because a self-signed MSIX must trust the public certificate in `CurrentUser\TrustedPeople`, `CurrentUser\Root`, `LocalMachine\TrustedPeople`, and `LocalMachine\Root`; otherwise `Add-AppxPackage` can reject the package with `0x800B0109`. The private key remains in the current user's certificate store; the script does not write a `.pfx` file into the repository. Use `-SkipTrustImport` only when the target machine already trusts the signer through enterprise certificate deployment or a documented manual step; after skipping the import, still install the package on the target machine to verify it.

The signed package is written to:

```text
artifacts/msix/DellR730xdFanControlCenter_1.0.0.0_x64_Test/DellR730xdFanControlCenter_1.0.0.0_x64.msix
```

The script uses `WindowsAppSDKSelfContained=true` to create a self-contained MSIX. After signing, it runs `Get-AuthenticodeSignature` and fails if the signature status is not `Valid`; then it unpacks the MSIX, verifies that the generated `AppxManifest.xml` no longer declares external `PackageDependency` entries, and confirms that `Microsoft.WindowsAppRuntime.dll`, `Microsoft.ui.xaml.dll`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, the bundled `ipmitool.exe`, third-party license files, the dashboard page, and the app icon are inside the package. The script places the temporary publish directory needed for MSIX packaging under `obj\signed-msix\publish`, removes it after inspection, and also removes stale `bin\Release\...\publish` intermediates so a non-release exe is not mistaken for the unpackaged build. A valid signature only proves that the package has not been tampered with and that Authenticode can validate the signer; it does not prove that Windows deployment will accept the MSIX. Missing deployment trust, runtime dependencies, a bad entry point, or missing packaged files can still make installation or launch fail. Reinstalling changed MSIX content with the same `Identity` and the same `Version` is rejected by Windows with `0x80073CFB`; real releases should increase the `Package.appxmanifest` `Identity Version`, while local same-version verification requires removing the installed package with `Remove-AppxPackage` before installing again. The self-signed certificate is appropriate for local testing or controlled internal distribution. Public releases should use a trusted code-signing certificate whose subject exactly matches the manifest publisher. After publishing, run `Add-AppxPackage -Path artifacts\msix\DellR730xdFanControlCenter_1.0.0.0_x64_Test\DellR730xdFanControlCenter_1.0.0.0_x64.msix` on the target machine and start the app once to confirm that the main window, bundled `ipmitool.exe`, dashboard page, license/notice files, and tray icon resolve from the installed package.

## IPMI Command Behavior

The command runner uses:

```text
ipmitool -I lanplus -H <host> -U <user> -E <ipmi-arguments>
```

The password is passed through the `IPMI_PASSWORD` environment variable and consumed by `ipmitool -E`, so it is not placed in command-line arguments. UI logs show command text, exit code, and elapsed time, but not the password.

See [IPMI Command Reference](docs/COMMANDS.en-US.md) for raw commands and byte details.

## Runtime Logging System

The app has two logging entry points:

- Overview Recent Log: keeps the latest 80 in-memory entries for immediate confirmation in the UI.
- Local JSONL runtime log: writes `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`, reachable from Overview with "Open logs".

Every JSONL line is one complete JSON object with `eventId`, `timestamp`, `level`, `category`, `eventName`, and `message`. Long-running operations also include `operationId`, `operationName`, `phase`, `startedAt`, `finishedAt`, `durationMilliseconds`, and `succeeded`. Current major categories are:

- `Application/UiLog`: settings saves, preset changes, polling warnings, log-file path notices, and other UI events.
- `Operation/UiCommand`: button-triggered user commands with `Started` plus either `Succeeded` or `Failed` terminal records.
- `Operation/SensorRefresh`: each SDR sensor refresh, including host, polling seconds, sensor count, and duration.
- `Operation/SmartAutoPolicyTick`: each smart auto tick, including temperature thresholds, CPU temperature, computed fan percent, or emergency Dell Auto action. If a target percentage has been computed but the later raw fan command fails, the failed record still keeps `cpuTemperatureCelsius`, `fanPercent`, `action = SetAllFansManualSpeed`, and `powerWatts` for power curves.
- `IpmiCommand/CommandCompleted`: each completed `ipmitool` child command with command line, exit code, success state, and duration.

Log-write failure is not treated as success. If the runtime log cannot be written during a user command, the current operation stops and the status bar plus Recent Log show "Runtime log write failed". Startup-stage unhandled exceptions still write `startup-error.log`. Runtime logs do not include the iDRAC password, but they can include iDRAC host, username in command text, tool paths, preset names, and local paths; review logs with [Security](SECURITY.en-US.md) before sharing.

Current limitation: runtime logs rotate by day, but there is no automatic retention period or cleanup policy. Long-running polling continues growing the file, so users must archive or delete old logs manually.

## Smart Auto Policy

The smart auto policy performs one `sdr elist` read per tick, parses CPU temperature, and computes the all-fan percentage as follows:

- CPU temperature less than or equal to target temperature: use the smart auto minimum fan percentage.
- CPU temperature greater than or equal to high temperature: use the smart auto maximum fan percentage.
- CPU temperature within the target-to-high policy curve: evaluate the fan percent at the current temperature's position on that linear policy curve.
- CPU temperature at or above the emergency auto threshold: send the Dell automatic mode command; the software auto timer is not stopped by this command.

CPU temperature detection prefers temperature rows whose key contains `CPU`. If no CPU-named temperature rows exist, it uses the highest value among all temperature sensors. If no temperature sensor is found, the app reports an error.

Smart auto ticks share the same IPMI lock as sensor polling, manual fan commands, and Dell automatic restore. When the user starts smart auto or switches to a curve preset, the app waits for the current IPMI command to finish, then runs the first tick before starting the background timer; if that first tick fails, the timer is not started and the UI/logs show the root cause. While an auto policy is running, regular sensor polling does not start its own `sdr elist`, which prevents two background timers from creating duplicate RMCP+ sessions in the same period; the auto-policy tick's SDR result is the sensor and chart update source during that time. If a later background auto tick fires while another IPMI command is running, that auto policy cycle is skipped, and the latest request status plus logs state that no new `ipmitool` process or RMCP+ session was started. If a background auto tick completes SDR reading but computes the same target percentage as the last successful send for the same automatic mode, it skips the fan raw command and logs "No fan command sent"; sensors, boards, charts, and history points have still been refreshed from that SDR read. Manual fan commands, Dell Auto commands, and Overview/tray restore only clear the current curve state; they do not stop an already running software auto timer. To keep Dell firmware in control, click "Stop auto" before or after restoring Dell automatic mode.

Curve presets use the same tick and emergency protection, but they compute the fan percentage from user-defined points:

- The user adds or edits curve points on the Fan Control page with the curve chart and point controls. A chart click creates a temperature/fan-percent point from that position, and the right-side list can fine-tune it.
- Save or switch validates point count, temperature range, percent range, and duplicate temperatures. The point list and `SmoothCurve` setting are stored in local settings.
- If CPU temperature is below the first point, the first point percent is used. If it is above the last point, the last point percent is used.
- Each tick evaluates the current CPU temperature or current SDR power reading against the saved curve. With Smooth curve off, the app evaluates the polyline formed by the configured points and rounds the resulting fan percent. With Smooth curve on, the same points are evaluated with a smoothed curve position before rounding.
- At or above the emergency auto threshold, Dell Auto is restored first whether the app is using the global linear policy or a curve preset; this action does not stop the software auto timer.
- Curve presets still depend on `sdr elist`, CPU temperature detection, IPMI over LAN, and Dell OEM raw commands. Any failure is shown and logged; the app does not pretend that the curve was applied.

## Polling And Concurrency

- Clicking "Start polling" or successfully saving settings tests the connection, reads SDR once, and starts sensor polling; while polling is active, the same button reads "Cancel polling" and stops future polling ticks when clicked.
- Each successful poll reads `sdr elist`, refreshes the table, dashboard cards, and chart data, and writes one JSONL chart history point; failed or skipped polls do not write synthetic history points. History points are retained for 7 days by default and are reloaded from `%LocalAppData%\DellR730xdFanControlCenter\chart-history` at startup while still inside the retention window.
- If the previous SDR read is still running, the next tick is skipped; skipped tick records are written to the in-page log and runtime JSONL log, but they do not open or overwrite the top InfoBar.
- If another IPMI command is running, the polling tick is also skipped to avoid overlapping commands against the BMC; only the first skipped tick in the same busy period is logged.
- Smart auto and curve auto ticks also never run concurrently with another IPMI command; when a background tick finds IPMI busy, it is skipped and logs "No new ipmitool process" instead of queuing and increasing handling time.
- A skipped tick is a scheduling fact, not a successful IPMI request; the log explicitly states that no new `ipmitool` process or RMCP+ session was started.
- If one SDR read takes longer than the configured polling interval, the app shows a top warning with a recommended interval because a real command exceeded the configured cadence.
- A polling command failure stops the current poll, updates connection state, and shows the failure reason; the app then releases the IPMI lock and runs one real reconnect flow (`mc info` + `sdr elist`). Persistent polling resumes only if reconnect succeeds; reconnect failure remains visible and disconnected without silent degradation or pretending success.
- Dell fan-control raw commands and individual sensor reads do not retry the same failed command: non-zero exits from `raw 0x30 0x30 ...` fail immediately and show stdout/stderr, while failed `sdr elist` reads do not create synthetic history points. Only background polling failures append one visible reconnect attempt.

## Individual Fan Risk

Individual fan mode uses these target selectors. Note that `0x00-0x05` select which fan the raw command targets; they are not fan speeds. The actual fan speed percentage is the final command argument.

| Fan | Target selector |
| --- | --- |
| All fans | `0xff` |
| Fan 1 | `0x00` |
| Fan 2 | `0x01` |
| Fan 3 | `0x02` |
| Fan 4 | `0x03` |
| Fan 5 | `0x04` |
| Fan 6 | `0x05` |

On the locally tested R730xd/iDRAC 2.82, target selector `0x00` was not `0%` fan speed and did not isolate Fan 1; it ramped all fans high. For that reason, individual fan control is disabled by default. Before enabling it, verify your firmware behavior. After enabling it, watch RPM and temperatures after every action. If behavior is unexpected, restore Dell automatic mode immediately.

## Safety

Fan control directly affects server thermal margin. Low fan speeds can raise CPU, drive, PCIe card, power supply, or chassis temperatures. After changing fan speed, monitor:

- CPU temperature and CPU usage.
- Inlet and exhaust temperatures.
- Drive, backplane, cable presence, and redundancy states.
- Fan 1-6 RPM.
- Power, voltage, and current.
- iDRAC alerts.

If workload is unknown, the chassis is drive-dense, ambient temperature is high, or any sensor state looks abnormal, prefer Dell automatic mode.

See [Security](SECURITY.en-US.md) for credential handling, logs, command visibility, and supply-chain notes.

## Troubleshooting

### Bundled ipmitool is missing

The error usually says "Bundled ipmitool.exe is missing from the application output". Confirm that the build output contains:

```text
BundledTools\ipmitool\ipmitool.exe
```

The project file is configured to copy `BundledTools\ipmitool\**\*` to output. If a published package is missing it, check whether the publish process excluded content files.

### Authentication fails or privileges are insufficient

Check iDRAC address, username, password, and user privileges. The app requires an account that can send Dell OEM raw IPMI commands. A read-only or restricted account may be able to read SDR but fail to control fans.

### Sensors are empty

If `ipmitool` exits successfully but no SDR rows are returned, the app reports an error. Validate manually:

```powershell
$env:IPMI_PASSWORD = "<your-password>"
.\BundledTools\ipmitool\ipmitool.exe -I lanplus -H <host> -U <user> -E sdr elist
```

### Polling takes too long or RMCP+ sessions fail

A full `sdr elist` read can take several to more than ten seconds; the locally observed R730xd/iDRAC 2.82 takes about 11-13 seconds. `SensorRefreshSeconds = 1` only means the app triggers a polling tick every second; it does not mean iDRAC can return a complete SDR read every second. The app serializes IPMI operations: when the previous poll is still running or another IPMI command holds the lock, the tick is skipped and no new `ipmitool` process or RMCP+ session is started. While smart auto or curve auto is running, the auto-policy tick already reads SDR and updates the UI, so regular sensor polling does not independently read SDR again. If a background sensor polling `ipmitool` command returns `Unable to establish IPMI v2 / RMCP+ session`, times out, or exits non-zero, the app stops that polling run, shows the failure reason, and then performs one real reconnect. If reconnect `mc info` or `sdr elist` still fails, the app stays disconnected and continues to show the root cause. If the top warning says one read exceeded the configured interval, adjust polling seconds manually to the UI recommendation; the app does not force-rewrite your setting.

### Charts fail to load

Confirm that output contains:

```text
Assets\Charts\dashboard.html
Assets\Charts\echarts.min.js
```

Charts use local WebView2 resources. If the WebView2 runtime is unavailable, install or repair Microsoft Edge WebView2 Runtime.

### UI is crowded under high DPI, scaling, or narrow windows

The app declares `PerMonitorV2` DPI awareness and switches layout in `MainPage.xaml.cs` at `<641`, `641-1007`, and `>=1008` effective-pixel widths. If text is still crowded, charts are covered, performance/electrical labels are abbreviated, the page overflows horizontally, or a bottom divider crosses content, first confirm that you are running the latest build output and run:

```powershell
dotnet build .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj
```

If the checks pass but the UI is still abnormal, record the Windows display scaling percentage, window size, monitor resolution, whether the app is running through Remote Desktop, a screenshot, and the matching log entries under `%LocalAppData%\DellR730xdFanControlCenter\logs`. Chart resource load failures show real errors as described in "Charts fail to load"; the app should not hide labels, abbreviate text, or write fake history points to mask the problem.

### Runtime log write fails

If the status bar shows "Runtime log write failed", check whether the current Windows user can create and append files under:

```text
%LocalAppData%\DellR730xdFanControlCenter\logs
```

This failure is not silently ignored. Button-triggered user commands stop and show the root cause; fix directory permissions, disk space, or security-software blocking before retrying.

### App is still running after closing the window

The default close behavior minimizes to tray. Right-click the tray icon to restore or exit, or disable "Minimize to tray on close" in Settings.

## License

This project's own source code is licensed under the [MIT License](LICENSE). Bundled `ipmitool.exe`, Cygwin/GCC/OpenSSL/zlib runtime DLLs, and ECharts frontend assets keep their upstream licenses and are not relicensed by this project's MIT license. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the full third-party notice, and [BundledTools/ipmitool/README.md](BundledTools/ipmitool/README.md) for bundled command-tool versions, SHA-256 hashes, and license files.

## Repository Structure

```text
Assets/                  Icons, logo, chart HTML, and ECharts assets
BundledTools/ipmitool/   Bundled ipmitool.exe and required runtime DLLs
Models/                  Settings, presets, sensors, dashboard, and log models
Services/                IPMI commands, runtime logging, settings storage, localization, and tray service
docs/                    Command reference and project metadata
MainPage.xaml            Main UI layout
MainPage.xaml.cs         Main page interactions, polling, smart auto policy, and chart data
MainWindow.xaml.cs       Window, tray, and close behavior
```

## Repository Topics

`dell`, `poweredge`, `r730xd`, `idrac`, `ipmi`, `ipmitool`, `fan-control`, `server-management`, `homelab`, `winui`, `windows`, `dotnet`
