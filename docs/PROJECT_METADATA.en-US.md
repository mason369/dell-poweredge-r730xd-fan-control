# Project Metadata

Language: [中文](PROJECT_METADATA.md) | English

This file organizes repository presentation, release notes, search keywords, and project positioning. README targets users, `COMMANDS.md` targets command details, and this file targets repository naming, search, and public presentation information.

## English Name

Dell R730xd iDRAC Fan Control Center

## Chinese Name

R730XD 智控风扇中心

## GitHub Repository

`dell-poweredge-r730xd-fan-control`

## One-Line Description

Windows WinUI 3 automatic fan-control center for Dell PowerEdge R730xd, focused on iDRAC/IPMI fan control, `ipmitool raw 0x30 0x30` fan speeds, BMC SDR sensor monitoring, temperature/power fan-curve automation, local historical charts, tray actions, and JSONL runtime logs.

## Long Description

Dell R730xd iDRAC Fan Control Center brings common R730xd iDRAC fan control, IPMI over LAN, `ipmitool raw 0x30 0x30` fan-speed commands, BMC SDR sensor polling, preset management, temperature-fan curves, power-fan curves, tray quick actions, local visualization, and JSONL runtime logging into one Windows desktop app. It bundles `ipmitool.exe` and local chart assets, avoiding dependency on external command paths or online CDNs. The app defaults to Chinese, includes 22 interface languages, routes visible UI strings through a localization service, and localizes the MSIX package manifest display name and description through `Strings/<language>/Resources.resw`.

The core goal is to make it clear what the software is doing to the BMC: every command has an explicit UI trigger, failures are displayed directly, runtime logs record atomic events and operation durations, passwords do not enter command-line arguments, and low-speed plus individual fan target-selector risks are documented in the UI and docs, especially that `0x00` is a target selector rather than `0%` fan speed.

## Search Positioning And Repository Intro Notes

Public-facing copy should keep both the English primary name `Dell R730xd iDRAC Fan Control Center` and the Chinese name `R730XD 智控风扇中心`. Search-friendly summaries should front-load these terms: `Dell PowerEdge R730xd fan control`, `iDRAC fan control`, `IPMI fan speed`, `ipmitool raw 0x30 0x30`, `automatic fan control`, `temperature fan curve`, `BMC SDR sensor monitoring`, and `Windows GUI`.

Repository descriptions, release summaries, and external share text should emphasize:

- Controls Dell PowerEdge R730xd fans through iDRAC/IPMI over LAN and `ipmitool raw 0x30 0x30`.
- Supports manual percentages, Dell Auto mode, smart temperature automation, temperature curves, and power-based fan curves.
- Monitors BMC SDR sensors, Fan RPM, CPU/inlet/exhaust temperatures, power, voltage, current, and health states in real time.
- Provides a Windows WinUI 3 graphical interface for homelab noise control, R730xd caretaker machines, third-party drive or PCIe fan-noise situations, and visible server-room supervision.
- Compared with Bash scripts, Docker containers, cron jobs, Home Assistant add-ons, or background services, this project emphasizes local visible state, chart history, tray actions, command transparency, and explicit failure reporting.
- The project is not a general server asset-management platform, does not write firmware-level real-time fan curves, and does not claim support for every Dell PowerEdge model. README and security docs should keep documenting scope, failure behavior, and hardware risk.

Recommended search phrases or external link anchors:

`Dell R730xd fan control`, `Dell PowerEdge R730xd fan speed`, `iDRAC IPMI fan control`, `ipmitool raw 0x30 0x30`, `automatic Dell server fan control`, `temperature based fan curve`, `BMC SDR sensor monitor`, `homelab server noise`, `Windows GUI fan control`, `R730xd 风扇控制`, `R730xd 风扇降噪`, `Dell 服务器自动风扇控制`.

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
- Editable temperature-fan and power-fan curve presets stored in the settings file and continuously executed through software auto polling.
- Software auto automation, including the global temperature linear policy, temperature curve evaluation from the current reading, and power curve evaluation from the current reading.
- Sensor polling, explicit "Start polling / Cancel polling" commands, running-preset state restore, and latency warnings. Starting polling runs `mc info` and one `sdr elist` read first; canceling polling only stops future polling ticks and does not start a new IPMI command. User-triggered fan commands wait for the current IPMI command to finish before continuing, while background ticks still skip when IPMI is busy. After a background sensor polling failure, the app first records the real failure and stops the current poll, then performs one real reconnect (`mc info` + `sdr elist`); polling resumes only if reconnect succeeds, and the reconnect does not reapply saved fan presets or automatic policies.
- Hero live hardware summary for current temperature, average RPM, power, average voltage, and total current from the latest successful SDR refresh; each group shows every concrete sensor detail on separate lines instead of truncating to the first three items, cards use a taller baseline and grow with the actual number of returned sensors, missing readings show the waiting state, the temperature summary does not use a historical maximum, and live values are colored green, yellow, orange, or red by recommended ranges. A "Current thermal mode" badge under the title shows idle, manual, Dell automatic thermal control, smart temperature policy, temperature-curve auto, or power-curve auto from the same runtime mode state as the right-side status card.
- Overview top hardware summary cards for max CPU temperature, fan state, live power, average voltage, total current, and control mode. Temperature, fan, and Power & Health boards use icon-based hardware cards, fan cards rotate continuously based on RPM, and card subtitles label SDR metadata as "ID 0x30 / Location 7.1" so the `h` suffix is not mistaken for hours and the location number is not mistaken for a version or reading.
- WebView2 interactive charts forward mouse-wheel events to the outer WinUI scroll container. Discrete mouse wheels use WinUI platform scroll animation, while touchpad-style high-frequency small deltas move by the coalesced distance directly. ECharts internal wheel zoom is disabled, and history ranges are still adjusted with the bottom slider, so scrolling over the chart does not trap page motion.
- Right-side hero live status card for current target, connection state, control mode, latest request status, and last update time.
- WinUI 3 theme, tray, and i18n. The tray right-click menu provides window/page entries, refresh sensors, open iDRAC, open logs, stop auto policy, restore Dell automatic mode, all-fan 20/35/50%, and a one-level dynamic preset submenu. Package manifest display name and description are emitted into the PRI index through `.resw` resources.
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
- Charts: local `Assets/Charts/dashboard.html` plus ECharts; Overview charts save a complete JSONL history point after each successful SDR poll, and users can switch Current, Last 6 hours, Last 1 day, Last 3 days, Last 7 days, or Custom ranges through the "History range" control.
- Credential protection: `System.Security.Cryptography.ProtectedData` / Windows DPAPI.
- Runtime logging: local JSON Lines at `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.

## Key Defaults

| Setting | Default |
| --- | --- |
| Host | `192.0.2.10` |
| UserName | `idrac-user` |
| RememberPassword | `false` |
| IpmiToolPath | `BundledTools\ipmitool\ipmitool.exe` |
| FanCount | `6` |
| Built-in Restore Manual preset percent | `10%` |
| Minimize to tray on close | `true` |
| Chart history retention | Latest `7` days of successful polling history points, stored at `%LocalAppData%\DellR730xdFanControlCenter\chart-history\chart-history-YYYYMMDD.jsonl` and reloaded on startup while still inside the retention window |
| Sensor polling interval | `1s`, edited from the Settings page's Application area |
| Command timeout | `35s` |
| Target CPU temperature | `68 °C` |
| High temperature threshold | `78 °C` |
| Emergency auto threshold | `84 °C` |
| Auto policy minimum fan percent | `10%` |
| Auto policy maximum fan percent | `42%` |
| New temperature curve editor default points | `45 C = 18%`, `68 C = 28%`, `78 C = 42%` |
| New power curve editor default points | `280 W = 18%`, `500 W = 28%`, `750 W = 42%` |
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

These presets are first-run seed entries, not undeletable fixed entries. The user can click the delete button on any starter or custom preset card; after saving, `settings.json` keeps the remaining preset list, and later launches or settings loads do not automatically restore deleted starter presets. If the user deletes every preset, the empty list is saved as-is. The app recreates the table above only when the settings file does not exist or the `Presets` field is missing.

## Custom Curve Presets

Curve presets are not built-in and are not created automatically by default. On the Fan Control page, the user enters a curve preset name, clicks empty chart space to add points, drags existing points for live adjustment, or fine-tunes values from the right-side numeric point controls. Hovering over a curve chart shows crosshair guides, the current X-axis temperature/power value, and the Y-axis fan-speed percent. During drag, the editor performs lightweight chart and bound-value updates; the full preview text and strict validation refresh after pointer release, right-side numeric edits, add-point actions, or save. Temperature curves are stored as `Kind = TemperatureCurve`, and points contain `TemperatureCelsius` plus `FanPercent`; power curves are stored as `Kind = PowerCurve`, and points contain `PowerWatts` plus `FanPercent`. Both write `CurvePoints` and `SmoothCurve` into `settings.json`; existing curve presets can be loaded into the matching editor from the preset card's "Edit points" button, and the page automatically scrolls to the matching temperature or power curve chart. After a successful save, the page scrolls back to the newly added or updated preset card.

Runtime behavior:

- Switching to a curve preset starts software auto polling.
- Each tick reads `sdr elist`. Temperature curves parse CPU temperature and compute the percentage from the curve points and `SmoothCurve` setting; power curves parse CPU temperature and check the emergency auto threshold first, then compute the percentage from the current SDR power reading whose unit contains `Watts` or whose key contains `Pwr Consumption`. While the automatic policy is running, regular sensor polling does not add another SDR read; this tick's SDR result refreshes sensors, charts, and history points. If the computed percentage differs from the last successful send for the same automatic mode, the final command is an all-fan percentage command; if it is unchanged, the tick records `SkipUnchangedFanPercent`, shows "No fan command sent", and does not repeat the same raw fan-control command.
- If the currently running manual preset, Dell Auto preset, temperature curve, or power curve is saved, the app waits for the current IPMI command to finish and immediately re-applies that preset. Saving an active curve immediately runs one real `sdr elist` read, curve percentage calculation, and fan command; if the first run fails, the error is shown and that automatic policy is stopped.
- Non-zero exits from Dell fan-control raw commands (`raw 0x30 0x30 ...`) fail immediately; every actual `ipmitool` child process writes one real result without `attempt`, `maxAttempts`, or `willRetry`, and stdout/stderr is shown directly to the user. `sdr elist` polling failures do not retry the same failed command and do not write fabricated history points; only background sensor polling failures append one visible real reconnect.
- Input values below the first point use the first point. Values above the last point use the last point.
- `SmoothCurve = false` evaluates the current temperature or power reading on the polyline formed by the configured points. `SmoothCurve = true` evaluates the same points with a smoothed curve position; endpoints and emergency auto protection do not change.
- At the emergency auto threshold, the app sends Dell automatic mode instead of continuing to send curve percentages; this action does not stop the software auto timer.
- If a power curve tick has no power reading, the app shows the failure reason and stops that tick instead of continuing with a default or previous power value.
- Manual all-fan control, individual fan control, the built-in Restore Manual preset, Dell Auto, Overview/tray Restore Dell factory fan speed, and Stop Auto all clear the active curve state. Only Stop Auto, deleting the running curve preset, or an auto-policy failure stops the software auto timer; if the timer is still running, the next tick continues controlling all fans with the global linear policy.

Validation boundaries:

- At least 2 points.
- Temperature curves allow temperatures from `-40` to `125` C; power curves allow power from `0` to `1200` W.
- Fan percent range from `0` to `100`.
- Temperature points or power points cannot repeat.
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
Windows 10/11 WinUI 3 GUI for Dell PowerEdge R730xd iDRAC fan control over IPMI: manual speed, CPU temperature and power fan curves, BMC SDR/RPM monitoring, presets, charts, tray actions, and 22 UI languages. 戴尔 R730xd 风扇控制与硬件监控。
```

## Recommended Chinese Description

```text
面向 Dell PowerEdge R730xd 的 Windows 10/11 WinUI 3 风扇控制与硬件监控工具，支持 iDRAC/IPMI、手动转速、CPU 温度与功耗曲线、BMC SDR/RPM 监控、预设、本地图表、托盘操作和 22 种 UI 语言。
```

## Topics

- dell
- dell-poweredge
- r730xd
- idrac
- bmc
- ipmi
- ipmitool
- fan-control
- fan-curve
- thermal-management
- temperature-monitoring
- hardware-monitoring
- server-monitoring
- noise-reduction
- homelab
- windows
- desktop-application
- winui3
- dotnet
- csharp

## License And Third-Party Notices

- Project source license: MIT License in the repository root `LICENSE`.
- Third-party notices: repository root `THIRD_PARTY_NOTICES.md`.
- Bundled command runtime notices: `BundledTools/ipmitool/README.md` records versions, SHA-256 hashes, licenses, and source entry points for `ipmitool.exe` plus Cygwin/GCC/OpenSSL/zlib DLLs.
- ECharts notices: `Assets/Charts/echarts.LICENSE.txt` and `Assets/Charts/echarts.NOTICE.txt` must be distributed with the chart assets.
- Release packages must not present bundled third-party binaries as MIT-licensed project code; MIT covers only this repository's own source and documentation.

## Release Checklist

Before publishing or packaging, confirm:

- `dotnet build` succeeds for the target platform.
- `dotnet run --project Tests\PresetModelTests\PresetModelTests.csproj` succeeds, covering manual preset editing, temperature/power curve evaluation from current readings, smooth curve evaluation, settings storage, sensor-state translation keys, and runtime logging JSONL behavior.
- Repository root `LICENSE` and `THIRD_PARTY_NOTICES.md` exist and are present in published output.
- Output contains `BundledTools/ipmitool/ipmitool.exe`.
- Output contains `BundledTools/ipmitool/README.md` and `BundledTools/ipmitool/LICENSES/**`.
- Output contains `Assets/Charts/dashboard.html` and `Assets/Charts/echarts.min.js`.
- Output contains `Assets/Charts/echarts.LICENSE.txt` and `Assets/Charts/echarts.NOTICE.txt`.
- For directly runnable exe-directory releases, use `tools/Publish-UnpackagedExe.ps1` or an equivalent command and confirm that the output directory contains `DellR730xdFanControlCenter.exe`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, `Assets/AppIcon.ico`, dashboard assets, ECharts license/NOTICE files, the bundled `ipmitool.exe`, and bundled command-runtime license files; this mode requires distributing the whole directory, not just the single exe file. Do not treat an MSIX build intermediate under `bin\Release\...\publish` as the unpackaged release artifact.
- For GitHub Actions or GitHub Release downloadable zips, use `tools/Publish-ReleaseZip.ps1` or `.github/workflows/release.yml`, and confirm that the extracted zip contains the exe, `Microsoft.WindowsAppRuntime.dll`, `Microsoft.ui.xaml.dll`, `DellR730xdFanControlCenter.pri`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, dashboard assets, ECharts license/NOTICE files, bundled `ipmitool.exe`, and bundled command-runtime license files. Local verification can run `tools/Publish-ReleaseZip.ps1 -VerifyLaunch` to confirm that the extracted zip starts a window. This zip is an unsigned unpackaged release and should not contain `.msix`, `.pfx`, `.cer`, `AppxManifest.xml`, or `Package.appxmanifest`; the script must fail when it detects those signed-package or package-identity files. The GitHub Actions workflow should not call `tools\Publish-SignedMsix.ps1`, `Add-AppxPackage`, or `Get-AuthenticodeSignature`. GitHub Actions manual runs upload only a workflow artifact; `v*` tags upload the GitHub Release asset directly and use `gh release upload --clobber` so rerunning the same tag can replace the same-named zip, without uploading a workflow artifact that could be blocked by full artifact storage quota.
- For MSIX releases, sign with `tools/Publish-SignedMsix.ps1` or an equivalent command; the script must publish with `WindowsAppSDKSelfContained=true`, the signing certificate subject must exactly match the `Package.appxmanifest` `Publisher`, and `Get-AuthenticodeSignature` must return `Valid`. After signing, the package must be unpacked to verify that the generated `AppxManifest.xml` contains no external `PackageDependency`, and that `Microsoft.WindowsAppRuntime.dll`, `Microsoft.ui.xaml.dll`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, dashboard assets, ECharts license/NOTICE files, bundled `ipmitool.exe`, and bundled command-runtime license files are inside the package. The script should place the MSIX publish intermediate under `obj\signed-msix\publish` and clean it after completion so it does not leave a misleading `bin\Release\...\publish` byproduct.
- The default self-signed MSIX publish flow must run from an elevated PowerShell session and confirm that the public certificate is present in `CurrentUser\TrustedPeople`, `CurrentUser\Root`, `LocalMachine\TrustedPeople`, and `LocalMachine\Root`; without local deployment trust, `Add-AppxPackage` rejects the package with `0x800B0109` even when `Get-AuthenticodeSignature` returns `Valid`.
- Reinstalling changed MSIX content under the same `Identity` and same `Version` is rejected by Windows with `0x80073CFB`; real releases must increase the `Package.appxmanifest` `Identity Version`, while local same-version verification must remove the old package before installing the new one.
- Signature verification is not launch verification. After MSIX publishing, run `Add-AppxPackage` on the target machine and launch the package to confirm that the main window appears, settings can be saved, and tray/dashboard features work; missing Windows deployment trust, runtime dependencies, a bad entry point, or missing packaged files can still make a package fail even when its signature is `Valid`.
- Keep self-signed certificate private keys only in the local certificate store or a controlled key system. Do not commit `.pfx` files; the public `.cer` is only for trusting that signer on target machines.
- The app starts and shows the main window.
- Settings can be saved.
- The tray right-click menu shows window/page entries, refresh sensors, logs/iDRAC entries, Dell automatic restore, stop auto, all-fan 20/35/50%, and a preset submenu; common fixed actions are no longer nested behind a second-level Fan Control menu.
- Overview "Open logs" opens `%LocalAppData%\DellR730xdFanControlCenter\logs`, and today's `runtime-YYYYMMDD.jsonl` can be written.
- No password, real private address, private IP plan, or other sensitive data is committed; documentation examples use `192.0.2.10` and `idrac-user`.
