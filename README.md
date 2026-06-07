<p align="center">
  <img src="Assets/Logo.svg" width="128" alt="R730XD Fan Control Center logo" />
</p>

# Dell R730xd iDRAC Fan Control Center / R730XD 智控风扇中心

Windows WinUI 3 desktop app for managing Dell PowerEdge R730xd fan speed through iDRAC/IPMI. It provides all-fan control, individual fan target control, Dell automatic mode reset, smart temperature-based automation, sensor monitoring, logs, and tray quick actions.

面向 Dell PowerEdge R730xd 的 Windows WinUI 3 桌面工具，通过 iDRAC/IPMI 控制服务器风扇。支持全部风扇、单风扇目标、Dell 自动模式恢复、按温度自动调速、传感器监控、日志以及右下角托盘快捷控制。

## Highlights / 功能亮点

- Modern WinUI 3 interface with light/dark/system theme support.
- Full fan speed control from 0-100 percent.
- Individual Fan 1-6 raw target control, disabled by default until the firmware mapping is verified.
- Restore default local mode: manual all-fan 10%.
- Dell automatic fan mode remains available as a separate action.
- Smart auto policy: reads BMC sensors, calculates CPU temperature, and adjusts all fans.
- Tray behavior: close button minimizes to tray; right-click tray menu provides quick fan presets, reset auto mode, settings, restore, and exit.
- iDRAC web console shortcut.
- SDR sensor table for fan RPM, temperatures, power, voltage, and platform status.
- Explicit failures: missing `ipmitool.exe`, authentication errors, iDRAC privilege errors, and unsupported firmware commands are shown directly.
- Password can be stored locally with Windows DPAPI; it is not committed to the repository.

- 现代 WinUI 3 界面，支持浅色、深色、跟随系统主题。
- 全部风扇 0-100% 百分比控制。
- Fan 1-6 单风扇 raw target 控制，默认关闭，需确认固件映射后再启用。
- 还原本机默认模式：手动模式 + 全部风扇 10%。
- Dell 自动风扇模式保留为单独操作。
- 软件恒温策略：读取 BMC 传感器，根据 CPU 温度自动调整全部风扇。
- 托盘行为：点击关闭最小化到托盘；右键托盘图标可快速设置风扇、恢复自动模式、进入设置、打开主窗口和退出。
- iDRAC Web 控制台快捷入口。
- SDR 传感器表格展示风扇 RPM、温度、功耗、电压和平台状态。
- 失败显式展示：缺少 `ipmitool.exe`、认证失败、iDRAC 权限不足、固件不支持 raw 命令都会直接报错。
- 密码可用 Windows DPAPI 本机加密保存，不会提交到仓库。

## Screens / 界面

The app is organized into four views:

应用包含四个主要页面：

- Overview / 总览: CPU temperature, fan RPM summary, quick actions, recent logs.
- Fan Control / 风扇: presets, all-fan control, Fan 1-6 individual control, smart auto thresholds.
- Sensors / 传感器: complete `ipmitool sdr elist` table.
- Settings / 设置: iDRAC credentials, `ipmitool.exe` path, tray behavior, fan count, timeout, theme, auto policy range.

## Requirements / 运行要求

- Windows 10 2004 / build 19041 or newer.
- .NET 8 desktop runtime or a self-contained published build.
- Dell PowerEdge R730xd with reachable iDRAC/BMC.
- IPMI over LAN enabled in iDRAC.
- `ipmitool.exe` installed locally.
- iDRAC user with enough privilege to send OEM raw IPMI commands.

- Windows 10 2004 / build 19041 或更新版本。
- .NET 8 Desktop Runtime，或使用自包含发布包。
- 可访问的 Dell PowerEdge R730xd iDRAC/BMC。
- iDRAC 中已启用 IPMI over LAN。
- 本机已安装 `ipmitool.exe`。
- iDRAC 用户具备发送 OEM raw IPMI 命令的权限。

## Build / 构建

```powershell
cd C:\DellR730xdFanControlCenter
dotnet build .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
```

## Run / 运行

```powershell
cd C:\DellR730xdFanControlCenter
dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
```

The first run opens the Settings page with default host `192.168.1.73`, username `root`, detected local `ipmitool.exe` path, and default restore speed 10%. Enter the password locally before sending commands.

首次运行可在设置页看到默认主机 `192.168.1.73`、用户名 `root`、本机已检测到的 `ipmitool.exe` 路径，以及默认还原速度 10%。发送命令前请在本机输入密码。

## IPMI Commands / IPMI 命令

The command runner uses `IPMI_PASSWORD` plus `ipmitool -E`, so the password is not placed in command-line arguments.

命令执行层使用 `IPMI_PASSWORD` 环境变量和 `ipmitool -E`，不会把密码放入命令行参数。

Core operations:

核心操作：

```text
Test connection:
ipmitool -I lanplus -H <host> -U <user> -E mc info

Read sensors:
ipmitool -I lanplus -H <host> -U <user> -E sdr elist

Enable manual fan mode:
ipmitool -I lanplus -H <host> -U <user> -E raw 0x30 0x30 0x01 0x00

Set all fans:
ipmitool -I lanplus -H <host> -U <user> -E raw 0x30 0x30 0x02 0xff 0x14

Reset Dell automatic fan mode:
ipmitool -I lanplus -H <host> -U <user> -E raw 0x30 0x30 0x01 0x01
```

Individual fan mode sends target bytes `0x00` through `0x05` for Fan 1-6. It is disabled by default because the local R730xd/iDRAC 2.82 test showed `0x00` did not isolate Fan 1 and instead ramped all fans. Enable this only after verifying your firmware behavior.

单风扇模式使用 `0x00` 到 `0x05` 作为 Fan 1-6 的 target byte。该功能默认关闭，因为本机 R730xd/iDRAC 2.82 实测 `0x00` 没有单独控制 Fan 1，而是让全部风扇升到高转。请确认你的固件行为后再启用。

## Safety / 安全提醒

Fan control can affect server stability and hardware temperature. Watch CPU, inlet, exhaust, storage, PCIe, and power sensors after changing fan speed. If temperature rises unexpectedly, use **Dell Auto Mode** immediately.

风扇控制会影响服务器稳定性和硬件温度。调整风扇后请持续观察 CPU、进风、排风、硬盘、PCIe 和电源传感器。若温度异常升高，请立即使用 **Dell 自动模式**。

## Repository Topics / 仓库标签

`dell`, `poweredge`, `r730xd`, `idrac`, `ipmi`, `ipmitool`, `fan-control`, `server-management`, `homelab`, `winui`, `windows`, `dotnet`
