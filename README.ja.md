[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Dell PowerEdge R730xd iDRAC ファン制御センター

iDRAC/IPMI 経由で Dell PowerEdge R730xd のファンを制御し、ハードウェアを監視する Windows 10/11 向け WinUI 3 アプリです。手動速度、Dell Auto、CPU 温度/電力ファンカーブ、BMC SDR センサー、Fan 1-6 RPM、ローカルチャート、プリセット、トレイ操作をまとめて提供します。

## ダウンロードと対象範囲

- [最新 GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest) から `DellR730xdFanControlCenter-win-x64.zip` を取得し、すべて展開して `DellR730xdFanControlCenter.exe` を実行します。
- 現在のソースバージョンは `1.1.0` です。Release `v1.1.0` は対応する tag からビルドされ、同梱 exe/dll のファイルバージョンは `1.1.0.0` です。
- 対象は Dell PowerEdge R730xd です。ローカルでは R730xd / iDRAC 2.82 のみ確認しており、他のファームウェアは監視下で検証が必要です。

## 主な機能

- 全ファンの `0-100%` 制御、手動プリセット保存、Dell Auto への制御返却。
- CPU 温度による自動制御と、編集可能な温度-ファン/電力-ファンカーブ。
- 実際の `mc info` / `sdr elist` を実行し、温度、RPM、電力、電圧、電流、冗長性、離散状態を表示。
- ECharts/WebView2 による 7 日間のローカル JSONL 履歴、22 UI 言語、トレイメニュー、DPAPI で保護した任意のパスワード保存。

## クイックスタート

1. iDRAC で **IPMI over LAN** を有効にし、Windows から管理ネットワークに到達できることを確認します。
2. 展開済み Release を実行するか、ソースから起動します: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`。
3. 設定ページで iDRAC/BMC アドレス、ユーザー名、パスワードを入力します。次回の自動接続が必要な場合のみ DPAPI 保存を有効にします。
4. 保存時は `mc info` の後に `sdr elist` を実行します。コマンドとログ書き込みが実際に成功した後だけポーリングと成功表示が始まります。
5. Dell Auto または控えめな `20%`/`35%` から始め、温度と RPM を監視します。

## 既定値とローカルファイル

- ポーリング `1 s`、コマンドタイムアウト `35 s`、目標/高温/緊急温度 `68 °C` / `78 °C` / `84 °C`、自動範囲 `10-42%`、履歴 `7` 日。
- 設定: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`。
- 実行ログ: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`。
- チャート履歴/WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`。
- パスワードは `IPMI_PASSWORD` で `ipmitool -E` に渡され、コマンドライン引数には入りません。

## 失敗とハードウェア上の制限

- 認証、ネットワーク、SDR、WebView2、raw コマンド、ログ書き込みの失敗は表示・記録され、成功として扱われません。
- 手動モード切り替え後のパーセントコマンドが失敗した場合、Dell Auto 復旧コマンドを 1 回だけ送信します。元の要求は失敗のままで、自動再試行しません。
- `0x00-0x05` はファームウェアの対象セレクターであり、速度のパーセントではありません。個別ファン制御は既定で無効で、テスト機の `0x00` は全ファンを高速回転させました。
- ユーザーコマンドは IPMI ロックを待ちます。ロック使用中はバックグラウンドポーリングと自動 tick を明示的にスキップし、2 つ目の `ipmitool` プロセスは起動しません。
- 低速度や不適切なカーブは CPU、ディスク、PCIe カード、電源を過熱させる可能性があります。判断できない場合は Dell Auto を使用してください。

## 検証とドキュメント

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

これらの検査は実機サーバーでの検証の代わりにはなりません。GUI 起動、実際の raw コマンド、他の iDRAC ファームウェア、すべての DPI 組み合わせは完全には覆蓋しません。詳細は [英語ガイド](README.en-US.md)、[IPMI コマンド](docs/COMMANDS.en-US.md)、[セキュリティ](SECURITY.en-US.md) を参照してください。
