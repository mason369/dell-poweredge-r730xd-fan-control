# IPMI 命令参考

语言：中文 | [English](COMMANDS.en-US.md)

本应用通过内置 `ipmitool.exe` 直接执行 iDRAC/IPMI 命令，并把真实失败原因显示给用户。命令失败时不会静默切换到其他后端，也不会构造假数据或默认成功。

应用已在 `BundledTools/ipmitool` 内置 `ipmitool.exe` 及所需 Cygwin DLL。运行时会从应用输出目录解析内置工具路径：

```text
BundledTools\ipmitool\ipmitool.exe
```

## 命令执行格式

所有 IPMI 命令都会被包装成以下形式：

```text
ipmitool -I lanplus -H <host> -U <user> -E <ipmi-arguments>
```

说明：

- `-I lanplus`：使用 IPMI 2.0 RMCP+ LAN 接口。
- `-H <host>`：设置页中的 iDRAC/BMC 地址。
- `-U <user>`：设置页中的 iDRAC 用户名。
- `-E`：从 `IPMI_PASSWORD` 环境变量读取密码。
- `<ipmi-arguments>`：本应用传入的 `mc info`、`sdr elist` 或 Dell OEM raw 命令。

命令日志会显示工具路径、参数、退出码和耗时，不显示密码。

## 必要 iDRAC 设置

- 启用 IPMI over LAN。
- 使用具备 OEM raw 命令权限的账号。
- 确保运行本软件的 Windows 主机可以访问 iDRAC。
- 确保防火墙、VLAN、VPN 或管理网络不会阻断 IPMI over LAN。
- 如果只读 SDR 可以成功但 raw 命令失败，请检查 iDRAC 账号权限。

## 应用命令清单

| 用途 | IPMI 参数 | UI 入口 | 说明 |
| --- | --- | --- | --- |
| 测试连接 | `mc info` | 设置页“连接”、保存设置后自动连接 | 用于确认 host、用户、密码、网络和 `ipmitool` 基本可用。 |
| 读取传感器 | `sdr elist` | 总览/传感器刷新、连接后轮询、软件恒温策略 | 返回完整 SDR 行，应用解析温度、风扇、功耗、电压、电流和状态。 |
| 进入手动风扇模式 | `raw 0x30 0x30 0x01 0x00` | 设置全部风扇、手动预设、还原 10%、软件恒温策略 | 设置百分比前会先发送该命令。 |
| 设置全部风扇百分比 | `raw 0x30 0x30 0x02 0xff <percent-hex>` | 全部风扇控制、手动预设、托盘全部风扇菜单 | `0xff` 表示全部风扇。 |
| 设置单风扇百分比 | `raw 0x30 0x30 0x02 <fan-target-byte> <percent-hex>` | 单风扇控制 | 默认关闭，启用前必须确认固件映射。 |
| 恢复 Dell 自动模式 | `raw 0x30 0x30 0x01 0x01` | Dell 自动按钮/预设、紧急温度、托盘预设 | 把风扇控制权交回 iDRAC/BMC 固件策略。 |

## 百分比与十六进制

风扇百分比会按 0-100 校验，然后转换成 1 字节十六进制：

| 百分比 | 十六进制 |
| --- | --- |
| 0% | `0x00` |
| 10% | `0x0a` |
| 20% | `0x14` |
| 35% | `0x23` |
| 42% | `0x2a` |
| 50% | `0x32` |
| 100% | `0x64` |

应用不会允许小于 0 或大于 100 的风扇百分比。

## 典型执行序列

### 连接并启动轮询

```text
mc info
sdr elist
```

连接成功后，应用启动持续传感器轮询。总览页会显示最后轮询时间和本次读取耗时。

### 刷新传感器

```text
sdr elist
```

刷新会更新传感器表格、温度看板、风扇 RPM 看板、功耗与状态看板，以及交互式图表数据。

### 设置全部风扇为 20%

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff 0x14
```

第一条命令进入手动模式，第二条命令设置全部风扇 target。

### 本机默认还原

本机默认还原动作是手动模式 + 全部风扇 10%：

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff 0x0a
```

### 恢复 Dell 自动模式

```text
raw 0x30 0x30 0x01 0x01
```

该命令让 iDRAC/BMC 固件自动策略接管风扇。

### 软件恒温策略 tick

每次 tick 会执行：

```text
sdr elist
```

然后应用解析 CPU 温度：

- 小于或等于目标温度：使用自动策略最小风扇百分比。
- 大于或等于高温阈值：使用自动策略最大风扇百分比。
- 位于两者之间：线性插值。
- 达到或超过紧急自动阈值：发送 Dell 自动模式命令。

如果未达到紧急阈值，应用会发送：

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff <calculated-percent-hex>
```

如果达到紧急阈值，应用会发送：

```text
raw 0x30 0x30 0x01 0x01
```

## 目标字节

| 目标 | target byte |
| --- | --- |
| 全部风扇 | `0xff` |
| Fan 1 | `0x00` |
| Fan 2 | `0x01` |
| Fan 3 | `0x02` |
| Fan 4 | `0x03` |
| Fan 5 | `0x04` |
| Fan 6 | `0x05` |

UI 已实现单风扇 target，但默认关闭。本机 R730xd/iDRAC 2.82 实测 `0x00` 未能单独控制 Fan 1，而是导致全部风扇高转。请将单风扇 target 视为固件相关能力。

## 轮询行为

连接成功后，应用会启动持续传感器轮询。最短间隔为 1 秒。需要注意：

- 1 秒表示发起请求的最短周期，不代表 BMC 可以 1 秒返回完整 SDR。
- 如果上一次 SDR 读取尚未完成，本次 tick 会跳过并给出可见警告。
- 如果其他 IPMI 命令正在执行，本次 tick 会跳过，避免并发命令冲突。
- 如果单次读取耗时超过当前轮询间隔，应用会根据耗时给出建议轮询间隔。
- 如果轮询命令失败，应用会停止轮询、标记断开并显示失败原因。

推荐把轮询间隔设置为略高于实际 SDR 读取耗时。例如完整 SDR 读取耗时 3.2 秒时，可设置为 5 秒或更高。

## SDR 解析规则

应用读取 `sdr elist` 输出后按行解析：

- 使用 `|` 分割每一行。
- 至少需要 3 段字段。
- 对于常见 `sdr elist` 输出，状态取第 3 段，读数取第 5 段。
- 读数开头若存在数字，会拆分为 `Value`、`Unit` 和 `NumericValue`。
- 数字解析使用 invariant culture，支持整数、小数和负数。
- 没有数字前缀的读数会保留原始文本，不生成 `NumericValue`。
- 如果最终没有任何传感器行，应用直接报错。

## 传感器分类规则

图表和看板会按以下规则归类：

- 温度：单位包含 `degrees C`，或名称包含 `Temp`。
- 风扇：名称以 `Fan` 开头且单位包含 `RPM`。
- 性能：名称包含 `Usage`，或单位包含 `percent`。
- 功耗：单位包含 `Watts`，或名称包含 `Pwr Consumption`。
- 电压：单位包含 `Volts` 且存在数值。
- 电流：单位包含 `Amps` 且存在数值。
- 健康状态：名称包含 `Redundancy`、`Battery`、`Intrusion` 或 `Power Optimized`。
- 其他无数值传感器会归为状态类。
- 其他有数值传感器会归为其他数值类。

CPU 温度查找规则：

1. 在温度传感器中优先选择名称包含 `CPU` 的行。
2. 如果没有 CPU 命名行，则使用所有温度传感器。
3. 取候选行中的最高数值。
4. 如果没有任何候选温度行，抛出错误。

## 本机已测试 BMC

- Host：`192.168.1.73`
- User：`root`
- `mc info` 观察到的固件版本：`2.82`
- 观察到的传感器：Fan1-Fan6 RPM、Inlet Temp、Exhaust Temp、CPU 相关 Temp 行、功耗、电压、冗余、硬盘与线缆在位状态。
- 全部风扇 20% 命令：成功。
- Dell 自动模式还原：成功。
- Fan 1 target byte `0x00`：命令接受，但不是单风扇行为，而是全部风扇高转。

密码未写入本仓库。

## 手动验证命令

可以在 PowerShell 中手动验证命令。示例：

```powershell
$env:IPMI_PASSWORD = "<your-password>"
.\BundledTools\ipmitool\ipmitool.exe -I lanplus -H <host> -U <user> -E mc info
.\BundledTools\ipmitool\ipmitool.exe -I lanplus -H <host> -U <user> -E sdr elist
.\BundledTools\ipmitool\ipmitool.exe -I lanplus -H <host> -U <user> -E raw 0x30 0x30 0x01 0x01
```

不要把真实密码写进脚本或提交到仓库。

## 常见失败原因

### `ipmitool.exe` 缺失

输出目录中缺少 `BundledTools\ipmitool\ipmitool.exe`。重新构建或检查发布包内容。

### 认证失败

检查 host、用户名、密码、iDRAC 用户状态和网络可达性。

### raw 命令失败

常见原因是账号权限不足、iDRAC 禁用了相关能力、固件不支持该 OEM 命令，或目标机器不是兼容的 R730xd 环境。

### SDR 轮询慢

iDRAC 返回完整 SDR 可能需要数秒。提高轮询间隔，避免持续跳过 tick。

### 传感器分类不符合预期

传感器分类依赖 BMC 返回的名称和单位。不同固件或语言环境可能改变命名。需要改进分类时，应同步更新代码和文档，说明新的匹配规则。
