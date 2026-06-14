[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD 智控風扇中心

面向 Dell PowerEdge R730xd 的 WinUI 3 iDRAC/IPMI 風扇控制、BMC 感測器監控與歷史圖表工具。完整長版維護文件目前以 [简体中文](README.md) 和 [English](README.en-US.md) 為準；本頁提供繁體中文的核心入口。

## 快速使用

1. 在 iDRAC 啟用 **IPMI over LAN**，並確認 Windows 主機可連到管理網路。
2. 以原始碼執行：`dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`。
3. 在「設定」填入 iDRAC/BMC 位址、使用者名稱與密碼；需要自動連線時才啟用 DPAPI 儲存密碼。
4. 儲存設定後，程式會執行真實 `mc info` 和 `sdr elist`，成功後才開始輪詢。
5. 先使用 Dell Auto 或保守的 20%/35% 風扇百分比；低轉速和單風扇目標必須有人值守。

## 重要行為

- 失敗會顯示 stdout/stderr、狀態列與 JSONL 日誌，不會假裝成功。
- `RPM`、`W`、`V`、`A`、`°C`、`iDRAC`、`IPMI`、`BMC`、`SDR` 和 `ipmitool` 是專業名稱或單位，文件與介面會保留原文。
- 執行日誌位於 `%LocalAppData%\DellR730xdFanControlCenter\logs`，圖表歷史位於 `%LocalAppData%\DellR730xdFanControlCenter\chart-history`。
- 風扇控制會直接影響散熱；若負載未知、環境溫度高或感測器異常，優先使用 Dell Auto。
