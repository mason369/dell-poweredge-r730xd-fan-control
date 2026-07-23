[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Dell PowerEdge R730xd iDRAC 風扇控制中心

這是一套用於 Windows 10/11 的 WinUI 3 應用程式，透過 iDRAC/IPMI 控制 Dell PowerEdge R730xd 風扇並監看硬體。功能包括手動轉速、Dell Auto、CPU 溫度與功耗曲線、BMC SDR 感測器、Fan 1-6 RPM、本機圖表、預設與系統匣快速操作。

## 下載與適用範圍

- 從 [GitHub 最新 Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest) 下載 `DellR730xdFanControlCenter-win-x64.zip`，完整解壓後執行 `DellR730xdFanControlCenter.exe`。
- 當前原始碼與最新已發布的 Release 均為 `v1.1.3`；套件內 exe/dll 的檔案版本為 `1.1.3.0`，原始碼、tag 與二進位檔對應同一版本。
- 目標硬體是 Dell PowerEdge R730xd。本機僅觀察過 R730xd / iDRAC 2.82；其他韌體必須在有人值守時驗證。

## 主要功能

- 設定全部風扇 `0-100%`，儲存手動預設，或以 Dell Auto 將控制權交回 iDRAC/BMC。
- 依 CPU 溫度自動調速，並支援可編輯的溫度-風扇與功耗-風扇曲線。
- 執行真實 `mc info` 與 `sdr elist`，顯示溫度、RPM、功耗、電壓、電流、冗餘與離散狀態。
- 使用 ECharts/WebView2 保留 7 天本機 JSONL 歷史，內建 22 種 UI 語言、系統匣選單，並可用 DPAPI 保護儲存密碼。

## 快速開始

1. 在 iDRAC 啟用 **IPMI over LAN**，確認 Windows 主機可連到管理網路。
2. 執行解壓後的 Release，或從原始碼啟動：`dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`。
3. 在「設定」頁輸入 iDRAC/BMC 位址、使用者名稱與密碼。只有需要後續自動連線時才啟用 DPAPI。
4. 儲存時先執行 `mc info`，再執行 `sdr elist`。指令成功且完成日誌寫入後，才會開始輪詢並顯示成功。
5. 先使用 Dell Auto 或保守的 `20%`/`35%`，持續觀察溫度與 RPM。

## 預設值與本機檔案

- 輪詢 `1 s`、指令逾時 `35 s`、目標/高溫/緊急溫度 `68 °C` / `78 °C` / `84 °C`、自動範圍 `10-42%`、歷史 `7` 天。
- 設定：`%LocalAppData%\DellR730xdFanControlCenter\settings.json`。
- 執行日誌：`%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`。
- 圖表歷史與 WebView2：`%LocalAppData%\DellR730xdFanControlCenter\chart-history` 與 `%LocalAppData%\DellR730xdFanControlCenter\WebView2`。
- 密碼透過 `IPMI_PASSWORD` 交給 `ipmitool -E`，不會出現在命令列參數。

## 失敗與硬體風險

- 驗證、網路、SDR、WebView2、raw 指令或日誌寫入失敗都會顯示並記錄，不會標記為成功。
- 若切入手動模式成功，但後續百分比指令失敗，程式只會發送一次 Dell Auto 安全恢復。原請求仍是失敗，不會自動重試。
- `0x00-0x05` 是韌體目標選擇碼，不是轉速百分比。單風扇控制預設關閉；測試機上的 `0x00` 會讓全部風扇高轉。
- 使用者指令會等待 IPMI 鎖；後台輪詢與自動 tick 在忙碌時會明確跳過，不會啟動第二個 `ipmitool` 進程。
- 低轉速或不當曲線可能使 CPU、硬碟、PCIe 卡與電源過熱。無法確認時應使用 Dell Auto。

## 驗證與文件

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

這些檢查不能取代目標伺服器實機驗證，也未完整覆蓋 GUI 啟動、真實 raw 指令、其他 iDRAC 韌體與所有 DPI 組合。完整說明見 [簡體中文 README](README.md)、[IPMI 指令](docs/COMMANDS.md) 與 [安全說明](SECURITY.md)。
