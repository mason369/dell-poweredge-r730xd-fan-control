# Project Metadata

Language: [中文](PROJECT_METADATA.md) | English

This file organizes repository presentation, release notes, search keywords, project positioning, and maintenance conventions. README targets users, `COMMANDS.md` targets command details, and this file targets repository maintenance, naming, and public presentation.

## English Name

Dell R730xd iDRAC Fan Control Center

## Chinese Name

R730XD 智控风扇中心

## GitHub Repository

`r730xd-idrac-fan-control-center`

## One-Line Description

Windows WinUI 3 desktop app for Dell PowerEdge R730xd fan control through iDRAC/IPMI, BMC sensor monitoring, live hardware charts, JSONL runtime logs, Dell Auto mode, smart temperature automation, and user-defined temperature-fan curve presets.

## Long Description

Dell R730xd iDRAC Fan Control Center brings common R730xd IPMI fan control, sensor polling, preset management, temperature-fan curve presets, tray quick actions, local visualization, and JSONL runtime logging into one Windows desktop app. It bundles `ipmitool.exe` and local chart assets, avoiding dependency on external command paths or online CDNs. The app defaults to Chinese, includes English, and routes visible UI strings through a localization service.

The core goal is to make it clear what the software is doing to the BMC: every command has an explicit UI trigger, failures are displayed directly, runtime logs record atomic events and operation durations, passwords do not enter command-line arguments, and low-speed plus individual fan target-selector risks are documented in the UI and docs, especially that `0x00` is a target selector rather than `0%` fan speed.

## Target Users

- Dell PowerEdge R730xd homelab users.
- Server owners who need lower noise while still watching temperature.
- Users who need quick switching between manual fan control, presets, and Dell automatic mode.
- Users who need to save and quickly enable editable "temperature to fan speed" curves.
- Users who want a Windows graphical interface for BMC SDR sensors.
- Maintainers who want iDRAC fan command workflows documented, visualized, and repeatable.

## Project Scope

Includes:

- iDRAC/IPMI over LAN connection.
- `mc info` connection test.
- `sdr elist` sensor reading and parsing.
- Dell OEM raw manual fan mode.
- All-fan percentage control.
- Fan 1-6 individual target-selector control, disabled by default; `0x00` is a target selector, not `0%` fan speed.
- Dell automatic mode restore.
- Built-in and custom manual presets.
- Editable temperature-fan curve presets stored in the settings file and continuously executed through software auto polling.
- Smart temperature automation, including the global linear policy and curve preset interpolation.
- Sensor polling and latency warnings.
- Hero live hardware summary for current temperature, average RPM, power, average voltage, and total current from the latest successful SDR refresh; each group shows up to 3 concrete sensor details on separate lines, extra sensors are not shown in the hero, cards use a fixed compact height, missing readings show the waiting state, the temperature summary does not use a historical maximum, and live values are colored green, yellow, orange, or red by recommended ranges.
- Overview top hardware summary cards for max CPU temperature, fan state, live power, average voltage, total current, and control mode. Temperature, fan, and Power & Health boards use icon-based hardware cards, and fan cards rotate continuously based on RPM.
- WebView2 interactive charts forward mouse-wheel events to the outer WinUI scroll container so scrolling over the chart keeps native smooth page motion.
- Right-side hero live status card for current target, connection state, control mode, latest request status, and last update time.
- WinUI 3 theme, tray, and i18n.
- Local ECharts live visualization.
- DPAPI local password protection.
- Local JSON Lines runtime logs for UI events, operation start/finish durations, sensor refreshes, smart auto ticks, and IPMI command completion records; Overview Recent Log uses blue info, amber warning, green success, and red error/failure status badges.

Does not include:

- General server asset management.
- Centralized multi-host management.
- iDRAC user or firmware management.
- Redfish control logic outside IPMI.
- Firmware-level real-time fan curve writing.
- Cloud sync.
- Cross-platform UI.

## Tech Stack

- Language: C#.
- Framework: .NET 8.
- UI: WinUI 3 / Windows App SDK.
- Target framework: `net8.0-windows10.0.26100.0`.
- Minimum Windows version: `10.0.19041.0`.
- Platforms: `x86`, `x64`, `ARM64`.
- Command tool: bundled `ipmitool.exe`.
- Charts: local `Assets/Charts/dashboard.html` plus ECharts.
- Credential protection: `System.Security.Cryptography.ProtectedData` / Windows DPAPI.
- Runtime logging: local JSON Lines at `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.

## Key Defaults

| Setting | Default |
| --- | --- |
| Host | `192.168.1.73` |
| UserName | `root` |
| FanCount | `6` |
| Built-in Restore Manual preset percent | `10%` |
| Sensor polling interval | `1s` |
| Command timeout | `35s` |
| Target CPU temperature | `68 °C` |
| High temperature threshold | `78 °C` |
| Emergency auto threshold | `84 °C` |
| Auto policy minimum fan percent | `10%` |
| Auto policy maximum fan percent | `42%` |
| Default language | `zh-CN` |
| Default theme | System |
| Runtime log directory | `%LocalAppData%\DellR730xdFanControlCenter\logs` |

## Built-In Presets

| ID | Name | Type | Percent | Notes |
| --- | --- | --- | --- | --- |
| `restore-manual` | Default / 默认 | Restore manual | `10%` | Returns to local default manual 10%. |
| `balanced` | Balanced / 均衡 | Manual | `20%` | Daily light-load balance between noise and temperature. |
| `cooling` | Cooling / 散热 | Manual | `35%` | Adds cooling headroom. |
| `performance` | Performance / 性能 | Manual | `50%` | Prioritizes airflow for short high-load sessions. |
| `dell-auto` | Dell Auto / Dell 自动 | BMC automatic | No manual percentage | Hands control back to iDRAC/BMC firmware policy. |

## Custom Curve Presets

Curve presets are not built-in and are not created automatically by default. On the Fan Control page, the user enters a curve preset name, clicks the curve chart to add points, or fine-tunes `TemperatureCelsius` and `FanPercent` from the right-side numeric point controls. Existing curve presets can be loaded into the same editor from the preset card's "Edit points" button. After save, `settings.json` stores `Kind = TemperatureCurve`, `CurvePoints`, and `SmoothCurve`; each point contains `TemperatureCelsius` and `FanPercent`.

Runtime behavior:

- Switching to a curve preset starts software auto polling.
- Each tick reads `sdr elist`, parses CPU temperature, computes the percentage from the curve points and `SmoothCurve` setting, and sends the all-fan percentage command.
- If the currently running curve is edited and saved, the next tick uses the updated points and `SmoothCurve` setting.
- CPU temperatures below the first point use the first point. Temperatures above the last point use the last point.
- `SmoothCurve = false` uses linear interpolation between adjacent points. `SmoothCurve = true` uses the same points with a smooth transition; endpoints and emergency auto protection do not change.
- At the emergency auto threshold, the app sends Dell automatic mode instead of continuing to send curve percentages.
- Manual all-fan control, individual fan control, the built-in Restore Manual preset, Dell Auto, Overview/tray Restore Dell factory fan speed, or Stop Auto clears the active curve state.

Validation boundaries:

- At least 2 points.
- Temperature range from `-40` to `125` C.
- Fan percent range from `0` to `100`.
- Temperature points cannot repeat.
- Invalid points show an error in the curve preview area and prevent save or switch; they are not automatically replaced with a default curve.

## Documentation Map

- [README.md](../README.md): complete Chinese user guide.
- [README.en-US.md](../README.en-US.md): complete English user guide.
- [SECURITY.md](../SECURITY.md): Chinese security guide.
- [SECURITY.en-US.md](../SECURITY.en-US.md): English security guide.
- [COMMANDS.md](COMMANDS.md): Chinese IPMI command reference.
- [COMMANDS.en-US.md](COMMANDS.en-US.md): English IPMI command reference.
- [PROJECT_METADATA.md](PROJECT_METADATA.md): Chinese project metadata.
- [PROJECT_METADATA.en-US.md](PROJECT_METADATA.en-US.md): English project metadata.

## Logo Concept

The logo combines a rack server front panel, a turbine fan, and an amber status line:

- The front panel represents Dell PowerEdge R730xd.
- The turbine fan represents fan control and cooling.
- The amber status line reflects server health, alerts, and management control.
- The icon should remain recognizable on both light and dark backgrounds.

The search-facing English name keeps the important keywords: Dell, R730xd, iDRAC, and Fan Control. The Chinese name emphasizes R730XD and "Smart Fan Center".

## Recommended Repository Description

```text
WinUI 3 desktop app for Dell PowerEdge R730xd iDRAC/IPMI fan control, BMC sensor monitoring, smart temperature automation, tray quick actions, local ECharts visualization, and JSONL runtime logs.
```

## Recommended Chinese Description

```text
面向 Dell PowerEdge R730xd 的 WinUI 3 桌面风扇控制中心，支持 iDRAC/IPMI 调速、BMC 传感器监控、软件恒温策略、托盘快捷操作、本地 ECharts 可视化和 JSONL 运行日志。
```

## Topics

- dell
- poweredge
- r730xd
- idrac
- ipmi
- ipmitool
- fan-control
- server-management
- homelab
- winui
- windows
- dotnet
- bmc
- hardware-monitoring
- thermal-management

## Release Checklist

Before publishing or packaging, confirm:

- `dotnet build` succeeds for the target platform.
- `dotnet run --project Tests\PresetModelTests\PresetModelTests.csproj` succeeds, covering manual preset editing, curve preset linear/smooth interpolation, settings storage, sensor-state translation keys, and runtime logging JSONL behavior.
- Output contains `BundledTools/ipmitool/ipmitool.exe`.
- Output contains `Assets/Charts/dashboard.html` and `Assets/Charts/echarts.min.js`.
- The app starts and shows the main window.
- Settings can be saved.
- Overview "Open logs" opens `%LocalAppData%\DellR730xdFanControlCenter\logs`, and today's `runtime-YYYYMMDD.jsonl` can be written.
- No password, private IP plan, or other sensitive data is committed.
- Chinese and English README files both cover new features.
- Chinese and English security files both cover new risks.
- Chinese and English command references both cover new commands, raw byte behavior, or logging behavior.

## Documentation Standard

All README, `docs/`, and security documents must be complete, specific, and verifiable. When adding or changing a feature, documentation should cover:

- Purpose.
- User entry point.
- Default configuration.
- Execution flow.
- Related commands or file paths.
- User-visible success behavior.
- User-visible failure behavior.
- Security or hardware risk.
- Known limitations.
- Verification steps.
- Matching Chinese and English updates.

Do not add shallow feature lists only. For fan control, credentials, command execution, sensor parsing, package contents, or failure handling, document trigger conditions, boundaries, and risks clearly.
