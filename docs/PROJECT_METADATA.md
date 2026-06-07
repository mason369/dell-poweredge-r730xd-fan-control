# 项目元数据

语言：中文 | [English](PROJECT_METADATA.en-US.md)

本文件用于整理仓库展示、发布说明、搜索关键词、项目定位和维护约定。README 面向使用者，`COMMANDS.md` 面向命令细节，本文件面向仓库维护、命名和对外发布。

## 英文名称

Dell R730xd iDRAC Fan Control Center

## 中文名称

R730XD 智控风扇中心

## GitHub 仓库

`r730xd-idrac-fan-control-center`

## 一句话简介

面向 Dell PowerEdge R730xd 的 Windows WinUI 3 桌面工具，通过 iDRAC/IPMI 控制风扇、读取 BMC 传感器、展示实时硬件图表，并支持 Dell 自动模式与软件恒温策略。

## 长简介

R730XD 智控风扇中心把 R730xd 常用的 IPMI 风扇控制、传感器轮询、预设管理、托盘快捷操作和本地可视化整合到一个 Windows 桌面应用中。应用内置 `ipmitool.exe` 和本地图表资产，避免依赖外部命令路径或在线 CDN。它默认使用中文界面，同时提供 English，并通过本地化服务让可见 UI 字符串可切换。

项目的核心目标是让用户清楚知道软件正在对 BMC 做什么：每条命令有明确的触发入口，失败会直接显示，密码不进入命令行参数，低转速和单风扇 raw target 的风险会在界面和文档中说明。

## 目标用户

- Dell PowerEdge R730xd homelab 用户。
- 需要降低噪音但仍要观察温度的服务器用户。
- 需要快速在手动风扇、预设和 Dell 自动模式之间切换的用户。
- 需要在 Windows 上通过图形界面查看 BMC SDR 传感器的用户。
- 希望把 iDRAC 风扇命令流程文档化、可视化和可重复执行的维护者。

## 项目范围

包含：

- iDRAC/IPMI over LAN 连接。
- `mc info` 连接测试。
- `sdr elist` 传感器读取和解析。
- Dell OEM raw 手动风扇模式。
- 全部风扇百分比设置。
- Fan 1-6 单风扇 target byte 控制，默认关闭。
- Dell 自动模式恢复。
- 内置和自定义手动预设。
- 软件恒温策略。
- 传感器轮询和延迟提示。
- WinUI 3 主题、托盘和 i18n。
- 本地 ECharts 实时数据可视化。
- DPAPI 本地密码保护。

不包含：

- 通用服务器资产管理。
- 多主机集中管理。
- iDRAC 用户或固件管理。
- IPMI 以外的 Redfish 控制逻辑。
- 固件级实时风扇曲线写入。
- 云端同步。
- 跨平台 UI。

## 技术栈

- 语言：C#。
- 框架：.NET 8。
- UI：WinUI 3 / Windows App SDK。
- 目标框架：`net8.0-windows10.0.26100.0`。
- 最低 Windows 版本：`10.0.19041.0`。
- 平台：`x86`、`x64`、`ARM64`。
- 命令工具：内置 `ipmitool.exe`。
- 图表：本地 `Assets/Charts/dashboard.html` + ECharts。
- 凭据保护：`System.Security.Cryptography.ProtectedData` / Windows DPAPI。

## 关键默认值

| 项目 | 默认值 |
| --- | --- |
| Host | `192.168.1.73` |
| UserName | `root` |
| FanCount | `6` |
| 默认还原风扇百分比 | `10%` |
| 传感器轮询间隔 | `1s` |
| 命令超时 | `35s` |
| 目标 CPU 温度 | `68 °C` |
| 高温阈值 | `78 °C` |
| 紧急自动阈值 | `84 °C` |
| 自动策略最小风扇百分比 | `10%` |
| 自动策略最大风扇百分比 | `42%` |
| 默认语言 | `zh-CN` |
| 默认主题 | 跟随系统 |

## 内置预设

| ID | 名称 | 类型 | 百分比 | 说明 |
| --- | --- | --- | --- | --- |
| `restore-manual` | 默认 / Default | 恢复手动 | `10%` | 回到本机默认手动 10%。 |
| `balanced` | 均衡 / Balanced | 手动 | `20%` | 日常轻负载，兼顾噪音和温度。 |
| `cooling` | 散热 / Cooling | 手动 | `35%` | 提高散热余量。 |
| `performance` | 性能 / Performance | 手动 | `50%` | 高转速优先，适合短时高负载。 |
| `dell-auto` | Dell 自动 / Dell Auto | BMC 自动 | 无手动百分比 | 交还给 iDRAC/BMC 固件策略。 |

## 文档关系

- [README.md](../README.md)：中文完整使用说明。
- [README.en-US.md](../README.en-US.md)：英文完整使用说明。
- [SECURITY.md](../SECURITY.md)：中文安全说明。
- [SECURITY.en-US.md](../SECURITY.en-US.md)：英文安全说明。
- [COMMANDS.md](COMMANDS.md)：中文 IPMI 命令参考。
- [COMMANDS.en-US.md](COMMANDS.en-US.md)：英文 IPMI 命令参考。
- [PROJECT_METADATA.md](PROJECT_METADATA.md)：中文项目元数据。
- [PROJECT_METADATA.en-US.md](PROJECT_METADATA.en-US.md)：英文项目元数据。

## Logo 设计

Logo 由服务器前面板、涡轮风扇和黄色状态线组成：

- 服务器前面板代表 Dell PowerEdge R730xd。
- 涡轮风扇代表风扇控制和散热。
- 黄色状态线呼应服务器健康状态、告警和管理控制。
- 图标应在浅色、深色背景下都保持辨识度。

英文名称保留 Dell、R730xd、iDRAC、Fan Control 等搜索关键词，中文名称突出 R730XD 与“智控风扇中心”。

## 推荐仓库描述

```text
WinUI 3 desktop app for Dell PowerEdge R730xd iDRAC/IPMI fan control, BMC sensor monitoring, smart temperature automation, tray quick actions, and local ECharts visualization.
```

## 推荐中文描述

```text
面向 Dell PowerEdge R730xd 的 WinUI 3 桌面风扇控制中心，支持 iDRAC/IPMI 调速、BMC 传感器监控、软件恒温策略、托盘快捷操作和本地 ECharts 可视化。
```

## 标签

- dell
- poweredge
- r730xd
- idrac
- ipmi
- ipmitool
- fan-control
- server-management
- homelab
- winui
- windows
- dotnet
- bmc
- hardware-monitoring
- thermal-management

## 发布检查清单

发布或打包前应确认：

- `dotnet build` 在目标平台成功。
- 输出目录包含 `BundledTools/ipmitool/ipmitool.exe`。
- 输出目录包含 `Assets/Charts/dashboard.html` 和 `Assets/Charts/echarts.min.js`。
- 应用能启动并显示主窗口。
- 设置页能保存设置。
- 没有密码、私有 IP 规划或其他敏感信息进入仓库。
- 中文和英文 README 同步覆盖新功能。
- 中文和英文安全说明同步覆盖新风险。
- 中文和英文命令参考同步覆盖新命令或 raw byte 行为。

## 文档标准

所有 README、`docs/` 文档和安全说明必须覆盖全面、具体、可验证。新增或修改功能时，文档应说明：

- 功能目的。
- 用户入口。
- 默认配置。
- 执行流程。
- 涉及的命令或文件路径。
- 用户可见成功行为。
- 用户可见失败行为。
- 安全或硬件风险。
- 已知限制。
- 验证方式。
- 中英文对应更新。

不要只写浅层功能清单。涉及风扇控制、凭据、命令执行、传感器解析、发布包内容或故障处理时，必须写清楚触发条件、边界和风险。
