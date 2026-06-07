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

Command logs show the tool path, arguments, exit code, and elapsed time. They do not show the password.

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
| Enter manual fan mode | `raw 0x30 0x30 0x01 0x00` | All-fan control, manual presets, restore 10%, smart auto policy | Sent before setting a manual percentage. |
| Set all-fan percentage | `raw 0x30 0x30 0x02 0xff <percent-hex>` | All-fan control, manual presets, tray all-fan menu | `0xff` targets all fans. |
| Set individual fan percentage | `raw 0x30 0x30 0x02 <fan-target-byte> <percent-hex>` | Individual fan controls | Disabled by default; verify firmware mapping before enabling. |
| Restore Dell automatic mode | `raw 0x30 0x30 0x01 0x01` | Dell Auto button/preset, emergency temperature, tray preset | Hands fan control back to iDRAC/BMC firmware policy. |

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

### Local Default Restore

The local default restore action is manual mode plus all fans at 10%:

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff 0x0a
```

### Restore Dell Automatic Mode

```text
raw 0x30 0x30 0x01 0x01
```

This command lets the iDRAC/BMC firmware policy take over fan control.

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

If the emergency threshold is not reached, the app sends:

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff <calculated-percent-hex>
```

If the emergency threshold is reached, the app sends:

```text
raw 0x30 0x30 0x01 0x01
```

## Target Bytes

| Target | Target byte |
| --- | --- |
| All fans | `0xff` |
| Fan 1 | `0x00` |
| Fan 2 | `0x01` |
| Fan 3 | `0x02` |
| Fan 4 | `0x03` |
| Fan 5 | `0x04` |
| Fan 6 | `0x05` |

Individual targets are implemented but disabled by default in the UI. On the locally tested R730xd/iDRAC 2.82, `0x00` did not isolate Fan 1 and instead caused all fans to ramp high. Treat individual targets as firmware-dependent.

## Polling Behavior

After connection succeeds, the app starts persistent sensor polling. The minimum supported interval is 1 second. Important details:

- 1 second is the minimum request cadence, not a guarantee that the BMC can return complete SDR data in 1 second.
- If the previous SDR read is still running, the current tick is skipped with a visible warning.
- If another IPMI command is running, the current tick is skipped to avoid overlapping commands.
- If one read takes longer than the configured polling interval, the app recommends a higher interval.
- If polling fails, the app stops polling, marks the state as disconnected, and shows the failure reason.

A good polling interval is slightly higher than the observed SDR read duration. For example, if a full SDR read takes 3.2 seconds, use 5 seconds or higher.

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
- Individual Fan 1 target byte `0x00`: command accepted, but behavior was not individual; all fans ramped high.

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

### SDR polling is slow

iDRAC may take several seconds to return full SDR data. Increase the polling interval to avoid repeated skipped ticks.

### Sensor classification is unexpected

Classification depends on BMC-returned names and units. Different firmware or language environments can change names. If classification needs improvement, update both code and documentation with the new matching rules.
