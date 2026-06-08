<p align="center">
  <img src="Assets/Logo.svg" width="128" alt="R730XD Smart Fan Center logo" />
</p>

# Dell R730xd iDRAC Fan Control Center

Language: [中文](README.md) | English

Dell R730xd iDRAC Fan Control Center is a Windows WinUI 3 desktop app for controlling Dell PowerEdge R730xd fans through iDRAC/IPMI, reading BMC sensors, visualizing live hardware state, and handing control back to Dell firmware automatic mode when needed.

This project is not a generic server management suite. It is a local control center for the R730xd, iDRAC/BMC, `ipmitool`, and Dell OEM raw fan commands. The design priorities are command transparency, explicit failure reporting, and recoverable operation. The app does not silently switch backends or pretend a command succeeded. Missing tools, authentication errors, insufficient privileges, unsupported firmware commands, and empty SDR reads are shown directly in the UI and logs.

## Scope

- Target server: Dell PowerEdge R730xd.
- Target control plane: iDRAC/BMC IPMI over LAN.
- Target OS: Windows 10 2004 / build 19041 or newer.
- Target users: homelab operators, server caretakers, dense-drive R730xd owners, and noise-constrained environments that still need visible thermal control.
- Locally observed environment: R730xd / iDRAC firmware 2.82, example host `192.168.1.73`, example user `root`.

Different iDRAC firmware versions, backplanes, fan layouts, and sensor layouts can change individual fan target-selector behavior. Fan 1-6 target selectors are implemented in code but disabled by default; `0x00` is a target selector in the firmware raw command, not `0%` fan speed, so verify your firmware mapping before enabling them.

## Feature Overview

- Modern WinUI 3 interface with light, dark, and system theme support.
- All-fan percentage control from 0-100%; the app enters manual fan mode before setting a percentage.
- The built-in Default/Restore Manual preset keeps manual mode plus all fans at 10% for users who explicitly choose that preset as a local quiet baseline.
- Starter presets: Default 10%, Balanced 20%, Cooling 35%, Performance 50%, and Dell Auto.
- Edit preset names, descriptions, and available percentages; add, save, and delete custom manual percentage presets. Starter presets cannot be deleted.
- Add and edit temperature-fan curve presets with a chart-based point editor. Click the curve chart to add temperature/fan-percent points, fine-tune them with point controls, optionally enable smooth transitions, and switch to the preset to keep applying the curve on each polling tick.
- Dell automatic fan mode remains available as both a separate action and a preset entry.
- Individual target-byte control for fans 1-6 is implemented but disabled by default.
- Smart auto policy reads BMC CPU temperature and adjusts all fans from either the global target/high-temperature thresholds or the active curve preset.
- At the emergency temperature threshold, the smart auto policy restores Dell automatic mode.
- Persistent SDR polling starts after a successful connection or settings save, with a default and minimum saved interval of 15 seconds; older settings files with lower polling values pause automatic connection until the user explicitly changes the setting.
- Bundled `ipmitool.exe` and required Cygwin DLLs under `BundledTools/ipmitool`.
- Bundled local ECharts dashboard assets under `Assets/Charts/dashboard.html` and `Assets/Charts/echarts.min.js`; runtime does not depend on an online CDN.
- Tray icon supports background operation, window restore, quick presets, curve preset labels, all-fan 20/35/50%, Restore Dell factory fan speed, settings, and exit. The tray restore action sends Dell automatic mode and does not switch to manual 10%.
- iDRAC web console shortcut opens `https://<host>/`.
- All visible UI strings are wired to localization resources; Simplified Chinese and English are included. The repository defaults to the Chinese README, while English content lives in separate files.
- Charts, dashboard cards, and status messages use localized display names; the Sensors table keeps the raw BMC `Key` for command-output troubleshooting.
- The Overview interactive chart runs in WebView2, but mouse-wheel events are forwarded to the outer WinUI `ScrollViewer` so scrolling over the chart uses native smooth page scrolling like the non-Web boards.
- Passwords can be stored with Windows DPAPI under the current Windows user context.
- Runtime logs are written as JSON Lines to `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`; each line is a complete atomic event, including user commands, sensor refreshes, smart auto ticks, and IPMI command timing.
- Startup exceptions are written to `%LocalAppData%\DellR730xdFanControlCenter\startup-error.log`.

## Screens

### Overview

The Overview page is for observing hardware state and running common actions:

- The hero live summary shows current temperature, average fan RPM, live power, average voltage, and total current from the latest successful SDR sensor refresh. Under each large value, the app shows up to 3 concrete sensor details on separate lines, such as inlet/exhaust temperature, Fan 1-6 RPM, power consumption, voltage rails, and current rails; sensors beyond those 3 are not shown in the hero, so use the matching board for the complete list. The live summary cards use a fixed compact height and are not stretched by long request-status text on the right. Before the first refresh, or when one sensor category is missing, that value shows "Waiting". The temperature summary uses the average of the latest temperature sensor readings, not a historical maximum; the overview card and emergency automatic protection keep their separate max/CPU temperature semantics. Large values and details are color-coded by recommended live ranges: normal green, near-risk yellow, clear deviation orange, and danger red. Current thresholds are temperature `<60/60-69/70-79/>=80 C`; average RPM `2500-6000` green, `1500-2499 or 6001-9000` yellow, `500-1499 or >9000` orange, `<500` red; power `<500/500-699/700-899/>=900 W`; voltage `210-240` green, `200-209 or 241-250` yellow, `190-199 or 251-260` orange, otherwise red; and total current `<4/4-5.9/6-7.9/>=8 A`. These colors are UI hints only and do not replace the full sensor table, iDRAC alerts, or emergency automatic protection.
- The right-side hero status card shows the current iDRAC target, connection state, current control mode, latest request status, and last update time.
- Latest request status updates in real time for Switch actions, connect/refresh sensors, save settings, start/stop smart auto, curve auto ticks, and polling success/skip/failure. The hero card shows compact states such as "Requesting", "OK", "Skipped", "Failed", and "Applied"; the complete reason remains visible in the top InfoBar and runtime log.
- Metric cards show max CPU temperature, fan state, live power, average voltage, total current, and current control mode. Power, voltage, and current also come from the latest successful SDR refresh and show waiting/no-reading text when absent.
- Interactive visualization shows overall trends, hardware profile, individual temperature ranking, individual fan RPM, performance/electrical data, and a treemap grouped by hardware type and state.
- Temperature Board shows every temperature sensor reported by BMC SDR, with temperature icons and live recommended color states.
- Fan RPM Board shows current RPM readings for fan sensors, with a fan icon that rotates continuously. The animation period is derived from RPM: higher RPM rotates faster, while zero or missing readings are not shown as normal high-speed motion.
- Power & Health shows power, voltage, current, redundancy, battery, intrusion, Power Optimized, and related state sensors with matching electrical/state icons and the same recommended color states.
- Quick Actions include refresh sensors, Restore Dell factory fan speed, open iDRAC, and all-fan percentage control. Start/stop controls for the smart auto policy now live on the Fan Control page.
- Recent Log shows command results, success/failure state, and polling warnings, and includes an "Open logs" entry point. UI status badges are color-coded by level: info is blue, warning is amber, success is green, and only error/failure uses red; the local JSONL runtime log still writes plain structured fields and does not store color values.

### Fan Control

The Fan Control page manages presets and advanced control:

- The preset area shows the current mode, starter presets, custom presets, and editable descriptions.
- Manual presets send Dell OEM raw commands for all-fan percentage control.
- Default/restore and manual presets can edit their percentage. The Overview quick restore and tray restore actions now restore Dell factory fan speed by sending Dell automatic mode instead of manual 10%.
- The Dell Auto preset restores the BMC firmware fan policy and does not expose a percentage field.
- The Smart Auto Policy start/stop card lives on the Fan Control page. Temperature thresholds and polling interval remain editable lower on the same page, and each tick writes runtime log records and updates the hero request status.
- Adding a manual preset requires a name and validates the percentage from 0-100.
- Curve presets are maintained with a graphical editor instead of a multiline text box. After entering a curve name, click the chart to add a point, or fine-tune temperature and fan percentage in the point list on the right. Clicking "Edit points" on an existing curve preset loads that preset into the same editor.
- Saving a curve requires at least 2 points, temperatures from `-40` to `125` C, fan percentages from `0` to `100`, and no duplicate temperatures. Invalid points show the validation reason in the preview area, and Add/Save still run the same strict validation instead of silently replacing the input with a default curve.
- The Smooth curve switch is saved with the preset. When it is off, the app linearly interpolates between adjacent points. When it is on, the same points are used with a smooth transition between points, reducing abrupt percentage jumps as temperature crosses a point. Temperatures below the first point and above the last point are still clamped to the endpoint percentages.
- Switching to a curve preset starts the software auto polling loop. Each tick reads SDR, parses CPU temperature, computes the percentage from the curve points and smooth setting, and sends the resulting all-fan percentage command.
- Manual all-fan control, individual fan control, the built-in Restore Manual preset, Dell Auto, Overview/tray Restore Dell factory fan speed, or Stop Auto clears the current curve state so a curve tick does not keep overriding the user's manual command.
- Saving a preset writes it to local settings, and the tray menu reads those saved presets. Curve presets are labeled as curves in the tray menu. If the currently running curve is edited and saved, the next software auto tick uses the updated points and `SmoothCurve` setting.
- Individual fan controls are disabled by default and must be enabled and saved in Settings.
- Smart auto parameters include target temperature, high temperature, emergency auto threshold, polling seconds, minimum fan percentage, and maximum fan percentage.

### Sensors

The Sensors page shows the parsed `ipmitool sdr elist` table:

- `Key`: raw sensor name returned by the BMC, such as `Fan1 RPM`, `Inlet Temp`, or `CPU Usage`.
- `ID`: sensor ID from SDR output.
- `Entity`: entity information from SDR output.
- `Value`: numeric value or status from the raw reading text. Common IPMI enum values are localized, including `No Reading`, `State Deasserted`, `Fully Redundant`, and `OEM Specific`.
- `Unit`: unit such as `degrees C`, `RPM`, `Watts`, `Volts`, `Amps`, or `percent`. The UI localizes them as `°C`, RPM, W, V, A, and `%`.
- `Status`: BMC state such as `ok`, `ns`, `na`, or an attention state. Known short codes are localized; unknown values stay raw for troubleshooting.

If `ipmitool` exits successfully but returns no SDR rows, the app raises an error instead of inventing placeholder sensor data.

### Settings

The Settings page controls connection, persistence, and runtime behavior:

- iDRAC/BMC IP address or host name.
- iDRAC username.
- iDRAC password.
- Whether to save the password with DPAPI.
- Read-only bundled `ipmitool.exe` path.
- Whether the close button minimizes to tray.
- Whether individual fan target-selector control is enabled.
- Fan count, default 6.
- Command timeout seconds, default 35, minimum enforced by code is 5.
- SDR polling seconds, default 15. Saving a value below 15 fails with a visible reason instead of silently changing it or continuing to connect.
- Smart auto minimum and maximum fan percentages.
- UI theme and language.

Settings are stored at:

```text
%LocalAppData%\DellR730xdFanControlCenter\settings.json
```

## Default Configuration

| Setting | Default | Notes |
| --- | --- | --- |
| Host | `192.168.1.73` | Example iDRAC address; replace it on first use. |
| UserName | `root` | Example user. |
| FanCount | `6` | Common R730xd Fan 1-6 layout. |
| DefaultAllFanPercent | `10` | Local manual baseline used by the built-in Default/Restore Manual preset. Overview/tray "Restore Dell factory fan speed" does not use this value; it restores Dell automatic mode. |
| EnableIndividualFanTargets | `false` | Individual fan target-selector control is disabled by default; `0x00` is a target selector, not `0%` fan speed. |
| SensorRefreshSeconds | `15` | Minimum saved polling interval. Actual SDR response speed depends on iDRAC; the locally observed R730xd/iDRAC 2.82 takes about 11-13 seconds for a full SDR read. |
| CommandTimeoutSeconds | `35` | Timeout for one `ipmitool` command. |
| TargetCpuTemperatureCelsius | `68` | Smart auto target temperature. |
| HighCpuTemperatureCelsius | `78` | Smart auto maximum fan percent is used at or above this threshold. |
| EmergencyCpuTemperatureCelsius | `84` | Dell Auto is restored at or above this threshold. |
| AutoMinimumFanPercent | `10` | Smart auto minimum all-fan percentage. |
| AutoMaximumFanPercent | `42` | Smart auto maximum all-fan percentage. |
| Theme | `Default` | Follows the system theme. |
| Language | `zh-CN` | Default UI language is Simplified Chinese. |

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

## Publish

The project enables MSIX tooling and configures `Microsoft.Windows.SDK.BuildTools.WinApp` to support WinUI `dotnet run` and packaging-related workflows. Published output must include:

- `BundledTools/ipmitool/**`
- `Assets/Charts/**`
- WinUI / Windows App SDK runtime files
- Application icon and manifest assets

Example publish command:

```powershell
cd C:\DellR730xdFanControlCenter
dotnet publish .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

After publishing, start the app on the target machine and confirm that the bundled `ipmitool.exe`, dashboard page, and tray icon resolve from the output directory.

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
- `Operation/SmartAutoPolicyTick`: each smart auto tick, including temperature thresholds, CPU temperature, computed fan percent, or emergency Dell Auto action.
- `IpmiCommand/CommandCompleted`: each completed `ipmitool` child command with command line, exit code, success state, and duration.

Log-write failure is not treated as success. If the runtime log cannot be written during a user command, the current operation stops and the status bar plus Recent Log show "Runtime log write failed". Startup-stage unhandled exceptions still write `startup-error.log`. Runtime logs do not include the iDRAC password, but they can include iDRAC host, username in command text, tool paths, preset names, and local paths; review logs with [Security](SECURITY.en-US.md) before sharing.

Current limitation: runtime logs rotate by day, but there is no automatic retention period or cleanup policy. Long-running polling continues growing the file, so users must archive or delete old logs manually.

## Smart Auto Policy

The smart auto policy performs one `sdr elist` read per tick, parses CPU temperature, and computes the all-fan percentage as follows:

- CPU temperature less than or equal to target temperature: use the smart auto minimum fan percentage.
- CPU temperature greater than or equal to high temperature: use the smart auto maximum fan percentage.
- CPU temperature between target and high thresholds: linearly interpolate.
- CPU temperature at or above the emergency auto threshold: restore Dell automatic mode and let iDRAC/BMC take over.

CPU temperature detection prefers temperature rows whose key contains `CPU`. If no CPU-named temperature rows exist, it uses the highest value among all temperature sensors. If no temperature sensor is found, the app reports an error.

Curve presets use the same tick and emergency protection, but they compute the fan percentage from user-defined points:

- The user adds or edits curve points on the Fan Control page with the curve chart and point controls. A chart click creates a temperature/fan-percent point from that position, and the right-side list can fine-tune it.
- Save or switch validates point count, temperature range, percent range, and duplicate temperatures. The point list and `SmoothCurve` setting are stored in local settings.
- If CPU temperature is below the first point, the first point percent is used. If it is above the last point, the last point percent is used.
- If CPU temperature falls between two points, the app linearly interpolates and rounds to an integer fan percentage by default. If the preset has Smooth curve enabled, the app uses the same points with a smooth transition before rounding.
- At or above the emergency auto threshold, Dell Auto is restored first whether the app is using the global linear policy or a curve preset.
- Curve presets still depend on `sdr elist`, CPU temperature detection, IPMI over LAN, and Dell OEM raw commands. Any failure is shown and logged; the app does not pretend that the curve was applied.

## Polling And Concurrency

- Sensor polling starts after a successful connection.
- Each poll reads `sdr elist` and refreshes the table, dashboard cards, and chart data.
- If the previous SDR read is still running, the next tick is skipped with a visible warning.
- If another IPMI command is running, the polling tick is also skipped to avoid overlapping commands against the BMC.
- If one SDR read takes longer than the configured polling interval, the app shows a recommended interval.
- A polling failure stops polling, updates connection state, and shows the failure reason.
- If an older version saved `SensorRefreshSeconds = 1`, the new version opens Settings and asks for 15 seconds or higher instead of auto-connecting. Saving below 15 seconds fails. This prevents continuous IPMI v2/RMCP+ session creation against iDRAC; the app does not retry automatically or pretend the failed poll succeeded.

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

A full `sdr elist` read can take several to more than ten seconds; the locally observed R730xd/iDRAC 2.82 takes about 11-13 seconds. Too-low polling can keep opening IPMI v2/RMCP+ sessions and eventually produce `Unable to establish IPMI v2 / RMCP+ session`. The default and minimum saved polling interval is now 15 seconds; if the UI still reports reads longer than the current interval, raise polling seconds to the recommended value.

### Charts fail to load

Confirm that output contains:

```text
Assets\Charts\dashboard.html
Assets\Charts\echarts.min.js
```

Charts use local WebView2 resources. If the WebView2 runtime is unavailable, install or repair Microsoft Edge WebView2 Runtime.

### Runtime log write fails

If the status bar shows "Runtime log write failed", check whether the current Windows user can create and append files under:

```text
%LocalAppData%\DellR730xdFanControlCenter\logs
```

This failure is not silently ignored. Button-triggered user commands stop and show the root cause; fix directory permissions, disk space, or security-software blocking before retrying.

### App is still running after closing the window

The default close behavior minimizes to tray. Right-click the tray icon to restore or exit, or disable "Minimize to tray on close" in Settings.

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

## Documentation Standard

README and `docs/` files in this repository should stay complete, specific, and actionable. When adding features, settings, commands, risks, error handling, or publishing behavior, update both Chinese and English documentation with trigger conditions, user-visible behavior, default configuration, known limitations, and verification steps. Do not add shallow one-line feature notes when the actual command, workflow, or risk boundary matters.

## Repository Topics

`dell`, `poweredge`, `r730xd`, `idrac`, `ipmi`, `ipmitool`, `fan-control`, `server-management`, `homelab`, `winui`, `windows`, `dotnet`
