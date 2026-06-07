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

不同 iDRAC 固件、不同背板和不同风扇/传感器布局可能改变 raw target 行为。尤其是单风扇 target byte，当前代码已实现但默认关闭，必须先确认固件映射再启用。

## 功能总览

- 现代 WinUI 3 界面，支持浅色、深色、跟随系统主题。
- 全部风扇 0-100% 百分比控制，设置前会切入手动风扇模式。
- 本机默认还原动作：手动模式 + 全部风扇 10%。
- 内置预设：默认 10%、均衡 20%、散热 35%、性能 50%、Dell 自动。
- 支持添加、保存、删除自定义手动百分比预设；内置预设不可删除。
- Dell 自动风扇模式保留为独立操作和预设入口，可把控制权交还给 iDRAC/BMC。
- 1-6 号风扇单独目标字节控制已实现，但默认关闭。
- 软件恒温策略会读取 BMC 传感器中的 CPU 温度，并在线性区间内调整全部风扇。
- 达到紧急温度阈值时，软件恒温策略会切回 Dell 自动模式。
- 连接或保存设置成功后启动持续 SDR 轮询，最短轮询间隔为 1 秒。
- 内置 `ipmitool.exe` 与所需 Cygwin DLL，路径为 `BundledTools/ipmitool`。
- 内置本地 ECharts 仪表板资产，路径为 `Assets/Charts/dashboard.html` 与 `Assets/Charts/echarts.min.js`，运行时不依赖在线 CDN。
- 托盘图标支持最小化后台运行、恢复窗口、快捷预设、全部风扇 20/35/50%、还原手动 10%、打开设置和退出。
- iDRAC Web 控制台快捷入口会打开 `https://<host>/`。
- 可见 UI 字符串接入多语言资源，当前内置简体中文和英文；仓库默认显示中文 README，英文内容放在独立文件中。
- 图表、看板和状态提示使用本地化显示名；传感器表格保留管理控制器返回的原始 `Key`，方便排查命令输出。
- 密码可使用 Windows DPAPI 在当前 Windows 用户上下文加密保存。
- 启动异常会写入 `%LocalAppData%\DellR730xdFanControlCenter\startup-error.log`。

## 界面结构

### 总览

总览页用于观察当前硬件状态和执行最常用操作：

- 顶部显示当前 iDRAC 目标、连接状态和轮询状态。
- 指标卡展示最高 CPU 温度、风扇状态和当前控制模式。
- 交互式数据可视化展示总体趋势、硬件画像、单项温度排行、单风扇 RPM、性能/电气数据，以及按硬件类型和状态分组的树图。
- 温度大看板逐项展示 BMC SDR 中每一个温度传感器。
- 风扇 RPM 看板展示 Fan 传感器当前转速。
- 功耗与状态看板展示功耗、电压、电流、冗余、电池、入侵、Power Optimized 等状态类传感器。
- 快速操作包含刷新传感器、还原 10%、打开 iDRAC、全部风扇百分比设置、启动/停止软件恒温策略。
- 最新日志展示最近命令、成功/失败状态和轮询提示。

### 风扇控制

风扇控制页用于管理预设和高级控制：

- 预设模式区显示当前运行模式、内置预设、自定义预设和每个预设的说明。
- 手动预设会发送 Dell OEM raw 命令设置全部风扇百分比。
- Dell 自动预设会发送命令恢复 BMC 固件自动风扇策略。
- 添加手动预设时必须填写名称，百分比会按 0-100 校验。
- 保存预设会写入本地设置文件，托盘菜单也会读取这些预设。
- 单风扇控制区默认禁用，需要在设置页开启并保存。
- 自动策略参数包括目标温度、高温阈值、紧急自动阈值、轮询秒数、最小风扇百分比和最大风扇百分比。

### 传感器

传感器页展示 `ipmitool sdr elist` 解析后的完整表格：

- `Key`：管理控制器返回的原始传感器名称，例如 `Fan1 RPM`、`Inlet Temp`、`CPU Usage`。
- `ID`：SDR 输出中的传感器 ID。
- `Entity`：SDR 输出中的实体信息。
- `Value`：原始读数文本中的数值或状态。
- `Unit`：单位，例如 `degrees C`、`RPM`、`Watts`、`Volts`、`Amps`、`percent`。
- `Status`：BMC 返回的状态，例如 `ok`、`ns`、`na` 或异常状态。

如果 `ipmitool` 成功退出但没有返回任何 SDR 行，应用会直接报错，不会构造假传感器数据。

### 设置

设置页控制连接、持久化和运行行为：

- iDRAC/BMC IP 或主机名。
- iDRAC 用户名。
- iDRAC 密码。
- 是否使用 DPAPI 保存密码。
- 只读内置 `ipmitool.exe` 路径。
- 点击关闭是否最小化到托盘。
- 是否启用单风扇 raw target。
- 风扇数量，默认 6。
- 命令超时秒数，默认 35，代码要求至少 5。
- SDR 轮询秒数，默认 1，保存时低于 1 会归一化为 1。
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
| DefaultAllFanPercent | `10` | 本机默认还原速度。 |
| EnableIndividualFanTargets | `false` | 单风扇 raw target 默认关闭。 |
| SensorRefreshSeconds | `1` | 最短轮询间隔。实际 SDR 返回速度取决于 iDRAC。 |
| CommandTimeoutSeconds | `35` | 单条 `ipmitool` 命令超时。 |
| TargetCpuTemperatureCelsius | `68` | 软件恒温策略目标温度。 |
| HighCpuTemperatureCelsius | `78` | 达到后使用自动策略最大风扇百分比。 |
| EmergencyCpuTemperatureCelsius | `84` | 达到后切回 Dell 自动模式。 |
| AutoMinimumFanPercent | `10` | 软件恒温策略最小风扇百分比。 |
| AutoMaximumFanPercent | `42` | 软件恒温策略最大风扇百分比。 |
| Theme | `Default` | 跟随系统。 |
| Language | `zh-CN` | 默认简体中文。 |

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
8. 先用 Dell 自动模式或较保守的手动百分比确认机器行为，再尝试更低转速。

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

## 软件恒温策略

软件恒温策略每次 tick 会执行一次 `sdr elist`，解析 CPU 温度，并按下面规则计算全部风扇百分比：

- CPU 温度小于或等于目标温度：使用自动策略最小风扇百分比。
- CPU 温度大于或等于高温阈值：使用自动策略最大风扇百分比。
- CPU 温度位于目标温度与高温阈值之间：按线性比例插值。
- CPU 温度达到或超过紧急自动阈值：发送 Dell 自动模式命令，让 iDRAC/BMC 接管风扇。

CPU 温度选择逻辑优先使用名称包含 `CPU` 的温度传感器；如果没有 CPU 命名行，则在所有温度传感器中取最高值。找不到温度传感器时会报错。

## 轮询与并发

- 连接成功后自动启动传感器轮询。
- 每次轮询读取 `sdr elist` 并刷新表格、看板和图表数据。
- 如果上一轮 SDR 读取尚未完成，下一次 tick 会跳过并记录可见警告。
- 如果其他 IPMI 命令正在执行，轮询 tick 也会跳过，避免同时向 BMC 发起多条命令。
- 如果单次 SDR 读取耗时超过当前轮询间隔，应用会给出推荐间隔。
- 轮询失败会停止轮询、更新连接状态并显示失败原因。

## 单风扇控制风险

单风扇模式使用以下 target byte：

| 风扇 | target byte |
| --- | --- |
| 全部风扇 | `0xff` |
| Fan 1 | `0x00` |
| Fan 2 | `0x01` |
| Fan 3 | `0x02` |
| Fan 4 | `0x03` |
| Fan 5 | `0x04` |
| Fan 6 | `0x05` |

本机 R730xd/iDRAC 2.82 实测 `0x00` 并没有单独控制 Fan 1，而是让全部风扇升到高转。因此单风扇控制默认关闭。启用前请确认你的固件行为，启用后每次操作都应观察 RPM 和温度；若行为不符合预期，请立即切回 Dell 自动模式。

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

### 轮询提示耗时过长

1 秒是请求发起间隔，不代表 BMC 能 1 秒返回完整 SDR。若 SDR 读取耗时超过间隔，按界面推荐值提高轮询秒数。

### 图表加载失败

确认输出目录包含：

```text
Assets\Charts\dashboard.html
Assets\Charts\echarts.min.js
```

图表使用本地 WebView2 加载资源。若 WebView2 运行时不可用，请安装或修复 Microsoft Edge WebView2 Runtime。

### 关闭后软件仍在运行

默认行为是点击关闭最小化到托盘。右键托盘图标可恢复窗口或退出；也可在设置中关闭“点击关闭时最小化到托盘”。

## 仓库结构

```text
Assets/                  图标、Logo、图表 HTML 和 ECharts 资源
BundledTools/ipmitool/   内置 ipmitool.exe 与运行所需 DLL
Models/                  设置、预设、传感器、看板和日志模型
Services/                IPMI 命令、设置存储、本地化和托盘服务
docs/                    命令参考和项目元数据
MainPage.xaml            主界面布局
MainPage.xaml.cs         主页面交互、轮询、自动策略和图表数据
MainWindow.xaml.cs       窗口、托盘和关闭行为
```

## 文档维护要求

本仓库的 README 和 `docs/` 文档应保持全面、具体、可执行。新增功能、设置项、命令、风险、错误处理或发布行为时，需要同步更新中文和英文文档，说明触发条件、用户可见行为、配置默认值、已知限制和验证方式。不要只写一句功能名，也不要用模糊描述替代实际命令或流程。

## 仓库标签

`dell`, `poweredge`, `r730xd`, `idrac`, `ipmi`, `ipmitool`, `fan-control`, `server-management`, `homelab`, `winui`, `windows`, `dotnet`
