<p align="center">
  <img src="Assets/Logo.svg" width="128" alt="R730XD 智控风扇中心图标" />
</p>

# R730XD 智控风扇中心

语言：中文 | [English](README.en-US.md)

面向 Dell PowerEdge R730xd 的 Windows WinUI 3 桌面工具，通过 iDRAC/IPMI 控制服务器风扇。软件默认中文界面，所有可见界面字符已接入 i18n，当前内置简体中文与 English 两种语言。

## 功能亮点

- 现代 WinUI 3 界面，支持浅色、深色、跟随系统主题。
- 全部风扇 0-100% 百分比控制。
- 本机默认还原模式：手动模式 + 全部风扇 10%。
- 预设模式可直接切换，也可手动添加、保存和删除自定义手动百分比规则；界面会显示当前运行模式。
- Dell 自动风扇模式保留为单独操作，并可作为预设入口切换。
- Fan 1-6 单风扇 raw target 控制已实现，但默认关闭，需确认固件映射后再启用。
- 软件恒温策略：读取 BMC 传感器，根据 CPU 温度自动调整全部风扇。
- 持续轮询：连接或保存设置成功后自动持续读取 BMC 传感器，最短轮询间隔 1 秒。
- 内置 `ipmitool.exe` 与所需 Cygwin DLL，路径为 `BundledTools/ipmitool`，不依赖外部 `C:\Program Files\...` 工具。
- 托盘行为：点击关闭最小化到托盘；右键托盘图标的“风扇控制”分组中包含预设模式、全部风扇转速和恢复手动 10%，并可进入设置、打开主窗口和退出。
- iDRAC Web 控制台快捷入口。
- 实时大看板展示每个 BMC 温度传感器、风扇 RPM、功耗、电压和平台状态。
- 内置本地 ECharts 交互式数据可视化：总体趋势、总体硬件画像、单项温度排行、单风扇 RPM、CPU/IO/MEM/SYS 性能、功耗、电压、电流，以及覆盖全部 SDR 项的硬件类型与状态树图。
- 失败显式展示：缺少内置 `ipmitool.exe`、认证失败、iDRAC 权限不足、固件不支持 raw 命令都会直接报错。
- 密码可用 Windows DPAPI 本机加密保存，不会提交到仓库。

## 界面结构

- 总览：交互式图表看板、温度大看板、风扇 RPM 看板、功耗与状态、快速操作、最新日志。
- 风扇控制：可编辑预设模式、当前运行模式、全部风扇控制、Fan 1-6 单风扇控制、软件恒温策略阈值。
- 传感器：完整 `ipmitool sdr elist` 表格。
- 设置：iDRAC 凭据、只读内置 `ipmitool.exe` 路径、托盘行为、风扇数量、轮询间隔、超时、主题、界面语言、自动策略范围。

## 运行要求

- Windows 10 2004 / build 19041 或更新版本。
- .NET 8 Desktop Runtime，或使用自包含发布包。
- 可访问的 Dell PowerEdge R730xd iDRAC/BMC。
- iDRAC 中已启用 IPMI over LAN。
- 项目已内置 `BundledTools/ipmitool/ipmitool.exe`。
- 图表资产已内置 `Assets/Charts/echarts.min.js`，运行时不依赖在线 CDN。
- iDRAC 用户具备发送 OEM raw IPMI 命令的权限。

## 构建

```powershell
cd C:\DellR730xdFanControlCenter
dotnet build .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
```

## 运行

```powershell
cd C:\DellR730xdFanControlCenter
dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64
```

首次运行或未保存密码时会自动打开设置页。设置页会显示默认主机 `192.168.1.73`、用户名 `root`、内置 `ipmitool.exe` 路径、默认还原速度 10% 和 1 秒轮询。填写账号密码并保存后即可连接；若使用 DPAPI 保存密码，应用后续启动会自动连接并开始轮询。

## IPMI 命令

命令执行层使用 `IPMI_PASSWORD` 环境变量和 `ipmitool -E`，不会把密码放入命令行参数。

核心操作见 [IPMI 命令参考](docs/COMMANDS.md)。

## 单风扇说明

单风扇模式使用 `0x00` 到 `0x05` 作为 Fan 1-6 的 target byte。该功能默认关闭，因为本机 R730xd/iDRAC 2.82 实测 `0x00` 没有单独控制 Fan 1，而是让全部风扇升到高转。请确认你的固件行为后再启用。

## 安全提醒

风扇控制会影响服务器稳定性和硬件温度。调整风扇后请持续观察 CPU、进风、排风、硬盘、PCIe 和电源传感器。若温度异常升高，请立即使用 Dell 自动模式。

## 仓库标签

`dell`, `poweredge`, `r730xd`, `idrac`, `ipmi`, `ipmitool`, `fan-control`, `server-management`, `homelab`, `winui`, `windows`, `dotnet`
