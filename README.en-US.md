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

Different iDRAC firmware versions, backplanes, fan layouts, and sensor layouts can change raw target behavior. Individual fan target bytes are implemented in code but disabled by default; verify your firmware mapping before enabling them.

## Feature Overview

- Modern WinUI 3 interface with light, dark, and system theme support.
- All-fan percentage control from 0-100%; the app enters manual fan mode before setting a percentage.
- Local default restore action: manual mode plus all fans at 10%.
- Built-in presets: Default 10%, Balanced 20%, Cooling 35%, Performance 50%, and Dell Auto.
- Add, save, and delete custom manual percentage presets; built-in presets cannot be deleted.
- Dell automatic fan mode remains available as both a separate action and a preset entry.
- Individual target-byte control for fans 1-6 is implemented but disabled by default.
- Smart auto policy reads BMC CPU temperature and linearly adjusts all fans.
- At the emergency temperature threshold, the smart auto policy restores Dell automatic mode.
- Persistent SDR polling starts after a successful connection or settings save, with a minimum interval of 1 second.
- Bundled `ipmitool.exe` and required Cygwin DLLs under `BundledTools/ipmitool`.
- Bundled local ECharts dashboard assets under `Assets/Charts/dashboard.html` and `Assets/Charts/echarts.min.js`; runtime does not depend on an online CDN.
- Tray icon supports background operation, window restore, quick presets, all-fan 20/35/50%, restore manual 10%, settings, and exit.
- iDRAC web console shortcut opens `https://<host>/`.
- All visible UI strings are wired to localization resources; Simplified Chinese and English are included. The repository defaults to the Chinese README, while English content lives in separate files.
- Charts, dashboard cards, and status messages use localized display names; the Sensors table keeps the raw BMC `Key` for command-output troubleshooting.
- Passwords can be stored with Windows DPAPI under the current Windows user context.
- Startup exceptions are written to `%LocalAppData%\DellR730xdFanControlCenter\startup-error.log`.

## Screens

### Overview

The Overview page is for observing hardware state and running common actions:

- The header shows the current iDRAC target, connection state, and polling state.
- Metric cards show max CPU temperature, fan state, and current control mode.
- Interactive visualization shows overall trends, hardware profile, individual temperature ranking, individual fan RPM, performance/electrical data, and a treemap grouped by hardware type and state.
- Temperature Board shows every temperature sensor reported by BMC SDR.
- Fan RPM Board shows current RPM readings for fan sensors.
- Power & Health shows power, voltage, current, redundancy, battery, intrusion, Power Optimized, and related state sensors.
- Quick Actions include refresh sensors, restore 10%, open iDRAC, all-fan percentage control, and start/stop smart auto policy.
- Recent Log shows command results, success/failure state, and polling warnings.

### Fan Control

The Fan Control page manages presets and advanced control:

- The preset area shows the current mode, built-in presets, custom presets, and detailed descriptions.
- Manual presets send Dell OEM raw commands for all-fan percentage control.
- The Dell Auto preset restores the BMC firmware fan policy.
- Adding a manual preset requires a name and validates the percentage from 0-100.
- Saving a preset writes it to local settings, and the tray menu reads those saved presets.
- Individual fan controls are disabled by default and must be enabled and saved in Settings.
- Smart auto parameters include target temperature, high temperature, emergency auto threshold, polling seconds, minimum fan percentage, and maximum fan percentage.

### Sensors

The Sensors page shows the parsed `ipmitool sdr elist` table:

- `Key`: raw sensor name returned by the BMC, such as `Fan1 RPM`, `Inlet Temp`, or `CPU Usage`.
- `ID`: sensor ID from SDR output.
- `Entity`: entity information from SDR output.
- `Value`: numeric value or status from the raw reading text.
- `Unit`: unit such as `degrees C`, `RPM`, `Watts`, `Volts`, `Amps`, or `percent`.
- `Status`: BMC state such as `ok`, `ns`, `na`, or an attention state.

If `ipmitool` exits successfully but returns no SDR rows, the app raises an error instead of inventing placeholder sensor data.

### Settings

The Settings page controls connection, persistence, and runtime behavior:

- iDRAC/BMC IP address or host name.
- iDRAC username.
- iDRAC password.
- Whether to save the password with DPAPI.
- Read-only bundled `ipmitool.exe` path.
- Whether the close button minimizes to tray.
- Whether individual fan raw targets are enabled.
- Fan count, default 6.
- Command timeout seconds, default 35, minimum enforced by code is 5.
- SDR polling seconds, default 1, normalized to at least 1 on save.
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
| DefaultAllFanPercent | `10` | Local default restore speed. |
| EnableIndividualFanTargets | `false` | Individual raw targets are disabled by default. |
| SensorRefreshSeconds | `1` | Minimum polling interval. Actual SDR response speed depends on iDRAC. |
| CommandTimeoutSeconds | `35` | Timeout for one `ipmitool` command. |
| TargetCpuTemperatureCelsius | `68` | Smart auto target temperature. |
| HighCpuTemperatureCelsius | `78` | Smart auto maximum fan percent is used at or above this threshold. |
| EmergencyCpuTemperatureCelsius | `84` | Dell Auto is restored at or above this threshold. |
| AutoMinimumFanPercent | `10` | Smart auto minimum all-fan percentage. |
| AutoMaximumFanPercent | `42` | Smart auto maximum all-fan percentage. |
| Theme | `Default` | Follows the system theme. |
| Language | `zh-CN` | Default UI language is Simplified Chinese. |

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
8. Start with Dell Auto or a conservative manual percentage before trying lower fan speeds.

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

## Smart Auto Policy

The smart auto policy performs one `sdr elist` read per tick, parses CPU temperature, and computes the all-fan percentage as follows:

- CPU temperature less than or equal to target temperature: use the smart auto minimum fan percentage.
- CPU temperature greater than or equal to high temperature: use the smart auto maximum fan percentage.
- CPU temperature between target and high thresholds: linearly interpolate.
- CPU temperature at or above the emergency auto threshold: restore Dell automatic mode and let iDRAC/BMC take over.

CPU temperature detection prefers temperature rows whose key contains `CPU`. If no CPU-named temperature rows exist, it uses the highest value among all temperature sensors. If no temperature sensor is found, the app reports an error.

## Polling And Concurrency

- Sensor polling starts after a successful connection.
- Each poll reads `sdr elist` and refreshes the table, dashboard cards, and chart data.
- If the previous SDR read is still running, the next tick is skipped with a visible warning.
- If another IPMI command is running, the polling tick is also skipped to avoid overlapping commands against the BMC.
- If one SDR read takes longer than the configured polling interval, the app shows a recommended interval.
- A polling failure stops polling, updates connection state, and shows the failure reason.

## Individual Fan Risk

Individual fan mode uses these target bytes:

| Fan | Target byte |
| --- | --- |
| All fans | `0xff` |
| Fan 1 | `0x00` |
| Fan 2 | `0x01` |
| Fan 3 | `0x02` |
| Fan 4 | `0x03` |
| Fan 5 | `0x04` |
| Fan 6 | `0x05` |

On the locally tested R730xd/iDRAC 2.82, `0x00` did not isolate Fan 1 and instead ramped all fans high. For that reason, individual fan control is disabled by default. Before enabling it, verify your firmware behavior. After enabling it, watch RPM and temperatures after every action. If behavior is unexpected, restore Dell automatic mode immediately.

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

### Polling takes too long

1 second is the request cadence, not a guarantee that the BMC returns full SDR data in 1 second. If reads exceed the interval, raise the polling seconds to the value recommended in the UI.

### Charts fail to load

Confirm that output contains:

```text
Assets\Charts\dashboard.html
Assets\Charts\echarts.min.js
```

Charts use local WebView2 resources. If the WebView2 runtime is unavailable, install or repair Microsoft Edge WebView2 Runtime.

### App is still running after closing the window

The default close behavior minimizes to tray. Right-click the tray icon to restore or exit, or disable "Minimize to tray on close" in Settings.

## Repository Structure

```text
Assets/                  Icons, logo, chart HTML, and ECharts assets
BundledTools/ipmitool/   Bundled ipmitool.exe and required runtime DLLs
Models/                  Settings, presets, sensors, dashboard, and log models
Services/                IPMI commands, settings storage, localization, and tray service
docs/                    Command reference and project metadata
MainPage.xaml            Main UI layout
MainPage.xaml.cs         Main page interactions, polling, smart auto policy, and chart data
MainWindow.xaml.cs       Window, tray, and close behavior
```

## Documentation Standard

README and `docs/` files in this repository should stay complete, specific, and actionable. When adding features, settings, commands, risks, error handling, or publishing behavior, update both Chinese and English documentation with trigger conditions, user-visible behavior, default configuration, known limitations, and verification steps. Do not add shallow one-line feature notes when the actual command, workflow, or risk boundary matters.

## Repository Topics

`dell`, `poweredge`, `r730xd`, `idrac`, `ipmi`, `ipmitool`, `fan-control`, `server-management`, `homelab`, `winui`, `windows`, `dotnet`
