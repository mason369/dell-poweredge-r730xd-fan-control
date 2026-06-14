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

命令日志会显示工具路径、参数、退出码和耗时，不显示密码。总览页“最新日志”会把信息、警告、成功、错误/失败显示为不同颜色状态标记，其中只有错误和失败使用红色；本地 JSONL 运行日志不写入颜色值，只写入 `level`、`displayLevel`、`succeeded`、`exitCode` 等结构化字段。运行日志同时会为用户命令、传感器刷新和软件恒温 tick 写入起止时间段记录。

## 发布与压缩包验证命令

GitHub Actions 下载 zip 与本机 Release zip 使用同一条本地脚本生成：

```powershell
cd C:\DellR730xdFanControlCenter
.\tools\Publish-ReleaseZip.ps1
```

脚本会先执行 `tools\Publish-UnpackagedExe.ps1` 生成 `artifacts/exe/win-x64/`，再创建 `artifacts/release/DellR730xdFanControlCenter-win-x64.zip`。创建后会把 zip 解压到临时目录，检查 `DellR730xdFanControlCenter.exe`、`Microsoft.WindowsAppRuntime.dll`、`Microsoft.ui.xaml.dll`、`DellR730xdFanControlCenter.pri`、`Assets/Charts/dashboard.html`、`Assets/Charts/echarts.min.js` 和 `BundledTools/ipmitool/ipmitool.exe`。该 zip 是 unsigned unpackaged 下载物，不走 MSIX 安装；如果解压内容包含 `.msix`、`.pfx`、`.cer`、`AppxManifest.xml` 或 `Package.appxmanifest`，脚本会失败停止，避免 Release 包被证书信任链或包身份问题影响。本机验证下载后的 zip 能否启动时运行：

```powershell
.\tools\Publish-ReleaseZip.ps1 -VerifyLaunch
```

`-VerifyLaunch` 会从解压后的 zip 启动 exe，等待顶层窗口和窗口标题，并检查本次启动后是否出现新的 `.NET Runtime` 或 `Application Error` 事件；验证结束后会关闭该临时启动进程并删除临时解压目录。GitHub Actions 的 `Package Release` workflow 默认只做文件结构和无签名包泄漏验证，不启动 GUI，也不调用 `tools\Publish-SignedMsix.ps1`、`Add-AppxPackage` 或 `Get-AuthenticodeSignature`；手动触发时上传 workflow artifact，推送 `v*` tag 时还会用 `gh release upload --clobber` 覆盖同名 GitHub Release zip。

## 命令日志与时间段记录

运行日志路径为：

```text
%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl
```

每条日志是单行 JSON 对象，便于按行解析和审计。和命令执行直接相关的记录包括：

- `Operation/UiCommand`：设置页连接、刷新传感器、设置风扇、总览/托盘“还原戴尔出厂设置转速”、Dell 自动预设、手动 10% 预设等按钮操作。每次操作会写 `Started` 和 `Succeeded` 或 `Failed`，并带 `operationId`、`startedAt`、`finishedAt`、`durationMilliseconds`。
- `Operation/SensorRefresh`：每次 `sdr elist` 刷新。记录 host、轮询秒数、传感器数量和耗时。
- `Operation/SmartAutoPolicyTick`：每次软件自动策略 tick。记录温度阈值、CPU 温度、曲线预设类型、功耗曲线使用的 `powerWatts`、计算出的风扇百分比，或达到紧急阈值后的 Dell 自动动作。如果本轮计算出的风扇百分比与同一自动模式下上次成功下发值一致，`action` 记录为 `SkipUnchangedFanPercent`，表示本轮刷新了 SDR 和图表但没有重复发送风扇 raw 控制命令。如果已经计算出目标百分比但后续 raw 风扇命令失败，`Failed` 记录仍会保留 `cpuTemperatureCelsius`、`fanPercent`、`action = SetAllFansManualSpeed`，功耗曲线还会保留 `powerWatts`，便于直接定位失败前准备下发的目标。
- `IpmiCommand/CommandCompleted`：每条实际 `ipmitool` 子命令完成。记录完整命令行、退出码、成功状态和耗时。

如果运行日志无法写入，应用不会把按钮触发的用户命令显示为成功；状态栏和最近日志会显示“运行日志写入失败”。当前没有自动清理策略，持续轮询会让当天 JSONL 文件增长。

## 必要 iDRAC 设置

- 启用 IPMI over LAN。
- 使用具备 OEM raw 命令权限的账号。
- 确保运行本软件的 Windows 主机可以访问 iDRAC。
- 确保防火墙、VLAN、VPN 或管理网络不会阻断 IPMI over LAN。
- 如果只读 SDR 可以成功但 raw 命令失败，请检查 iDRAC 账号权限。

## 应用命令清单

| 用途 | IPMI 参数 | UI 入口 | 说明 |
| --- | --- | --- | --- |
| 开始轮询前测试连接 | `mc info` | 总览/设置页“开始轮询”、保存设置后自动开始轮询 | 用于确认 host、用户、密码、网络和 `ipmitool` 基本可用；测试成功后继续读取一次 SDR 并启动传感器轮询。 |
| 读取传感器 | `sdr elist` | 总览/传感器刷新、开始轮询后的持续轮询、软件恒温策略 | 返回完整 SDR 行，应用解析温度、风扇、功耗、电压、电流和状态。 |
| 进入手动风扇模式 | `raw 0x30 0x30 0x01 0x00` | 设置全部风扇、手动预设、内置“默认/恢复手动 10%”预设、软件恒温策略 | 设置百分比前会先发送该命令。总览和托盘的“还原戴尔出厂设置转速”不发送该命令。 |
| 设置全部风扇百分比 | `raw 0x30 0x30 0x02 0xff <percent-hex>` | 全部风扇控制、手动预设、托盘全部风扇菜单 | `0xff` 表示全部风扇。 |
| 设置单风扇百分比 | `raw 0x30 0x30 0x02 <fan-target-selector> <percent-hex>` | 单风扇控制 | 默认关闭。`<fan-target-selector>` 是风扇目标编号，不是转速；启用前必须确认固件映射。 |
| 恢复 Dell 自动模式 | `raw 0x30 0x30 0x01 0x01` | 总览/托盘“还原戴尔出厂设置转速”、Dell 自动预设、紧急温度保护 | 把风扇控制权交回 iDRAC/BMC 固件策略；如果软件自动计时器仍在运行，后续 tick 仍可能再次发送软件风扇命令。 |

## 风扇 raw 命令失败处理

Dell 风扇控制 raw 命令指所有以 `raw 0x30 0x30` 开头的命令，包括进入手动模式、设置全部风扇、设置单风扇和恢复 Dell 自动模式。若这类命令的 `ipmitool` 子进程返回非零退出码，应用会立即把该操作标记为失败，写入一次 `IpmiCommand/CommandCompleted` 记录，并把本次 stdout/stderr 显示给用户。

软件自动或曲线自动已经完成 SDR 读取并计算出目标百分比后，如果 `raw 0x30 0x30 0x02 ...` 失败，失败的 `Operation/SmartAutoPolicyTick` 记录会带上本轮 CPU 温度、功耗读数、目标百分比和动作名。该记录用于排查，不代表命令成功；应用仍会显示真实失败并停止该自动策略周期。

`mc info`、`sdr elist`、raw 命令、参数校验失败、缺少 `ipmitool.exe` 和命令超时都不会重试同一条失败命令、延迟重试或写入“将重试”记录。传感器后台轮询失败会先停止本轮轮询并显示根因，然后执行一次真实重连流程（`mc info` + `sdr elist`）；重连成功才恢复持续轮询，重连失败继续显示真实错误并保持断开，不会写入伪造图表历史点。

## 托盘右键菜单

托盘菜单按操作风险和使用频率组织，避免把常用风扇动作藏在多级子菜单中：

- `打开主窗口`、`打开总览`、`打开风扇控制`、`打开传感器`、`设置`：只恢复窗口并切换页面，不执行 IPMI 命令。
- `刷新传感器`：执行 `sdr elist`，成功后刷新表格、看板和图表，并写入一个可在总览图表“历史范围”中查看的完整 JSONL 图表历史点；失败时显示真实错误并写入运行日志，不会生成假历史点。
- `打开远程管理网页`：使用当前设置中的 host 打开 `https://<host>/`。
- `打开日志文件夹`：打开 `%LocalAppData%\DellR730xdFanControlCenter\logs`，如果目录不存在则创建目录。
- `还原戴尔出厂设置转速`：发送 `raw 0x30 0x30 0x01 0x01`，不是手动 10%。
- `停止自动策略`：停止软件自动计时器并清除当前曲线状态，不发送 IPMI 命令。
- `全部风扇 20%`、`全部风扇 35%`、`全部风扇 50%`：直接发送手动全部风扇百分比命令，发送前仍会进入手动模式。
- `预设模式`：唯一保留的一层子菜单，动态读取 `settings.json` 中保存的预设。手动预设显示百分比，曲线预设显示“曲线”标识。
- `退出`：关闭应用并移除托盘图标。

所有会执行 IPMI 的托盘动作都共用应用内同一把 IPMI 锁。已有 IPMI 命令运行时，用户主动触发的托盘命令会等待当前命令结束后继续执行，不会启动并发 `ipmitool` 进程，也不会把等待中的切换伪装成已经完成。后台轮询、软件自动和曲线自动 tick 仍然在 IPMI 忙时跳过；同一段忙碌期间只把第一条跳过原因写入页面日志和运行日志。

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

### 开始或取消轮询

```text
mc info
sdr elist
```

点击“开始轮询”会先执行 `mc info`，然后读取一次 `sdr elist`，成功后启动持续传感器轮询。总览页会显示最后轮询时间和本次读取耗时。轮询启动后，同一按钮显示为“取消轮询”；点击“取消轮询”不会执行新的 IPMI 命令，只会停止后续轮询 tick、更新连接/轮询状态并写入运行日志。如果点击时已有一条 `ipmitool` 命令正在执行，该命令会按现有流程结束，应用不会为取消动作再启动新的 `ipmitool` 进程。

### 刷新传感器

```text
sdr elist
```

刷新会更新传感器表格、温度看板、风扇 RPM 看板、功耗与状态看板，以及交互式图表数据。成功刷新还会保存一个 JSONL 图表历史点，包含当时的摘要、温度、风扇、性能/电气、类型统计和传感器树，并写入 `timestamp` 与 `unixMilliseconds`。总览图表右上角“历史范围”可切换“当前、近 6 小时、近 1 天、近 3 天、近 7 天、自定义”；应用默认保留最近 7 天历史，启动时从 `%LocalAppData%\DellR730xdFanControlCenter\chart-history` 加载仍在保留期内的数据。刷新失败不会写入历史。

用户触发的风扇控制命令成功后，应用会在命令释放 IPMI 锁后立即追加执行这一轮 `sdr elist` 刷新，而不是等待下一次轮询 tick。该刷新会用 BMC 真实返回更新总览看板、风扇 RPM、性能/电气图表和 JSONL 历史点；如果刷新失败，界面和日志显示真实错误，不会用刚下发的百分比伪造图表数据。刷新成功后，下一次后台轮询会从刷新完成时重新计时，避免马上重复读取。

### 设置全部风扇为 20%

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff 0x14
```

第一条命令进入手动模式，第二条命令设置全部风扇 target。

如果这两条命令成功，应用随后会立即执行一次 `sdr elist`，因此总览风扇转速看板和性能图表不再只依赖设置页的轮询秒数等待下一轮刷新。该后置刷新仍然是串行 IPMI 操作；它失败时会显示失败原因，但不会回滚已经成功下发的风扇命令。

### 内置恢复手动预设

内置 `restore-manual` 预设是手动模式 + 全部风扇 10%。它只在用户切换该预设时执行，不再是总览或托盘“还原戴尔出厂设置转速”的行为：

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff 0x0a
```

### 恢复 Dell 自动模式

```text
raw 0x30 0x30 0x01 0x01
```

该命令让 iDRAC/BMC 固件自动策略接管风扇。总览快速操作和托盘菜单中的“还原戴尔出厂设置转速”会执行这一条命令；成功后界面显示 Dell 自动模式，失败时显示真实 IPMI 错误，不会假装已经恢复。该命令不会停止已经运行的软件自动计时器；如果需要持续固件接管，请点击“停止自动”。

成功切回 Dell 自动模式后同样会立即追加一次 `sdr elist` 刷新，用实际固件策略下的传感器读数刷新看板、图表和历史点。

### 软件恒温策略 tick

每次 tick 会执行：

```text
sdr elist
```

然后应用解析 CPU 温度：

- 小于或等于目标温度：使用自动策略最小风扇百分比。
- 大于或等于高温阈值：使用自动策略最大风扇百分比。
- 位于目标温度到高温阈值这段策略曲线内：按当前温度在该线性策略曲线上的位置求风扇百分比。
- 达到或超过紧急自动阈值：发送 Dell 自动模式命令；软件自动计时器不会因此自动停止。

软件自动 tick 与传感器轮询和用户触发的风扇命令互斥。用户主动启动软件自动或切换曲线预设时，应用会等待当前 IPMI 命令完成，再执行首轮 tick；首轮成功后才启动后台计时器，首轮失败会显示真实错误且不会启动后台计时器。后台 tick 触发时如果上一轮自动策略仍在运行，或已有 IPMI 命令占用锁，本次自动策略周期会跳过；同一段忙碌期间只记录第一条跳过日志，不会打开或覆盖顶部 InfoBar，也不会启动新的 `ipmitool` 进程或 RMCP+ 会话。
后台 tick 成功读取 SDR 并计算出目标百分比后，会先与同一自动模式最近一次成功下发的全部风扇百分比比较；如果一致，本轮只更新传感器、看板、图表和历史点，并记录“未下发风扇命令”，不会重复执行 `raw 0x30 0x30 0x02 ...`。切换自动模式、手动全部风扇、单风扇、Dell 自动或停止自动会清除该缓存，下一次自动接管会重新下发需要的目标百分比。

如果当前运行的是曲线预设，tick 仍然先执行同一个 `sdr elist`，但百分比不再来自全局目标/高温阈值，而是来自保存的曲线点。曲线分为两类：

- 温度曲线保存为 `Kind = TemperatureCurve`，点位包含 `TemperatureCelsius` 和 `FanPercent`；功耗曲线保存为 `Kind = PowerCurve`，点位包含 `PowerWatts` 和 `FanPercent`。两者都保存在 `%LocalAppData%\DellR730xdFanControlCenter\settings.json` 的 `Presets[].CurvePoints`，并保存同一个 `SmoothCurve` 开关。
- 风扇控制页用两个图形编辑器维护点位：上方温度曲线图在空白处点击会生成温度/风扇百分比点；下方功耗曲线图在空白处点击会生成功耗瓦数/风扇百分比点。鼠标移入曲线图会显示十字辅助线、当前温度/功耗和风扇速度百分比；拖动已有点位会实时更新曲线点位和右侧对应数字控件，右侧点位列表也可用数字控件继续微调；拖动中只执行轻量绘制，完整预览文本和严格校验在松手、右侧数值编辑、添加点或保存时刷新；已有曲线预设可从预设卡片载入对应编辑器，页面会自动滚到匹配图表。保存成功后，页面会滚回刚新增或刚更新的预设卡片。
- 温度曲线保存或切换时至少需要 2 个点，温度范围为 `-40` 到 `125` °C，风扇百分比范围为 `0` 到 `100`，温度点不能重复。功耗曲线至少需要 2 个点，功耗范围为 `0` 到 `1200` W，风扇百分比范围为 `0` 到 `100`，功耗点不能重复。
- 温度曲线使用 CPU 温度计算百分比；功耗曲线使用本轮 SDR 中单位包含 `Watts` 或名称包含 `Pwr Consumption` 的功耗传感器计算百分比。功耗曲线仍会先解析 CPU 温度并检查紧急自动阈值；找不到 CPU 温度或找不到功耗读数都会显示错误并停止本轮，不会下发风扇命令。计算出的百分比与同一曲线预设上次成功下发值一致时，也不会重复下发风扇命令。
- 输入值低于第一个点时使用第一个点，高于最后一个点时使用最后一个点；其余情况会把当前温度或当前功耗代入由点位连接成的曲线，按该位置求风扇百分比并四舍五入为整数百分比。
- 如果预设启用了 `SmoothCurve`，点位和端点仍相同，但中间百分比会按平滑曲线过渡后再四舍五入；最终发送的仍是单个 `<calculated-percent-hex>` 全部风扇百分比。
- 达到紧急自动阈值时，曲线计算结果不会继续下发，应用先发送 Dell 自动模式命令；该动作不停止软件自动计时器。

如果未达到紧急阈值，应用会发送：

```text
raw 0x30 0x30 0x01 0x00
raw 0x30 0x30 0x02 0xff <calculated-percent-hex>
```

如果达到紧急阈值，应用会发送：

```text
raw 0x30 0x30 0x01 0x01
```

## 单风扇目标编号

下表中的 `0x00-0x05` 是 raw 命令第三段之后的风扇目标编号，用来选择 Fan 1-6，不是转速百分比。转速百分比是最后一个 `<percent-hex>` 参数；例如百分比表里的 `0% = 0x00` 只适用于 `<percent-hex>`，不代表 Fan 1 目标编号 `0x00` 会让风扇停转。

| 目标 | 目标编号 |
| --- | --- |
| 全部风扇 | `0xff` |
| Fan 1 | `0x00` |
| Fan 2 | `0x01` |
| Fan 3 | `0x02` |
| Fan 4 | `0x03` |
| Fan 5 | `0x04` |
| Fan 6 | `0x05` |

UI 已实现单风扇目标编号控制，但默认关闭。本机 R730xd/iDRAC 2.82 实测目标编号 `0x00` 不是 `0%` 转速，也未能单独控制 Fan 1，而是导致全部风扇高转。请将单风扇目标编号视为固件相关能力。

## 轮询行为

点击“开始轮询”或保存设置成功后，应用会测试连接、读取一次 SDR，并启动持续传感器轮询；轮询运行后按钮变为“取消轮询”，点击后停止后续轮询 tick。轮询秒数在设置页“应用设置”中编辑，并通过设置页顶部横跨两栏的“保存设置”命令保存；默认间隔为 1 秒，保存时允许 1 秒及以上的值。需要注意：

- 1 秒是轮询 tick 的发起频率，不是 SDR 数据的实时流式返回速度；实际耗时以总览页和运行日志为准。
- 每次成功轮询会追加一个 JSONL 图表历史点；上一轮未完成、IPMI 忙或轮询失败时不会追加历史点，因为这些情况没有新的 SDR 数据可展示。历史加载或写入失败会显示错误并写入运行日志，不会被当成成功。
- 软件自动或曲线自动策略运行时，普通传感器轮询 tick 不再独立发起 `sdr elist`；自动策略 tick 的 SDR 结果负责刷新传感器、看板、图表和历史点。停止自动策略后，普通轮询按原设置继续触发。
- 如果上一次 SDR 读取尚未完成，本次 tick 会跳过；同一段上一轮未完成期间只记录第一条跳过日志，日志写入页面日志和运行 JSONL 日志，不会打开或覆盖顶部 InfoBar。
- 如果其他 IPMI 命令正在执行，本次 tick 会跳过，避免并发命令冲突；同一段 IPMI 忙碌期间只记录第一条跳过日志。
- 跳过 tick 不会启动新的 `ipmitool` 进程，也不会建立新的 RMCP+ 会话。它不是成功请求，也不会更新为“成功”状态。
- 如果单次读取耗时超过当前轮询间隔，应用会根据实际耗时在顶部提示建议轮询间隔。
- 如果轮询命令失败，应用会停止当前轮询、标记断开并显示失败原因，然后释放 IPMI 锁并执行一次真实重连流程（`mc info` + `sdr elist`）。重连成功才恢复持续轮询；重连失败继续显示真实错误并保持断开，不会静默降级或伪装成功。

后台轮询失败后的重连不会重新应用上次保存的手动预设、Dell 自动预设、温度曲线或功耗曲线，避免一次 SDR 读取故障触发重复下发风扇命令。手动点击“开始轮询”或保存设置后的首次连接仍会在连接成功后恢复保存的运行状态。

本机 R730xd/iDRAC 2.82 观察到完整 SDR 读取约 11-13 秒。若你的环境中顶部提示单次读取超过当前间隔，可以手动把轮询间隔设置为略高于实际 SDR 读取耗时；应用不会强制改写该设置。

## 运行状态保存与恢复

- 手动预设、Dell 自动预设、温度曲线、功耗曲线或软件恒温策略首轮执行成功后，应用会把运行状态写入 `%LocalAppData%\DellR730xdFanControlCenter\settings.json`。
- `LastRunningPresetId` 保存最近一次成功运行的预设 ID；`LastSmartAutoPolicyRunning` 保存软件恒温策略是否是最近一次成功运行状态。两者同时存在时，预设 ID 优先，加载设置时会把 `LastSmartAutoPolicyRunning` 规范化为 `false`。
- 下次打开软件后，应用仍会先执行“开始轮询”流程：`mc info` 成功、一次 `sdr elist` 成功并启动轮询后，才会重新执行上次保存的预设或软件恒温策略。连接或首轮读取失败时不会假装恢复成功。
- 如果保存的预设 ID 已被删除、预设类型无效、曲线无匹配功耗读数，或 IPMI 命令失败，界面会显示真实错误并写入运行日志。
- 保存正在运行的手动预设、Dell 自动预设、温度曲线或功耗曲线后，应用会等待当前 IPMI 命令完成并立即重新应用该预设；不需要再点击“切换”。保存活动曲线会立即执行一轮真实 `sdr elist` 和曲线百分比计算，首轮失败则停止该自动策略。
- 用户主动点击切换预设、应用预设、全部风扇百分比或恢复 Dell 自动时，如果已有 IPMI 命令正在执行，该操作会等待当前命令完成后继续执行；后台轮询和自动策略 tick 不排队，遇到 IPMI 忙仍然跳过。
- 自动策略运行期间，普通轮询不再发起第二条 SDR 读取链路；每次自动策略 tick 会读取真实 SDR 并更新传感器、图表和历史点。

## SDR 解析规则

应用读取 `sdr elist` 输出后按行解析：

- 使用 `|` 分割每一行。
- 至少需要 3 段字段。
- 对于常见 `sdr elist` 输出，状态取第 3 段，读数取第 5 段。
- 读数开头若存在数字，会拆分为 `Value`、`Unit` 和 `NumericValue`。
- 数字解析使用 invariant culture，支持整数、小数和负数。
- 没有数字前缀的读数会保留原始文本，不生成 `NumericValue`；界面显示时会按当前语言翻译已登记的离散值，例如 `No Reading`、`State Deasserted`、`Fully Redundant`、`OEM Specific`/`Vendor specific`、`Bus Uncorrectable error`。其中 `OEM Specific`/`Vendor specific` 在 R730xd 界面显示为“Dell 自定义状态”，表示 BMC 返回的是 Dell/iDRAC 私有枚举；它不是单独的故障结论，健康结论仍以同一卡片的 `Status`/“状态”行和 iDRAC 告警为准。未知读数保留 BMC 原文，不猜测翻译。
- 传感器名称会先匹配已登记的 SDR 名称和模式；未登记的英文/厂商离散事件名不会直接显示 raw key，而是显示为本地化的“硬件事件 <SDR 编号>”。原始名称仍参与内部分类，需要逐字核对时请用手动 `sdr elist` 输出。
- 如果最终没有任何传感器行，应用直接报错。

## 传感器分类规则

图表和看板会按以下规则归类：

- 温度：单位包含 `degrees C`，或名称包含 `Temp`。
- 风扇：名称以 `Fan` 开头且单位包含 `RPM`。
- 性能：名称包含 `Usage`，或单位包含 `percent`。
- 功耗：单位包含 `Watts`，或名称包含 `Pwr Consumption`。
- 电压：单位包含 `Volts` 且存在数值。
- 电流：单位包含 `Amps` 且存在数值。
- 健康状态：名称包含 `Redundancy`、`Battery`、`Intrusion` 或 `Power Optimized`；其中 `Power Optimized` 的界面显示名是“电源优化策略”。
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
- Fan 1 目标编号 `0x00`：命令接受，但它不是 `0%` 转速，也不是单风扇行为，而是全部风扇高转。

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

### SDR 轮询慢或 RMCP+ 会话失败

iDRAC 返回完整 SDR 可能需要数秒到十几秒。过低轮询会持续建立 IPMI v2/RMCP+ 会话，可能导致 `Unable to establish IPMI v2 / RMCP+ session`。软件自动或曲线自动运行时，普通轮询不会再额外读取 SDR，避免与自动策略 tick 重复建立会话。在设置页“应用设置”中把轮询间隔提高到界面推荐值或更高。后台轮询失败会触发一次可见的真实重连，但失败命令本身不会被重试，应用也不会假装失败轮询成功。

### 传感器分类不符合预期

传感器分类依赖 BMC 返回的名称和单位。不同固件或语言环境可能改变命名。需要改进分类时，应同步更新代码和文档，说明新的匹配规则。
