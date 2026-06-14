[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# R730XD スマートファンセンター

Dell PowerEdge R730xd 向けの WinUI 3 アプリです。iDRAC/IPMI によるファン制御、BMC センサー監視、履歴グラフを扱います。完全な長文ドキュメントは [简体中文](README.md) と [English](README.en-US.md) にあり、このページは日本語の要点です。

## クイックスタート

1. iDRAC で **IPMI over LAN** を有効にし、Windows から管理ネットワークへ到達できることを確認します。
2. ソースから起動します: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`。
3. Settings で iDRAC/BMC のホスト、ユーザー名、パスワードを入力します。自動接続が必要な場合だけ DPAPI 保存を有効にします。
4. Save settings 後、実際の `mc info` と `sdr elist` が成功した場合だけ継続ポーリングを開始します。
5. まず Dell Auto または 20%/35% の控えめな値を使ってください。低速回転や単一 Fan 対象は監視しながら検証してください。

## 重要な動作

- 失敗は stdout/stderr、UI 状態、JSONL ログに表示され、成功のようには隠しません。
- `RPM`、`W`、`V`、`A`、`°C`、`iDRAC`、`IPMI`、`BMC`、`SDR`、`ipmitool` は専門用語または単位として保持します。
- ランタイムログは `%LocalAppData%\DellR730xdFanControlCenter\logs`、グラフ履歴は `%LocalAppData%\DellR730xdFanControlCenter\chart-history` に保存されます。
- ファン制御は冷却余裕に直接影響します。負荷、室温、センサー状態が不明な場合は Dell Auto を優先してください。
