# Security / 安全

## Credentials / 凭据

Do not commit iDRAC passwords. The app can store the password locally using Windows DPAPI under the current Windows user profile.

不要提交 iDRAC 密码。本应用可使用 Windows DPAPI 在当前 Windows 用户配置下加密保存密码。

## Command Visibility / 命令可见性

The app uses `ipmitool -E` and passes the secret through `IPMI_PASSWORD`, avoiding command-line password exposure.

应用使用 `ipmitool -E` 并通过 `IPMI_PASSWORD` 传递密码，避免在命令行参数中暴露密码。

## Operational Safety / 操作安全

Manual fan speeds can cause overheating if set too low. Keep monitoring sensors and use Dell automatic mode when the server is under unknown or high load.

手动风扇转速过低可能导致过热。请持续监控传感器；当服务器负载未知或较高时，优先使用 Dell 自动模式。
