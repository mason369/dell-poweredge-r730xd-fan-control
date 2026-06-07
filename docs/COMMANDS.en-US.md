# IPMI Command Reference

Language: [中文](COMMANDS.md) | English

This app intentionally executes direct IPMI commands and reports failures exactly. It does not silently switch to a different backend when a command fails.

The app bundles `ipmitool.exe` and its required Cygwin DLLs in `BundledTools/ipmitool`. Runtime execution resolves the bundled path from the application output directory.

## Required iDRAC Settings

- Enable IPMI over LAN.
- Use an account with enough privilege for OEM raw commands.
- Keep iDRAC reachable from the Windows machine running the app.

## Commands

```text
mc info
sdr elist
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff <percent-hex>
raw 0x30 0x30 0x02 <fan-target-byte> <percent-hex>
raw 0x30 0x30 0x01 0x01
```

## Polling

After connection succeeds, the app starts persistent sensor polling. The minimum supported interval is 1 second. The overview page shows the last polling time and the read duration. If one SDR read is still running, or another IPMI command is executing, the next tick is skipped with a visible warning instead of launching overlapping IPMI commands. If a read takes longer than the configured polling interval, the app warns that BMC network or iDRAC response latency is fluctuating.

## Local Default Restore

The local default restore action is manual mode plus all fans at 10%:

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff 0x0a
```

## Target Bytes

- All fans: `0xff`
- Fan 1: `0x00`
- Fan 2: `0x01`
- Fan 3: `0x02`
- Fan 4: `0x03`
- Fan 5: `0x04`
- Fan 6: `0x05`

Individual targets are implemented but disabled by default in the UI. On the locally tested R730xd/iDRAC 2.82, `0x00` did not isolate Fan 1 and caused all fans to ramp high. Treat individual targets as firmware-dependent.

## Tested Local BMC

- Host: `192.168.1.73`
- User: `root`
- Firmware observed by `mc info`: `2.82`
- Sensors observed: Fan1-Fan6 RPM, Inlet Temp, Exhaust Temp, CPU-related Temp rows, power, voltage, redundancy, drive and cable presence.
- All-fan 20% command: succeeded.
- Dell automatic mode reset: succeeded.
- Individual Fan 1 target byte `0x00`: command accepted, but behavior was not individual; all fans ramped high.

The password is not stored in this repository.
