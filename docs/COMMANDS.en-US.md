# IPMI Command Reference

Language: [中文](COMMANDS.md) | English

This app executes direct iDRAC/IPMI commands through the bundled `ipmitool.exe` and reports real failure reasons to the user. When a command fails, the app does not silently switch to another backend, fabricate sensor data, or assume success.

The app bundles `ipmitool.exe` and its required Cygwin DLLs in `BundledTools/ipmitool`. Runtime execution resolves the bundled path from the application output directory:

```text
BundledTools\ipmitool\ipmitool.exe
```

## Command Execution Format

All IPMI commands are wrapped as:

```text
ipmitool -I lanplus -H <host> -U <user> -E <ipmi-arguments>
```

Details:

- `-I lanplus`: uses the IPMI 2.0 RMCP+ LAN interface.
- `-H <host>`: iDRAC/BMC address from Settings.
- `-U <user>`: iDRAC username from Settings.
- `-E`: reads the password from the `IPMI_PASSWORD` environment variable.
- `<ipmi-arguments>`: `mc info`, `sdr elist`, or Dell OEM raw commands passed by the app.

Command logs show the tool path, arguments, exit code, and elapsed time. They do not show the password. The Overview Recent Log displays info, warning, success, and error/failure as different colored status badges, with red reserved for error and failure only; the local JSONL runtime log does not store color values and instead writes structured fields such as `level`, `displayLevel`, `succeeded`, and `exitCode`. Runtime logs also write start/finish duration records for user commands, sensor refreshes, and smart auto ticks.

## Command Logs And Duration Records

Runtime logs are written to:

```text
%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl
```

Each log entry is a single-line JSON object for line-oriented parsing and auditing. Command-related records include:

- `Operation/UiCommand`: Settings connection, refresh sensors, set fans, Overview/tray "Restore Dell factory fan speed", Dell Auto preset, manual 10% preset, and similar button actions. Each operation writes `Started` plus either `Succeeded` or `Failed`, with `operationId`, `startedAt`, `finishedAt`, and `durationMilliseconds`.
- `Operation/SensorRefresh`: each `sdr elist` refresh. Records host, polling seconds, sensor count, and duration.
- `Operation/SmartAutoPolicyTick`: each smart auto policy tick. Records temperature thresholds, CPU temperature, computed fan percent, or the Dell Auto action after the emergency threshold is reached.
- `IpmiCommand/CommandCompleted`: each actual completed `ipmitool` child command. Records full command line, exit code, success state, and duration.

If the runtime log cannot be written, the app does not show a button-triggered user command as successful; the status bar and Recent Log show "Runtime log write failed". There is currently no automatic cleanup policy, so persistent polling grows the daily JSONL file.

## Required iDRAC Settings

- Enable IPMI over LAN.
- Use an account with enough privilege for OEM raw commands.
- Keep iDRAC reachable from the Windows machine running the app.
- Ensure firewalls, VLANs, VPNs, or management network policies do not block IPMI over LAN.
- If read-only SDR commands succeed but raw commands fail, check iDRAC account privileges.

## App Command List

| Purpose | IPMI arguments | UI entry | Notes |
| --- | --- | --- | --- |
| Test connection | `mc info` | Settings "Connect", automatic connection after saving settings | Confirms host, user, password, network, and basic `ipmitool` availability. |
| Read sensors | `sdr elist` | Overview/Sensors refresh, connection polling, smart auto policy | Returns full SDR rows; the app parses temperature, fans, power, voltage, current, and state. |
| Enter manual fan mode | `raw 0x30 0x30 0x01 0x00` | All-fan control, manual presets, built-in Default/Restore Manual 10% preset, smart auto policy | Sent before setting a manual percentage. Overview/tray "Restore Dell factory fan speed" does not send this command. |
| Set all-fan percentage | `raw 0x30 0x30 0x02 0xff <percent-hex>` | All-fan control, manual presets, tray all-fan menu | `0xff` targets all fans. |
| Set individual fan percentage | `raw 0x30 0x30 0x02 <fan-target-selector> <percent-hex>` | Individual fan controls | Disabled by default. `<fan-target-selector>` selects the fan and is not a speed; verify firmware mapping before enabling. |
| Restore Dell automatic mode | `raw 0x30 0x30 0x01 0x01` | Overview/tray "Restore Dell factory fan speed", Dell Auto preset, emergency temperature protection | Hands fan control back to iDRAC/BMC firmware policy. |

## Percent To Hex

Fan percentages are validated from 0-100 and converted to a one-byte hex value:

| Percent | Hex |
| --- | --- |
| 0% | `0x00` |
| 10% | `0x0a` |
| 20% | `0x14` |
| 35% | `0x23` |
| 42% | `0x2a` |
| 50% | `0x32` |
| 100% | `0x64` |

The app does not allow fan percentages below 0 or above 100.

## Common Execution Sequences

### Connect And Start Polling

```text
mc info
sdr elist
```

After connection succeeds, the app starts persistent sensor polling. The Overview page shows the last polling time and the read duration.

### Refresh Sensors

```text
sdr elist
```

Refresh updates the sensor table, temperature board, fan RPM board, power and state board, and interactive chart data.

### Set All Fans To 20%

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff 0x14
```

The first command enters manual mode. The second command sets the all-fan target.

### Built-In Restore Manual Preset

The built-in `restore-manual` preset is manual mode plus all fans at 10%. It runs only when the user switches to that preset; it is no longer the behavior of Overview/tray "Restore Dell factory fan speed":

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff 0x0a
```

### Restore Dell Automatic Mode

```text
raw 0x30 0x30 0x01 0x01
```

This command lets the iDRAC/BMC firmware policy take over fan control. Overview Quick Actions and the tray menu item "Restore Dell factory fan speed" execute this command. On success, the UI shows Dell automatic mode; on failure, the real IPMI error is shown and the app does not pretend restoration succeeded.

### Smart Auto Policy Tick

Each tick executes:

```text
sdr elist
```

The app then parses CPU temperature:

- Less than or equal to target temperature: use smart auto minimum fan percent.
- Greater than or equal to high temperature: use smart auto maximum fan percent.
- Between the two thresholds: linearly interpolate.
- At or above emergency auto threshold: send Dell automatic mode command.

When a curve preset is active, the tick still starts with the same `sdr elist` read, but the percentage comes from the saved temperature-fan curve instead of the global target/high thresholds:

- Curve points are stored in `%LocalAppData%\DellR730xdFanControlCenter\settings.json` under `Presets[].CurvePoints`.
- The Fan Control page maintains points with a graphical editor: clicking the curve chart creates a temperature/fan-percent point from that position, the right-side point list can fine-tune values with numeric controls, and existing curve presets can be loaded into the same editor from their preset card.
- Save or switch requires at least 2 points, temperatures from `-40` to `125` C, fan percentages from `0` to `100`, and no duplicate temperatures.
- Below the first point, the first point percent is used. Above the last point, the last point percent is used. Between two points, the app linearly interpolates and rounds to an integer percent by default.
- If the preset enables `SmoothCurve`, the points and endpoints stay the same, but the middle percentage uses a smooth transition before rounding. The command still sends one final `<calculated-percent-hex>` all-fan percentage.
- At or above the emergency auto threshold, the curve result is not sent; the app sends Dell automatic mode first.

If the emergency threshold is not reached, the app sends:

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff <calculated-percent-hex>
```

If the emergency threshold is reached, the app sends:

```text
raw 0x30 0x30 0x01 0x01
```

## Individual Fan Target Selectors

The `0x00-0x05` values below are fan target selectors after the raw command prefix. They choose Fan 1-6 and are not speed percentages. The speed percentage is the final `<percent-hex>` argument; for example, `0% = 0x00` in the percentage table applies only to `<percent-hex>` and does not mean Fan 1 target selector `0x00` stops the fan.

| Target | Target selector |
| --- | --- |
| All fans | `0xff` |
| Fan 1 | `0x00` |
| Fan 2 | `0x01` |
| Fan 3 | `0x02` |
| Fan 4 | `0x03` |
| Fan 5 | `0x04` |
| Fan 6 | `0x05` |

Individual target-selector control is implemented but disabled by default in the UI. On the locally tested R730xd/iDRAC 2.82, target selector `0x00` was not `0%` fan speed and did not isolate Fan 1; it caused all fans to ramp high. Treat individual target selectors as firmware-dependent.

## Polling Behavior

After connection succeeds, the app starts persistent sensor polling. The default and minimum saved interval is 15 seconds. Important details:

- 15 seconds is the current safety floor, not a guarantee that every BMC returns complete SDR data within 15 seconds; use the Overview page and runtime log for actual timing.
- If the previous SDR read is still running, the current tick is skipped with a visible warning.
- If another IPMI command is running, the current tick is skipped to avoid overlapping commands.
- If one read takes longer than the configured polling interval, the app recommends a higher interval.
- If polling fails, the app stops polling, marks the state as disconnected, and shows the failure reason.
- Older settings files with values below 15 seconds do not auto-connect. Saving below 15 seconds fails visibly instead of silently changing the value.

A good polling interval is slightly higher than the observed SDR read duration. The locally observed R730xd/iDRAC 2.82 takes about 11-13 seconds for a full SDR read, so the default is 15 seconds; if your environment is slower, raise the interval to the UI recommendation.

## SDR Parsing Rules

The app parses `sdr elist` output line by line:

- Splits each line by `|`.
- Requires at least 3 fields.
- For common `sdr elist` output, status comes from field 3 and reading text from field 5.
- If the reading starts with a number, it is split into `Value`, `Unit`, and `NumericValue`.
- Numeric parsing uses invariant culture and supports integers, decimals, and negative numbers.
- Readings without a numeric prefix keep the raw text and do not produce `NumericValue`.
- If no sensor rows are produced, the app raises an error.

## Sensor Classification Rules

Charts and dashboard cards classify sensors as follows:

- Temperature: unit contains `degrees C`, or name contains `Temp`.
- Fan: name starts with `Fan` and unit contains `RPM`.
- Performance: name contains `Usage`, or unit contains `percent`.
- Power: unit contains `Watts`, or name contains `Pwr Consumption`.
- Voltage: unit contains `Volts` and has a numeric value.
- Current: unit contains `Amps` and has a numeric value.
- Health state: name contains `Redundancy`, `Battery`, `Intrusion`, or `Power Optimized`.
- Other non-numeric sensors are treated as state sensors.
- Other numeric sensors are treated as other numeric sensors.

CPU temperature lookup:

1. Prefer temperature rows whose key contains `CPU`.
2. If no CPU-named rows exist, use all temperature sensors.
3. Return the highest numeric value among candidates.
4. If no candidate temperature rows exist, raise an error.

## Tested Local BMC

- Host: `192.168.1.73`
- User: `root`
- Firmware observed by `mc info`: `2.82`
- Sensors observed: Fan1-Fan6 RPM, Inlet Temp, Exhaust Temp, CPU-related Temp rows, power, voltage, redundancy, drive and cable presence.
- All-fan 20% command: succeeded.
- Dell automatic mode reset: succeeded.
- Individual Fan 1 target selector `0x00`: command accepted, but it was not `0%` fan speed and behavior was not individual; all fans ramped high.

The password is not stored in this repository.

## Manual Validation Commands

You can validate commands manually in PowerShell. Example:

```powershell
$env:IPMI_PASSWORD = "<your-password>"
.\BundledTools\ipmitool\ipmitool.exe -I lanplus -H <host> -U <user> -E mc info
.\BundledTools\ipmitool\ipmitool.exe -I lanplus -H <host> -U <user> -E sdr elist
.\BundledTools\ipmitool\ipmitool.exe -I lanplus -H <host> -U <user> -E raw 0x30 0x30 0x01 0x01
```

Do not write real passwords into scripts or commit them to the repository.

## Common Failure Reasons

### `ipmitool.exe` is missing

The output directory is missing `BundledTools\ipmitool\ipmitool.exe`. Rebuild or inspect the published package contents.

### Authentication fails

Check host, username, password, iDRAC user state, and network reachability.

### Raw command fails

Common causes include insufficient account privileges, disabled iDRAC capability, unsupported OEM command, or a target machine that is not a compatible R730xd environment.

### SDR polling is slow or RMCP+ sessions fail

iDRAC may take several to more than ten seconds to return full SDR data. Too-low polling keeps opening IPMI v2/RMCP+ sessions and can lead to `Unable to establish IPMI v2 / RMCP+ session`. Raise polling seconds to the UI recommendation or higher; the app does not retry automatically or pretend the failed poll succeeded.

### Sensor classification is unexpected

Classification depends on BMC-returned names and units. Different firmware or language environments can change names. If classification needs improvement, update both code and documentation with the new matching rules.
