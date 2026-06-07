# IPMI Command Reference / IPMI 命令参考

This app intentionally executes direct IPMI commands and reports failures exactly. It does not silently switch to a different backend when a command fails.

本应用会直接执行 IPMI 命令并显示真实失败原因。命令失败时不会静默切换到其他后端。

## Required iDRAC Settings / 必要 iDRAC 设置

- Enable IPMI over LAN.
- Use an account with enough privilege for OEM raw commands.
- Keep iDRAC reachable from the Windows machine running the app.

- 启用 IPMI over LAN。
- 使用具备 OEM raw 命令权限的账号。
- 确保运行本软件的 Windows 主机可以访问 iDRAC。

## Commands / 命令

```text
mc info
sdr elist
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff <percent-hex>
raw 0x30 0x30 0x02 <fan-target-byte> <percent-hex>
raw 0x30 0x30 0x01 0x01
```

## Local Default Restore / 本机默认还原

The local default restore action is manual mode plus all fans at 10%:

本机默认还原动作是手动模式 + 全部风扇 10%：

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff 0x0a
```

## Target Bytes / 目标字节

- All fans: `0xff`
- Fan 1: `0x00`
- Fan 2: `0x01`
- Fan 3: `0x02`
- Fan 4: `0x03`
- Fan 5: `0x04`
- Fan 6: `0x05`

Individual targets are implemented but disabled by default in the UI. On the locally tested R730xd/iDRAC 2.82, `0x00` did not isolate Fan 1 and caused all fans to ramp high. Treat individual targets as firmware-dependent.

UI 已实现单风扇 target，但默认关闭。本机 R730xd/iDRAC 2.82 实测 `0x00` 未能单独控制 Fan 1，而是导致全部风扇高转。请将单风扇 target 视为固件相关能力。

## Tested Local BMC / 本机已测试 BMC

- Host: `192.168.1.73`
- User: `root`
- Firmware observed by `mc info`: `2.82`
- Sensors observed: Fan1-Fan6 RPM, Inlet Temp, Exhaust Temp, CPU-related Temp rows, power, voltage, redundancy, drive and cable presence.
- All-fan 20% command: succeeded.
- Dell automatic mode reset: succeeded.
- Individual Fan 1 target byte `0x00`: command accepted, but behavior was not individual; all fans ramped high.

The password is not stored in this repository.

密码未写入本仓库。
