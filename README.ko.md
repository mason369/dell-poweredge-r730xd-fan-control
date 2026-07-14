[English](README.en-US.md) | [简体中文](README.md) | [繁體中文](README.zht.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Dansk](README.da.md) | [日本語](README.ja.md) | [Polski](README.pl.md) | [Русский](README.ru.md) | [Bosanski](README.bs.md) | [العربية](README.ar.md) | [Norsk](README.no.md) | [Português (Brasil)](README.br.md) | [ไทย](README.th.md) | [Türkçe](README.tr.md) | [Українська](README.uk.md) | [বাংলা](README.bn.md) | [Ελληνικά](README.gr.md) | [Tiếng Việt](README.vi.md)

# Dell PowerEdge R730xd iDRAC 팬 제어 센터

iDRAC/IPMI를 통해 Dell PowerEdge R730xd의 팬을 제어하고 하드웨어를 모니터링하는 Windows 10/11 WinUI 3 앱입니다. 수동 속도, Dell Auto, CPU 온도/전력 팬 커브, BMC SDR 센서, Fan 1-6 RPM, 로컬 차트, 프리셋, 트레이 기능을 한 곳에 제공합니다.

## 다운로드와 지원 범위

- [최신 GitHub Release](https://github.com/mason369/dell-poweredge-r730xd-fan-control/releases/latest)에서 `DellR730xdFanControlCenter-win-x64.zip`을 받아 전체를 풀고 `DellR730xdFanControlCenter.exe`를 실행하세요.
- 현재 소스와 최신 공개 Release는 모두 `v1.1.2`입니다. 패키지의 exe/dll 파일 버전은 `1.1.2.0`이며 소스, tag, 바이너리가 동일한 버전을 가리킵니다.
- 대상 하드웨어는 Dell PowerEdge R730xd입니다. 로컬에서는 R730xd / iDRAC 2.82만 확인했으며, 다른 펌웨어는 사람이 지켜보는 상태에서 검증해야 합니다.

## 주요 기능

- 전체 팬 `0-100%` 제어, 수동 프리셋 저장, Dell Auto로 제어권 반환.
- CPU 온도 자동 제어와 편집 가능한 온도-팬/전력-팬 커브.
- 실제 `mc info` 및 `sdr elist`를 실행해 온도, RPM, 전력, 전압, 전류, 이중화, 이산 상태를 표시.
- ECharts/WebView2로 7일간의 로컬 JSONL 기록, 22개 UI 언어, 트레이 메뉴, DPAPI로 보호되는 선택적 비밀번호 저장.

## 빠른 시작

1. iDRAC에서 **IPMI over LAN**을 켜고 Windows PC에서 관리망에 접속할 수 있는지 확인합니다.
2. 압축을 푼 Release를 실행하거나 소스에서 시작합니다: `dotnet run --project .\DellR730xdFanControlCenter.csproj -c Debug -p:Platform=x64`.
3. 설정 페이지에 iDRAC/BMC 주소, 사용자 이름, 비밀번호를 입력합니다. 다음 실행 시 자동 연결이 필요할 때만 DPAPI 저장을 켜세요.
4. 저장하면 `mc info`와 `sdr elist`를 순서대로 실행합니다. 명령과 로그 쓰기가 실제로 성공한 뒤에만 폴링과 성공 상태가 시작됩니다.
5. Dell Auto 또는 보수적인 `20%`/`35%`로 시작하고 온도와 RPM을 계속 확인하세요.

## 기본값과 로컬 파일

- 폴링 `1 s`, 명령 시간 제한 `35 s`, 목표/고온/긴급 온도 `68 °C` / `78 °C` / `84 °C`, 자동 범위 `10-42%`, 기록 `7`일.
- 설정: `%LocalAppData%\DellR730xdFanControlCenter\settings.json`.
- 실행 로그: `%LocalAppData%\DellR730xdFanControlCenter\logs\runtime-YYYYMMDD.jsonl`.
- 차트 기록/WebView2: `%LocalAppData%\DellR730xdFanControlCenter\chart-history`, `%LocalAppData%\DellR730xdFanControlCenter\WebView2`.
- 비밀번호는 `IPMI_PASSWORD`로 `ipmitool -E`에 전달되며 명령줄 인수에 포함되지 않습니다.

## 실패 처리와 하드웨어 제한

- 인증, 네트워크, SDR, WebView2, raw 명령, 로그 쓰기 실패는 화면과 로그에 표시되며 성공으로 처리되지 않습니다.
- 수동 모드 전환은 성공했지만 다음 퍼센트 명령이 실패하면 Dell Auto 복구 명령을 딱 한 번 보냅니다. 원래 요청은 실패로 남고 자동 재시도하지 않습니다.
- `0x00-0x05`는 펌웨어 대상 선택자이지 속도 퍼센트가 아닙니다. 개별 팬 제어는 기본적으로 꺼져 있으며, 테스트 서버에서 `0x00`은 모든 팬을 고속으로 돌렸습니다.
- 사용자 명령은 IPMI 잠금을 기다립니다. 잠금이 사용 중이면 백그라운드 폴링과 자동 tick을 명확히 건너뛰며 두 번째 `ipmitool` 프로세스를 시작하지 않습니다.
- 낮은 속도나 잘못된 커브는 CPU, 디스크, PCIe 카드, 파워서플라이를 과열시킬 수 있습니다. 확신할 수 없으면 Dell Auto를 사용하세요.

## 검증과 문서

```powershell
dotnet run --project .\Tests\PresetModelTests\PresetModelTests.csproj -c Release
dotnet build .\DellR730xdFanControlCenter.csproj -c Release -p:Platform=x64
```

이 검사는 실제 서버 테스트를 대신하지 않습니다. GUI 시작, 실제 raw 명령, 다른 iDRAC 펌웨어, 모든 DPI 조합은 완전히 검증되지 않았습니다. 자세한 내용은 [영문 안내](README.en-US.md), [IPMI 명령](docs/COMMANDS.en-US.md), [보안](SECURITY.en-US.md)을 참고하세요.
