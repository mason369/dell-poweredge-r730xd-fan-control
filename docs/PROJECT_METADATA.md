# 项目元数据

语言：中文 | [English](PROJECT_METADATA.en-US.md)

本文件用于整理仓库展示、发布说明、搜索关键词和项目定位。README 面向使用者，`COMMANDS.md` 面向命令细节，本文件面向仓库命名、检索和对外发布信息。

## 英文名称

Dell R730xd iDRAC Fan Control Center

## 中文名称

R730XD 智控风扇中心

## GitHub 仓库

`r730xd-idrac-fan-control-center`

## 一句话简介

面向 Dell PowerEdge R730xd 的 Windows WinUI 3 桌面工具，通过 iDRAC/IPMI 控制风扇、读取 BMC 传感器、展示实时硬件图表，提供 JSONL 运行日志，并支持 Dell 自动模式、软件恒温策略与用户自定义温度-风扇曲线预设。

## 长简介

R730XD 智控风扇中心把 R730xd 常用的 IPMI 风扇控制、传感器轮询、预设管理、温度-风扇曲线预设、托盘快捷操作、本地可视化和 JSONL 运行日志整合到一个 Windows 桌面应用中。应用内置 `ipmitool.exe` 和本地图表资产，避免依赖外部命令路径或在线 CDN。它默认使用中文界面，内置 22 种界面语言，通过本地化服务让可见 UI 字符串可切换，并用 `Strings/<language>/Resources.resw` 本地化 MSIX 包清单显示名和描述。

项目的核心目标是让用户清楚知道软件正在对 BMC 做什么：每条命令有明确的触发入口，失败会直接显示，运行日志会记录原子事件和操作时间段，密码不进入命令行参数，低转速和单风扇目标编号风险会在界面和文档中说明，尤其明确 `0x00` 是目标编号而不是 `0%` 转速。

## 目标用户

- Dell PowerEdge R730xd homelab 用户。
- 需要降低噪音但仍要观察温度的服务器用户。
- 需要快速在手动风扇、预设和 Dell 自动模式之间切换的用户。
- 需要把“多少度对应多少转速”保存成可编辑曲线并快速启用的用户。
- 需要在 Windows 上通过图形界面查看 BMC SDR 传感器的用户。
- 希望把 iDRAC 风扇命令流程文档化、可视化和可重复执行的维护者。

## 项目范围

包含：

- iDRAC/IPMI over LAN 连接。
- `mc info` 连接测试。
- `sdr elist` 传感器读取和解析。
- Dell OEM raw 手动风扇模式。
- 全部风扇百分比设置。
- Fan 1-6 单风扇目标编号控制，默认关闭；`0x00` 是目标编号，不是 `0%` 转速。
- Dell 自动模式恢复。
- 内置和自定义手动预设。
- 可编辑温度-风扇曲线和功耗-风扇曲线预设，保存在设置文件并通过软件自动轮询持续执行。
- 软件自动策略，包括全局温度线性策略、温度曲线按当前读数求值策略和功耗曲线按当前读数求值策略。
- 传感器轮询、显式“开始轮询 / 取消轮询”入口、运行预设状态恢复和延迟提示；开始轮询会先执行 `mc info` 与一次 `sdr elist`，取消轮询只停止后续轮询 tick，不启动新的 IPMI 命令。用户主动风扇命令会等待当前 IPMI 命令完成后继续执行，后台 tick 遇到 IPMI 忙仍跳过。
- 横幅实时硬件摘要，显示最近一次成功 SDR 刷新的当前温度、平均转速、功耗、平均电压和总电流；每组下方按行展示该类别的全部具体传感器小项，不再截断前三项，卡片使用更高的基础高度并随实际传感器数量自动增高，缺少读数时显示等待刷新，温度摘要不使用历史最高值，并按推荐区间用绿色、黄色、橙色、红色提示实时数值状态。标题下方的“当前温控模式”徽章会显示待机、手动、Dell 自动温控、软件恒温、温度曲线自动或功耗曲线自动，并与右侧状态卡使用同一运行模式状态源。
- 总览顶部硬件摘要卡，除最高 CPU 温度、风扇状态和控制模式外，也显示实时功耗、平均电压和总电流；温度、风扇、功耗与状态看板使用图标化硬件卡片，风扇卡片根据 RPM 持续旋转，卡片副标题以“编号 0x30 / 位置 7.1”这类紧凑文本显示 SDR 记录 ID 和 IPMI 实体/实例位置，避免把 `h` 误读为小时或把位置编号误读为版本号。
- WebView2 交互图表会把滚轮转交外层 WinUI 滚动容器，并禁用额外滚动动画，鼠标停在图表上仍保持页面滚动跟手。
- 横幅右侧实时状态卡，显示当前目标、连接状态、控制模式、最近请求状态和最后更新时间。
- WinUI 3 主题、托盘和 i18n；托盘右键菜单提供窗口/页面入口、刷新传感器、打开 iDRAC、打开日志、停止自动策略、还原 Dell 自动、全部风扇 20/35/50% 和一层动态预设子菜单；包清单的应用显示名和描述通过 `.resw` 资源进入 PRI 索引。
- 本地 ECharts 实时数据可视化。
- DPAPI 本地密码保护。
- JSON Lines 本地运行日志，记录 UI 事件、操作起止时间段、传感器刷新、软件恒温 tick 和 IPMI 命令完成记录；总览页最近日志使用蓝色信息、琥珀色警告、绿色成功、红色错误/失败状态标记。

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
- 图表：本地 `Assets/Charts/dashboard.html` + ECharts；总览图表在每次成功 SDR 轮询后保存完整 JSONL 历史点，用户可通过“历史范围”切换当前、近 6 小时、近 1 天、近 3 天、近 7 天或自定义范围查看历史图表。
- 凭据保护：`System.Security.Cryptography.ProtectedData` / Windows DPAPI。
- 运行日志：本地 JSON Lines，路径为 `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`。

## 关键默认值

| 项目 | 默认值 |
| --- | --- |
| Host | `192.168.1.73` |
| UserName | `root` |
| RememberPassword | `false` |
| IpmiToolPath | `BundledTools\ipmitool\ipmitool.exe` |
| FanCount | `6` |
| 内置恢复手动预设百分比 | `10%` |
| 点击关闭时最小化到托盘 | `true` |
| 图表历史保留 | 最近 `7` 天成功轮询历史点，保存到 `%LocalAppData%\DellR730xdFanControlCenter\chart-history\chart-history-YYYYMMDD.jsonl`，应用启动时重新加载仍在保留期内的数据 |
| 传感器轮询间隔 | `1s`，在设置页“应用设置”中编辑 |
| 命令超时 | `35s` |
| 目标 CPU 温度 | `68 °C` |
| 高温阈值 | `78 °C` |
| 紧急自动阈值 | `84 °C` |
| 自动策略最小风扇百分比 | `10%` |
| 自动策略最大风扇百分比 | `42%` |
| 新建温度曲线编辑器默认点 | `45 °C = 18%`、`68 °C = 28%`、`78 °C = 42%` |
| 新建功耗曲线编辑器默认点 | `280 W = 18%`、`500 W = 28%`、`750 W = 42%` |
| 默认语言 | `zh-CN` |
| 默认主题 | 跟随系统 |
| 运行日志目录 | `%LocalAppData%\DellR730xdFanControlCenter\logs` |

## 内置预设

| ID | 名称 | 类型 | 百分比 | 说明 |
| --- | --- | --- | --- | --- |
| `restore-manual` | 默认 / Default | 恢复手动 | `10%` | 回到本机默认手动 10%。 |
| `balanced` | 均衡 / Balanced | 手动 | `20%` | 日常轻负载，兼顾噪音和温度。 |
| `cooling` | 散热 / Cooling | 手动 | `35%` | 提高散热余量。 |
| `performance` | 性能 / Performance | 手动 | `50%` | 高转速优先，适合短时高负载。 |
| `dell-auto` | Dell 自动 / Dell Auto | BMC 自动 | 无手动百分比 | 交还给 iDRAC/BMC 固件策略。 |

## 自定义曲线预设

曲线预设不是内置预设，默认不会自动创建。用户可在风扇控制页填写曲线预设名称，然后在曲线图空白处点击添加点位、拖动已有点位实时调整，或用右侧点位数字控件微调。鼠标移入曲线图时会显示十字辅助线、横轴当前温度/功耗和纵轴风扇速度百分比；拖动过程中只轻量更新曲线和右侧绑定数值，完整预览文本和严格校验会在松手、右侧数值编辑、添加点或保存时刷新。温度曲线保存为 `Kind = TemperatureCurve`，点位包含 `TemperatureCelsius` 与 `FanPercent`；功耗曲线保存为 `Kind = PowerCurve`，点位包含 `PowerWatts` 与 `FanPercent`。两者都在 `settings.json` 中写入 `CurvePoints` 和 `SmoothCurve`；已有曲线预设可从预设卡片的“编辑点位”按钮载入对应编辑器，页面会自动滚到匹配的温度或功耗曲线图。保存成功后，页面会滚回刚新增或刚更新的预设卡片。

运行行为：

- 切换曲线预设会启动软件自动轮询。
- 每次 tick 读取 `sdr elist`。温度曲线解析 CPU 温度并按曲线点和 `SmoothCurve` 设置计算百分比；功耗曲线先解析 CPU 温度并检查紧急自动阈值，再使用本轮 SDR 中单位包含 `Watts` 或名称包含 `Pwr Consumption` 的功耗读数计算百分比。自动策略运行时普通传感器轮询不再额外读取 SDR；该 tick 的 SDR 结果负责刷新传感器、图表和历史点。最终发送全部风扇百分比命令。
- 如果当前正在运行的手动预设、Dell 自动预设、温度曲线或功耗曲线被保存，应用会等待当前 IPMI 命令完成并立即重新应用该预设；保存活动曲线会立即执行一轮真实 `sdr elist`、曲线百分比计算和风扇命令，首轮失败则显示错误并停止该自动策略。
- Dell 风扇控制 raw 命令（`raw 0x30 0x30 ...`）非零退出码会立即失败；每次实际 `ipmitool` 子进程只写入一次真实结果，不写入 `attempt`、`maxAttempts` 或 `willRetry`，stdout/stderr 会直接显示给用户。`sdr elist` 轮询失败不重试也不写入伪造历史点。
- 输入值低于第一个点时使用第一个点，高于最后一个点时使用最后一个点。
- `SmoothCurve = false` 时把当前温度或功耗代入点位连接成的折线曲线求值；`SmoothCurve = true` 时使用同一组点做平滑位置求值，端点和紧急自动保护不变。
- 达到紧急自动阈值时发送 Dell 自动模式命令，而不是继续下发曲线百分比；该动作不会停止软件自动计时器。
- 功耗曲线缺少功耗读数时会显示失败原因并停止本轮，不会用默认功耗或上一次功耗继续下发。
- 手动全部风扇、单风扇、内置恢复手动预设、Dell 自动、总览/托盘还原戴尔出厂设置转速和停止自动都会清除当前曲线状态；只有“停止自动”、删除正在运行的曲线预设或自动策略失败会停止软件自动计时器。若计时器仍在运行，下一次 tick 会按全局线性策略继续控制全部风扇。

校验边界：

- 至少 2 个点。
- 温度曲线温度范围 `-40` 到 `125` °C；功耗曲线功耗范围 `0` 到 `1200` W。
- 风扇百分比范围 `0` 到 `100`。
- 温度点或功耗点不能重复。
- 无效点位会在曲线预览区显示错误并阻止保存或切换，不会自动替换成默认曲线。

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
WinUI 3 desktop app for Dell PowerEdge R730xd iDRAC/IPMI fan control, BMC sensor monitoring, smart temperature automation, tray quick actions, local ECharts visualization, and JSONL runtime logs.
```

## 推荐中文描述

```text
面向 Dell PowerEdge R730xd 的 WinUI 3 桌面风扇控制中心，支持 iDRAC/IPMI 调速、BMC 传感器监控、软件恒温策略、托盘快捷操作、本地 ECharts 可视化和 JSONL 运行日志。
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
- `dotnet run --project Tests\PresetModelTests\PresetModelTests.csproj` 成功，覆盖手动预设编辑、温度/功耗曲线按当前读数求值、平滑曲线求值、设置存储、传感器状态翻译键和运行日志 JSONL 行为。
- 输出目录包含 `BundledTools/ipmitool/ipmitool.exe`。
- 输出目录包含 `Assets/Charts/dashboard.html` 和 `Assets/Charts/echarts.min.js`。
- 应用能启动并显示主窗口。
- 设置页能保存设置。
- 托盘右键菜单能显示窗口/页面入口、刷新传感器、日志/iDRAC 入口、Dell 自动恢复、停止自动、全部风扇 20/35/50% 和预设子菜单，常用固定动作不再嵌套在二级风扇控制菜单中。
- 总览页“打开日志文件夹”能打开 `%LocalAppData%\DellR730xdFanControlCenter\logs`，当天 `runtime-YYYYMMDD.jsonl` 能写入。
- 没有密码、私有 IP 规划或其他敏感信息进入仓库。
