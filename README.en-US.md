<p align="center">
  <img src="Assets/Logo.svg" width="128" alt="R730XD Smart Fan Center logo" />
</p>

# Dell R730xd iDRAC Fan Control Center

Language: [中文](README.md) | English

Windows WinUI 3 desktop app for managing Dell PowerEdge R730xd fan speed through iDRAC/IPMI. The app defaults to Chinese, and all visible UI strings are wired for i18n with Simplified Chinese and English included.

## Highlights

- Modern WinUI 3 interface with light, dark, and system theme support.
- Full fan speed control from 0-100 percent.
- Local default restore mode: manual mode plus all fans at 10%.
- Dell automatic fan mode remains available as a separate action.
- Individual Fan 1-6 raw target control is implemented but disabled by default until firmware mapping is verified.
- Smart auto policy reads BMC sensors, calculates CPU temperature, and adjusts all fans.
- Persistent polling starts after connect or save succeeds, with a minimum interval of 1 second.
- Bundled `ipmitool.exe` and required Cygwin DLLs under `BundledTools/ipmitool`; no external `C:\Program Files\...` tool path is required.
- Tray behavior: close minimizes to tray; right-click the tray icon for quick fan presets, restore manual 10%, settings, restore window, and exit.
- iDRAC web console shortcut.
- Live dashboard cards for every BMC temperature sensor, fan RPM, power, voltage, and platform status.
- Explicit failures: missing bundled `ipmitool.exe`, authentication errors, iDRAC privilege errors, and unsupported firmware commands are shown directly.
- Password can be stored locally with Windows DPAPI; it is not committed to the repository.

## Screens

- Overview: temperature board, fan RPM board, power and health board, quick actions, recent logs.
- Fan Control: presets, all-fan control, Fan 1-6 individual control, smart auto thresholds.
- Sensors: complete `ipmitool sdr elist` table.
- Settings: iDRAC credentials, read-only bundled `ipmitool.exe` path, tray behavior, fan count, polling interval, timeout, theme, interface language, and auto policy range.

## Requirements

- Windows 10 2004 / build 19041 or newer.
- .NET 8 Desktop Runtime, or a self-contained published build.
- Reachable Dell PowerEdge R730xd iDRAC/BMC.
- IPMI over LAN enabled in iDRAC.
- Bundled `BundledTools/ipmitool/ipmitool.exe`.
- iDRAC user with enough privilege to send OEM raw IPMI commands.

## Build

```powershell
cd C:\DellR730xdFanControlCenter
dotnet build .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
```

## Run

```powershell
cd C:\DellR730xdFanControlCenter
dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
```

On first run, or when no password is saved, the app opens Settings automatically. Settings shows default host `192.168.1.73`, username `root`, the bundled `ipmitool.exe` path, default restore speed 10%, and 1 second polling. Enter the account and password, then save to connect. If the password is saved with DPAPI, the app reconnects and starts polling on later launches.

## IPMI Commands

The command runner uses `IPMI_PASSWORD` plus `ipmitool -E`, so the password is not placed in command-line arguments.

See [IPMI Command Reference](docs/COMMANDS.en-US.md).

## Individual Fan Note

Individual fan mode sends target bytes `0x00` through `0x05` for Fan 1-6. It is disabled by default because the local R730xd/iDRAC 2.82 test showed `0x00` did not isolate Fan 1 and instead ramped all fans. Enable this only after verifying your firmware behavior.

## Safety

Fan control can affect server stability and hardware temperature. Watch CPU, inlet, exhaust, storage, PCIe, and power sensors after changing fan speed. If temperature rises unexpectedly, use Dell automatic mode immediately.

## Repository Topics

`dell`, `poweredge`, `r730xd`, `idrac`, `ipmi`, `ipmitool`, `fan-control`, `server-management`, `homelab`, `winui`, `windows`, `dotnet`
