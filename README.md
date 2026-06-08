<p align="center">
  <img src="Assets/Logo.svg" width="128" alt="R730XD 智控风扇中心图标" />
</p>

# R730XD 智控风扇中心

语言：中文 | [English](README.en-US.md)

R730XD 智控风扇中心是一款面向 Dell PowerEdge R730xd 的 Windows WinUI 3 桌面工具，用于通过 iDRAC/IPMI 控制服务器风扇、读取 BMC 传感器、展示实时硬件状态，并在需要时把风扇控制权交回 Dell 固件自动策略。

这个项目不是通用服务器管理平台，而是针对 R730xd、iDRAC/BMC、`ipmitool` 和 Dell OEM raw 风扇命令整理出的本地控制中心。它强调三件事：命令透明、失败显式、操作可恢复。软件不会在命令失败时静默切换后端或假装成功；缺少工具、认证失败、权限不足、固件不支持 raw 命令等问题都会直接在界面和日志中暴露。

## 适用范围

- 目标机器：Dell PowerEdge R730xd。
- 目标控制面：iDRAC/BMC 的 IPMI over LAN。
- 目标系统：Windows 10 2004 / build 19041 或更新版本。
- 目标用户：需要在 homelab、机房值守、硬盘密集型 R730xd 或噪音受限环境中监控和调节风扇的用户。
- 已本机观察环境：R730xd / iDRAC firmware 2.82，主机示例为 `192.168.1.73`，用户示例为 `root`。

不同 iDRAC 固件、不同背板和不同风扇/传感器布局可能改变单风扇目标编号行为。当前代码已实现 Fan 1-6 目标编号控制但默认关闭；`0x00` 是固件 raw 命令中的目标编号，不是 `0%` 转速，必须先确认固件映射再启用。

## 功能总览

- 现代 WinUI 3 界面，支持浅色、深色、跟随系统主题。
- 全部风扇 0-100% 百分比控制，设置前会切入手动风扇模式。
- 内置“默认/恢复手动”预设保留手动模式 + 全部风扇 10%，用于用户明确选择该预设时回到本机安静基线。
- 初始预设：默认 10%、均衡 20%、散热 35%、性能 50%、Dell 自动。
- 支持编辑预设名称、说明和可用百分比，支持添加、保存、删除自定义手动百分比预设；初始预设不可删除。
- 支持添加和编辑温度-风扇曲线预设；可在曲线图上点击添加温度/风扇百分比点位，也可用点位数字控件微调，并可启用平滑曲线过渡，切换后按轮询周期持续控制全部风扇。
- Dell 自动风扇模式保留为独立操作和预设入口，可把控制权交还给 iDRAC/BMC。
- 1-6 号风扇单独目标字节控制已实现，但默认关闭。
- 软件恒温策略会读取 BMC 传感器中的 CPU 温度，并可按全局目标/高温阈值或当前曲线预设调整全部风扇。
- 达到紧急温度阈值时，软件恒温策略会切回 Dell 自动模式。
- 连接或保存设置成功后启动持续 SDR 轮询，默认和最短可保存间隔为 15 秒；旧设置文件中低于 15 秒的轮询值会暂停自动连接并要求用户显式修改。
- 内置 `ipmitool.exe` 与所需 Cygwin DLL，路径为 `BundledTools/ipmitool`。
- 内置本地 ECharts 仪表板资产，路径为 `Assets/Charts/dashboard.html` 与 `Assets/Charts/echarts.min.js`，运行时不依赖在线 CDN。
- 托盘图标支持最小化后台运行、恢复窗口、快捷预设、曲线预设标识、全部风扇 20/35/50%、还原戴尔出厂设置转速、打开设置和退出；托盘还原会发送 Dell 自动模式命令，不会改成手动 10%。
- iDRAC Web 控制台快捷入口会打开 `https://<host>/`。
- 可见 UI 字符串接入多语言资源，当前内置简体中文和英文；仓库默认显示中文 README，英文内容放在独立文件中。
- 图表、看板和状态提示使用本地化显示名；传感器表格保留管理控制器返回的原始 `Key`，方便排查命令输出。
- 总览交互图表位于 WebView2 中，滚轮事件会转交外层 WinUI `ScrollViewer` 执行原生平滑滚动；鼠标停在图表上滚动时应与非 Web 看板保持一致，不应卡住页面滚动。
- 密码可使用 Windows DPAPI 在当前 Windows 用户上下文加密保存。
- 运行日志会以 JSON Lines 写入 `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`，每一行都是完整原子事件，并记录用户命令、传感器刷新、软件恒温 tick 和 IPMI 命令耗时。
- 启动异常会写入 `%LocalAppData%\DellR730xdFanControlCenter\startup-error.log`。

## 界面结构

### 总览

总览页用于观察当前硬件状态和执行最常用操作：

- 横幅实时摘要显示当前温度、平均风扇转速、实时功耗、平均电压和总电流，数据来自最近一次成功 SDR 传感器刷新；每个大数值下方会按行显示最多 3 个具体传感器小项，例如进风/排风温度、Fan 1-6 RPM、电源功耗、电压轨和电流轨，超出的传感器不在横幅中显示，请到对应看板查看完整列表。实时摘要卡片使用固定紧凑高度，避免被右侧请求状态长文本撑高。首次刷新前或缺少某类传感器读数时显示“等待刷新”。温度摘要使用最新温度传感器读数的平均值，不使用历史最高值；概览卡和紧急自动保护仍保留独立的最高/CPU 温度口径。大数值和明细会按推荐区间实时着色：正常绿色、接近风险黄色、偏离明显橙色、危险红色；当前阈值为温度 `<60/60-69/70-79/>=80 °C`，平均转速 `2500-6000` 绿、`1500-2499 或 6001-9000` 黄、`500-1499 或 >9000` 橙、`<500` 红，功耗 `<500/500-699/700-899/>=900 W`，电压 `210-240` 绿、`200-209 或 241-250` 黄、`190-199 或 251-260` 橙、其他红，总电流 `<4/4-5.9/6-7.9/>=8 A`。颜色只是界面提示，不替代完整传感器表、iDRAC 告警或紧急自动保护。
- 横幅右侧状态卡显示当前 iDRAC 目标、连接状态、当前控制模式、最近请求状态和最后更新时间。
- 最近请求状态会在点击“切换”、连接/刷新传感器、保存设置、启动/停止软件自动、曲线自动 tick、轮询成功/跳过/失败时实时更新；横幅里显示“请求中、正常、已跳过、失败、已调速”等短状态，完整原因仍显示在顶部 InfoBar 和运行日志中。
- 指标卡展示最高 CPU 温度、风扇状态、实时功耗、平均电压、总电流和当前控制模式；功耗、电压、电流同样来自最近一次成功 SDR 刷新，缺少读数时显示等待或无读数。
- 交互式数据可视化展示总体趋势、硬件画像、单项温度排行、单风扇 RPM、性能/电气数据，以及按硬件类型和状态分组的树图。
- 温度大看板逐项展示 BMC SDR 中每一个温度传感器，卡片带温度图标并按当前读数显示推荐状态色。
- 风扇 RPM 看板展示 Fan 传感器当前转速，卡片带风扇图标；风扇图标会持续旋转，并根据 RPM 调整旋转周期，转速越高动画越快，0 或缺失读数不会显示成高速正常状态。
- 功耗与状态看板展示功耗、电压、电流、冗余、电池、入侵、Power Optimized 等状态类传感器，使用对应的电气/状态图标和同一套推荐状态色。
- 快速操作包含刷新传感器、还原戴尔出厂设置转速、打开 iDRAC 和全部风扇百分比设置；软件恒温策略的启动/停止入口位于“风扇控制”页。
- 最新日志展示最近命令、成功/失败状态和轮询提示，并提供“打开日志文件夹”入口。界面状态标记按级别着色：信息为蓝色、警告为琥珀色、成功为绿色，只有错误和失败使用红色；本地 JSONL 运行日志仍写入纯文本结构化字段，不写入颜色值。

### 风扇控制

风扇控制页用于管理预设和高级控制：

- 预设模式区显示当前运行模式、初始预设、自定义预设和每个预设的可编辑说明。
- 手动预设会发送 Dell OEM raw 命令设置全部风扇百分比。
- 默认/恢复类预设和手动预设的百分比可以编辑；总览快速还原和托盘还原现在恢复戴尔出厂设置转速，也就是发送 Dell 自动模式命令，不再执行手动 10%。
- Dell 自动预设会发送命令恢复 BMC 固件自动风扇策略，不显示可编辑百分比。
- 软件恒温策略的启动/停止卡片位于风扇控制页；温度阈值和轮询间隔仍在同页下方编辑，启动后每次 tick 都会写入运行日志并更新横幅请求状态。
- 添加手动预设时必须填写名称，百分比会按 0-100 校验。
- 曲线预设通过图形编辑器维护，不需要手写多行文本：填写曲线名称后，可在曲线图上点击添加点位，或用右侧点位列表的温度和风扇百分比数字控件微调；点击已有曲线预设卡片里的“编辑点位”会把该预设载入同一个编辑器。
- 曲线点保存时至少需要 2 个点，温度必须在 `-40` 到 `125` °C，风扇百分比必须在 `0` 到 `100`，重复温度会直接报错。无效点位会在预览区显示失败原因，点击添加或保存时仍按同一套规则严格校验，不会自动改成默认曲线。
- “平滑曲线”开关会保存到预设中。关闭时按相邻两点线性插值；开启时仍使用相同点位，但在点位之间使用平滑过渡曲线计算百分比，避免温度跨过点位时风扇百分比突变。达到第一个点之前和最后一个点之后仍分别钳制到端点百分比。
- 切换曲线预设会启动软件自动轮询；每次 tick 读取 SDR、解析 CPU 温度、按曲线点位和当前平滑设置计算百分比，再发送全部风扇百分比命令。
- 手动全部风扇、单风扇、内置恢复手动预设、Dell 自动、总览/托盘还原戴尔出厂设置转速或停止自动会清除当前曲线状态，避免曲线轮询继续覆盖用户刚刚执行的手动命令。
- 保存预设会写入本地设置文件，托盘菜单也会读取这些预设；曲线预设会在托盘菜单中显示“曲线”标识。如果正在运行的曲线被编辑保存，下一次软件自动 tick 会使用更新后的点位和 `SmoothCurve` 设置。
- 单风扇控制区默认禁用，需要在设置页开启并保存。
- 自动策略参数包括目标温度、高温阈值、紧急自动阈值、轮询秒数、最小风扇百分比和最大风扇百分比。

### 传感器

传感器页展示 `ipmitool sdr elist` 解析后的完整表格：

- `Key`：管理控制器返回的原始传感器名称，例如 `Fan1 RPM`、`Inlet Temp`、`CPU Usage`。
- `ID`：SDR 输出中的传感器 ID。
- `Entity`：SDR 输出中的实体信息。
- `Value`：原始读数文本中的数值或状态；常见 IPMI 枚举值会按界面语言显示，例如 `No Reading`、`State Deasserted`、`Fully Redundant`、`OEM Specific` 等。
- `Unit`：单位，例如 `degrees C`、`RPM`、`Watts`、`Volts`、`Amps`、`percent`；界面显示会本地化为 `°C`、`转/分钟`、`瓦`、`伏`、`安`、`%`。
- `Status`：BMC 返回的状态，例如 `ok`、`ns`、`na` 或异常状态；常见短码会本地化显示，未知值保留原始文本以便排查。

如果 `ipmitool` 成功退出但没有返回任何 SDR 行，应用会直接报错，不会构造假传感器数据。

### 设置

设置页控制连接、持久化和运行行为：

- iDRAC/BMC IP 或主机名。
- iDRAC 用户名。
- iDRAC 密码。
- 是否使用 DPAPI 保存密码。
- 只读内置 `ipmitool.exe` 路径。
- 点击关闭是否最小化到托盘。
- 是否启用单风扇目标编号控制。
- 风扇数量，默认 6。
- 命令超时秒数，默认 35，代码要求至少 5。
- SDR 轮询秒数，默认 15，保存时低于 15 会直接失败并显示原因，不会自动改成其他值或继续连接。
- 软件恒温策略最小/最大风扇百分比。
- 界面主题和语言。

设置文件路径为：

```text
%LocalAppData%\DellR730xdFanControlCenter\settings.json
```

## 默认配置

| 项目 | 默认值 | 说明 |
| --- | --- | --- |
| Host | `192.168.1.73` | 示例 iDRAC 地址，首次使用请改成你的 BMC/iDRAC 地址。 |
| UserName | `root` | 示例用户。 |
| FanCount | `6` | R730xd 常见 Fan 1-6 布局。 |
| DefaultAllFanPercent | `10` | 内置“默认/恢复手动”预设的本机手动基线；总览/托盘“还原戴尔出厂设置转速”不使用该值，而是恢复 Dell 自动模式。 |
| EnableIndividualFanTargets | `false` | 单风扇目标编号控制默认关闭；`0x00` 是目标编号，不是 `0%` 转速。 |
| SensorRefreshSeconds | `15` | 最短可保存轮询间隔。实际 SDR 返回速度取决于 iDRAC；本机 R730xd/iDRAC 2.82 观察到完整 SDR 读取约 11-13 秒。 |
| CommandTimeoutSeconds | `35` | 单条 `ipmitool` 命令超时。 |
| TargetCpuTemperatureCelsius | `68` | 软件恒温策略目标温度。 |
| HighCpuTemperatureCelsius | `78` | 达到后使用自动策略最大风扇百分比。 |
| EmergencyCpuTemperatureCelsius | `84` | 达到后切回 Dell 自动模式。 |
| AutoMinimumFanPercent | `10` | 软件恒温策略最小风扇百分比。 |
| AutoMaximumFanPercent | `42` | 软件恒温策略最大风扇百分比。 |
| Theme | `Default` | 跟随系统。 |
| Language | `zh-CN` | 默认简体中文。 |

运行日志不是设置项，默认固定写入：

```text
%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl
```

## 运行要求

- Windows 10 2004 / build 19041 或更新版本。
- .NET 8 Desktop Runtime，或使用自包含发布包。
- 可访问的 Dell PowerEdge R730xd iDRAC/BMC。
- iDRAC 中已启用 IPMI over LAN。
- iDRAC 用户具备发送 OEM raw IPMI 命令的权限。
- 项目输出目录中存在 `BundledTools/ipmitool/ipmitool.exe` 和所需 DLL。
- 项目输出目录中存在 `Assets/Charts/dashboard.html` 和 `Assets/Charts/echarts.min.js`。

## 首次使用流程

1. 在 iDRAC 中确认 IPMI over LAN 已启用。
2. 确认运行本软件的 Windows 主机可以访问 iDRAC 地址。
3. 启动应用。首次运行或没有保存密码时，应用会自动打开设置页。
4. 填写 iDRAC 地址、用户名和密码。
5. 需要自动连接时，打开“使用 DPAPI 保存密码”。
6. 保存设置。保存成功后，如果密码不为空，应用会立即测试连接、刷新传感器并启动轮询。
7. 在总览页确认 CPU 温度、风扇 RPM、功耗和状态传感器能正常显示。
8. 在总览页“最新日志”区域点击“打开日志文件夹”，确认当天 `runtime-YYYYMMDD.jsonl` 已生成。
9. 先用 Dell 自动模式或较保守的手动百分比确认机器行为，再尝试更低转速。

## 构建

```powershell
cd C:\DellR730xdFanControlCenter
dotnet restore .\DellR730xdFanControlCenter.csproj
dotnet build .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
```

项目支持 `x86`、`x64`、`ARM64` 平台。开发和本机调试通常使用 `x64`。

## 运行

```powershell
cd C:\DellR730xdFanControlCenter
dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
```

`Properties/launchSettings.json` 中包含两个启动配置：

- `DellR730xdFanControlCenter (Package)`：MSIX Package 启动。
- `DellR730xdFanControlCenter (Unpackaged)`：普通项目启动。

## 发布

项目文件启用了 MSIX tooling，并配置了 `Microsoft.Windows.SDK.BuildTools.WinApp`，用于支持 WinUI 应用的 `dotnet run` 和打包相关流程。发布时需要确保以下内容进入输出目录：

- `BundledTools/ipmitool/**`
- `Assets/Charts/**`
- WinUI/Windows App SDK 运行所需文件
- 应用图标和清单资产

示例发布命令：

```powershell
cd C:\DellR730xdFanControlCenter
dotnet publish .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

发布后请在目标机器上实际启动一次，确认内置 `ipmitool.exe`、图表页面和托盘图标都能从输出目录解析到。

## IPMI 命令行为

命令执行层使用：

```text
ipmitool -I lanplus -H <host> -U <user> -E <ipmi-arguments>
```

密码通过 `IPMI_PASSWORD` 环境变量传入，配合 `ipmitool -E` 使用，不会放进命令行参数。界面日志会显示命令、退出码和耗时，但不会显示密码。

核心命令和 raw byte 说明见 [IPMI 命令参考](docs/COMMANDS.md)。

## 运行日志系统

应用有两类日志入口：

- 总览页“最新日志”：保留最近 80 条内存记录，方便在界面里立即确认当前操作。
- 本地 JSONL 运行日志：写入 `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`，可通过总览页“打开日志文件夹”进入。

JSONL 文件每一行是一个完整 JSON 对象，单条记录包含 `eventId`、`timestamp`、`level`、`category`、`eventName`、`message`。长操作会额外记录 `operationId`、`operationName`、`phase`、`startedAt`、`finishedAt`、`durationMilliseconds` 和 `succeeded`。当前会写入这些主要类别：

- `Application/UiLog`：设置保存、预设变更、轮询告警、日志文件路径等普通界面事件。
- `Operation/UiCommand`：按钮触发的用户命令，写入 `Started` 和 `Succeeded` 或 `Failed` 终止记录。
- `Operation/SensorRefresh`：每次 SDR 传感器刷新，记录主机、轮询秒数、传感器数量和耗时。
- `Operation/SmartAutoPolicyTick`：软件恒温策略每次 tick，记录温度阈值、CPU 温度、计算出的风扇百分比或紧急 Dell 自动动作。
- `IpmiCommand/CommandCompleted`：每条 `ipmitool` 子命令完成后记录命令行、退出码、成功状态和耗时。

写日志失败不会伪装成成功。用户命令期间如果运行日志写入失败，当前操作会停止并在状态栏和最近日志中显示“运行日志写入失败”。启动阶段未处理异常仍写入 `startup-error.log`。运行日志不记录 iDRAC 密码，但会记录 iDRAC host、用户名所在的命令行、工具路径、预设名和本机路径；共享日志前请按 [安全说明](SECURITY.md) 检查敏感信息。

当前限制：运行日志按天分文件，但没有自动保留期或自动清理策略；长期轮询会持续增长文件，需要用户手动归档或删除旧日志。

## 软件恒温策略

软件恒温策略每次 tick 会执行一次 `sdr elist`，解析 CPU 温度，并按下面规则计算全部风扇百分比：

- CPU 温度小于或等于目标温度：使用自动策略最小风扇百分比。
- CPU 温度大于或等于高温阈值：使用自动策略最大风扇百分比。
- CPU 温度位于目标温度与高温阈值之间：按线性比例插值。
- CPU 温度达到或超过紧急自动阈值：发送 Dell 自动模式命令，让 iDRAC/BMC 接管风扇。

CPU 温度选择逻辑优先使用名称包含 `CPU` 的温度传感器；如果没有 CPU 命名行，则在所有温度传感器中取最高值。找不到温度传感器时会报错。

曲线预设使用同一个 tick 和同一个紧急保护入口，但计算百分比的来源不同：

- 用户在风扇控制页通过曲线图和点位数字控件添加或编辑曲线点；曲线图点击会按当前位置生成一个温度/风扇百分比点，右侧列表可继续精调。
- 保存或切换曲线预设时会校验点数、温度范围、百分比范围和重复温度；点位和 `SmoothCurve` 平滑开关都会写入本地设置。
- 当前 CPU 温度低于第一个点时使用第一个点的百分比；高于最后一个点时使用最后一个点的百分比。
- CPU 温度落在两个点之间时，默认按这两个点分段线性插值并四舍五入到整数百分比；如果该预设启用了平滑曲线，则使用同一组点做平滑过渡后再四舍五入。
- 达到紧急自动阈值时，无论当前使用全局线性策略还是曲线预设，都会优先发送 Dell 自动模式命令。
- 曲线预设仍依赖 `sdr elist`、CPU 温度识别、IPMI over LAN 和 Dell OEM raw 命令；任一环节失败都会显示错误并写入运行日志，不会假装曲线已执行。

## 轮询与并发

- 连接成功后自动启动传感器轮询。
- 每次轮询读取 `sdr elist` 并刷新表格、看板和图表数据。
- 如果上一轮 SDR 读取尚未完成，下一次 tick 会跳过并记录可见警告。
- 如果其他 IPMI 命令正在执行，轮询 tick 也会跳过，避免同时向 BMC 发起多条命令。
- 如果单次 SDR 读取耗时超过当前轮询间隔，应用会给出推荐间隔。
- 轮询失败会停止轮询、更新连接状态并显示失败原因。
- 旧版本保存过 `SensorRefreshSeconds = 1` 时，新版本启动会停在设置页并提示把轮询秒数调到 15 秒或更高；保存低于 15 秒的值会失败。这样做是为了避免连续建立 IPMI v2/RMCP+ 会话压垮 iDRAC，而不是在失败后自动重试或伪装成功。

## 单风扇控制风险

单风扇模式使用以下目标编号。注意：这里的 `0x00-0x05` 是 raw 命令里的“选择哪个风扇”的编号，不是风扇转速；真正的转速百分比是命令最后一个参数。

| 风扇 | 目标编号 |
| --- | --- |
| 全部风扇 | `0xff` |
| Fan 1 | `0x00` |
| Fan 2 | `0x01` |
| Fan 3 | `0x02` |
| Fan 4 | `0x03` |
| Fan 5 | `0x04` |
| Fan 6 | `0x05` |

本机 R730xd/iDRAC 2.82 实测目标编号 `0x00` 并不是 `0%` 转速，也没有单独控制 Fan 1，而是让全部风扇升到高转。因此单风扇控制默认关闭。启用前请确认你的固件行为，启用后每次操作都应观察 RPM 和温度；若行为不符合预期，请立即切回 Dell 自动模式。

## 安全提醒

风扇控制会直接影响服务器散热余量。低转速可能导致 CPU、硬盘、PCIe 卡、电源或机箱内部温度升高。调整风扇后请持续观察以下内容：

- CPU 温度和 CPU 使用率。
- Inlet / Exhaust 温度。
- 硬盘、背板、线缆在位和冗余状态。
- Fan 1-6 RPM。
- 功耗、电压、电流。
- iDRAC 自身告警。

如果服务器负载未知、机箱里有大量硬盘、环境温度高，或传感器状态异常，应优先使用 Dell 自动模式。

更多凭据、日志、命令可见性和供应链说明见 [安全说明](SECURITY.md)。

## 故障排查

### 缺少内置 ipmitool

错误通常类似“Bundled ipmitool.exe is missing from the application output”。请确认构建输出目录包含：

```text
BundledTools\ipmitool\ipmitool.exe
```

项目文件已配置 `BundledTools\ipmitool\**\*` 复制到输出目录。如果发布包缺失该目录，需要检查发布流程是否排除了内容文件。

### 认证失败或权限不足

检查 iDRAC 地址、用户名、密码和用户权限。该应用需要能执行 Dell OEM raw IPMI 命令的账号。只读或受限账号可能能读取 SDR，但不能控制风扇。

### 传感器为空

如果 `ipmitool` 成功退出但没有 SDR 行，应用会直接报错。请在命令行单独验证：

```powershell
$env:IPMI_PASSWORD = "<your-password>"
.\BundledTools\ipmitool\ipmitool.exe -I lanplus -H <host> -U <user> -E sdr elist
```

### 轮询提示耗时过长或 RMCP+ 会话失败

完整 `sdr elist` 读取可能需要数秒到十几秒；本机 R730xd/iDRAC 2.82 观察到约 11-13 秒。过低轮询会让软件持续建立 IPMI v2/RMCP+ 会话，可能出现 `Unable to establish IPMI v2 / RMCP+ session`。默认和最短可保存轮询间隔现在是 15 秒；如果界面仍提示单次读取超过当前间隔，请按推荐值继续提高轮询秒数。

### 图表加载失败

确认输出目录包含：

```text
Assets\Charts\dashboard.html
Assets\Charts\echarts.min.js
```

图表使用本地 WebView2 加载资源。若 WebView2 运行时不可用，请安装或修复 Microsoft Edge WebView2 Runtime。

### 运行日志写入失败

如果状态栏显示“运行日志写入失败”，请检查当前 Windows 用户是否有权限创建和追加以下目录中的文件：

```text
%LocalAppData%\DellR730xdFanControlCenter\logs
```

该失败不会被静默忽略。按钮触发的用户命令会停止并显示根因；请修复目录权限、磁盘空间或安全软件拦截后重试。

### 关闭后软件仍在运行

默认行为是点击关闭最小化到托盘。右键托盘图标可恢复窗口或退出；也可在设置中关闭“点击关闭时最小化到托盘”。

## 仓库结构

```text
Assets/                  图标、Logo、图表 HTML 和 ECharts 资源
BundledTools/ipmitool/   内置 ipmitool.exe 与运行所需 DLL
Models/                  设置、预设、传感器、看板和日志模型
Services/                IPMI 命令、运行日志、设置存储、本地化和托盘服务
docs/                    命令参考和项目元数据
MainPage.xaml            主界面布局
MainPage.xaml.cs         主页面交互、轮询、自动策略和图表数据
MainWindow.xaml.cs       窗口、托盘和关闭行为
```

## 文档维护要求

本仓库的 README 和 `docs/` 文档应保持全面、具体、可执行。新增功能、设置项、命令、风险、错误处理或发布行为时，需要同步更新中文和英文文档，说明触发条件、用户可见行为、配置默认值、已知限制和验证方式。不要只写一句功能名，也不要用模糊描述替代实际命令或流程。

## 仓库标签

`dell`, `poweredge`, `r730xd`, `idrac`, `ipmi`, `ipmitool`, `fan-control`, `server-management`, `homelab`, `winui`, `windows`, `dotnet`
