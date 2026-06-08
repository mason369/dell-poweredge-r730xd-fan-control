# Security

Language: [中文](SECURITY.md) | English

This project directly controls Dell R730xd fans through iDRAC/IPMI. Its security boundary includes both credential handling and hardware operational safety. Treat it as a local management tool that can affect server temperature and stability, not as a read-only monitoring panel.

## Security Model

- The app runs in the current Windows user session.
- The app connects to the user-configured iDRAC/BMC address.
- The app executes IPMI over LAN commands through bundled `ipmitool.exe`.
- The app does not commit passwords to the repository.
- The app does not place passwords in command-line arguments.
- The app does not silently switch to another backend or fabricate success when an IPMI command fails.
- Real failure reasons are shown in the UI status bar and recent log.

## Credential Storage

By default, the password exists only in the running process UI field and memory. Users can enable "Remember password with DPAPI". When enabled, the app encrypts the password with Windows DPAPI using `DataProtectionScope.CurrentUser` and writes the protected value to the local settings file:

```text
%LocalAppData%\DellR730xdFanControlCenter\settings.json
```

The DPAPI value is bound to the current Windows user context. Copying it to another user or machine will generally make it undecryptable. Deleting the local settings file removes the saved host, username, protected password, presets, and UI settings.

## Command Visibility

The command runner uses:

```text
ipmitool -I lanplus -H <host> -U <user> -E <ipmi-arguments>
```

The password is passed to `ipmitool -E` through the `IPMI_PASSWORD` environment variable. This avoids exposing the password in command-line arguments, Task Manager command-line columns, process enumeration output, or UI logs.

The Recent Log and JSONL runtime log record:

- The `ipmitool` path and arguments.
- Exit code.
- Success or failure state.
- Command duration.
- Start time, finish time, duration, `operationId`, and terminal state for user commands, sensor refreshes, and smart auto ticks.
- User-visible error text on failure; when `ipmitool` fails, the error text can include a stdout/stderr summary.

The Recent Log and JSONL runtime log do not record the password, but they can record the iDRAC host, username, tool path, local paths, preset names, sensor names, and command arguments.

## Local Logs

The in-app Recent Log keeps the latest 80 in-memory entries. Overview includes an "Open logs" button that opens:

```text
%LocalAppData%\DellR730xdFanControlCenter\logs
```

Runtime log files are named by local date:

```text
%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl
```

Each line is a complete JSON object; one event is not split across multiple lines. Common fields include `eventId`, `timestamp`, `level`, `category`, `eventName`, and `message`. Operation-duration records also include `operationId`, `phase`, `startedAt`, `finishedAt`, `durationMilliseconds`, and `succeeded`.

If runtime log writing fails, the app does not show the operation as successful. Button-triggered user commands stop, and the status bar plus Recent Log show "Runtime log write failed" with the underlying exception. If an unhandled exception happens during startup, the app writes:

```text
%LocalAppData%\DellR730xdFanControlCenter\startup-error.log
```

There is currently no automatic retention period or cleanup policy. Long-running polling continues growing the daily JSONL file; archive or delete old logs manually according to your operational needs. Before filing an issue, sharing a screenshot, or attaching logs, check whether they include your iDRAC address, username, host name, paths, preset names, sensor output, or other environment details you do not want public. Do not publish iDRAC passwords, VPN addresses, internal network ranges, security policies, asset numbers, or other sensitive data.

## iDRAC Privileges

The app needs an iDRAC account that can read SDR data and send Dell OEM raw IPMI commands. Restricted accounts may behave like this:

- `mc info` succeeds, but `raw 0x30 ...` control commands fail.
- `sdr elist` succeeds, but fan mode switching fails.
- Commands return privilege or authentication errors.

Use an iDRAC account with the minimum privileges required for this app, and avoid reusing high-value management passwords. If you are unsure about the privilege boundary, validate in an isolated network or maintenance window first.

## Network Boundary

IPMI over LAN should only be exposed on trusted management networks. Do not expose iDRAC/IPMI directly to the public Internet. Recommended practices:

- Use a dedicated management VLAN or controlled internal network.
- Restrict source IPs that can access iDRAC.
- Use strong passwords and rotate them periodically.
- Disable management protocols you do not need.
- Do not save passwords when operating on untrusted networks.

## Operational Safety

Fan control changes server cooling capacity. Low manual speeds can raise CPU, drive, PCIe card, power supply, memory, or chassis temperatures. After every fan change, monitor:

- CPU temperature and CPU usage.
- Inlet and exhaust temperatures.
- Fan 1-6 RPM.
- Drive, backplane, cable presence, and redundancy states.
- Power, voltage, and current.
- iDRAC alerts and system events.

When workload is unknown, ambient temperature is high, the chassis is drive-dense, sensors are abnormal, or you cannot keep watching the machine, use Dell automatic mode. Overview Quick Actions and the tray menu item "Restore Dell factory fan speed" send the Dell automatic mode command and hand fan control back to iDRAC/BMC firmware; they are not manual 10%.

The hero live summary and Overview top hardware summary are display-only and use the latest successful SDR refresh. The hero "Live temperature" value is the current average of temperature sensor readings, not a historical maximum, and it does not drive smart auto or emergency automatic protection; actual control still uses the CPU/max temperature semantics described in the smart auto policy section. The small detail rows under each hero value show at most 3 concrete sensor readings, and extra sensors are not shown in the hero, so they do not replace the full sensor table. Before the first refresh, or when one sensor category is missing, the hero and Overview electrical summaries show "Waiting" or no-reading text. Hero and hardware-card colors are UI hints: normal green, near-risk yellow, clear deviation orange, and danger red. These thresholds do not change fan commands and do not replace iDRAC alerts, the full sensor table, or emergency automatic protection. Fan-card rotation is only a visual rhythm derived from current RPM readings, not part of the control loop. For fan changes or troubleshooting, still check the Temperature Board, Fan RPM Board, Power & Health board, and iDRAC alerts.

## Individual Fan Target Selector Risk

Individual fan control is disabled by default. In individual-fan commands, `0x00-0x05` are firmware raw-command target selectors used to choose a fan; they are not fan speeds, and `0x00` is not `0%` fan speed. On the locally tested R730xd/iDRAC 2.82, Fan 1 target selector `0x00` did not isolate Fan 1 and instead ramped all fans high. Different firmware may map targets differently. Before enabling individual fan control, confirm:

- Your iDRAC firmware accepts the raw command.
- Target selectors map to the expected physical fans.
- RPM and temperature behave as expected after each operation.
- You can restore Dell automatic mode immediately.

If behavior is unexpected, restore Dell automatic mode immediately.

## Smart Auto Policy Risk

The smart auto policy depends on BMC SDR reads and CPU temperature detection. Current logic prefers temperature sensors whose key contains `CPU`; if no CPU-named temperature row exists, it uses the highest value across all temperature sensors. If no temperature sensor is found, the app reports an error.

This policy is not a firmware-level real-time control loop. It is affected by:

- iDRAC SDR response time.
- Network latency.
- Command timeout setting.
- Polling interval setting.
- Sensor naming and firmware output format.

The default polling interval is 1 second, and saved values of 1 second or higher are allowed. This value is only the polling tick cadence; it does not guarantee that iDRAC can return a complete SDR read at the same frequency. The app allows only one IPMI operation at a time; when the previous SDR read or another IPMI command is still running, the current tick is skipped and no new `ipmitool` process or RMCP+ session is started. If a real command returns `Unable to establish IPMI v2 / RMCP+ session`, the app stops polling and shows the failure reason without retrying automatically, silently degrading, or pretending success.

At the emergency temperature threshold, the app sends the Dell automatic mode command so the BMC can take over. That action still depends on successful IPMI command execution.

## Temperature Curve Preset Risk

Curve presets are a software smart-auto rule; they are not written into iDRAC/BMC firmware. After switching to a curve preset, each software auto tick reads SDR, parses CPU temperature, interpolates the user-saved temperature-fan points, and sends an all-fan percentage command. Points are maintained from the Fan Control page with chart clicks and numeric point controls; Smooth curve is only a software-side interpolation mode between adjacent points and does not program a firmware fan curve into iDRAC/BMC.

Safety boundaries for curve presets:

- Curve points control only the all-fan percentage, not individual fans.
- Save or switch validates at least 2 points, temperatures from `-40` to `125` C, percentages from `0` to `100`, and no duplicate temperatures.
- The point list and `SmoothCurve` setting are stored in local `settings.json`. Deleting, editing, or saving a preset affects later software tick calculations, but it does not change server firmware configuration.
- CPU temperatures below the first point use the first point; temperatures above the last point use the last point. If the final point is too low, sustained high load can still be under-cooled.
- Smooth mode makes percentage changes between two points softer, but it does not change endpoints, emergency thresholds, or percentage bounds. If the points themselves are too low, smoothing does not add safety margin.
- At the emergency temperature threshold, the app first sends Dell automatic mode, but that protection still depends on SDR reads, CPU temperature detection, network availability, and successful IPMI command execution.
- Manual all-fan control, individual fan control, the built-in Restore Manual preset, Dell Auto, Overview/tray Restore Dell factory fan speed, or Stop Auto clears the active curve state so an old curve does not continue overriding manual commands.

Validate curves first under low load and with someone watching the machine. Do not set the final temperature point below realistic high-load temperatures, and do not set the final fan percentage too low. If charts, sensor status, or iDRAC alerts look abnormal, restore Dell automatic mode immediately.

## Supply Chain And Bundled Assets

The repository includes bundled `ipmitool.exe`, Cygwin DLLs, and ECharts frontend assets. When publishing or repackaging, verify:

- Bundled binaries come from trusted sources.
- Files have not been replaced or tampered with.
- `Assets/Charts/echarts.LICENSE.txt` and `Assets/Charts/echarts.NOTICE.txt` remain with the chart assets.
- The release package includes `BundledTools/ipmitool/**` and `Assets/Charts/**`.

If you replace `ipmitool.exe` or chart assets, document the source, version, verification method, and compatibility changes.

## Failure Handling Principles

This project requires explicit failure exposure:

- Missing bundled `ipmitool.exe` raises an error.
- Empty iDRAC host, username, or password raises an error.
- Command timeout kills the process and shows the timeout reason.
- Non-zero `ipmitool` exit code shows stdout/stderr.
- Empty SDR output raises an error.
- Missing UI translations raise an error.

Unless a future change has an explicit design and documents trigger conditions, user-visible behavior, and risks, do not add silent fallback, default success, swallowed exceptions, or fabricated data that hides real failures.

## Reporting Security Issues

When reporting a problem, include:

- App version or commit.
- Windows version.
- iDRAC firmware version.
- Reproduction steps.
- Publicly shareable error message.
- Redacted relevant log snippet.

Do not include:

- iDRAC passwords.
- Unredacted internal network plans.
- VPN, bastion, or firewall policy details.
- Private asset numbers or host names.
