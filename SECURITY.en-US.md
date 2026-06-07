# Security

Language: [中文](SECURITY.md) | English

## Credentials

Do not commit iDRAC passwords. The app can store the password locally using Windows DPAPI under the current Windows user profile.

## Command Visibility

The app uses `ipmitool -E` and passes the secret through `IPMI_PASSWORD`, avoiding command-line password exposure.

## Operational Safety

Manual fan speeds can cause overheating if set too low. Keep monitoring sensors and use Dell automatic mode when the server is under unknown or high load.
