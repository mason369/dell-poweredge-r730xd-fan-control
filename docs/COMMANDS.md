# IPMI 命令参考

语言：中文 | [English](COMMANDS.en-US.md)

本应用会直接执行 IPMI 命令并显示真实失败原因。命令失败时不会静默切换到其他后端。

应用已在 `BundledTools/ipmitool` 内置 `ipmitool.exe` 及所需 Cygwin DLL。运行时会从应用输出目录解析内置工具路径。

## 必要 iDRAC 设置

- 启用 IPMI over LAN。
- 使用具备 OEM raw 命令权限的账号。
- 确保运行本软件的 Windows 主机可以访问 iDRAC。

## 命令

```text
mc info
sdr elist
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff <percent-hex>
raw 0x30 0x30 0x02 <fan-target-byte> <percent-hex>
raw 0x30 0x30 0x01 0x01
```

## 轮询

连接成功后，应用会启动持续传感器轮询。最短间隔为 1 秒。如果上一次 SDR 读取尚未完成，下一次 tick 会跳过，避免同时发起多条 IPMI 命令。

## 本机默认还原

本机默认还原动作是手动模式 + 全部风扇 10%：

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff 0x0a
```

## 目标字节

- 全部风扇：`0xff`
- Fan 1：`0x00`
- Fan 2：`0x01`
- Fan 3：`0x02`
- Fan 4：`0x03`
- Fan 5：`0x04`
- Fan 6：`0x05`

UI 已实现单风扇 target，但默认关闭。本机 R730xd/iDRAC 2.82 实测 `0x00` 未能单独控制 Fan 1，而是导致全部风扇高转。请将单风扇 target 视为固件相关能力。

## 本机已测试 BMC

- Host：`192.168.1.73`
- User：`root`
- `mc info` 观察到的固件版本：`2.82`
- 观察到的传感器：Fan1-Fan6 RPM、Inlet Temp、Exhaust Temp、CPU 相关 Temp 行、功耗、电压、冗余、硬盘与线缆在位状态。
- 全部风扇 20% 命令：成功。
- Dell 自动模式还原：成功。
- Fan 1 target byte `0x00`：命令接受，但不是单风扇行为，而是全部风扇高转。

密码未写入本仓库。
