# Security

Language: [中文](SECURITY.md) | English

This project directly controls Dell R730xd fans through iDRAC/IPMI. Its security boundary includes both credential handling and hardware operational safety. Treat it as a local management tool that can affect server temperature and stability, not as a read-only monitoring panel.

## Security Model

- The app runs in the current Windows user session.
- The app connects to the user-configured iDRAC/BMC address.
- The app executes IPMI over LAN commands through bundled `ipmitool.exe`.
- The app does not commit passwords to the repository.
- The app does not place passwords in command-line arguments.
- The app does not silently switch to another backend or record failed work as successful when an IPMI command fails.
- Real failure reasons are shown in the UI status bar and recent log.

## Credential Storage

By default, the password exists only in the running process UI field and memory. Users can enable "Remember password with DPAPI". When enabled, the app encrypts the password with Windows DPAPI using `DataProtectionScope.CurrentUser` and writes the protected value to the local settings file:

```text
%LocalAppData%\DellR730xdFanControlCenter\settings.json
```

The DPAPI value is bound to the current Windows user context. Copying it to another user or machine will generally make it undecryptable. Deleting the local settings file removes the saved host, username, protected password, presets, and UI settings.

Settings are saved through a same-directory temporary-file replacement. The complete JSON is written to a unique `.settings-<GUID>.tmp`, flushed to disk, and only then replaces `settings.json`. A write, flush, or replacement error stops the save before the active settings file is truncated, and the app does not present that save as successful. Power loss or forced termination can still leave a temporary file that is not the active state, but startup never loads it as settings. When startup encounters corrupt JSON, an empty settings document, or an invalid preset, the app preserves the original file, stays on Settings, shows and logs the root cause, and does not start polling or an automatic fan policy. This protects active-file integrity but does not provide settings version history or automatic backups; back up `settings.json` yourself before deleting or replacing it.

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

If runtime log writing fails, the app does not show the operation as successful. Button-triggered user commands, sensor refreshes, software-auto ticks, polling recovery, preset changes, and settings saves wait for the corresponding success or terminal record before showing success; when writing fails, the status bar plus Recent Log show "Runtime log write failed" with the underlying exception. Each `IpmiCommand/CommandCompleted` uses the child process's real start/finish timestamps and enters the durable queue before the Recent Log UI update, preventing UI queue delay from creating a false concurrency interpretation. If the underlying IPMI command already ran, the app does not roll back hardware state, but it also does not show success. If an unhandled exception happens during startup or a duplicate launch is blocked, the app writes:

```text
%LocalAppData%\DellR730xdFanControlCenter\startup-error.log
```

WebView2 chart runtime user data is written to:

```text
%LocalAppData%\DellR730xdFanControlCenter\WebView2
```

This directory is for WebView2 local runtime state and does not contain the iDRAC password. Creation failure shows the real chart load error instead of writing beside the release extraction directory as `DellR730xdFanControlCenter.exe.WebView2`.

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

When workload is unknown, ambient temperature is high, the chassis is drive-dense, sensors are abnormal, or you cannot keep watching the machine, use Dell automatic mode. Overview Quick Actions and the tray menu item "Restore Dell factory fan speed" stop any running software auto policy before waiting for the IPMI lock, then send the Dell automatic mode command and hand fan control back to iDRAC/BMC firmware; they are not manual 10%.

Manual percentage control is a two-command sequence: enter manual mode, then set the target percentage. Only when the first command is confirmed successful and the second fails does the app immediately send one `raw 0x30 0x30 0x01 0x01` to try to return control to Dell firmware. It does not retry the failed percentage command. After successful recovery, the user's request still appears as failed while the UI and `LastRunningPresetId` align to `dell-auto`. If recovery also fails, the log and error bar retain both the percentage failure and recovery failure, and the possibility that the BMC remains in manual mode must be treated as a hardware risk requiring manual inspection. If entering manual mode itself fails, the app sends no extra recovery command because a hardware mode change was not confirmed.

Software automatic policies use that two-command sequence only for the first tick or when the current BMC manual state has not yet been confirmed. After the first target succeeds, later percentage changes send only the target raw command and do not re-enter manual mode. If that later command fails, the app does not send Dell Auto, does not change the current software automatic mode, and does not send the same failed percentage again. It keeps the last confirmed percentage while scheduled SDR reads and temperature/power calculations continue. A new target command is allowed only after the computed percentage changes, a later success clears the rejected target, or the user starts a new control intent. This prevents a failed target from becoming a command loop and prevents the UI from claiming a mode change that was never attempted; the hardware still holds its last confirmed state, so continue watching RPM, temperatures, and iDRAC alerts.

The hero live summary and Overview top hardware summary are display-only and use the latest successful SDR refresh. The hero "Live temperature" value is the current average of temperature sensor readings, not a historical maximum, and it does not drive smart auto or emergency automatic protection; actual control still uses the CPU/max temperature semantics described in the smart auto policy section. The detail rows under each hero value show every concrete sensor item returned for that category, but they still do not replace the complete Sensors table. Before the first refresh, or when one sensor category is missing, the hero and Overview electrical summaries show "Waiting" or no-reading text. Hero and hardware-card colors are UI hints: normal green, near-risk yellow, clear deviation orange, and danger red. These thresholds do not change fan commands and do not replace iDRAC alerts, the full sensor table, or emergency automatic protection. Fan-card rotation is only a visual rhythm derived from current RPM readings, not part of the control loop. For fan changes or troubleshooting, still check the Temperature Board, Fan RPM Board, Power & Health board, and iDRAC alerts.

## Sensor Icon And Animation Boundary

Version 1.0.14 uses semantic icons and Windows Composition animation in the Overview Temperature Board, Fan RPM Board, and Power & Health board. Icons cover temperature, CPU / MEM / IO / SYS usage, power, voltage, current, intrusion, fan/power redundancy, CMOS / ROMB / BBU battery, drives, RAID/PERC controllers, cache, USB over-current, and power policy. Normal, information, inactive, unavailable, warning, critical, and stale also have non-color badges. Screen-reader names include the value, semantic state, and disconnected state.

- The fan card keeps the original four-blade cross shape and rotates around its center. Navigation uses a static circular housing with four curved blades, so it no longer reads as a diagonal X and does not indicate live RPM. Zero RPM stops; only positive RPM from the latest successful SDR result starts dashboard rotation. RPM maps linearly to rotations per second, with a low-end single-rotation period cap of `5.2 s` and `0.11 s` at 18000 RPM or above, so 3600 RPM is slightly faster than 3480 RPM. Speed changes update playback rate while preserving the current phase.
- Temperature and CPU / MEM / IO / SYS levels, plus the known `Voltage N` gauge, make one transition only when a real new sample changes; the app does not invent intermediate readings between polls. The trusted known-voltage gauge range is `190..260 V`. A vendor sensor recognized only through a `Volts`, `Amps`, or `Watts` unit keeps its icon but uses Information and a static neutral presentation; where the voltage gauge applies, its needle sits at the midpoint. The app does not assume a 230 V range or current/power thresholds. An unrecognized real type with `ok` status uses the generic normal icon, while explicit abnormal status still takes priority.
- The display layer accepts only real valid SDR rows. Numeric and discrete rows with `status=ok` remain, including real `0%`; rows with `ns`, `na`, `No Reading`, `Disabled`, `Not Available`, `N/A`, `Unknown`, or non-finite numbers do not enter boards, sensor details, or charts. Real warning/critical/failure/fault/degraded/lost status remains even without a numeric value. BBU capacity, cache hit rate, RAID performance, and other metrics appear only when the current `sdr elist` contains a valid row; missing metrics do not get defaults, placeholder cards, or substitute readings.
- Only positive current flows and only positive power shows activity. A normal health state is static; warning and critical states may pulse. Stopped polling, a disconnected or stale snapshot, 0 RPM, disabled state, or no reading stops animation and leaves a recognizable clock/state shape. Windows animations disabled, high contrast, or a window hidden to the tray also produces a static display; motion resumes after the window is visible and animations are allowed. High contrast uses the system foreground color at full opacity.
- The parser checks exact raw SDR keys before unit and real-name fallback. Abnormal status codes take priority, unknown ranges are not guessed, and an unregistered valid name keeps the BMC text. A Dispatcher callback or Composition update failure raises `VisualUpdateFailed`; MainPage routes it through `ShowFailure`, opens the error bar, and writes the runtime log instead of marking animation successful. Without an error handler the control throws explicitly; when unhandled, the global exception logger writes `%LocalAppData%\DellR730xdFanControlCenter\startup-error.log` and the process may terminate. Filtering occurs on display copies of `Sensors`; it does not change the raw SDR collection used by automatic fan policies, IPMI commands, Dell raw fan control, polling, or settings.

The display layer is implemented in `Models/SensorReadingAvailability.cs`, `Models/DashboardSensorPresentation.cs`, `Models/DashboardSnapshotFreshness.cs`, `Controls/DashboardSensorIcon.xaml(.cs)`, and `MainPage.xaml`. Run `dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release` and `dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64` to verify availability, classification/boundary rules, and the x64 Release build. These animations are not a fan-control loop or safety alarm; interpret iDRAC alerts together with the valid sensor details shown by the app. BMC/firmware naming differences can place a sensor in unit-only or generic handling, and update timing is limited by the duration of a full `sdr elist` read. Current local verification does not claim manual coverage of every iDRAC firmware, Windows display scale, or high-DPI combination.

## Individual Fan Target Selector Risk

Individual fan control is disabled by default. In individual-fan commands, `0x00-0x05` are firmware raw-command target selectors used to choose a fan; they are not fan speeds, and `0x00` is not `0%` fan speed. On the locally tested R730xd/iDRAC 2.82, Fan 1 target selector `0x00` did not isolate Fan 1 and instead ramped all fans high. Different firmware may map targets differently. Version 1.0.15 replaces the full-width yellow explanation with a compact locked/high-risk state, but the risk icon ToolTip and screen-reader HelpText retain the complete selector warning. This reduces layout use; it does not reduce hardware risk. Before enabling individual fan control, confirm:

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

The default polling interval is 1 second, and saved values of 1 second or higher are allowed. This value is only the polling tick cadence; it does not guarantee that iDRAC can return a complete SDR read at the same frequency. The app allows only one IPMI operation at a time; when a previous SDR read, smart auto tick, or another IPMI command is still running, the new background tick is skipped and no new `ipmitool` process or RMCP+ session is started. When starting smart auto or switching to a curve preset, the first tick must succeed before the background timer starts; first-tick failure, a busy IPMI lock, or a terminal runtime-log write failure stops startup, shows the root cause, and does not queue to increase handling time. While smart auto or curve auto is running, regular sensor polling does not independently start a second `sdr elist` path; the auto-policy tick's SDR result refreshes the UI and history points. An unchanged target records `SkipUnchangedFanPercent`; a target matching the rejected percentage for the same mode records `SkipPreviouslyFailedFanPercent`. Neither skip sends a raw fan command, but both retain the real SDR sensor, history, and chart refresh; a failed skip-record write still fails the tick. After the automatic timer is running, SDR reading, CPU/power parsing, or normal post-command confirmation-refresh failure before a new target is confirmed is shown and logged without clearing the policy or saved running preset; the next scheduled tick performs a real SDR read. A later percentage raw failure also keeps the current automatic mode but suppresses that target until the calculation changes. If real background polling returns `Unable to establish IPMI v2 / RMCP+ session`, times out, or exits non-zero, the app stops the current poll and shows the failure reason, then keeps running real reconnect attempts (`mc info` + `sdr elist`) at the configured interval; polling resumes only after one reconnect succeeds. Reconnect failures remain visible and disconnected without silent degradation, false success, or history without a real SDR source. Dell fan-control raw commands do not automatically reconnect or retry, and fail immediately on non-zero exit.

At the emergency temperature threshold, the app sends the Dell automatic mode command so the BMC can take over. That action still depends on successful IPMI command execution. After the command succeeds, the app stops the software auto timer, clears active curve/smart-auto state, persists Dell Auto as the running mode, and shows a visible warning. If the following sensor confirmation refresh fails, the refresh failure is shown and logged, but the already confirmed Dell Auto mode is not changed to Manual.

## Curve Preset Risk

Curve presets are a software auto rule; they are not written into iDRAC/BMC firmware. Temperature curves read SDR on each software auto tick, parse CPU temperature, interpolate the user-saved temperature-fan points, and send an all-fan percentage command. Power curves also read SDR, but use the power sensor whose unit contains `Watts` or whose key contains `Pwr Consumption` as the curve input; they still parse CPU temperature and check the emergency auto threshold first. Points are maintained from the Fan Control page with chart clicks and numeric controls. In 1.0.15, hover values stay in a fixed row outside the plot, crosshairs are non-hit-testable, and title instructions plus ASCII previews are removed; invalid points still show their real cause in that row. Smooth curve remains software-side interpolation and does not program a firmware fan curve into iDRAC/BMC.

Safety boundaries for curve presets:

- Curve points control only the all-fan percentage, not individual fans.
- Save or switch validates at least 2 points. Temperature curves validate temperatures from `-40` to `125` C, percentages from `0` to `100`, and no duplicate temperatures; power curves validate power from `0` to `1200` W, percentages from `0` to `100`, and no duplicate power points.
- The point list and `SmoothCurve` setting are stored in local `settings.json`. Deleting, editing, or saving a preset affects later software tick calculations, but it does not change server firmware configuration.
- Input values below the first point use the first point; values above the last point use the last point. If the final point is too low, sustained high load can still be under-cooled.
- Power curves do not replace temperature protection. Low power does not guarantee safe local hardware temperatures; drives, PCIe cards, memory, inlet, or exhaust temperatures can still rise. If the current SDR result has no power reading, the power curve shows the failure and stops that tick instead of continuing with a default or previous power value.
- Smooth mode uses monotone cubic Hermite interpolation with Fritsch-Carlson tangent constraints for both temperature and power curves. It passes through control points, cannot overshoot adjacent endpoint percentages, preserves flat intervals, and reduces to linear interpolation for two points; preview and automation share the evaluator. Final percentages still use existing rounding and the `0-100%` clamp. This does not change emergency thresholds or add safety margin to points that are configured too low.
- At the emergency temperature threshold, the app first sends Dell automatic mode. That protection still depends on SDR reads, CPU temperature detection, network availability, and successful IPMI command execution. After the Dell Auto command succeeds, the app stops the software auto timer, clears automatic-mode state, and persists Dell Auto as the running mode; a later confirmation-refresh failure reports the refresh failure without overwriting the confirmed Dell Auto state.
- Manual all-fan control, individual fan control, the built-in Restore Manual preset, Dell Auto, Overview/tray Restore Dell factory fan speed, and Stop Auto all clear the active curve state. User-triggered manual and Dell Auto commands stop the software auto policy before waiting for the IPMI lock, so an older queued or background automatic tick cannot overwrite the newer user command. Deleting the running curve preset, Stop Auto, first-tick full manual-mode/percentage failure, runtime-log failure, and emergency Dell Auto stop software auto and clear its mode state. An already-running tick retains automatic state after pre-target SDR reading or processing failure; a later percentage failure after the first confirmed target also retains automatic state and suppresses that target instead of retrying it in a loop.

Validate curves first under low load and with someone watching the machine. Do not set the final temperature point below realistic high-load temperatures, and do not set the final fan percentage too low. If charts, sensor status, or iDRAC alerts look abnormal, restore Dell automatic mode immediately.

## Supply Chain And Bundled Assets

The repository includes bundled `ipmitool.exe`, Cygwin/GCC/OpenSSL/zlib DLLs, and ECharts frontend assets. When publishing or repackaging, verify:

- Bundled binaries come from trusted sources.
- Files have not been replaced or tampered with.
- `LICENSE` and `THIRD_PARTY_NOTICES.md` remain with source and release packages.
- Versions, SHA-256 hashes, and source notes in `BundledTools/ipmitool/README.md` match the actual files.
- `BundledTools/ipmitool/LICENSES/**` remains with the bundled command runtime, especially the ipmitool, Cygwin, OpenSSL, GCC runtime, and zlib notices.
- `Assets/Charts/echarts.LICENSE.txt` and `Assets/Charts/echarts.NOTICE.txt` remain with the chart assets.
- The release package includes `BundledTools/ipmitool/**` and `Assets/Charts/**`.
- Release zip, verification, signed-MSIX output, and recursive-cleanup paths must resolve under the repository root. An out-of-root path makes the script fail before creation, compression, signing, or deletion.

When replacing `ipmitool.exe`, Cygwin/GCC/OpenSSL/zlib DLLs, or chart assets, document the source, version, SHA-256 hash, license files, source availability, and compatibility changes in the same change, and update release-script required-file checks. Do not publish a Release package when these notices are missing.

## Failure Handling Principles

This project requires explicit failure exposure:

- Missing bundled `ipmitool.exe` raises an error.
- Empty iDRAC host, username, or password raises an error.
- Command timeout kills the process and shows the timeout reason; if the timeout happened during background sensor polling, the app then keeps running real reconnect attempts until polling is restored or the user cancels polling.
- A non-zero `ipmitool` exit immediately shows stdout/stderr from that execution; Dell fan-control raw commands do not use a fixed retry. A failed later automatic target is logged and suppressed so periodic ticks cannot turn it into a repeated raw-command loop.
- Empty SDR output raises an error.
- Missing UI translations raise an error.

Apart from the one-shot Dell automatic safety recovery for residual manual-mode risk described above, do not add silent fallback, default success, swallowed exceptions, or data without a real source that hides real failures unless a future change has an explicit design and documents its trigger conditions, user-visible behavior, and risks.

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
