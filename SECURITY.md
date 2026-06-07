# 安全

语言：中文 | [English](SECURITY.en-US.md)

## 凭据

不要提交 iDRAC 密码。本应用可使用 Windows DPAPI 在当前 Windows 用户配置下加密保存密码。

## 命令可见性

应用使用 `ipmitool -E` 并通过 `IPMI_PASSWORD` 传递密码，避免在命令行参数中暴露密码。

## 操作安全

手动风扇转速过低可能导致过热。请持续监控传感器；当服务器负载未知或较高时，优先使用 Dell 自动模式。
