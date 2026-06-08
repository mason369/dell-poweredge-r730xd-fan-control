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

UI logs record:

- The `ipmitool` path and arguments.
- Exit code.
- Success or failure state.
- Command duration.
- stdout/stderr summary on failure.

UI logs do not record the password.

## Local Logs

The in-app Recent Log keeps the latest 80 runtime entries. If an unhandled exception happens during startup, the app writes:

```text
%LocalAppData%\DellR730xdFanControlCenter\startup-error.log
```

Before filing an issue, sharing a screenshot, or attaching logs, check whether they include your iDRAC address, username, host name, paths, or other environment details you do not want public. Do not publish iDRAC passwords, VPN addresses, internal network ranges, security policies, asset numbers, or other sensitive data.

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

When workload is unknown, ambient temperature is high, the chassis is drive-dense, sensors are abnormal, or you cannot keep watching the machine, use Dell automatic mode.

## Individual Fan Raw Target Risk

Individual fan control is disabled by default. On the locally tested R730xd/iDRAC 2.82, Fan 1 target byte `0x00` did not isolate Fan 1 and instead ramped all fans high. Different firmware may map targets differently. Before enabling individual fan control, confirm:

- Your iDRAC firmware accepts the raw command.
- Target bytes map to the expected physical fans.
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

The default and minimum saved polling interval is 15 seconds. Older settings below 15 seconds pause automatic connection, and saving a new value below 15 seconds fails with a visible reason. Too-low polling keeps opening IPMI v2/RMCP+ sessions and may cause iDRAC to reject new sessions with `Unable to establish IPMI v2 / RMCP+ session`.

At the emergency temperature threshold, the app sends the Dell automatic mode command so the BMC can take over. That action still depends on successful IPMI command execution.

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
